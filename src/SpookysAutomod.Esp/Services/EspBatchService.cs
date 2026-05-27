// ============================================================================
// EspBatchService - apply many ESP mutations against a SINGLE in-memory mod.
//
// PURPOSE
//   The existing per-command service methods on PluginService each do a full
//   `SkyrimMod.CreateFromBinary` + apply-one-mutation + `WriteToBinary` round
//   trip. For a build like _GaiusMissions's 50-slot unified pool that's
//   hundreds of invocations + hundreds of full plugin parse/serialize cycles.
//
//   This service does the same mutations but takes the SkyrimMod as a
//   parameter: the caller loads the mod once, runs an arbitrary number of
//   batch-* methods against it, and saves it once. Used by the `esp script`
//   CLI command which drives the ops from a JSON file.
//
// SCOPE (intentional)
//   Only the ops the per-slot build loops need:
//     - add-refr               (XMarker placement)
//     - add-package            (sandbox / travel / follow only - the types
//                               the unified slot pool uses today; extend
//                               by adding cases here as new types are needed)
//     - add-condition          (CTDA on packages, perks, etc.)
//     - attach-package         (PACK attached to a quest alias)
//     - add-alias              (REFR alias on a quest, optionally with script)
//     - attach-alias-script    (script attached to an existing alias)
//
//   Other ops still go through the existing per-command CLI. Add to this
//   service when the next bulk-generation script needs them.
//
// NON-GOALS
//   - Backward-reference substitution (`${id}`). The two-batch pattern in
//     consumer scripts (run add-refr first, collect FormKeys, then run the
//     rest) covers the only dependency we have today (Travel package's PLDT
//     pointing at the per-slot XMarker REFR's FormKey). Avoids parser
//     complexity in the dispatcher.
//   - Replacement of the existing per-command CLI. Those still work; this
//     service is additive.
// ============================================================================

// Created by Claude (LLM) - per user's global CLAUDE.md attribution rule.

using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using SpookysAutomod.Core.Logging;
using SpookysAutomod.Core.Models;
using SpookysAutomod.Esp.Builders;
using System.Text.Json;

namespace SpookysAutomod.Esp.Services;

/// <summary>
/// One entry in a batch script. `Op` selects which batch-* method to invoke;
/// `Args` carries op-specific arguments (the dispatcher pulls fields by name);
/// `Id` is optional and not used for back-refs today - reserved for future
/// extension if the dispatcher gains substitution.
/// </summary>
public class BatchOpEntry
{
    public string? Id { get; set; }
    public string Op { get; set; } = "";
    public JsonElement Args { get; set; }
}

/// <summary>
/// Per-op result returned by `ExecuteOps`. `Success` mirrors the underlying
/// service-method `Result.Success`. `Value` is the FormKey string for record
/// creators, or a status string for attach-style ops. The dispatcher records
/// failures with their index so callers can pinpoint where the script broke.
/// </summary>
public class BatchOpResult
{
    public int Index { get; set; }
    public string? Id { get; set; }
    public string Op { get; set; } = "";
    public bool Success { get; set; }
    public string? Value { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Aggregate return shape from `ExecuteOps`. The CLI command saves the mod
/// only when `Success == true`; on failure it bails out without saving so the
/// on-disk ESP doesn't end up in a half-applied state.
/// </summary>
public class BatchRunResult
{
    public bool Success { get; set; }
    public int OpCount { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int? FailedAt { get; set; }
    public List<BatchOpResult> Results { get; set; } = new();
}

public class EspBatchService
{
    private readonly IModLogger _logger;
    private readonly PluginService _pluginService;

    public EspBatchService(IModLogger logger, PluginService pluginService)
    {
        _logger = logger;
        _pluginService = pluginService;
    }

    // ------------------------------------------------------------------------
    // Top-level driver
    // ------------------------------------------------------------------------

    /// <summary>
    /// Run a list of ops against the given mod. Stops at the first failure and
    /// returns the per-op results collected so far. The caller is responsible
    /// for deciding whether to save the mod (typically only on overall success).
    /// </summary>
    public BatchRunResult ExecuteOps(SkyrimMod mod, IReadOnlyList<BatchOpEntry> ops)
    {
        var result = new BatchRunResult { OpCount = ops.Count };

        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            var opResult = DispatchOne(mod, op);
            opResult.Index = i;
            opResult.Id = op.Id;
            opResult.Op = op.Op;
            result.Results.Add(opResult);

            if (opResult.Success)
            {
                result.Succeeded++;
            }
            else
            {
                result.Failed++;
                result.FailedAt = i;
                result.Success = false;
                return result;
            }
        }

        result.Success = true;
        return result;
    }

    private BatchOpResult DispatchOne(SkyrimMod mod, BatchOpEntry op)
    {
        try
        {
            // The switch is the single place op-name → method binding lives.
            // When you add a new method to this service, register it here too.
            var inner = op.Op.ToLowerInvariant() switch
            {
                "add-refr"             => BatchAddRefr(mod, op.Args),
                "add-package"          => BatchAddPackage(mod, op.Args),
                "add-condition"        => BatchAddCondition(mod, op.Args),
                "attach-package"       => BatchAttachPackageToAlias(mod, op.Args),
                "add-alias"            => BatchAddAlias(mod, op.Args),
                "attach-alias-script"  => BatchAttachAliasScript(mod, op.Args),
                _ => Result<string>.Fail($"Unknown op: '{op.Op}'")
            };

            return new BatchOpResult
            {
                Success = inner.Success,
                Value = inner.Value,
                Error = inner.Error
            };
        }
        catch (Exception ex)
        {
            return new BatchOpResult { Success = false, Error = $"Op '{op.Op}' threw: {ex.Message}" };
        }
    }

    // ------------------------------------------------------------------------
    // Per-op implementations
    //
    // Each method takes the mod + a JsonElement of args. We pull strongly-
    // typed fields out of the JsonElement via small helpers (GetString / GetInt
    // / GetFloat) defined at the bottom. Returns Result<string> where the
    // string is the new record's FormKey (for record creators) or a status
    // message (for attach-style ops).
    //
    // The mutation logic deliberately mirrors what the corresponding
    // PluginService method does between load and save. When the underlying
    // PluginService method's logic changes, mirror it here too - or refactor
    // PluginService to expose a mod-based variant we can delegate to.
    // ------------------------------------------------------------------------

    private Result<string> BatchAddRefr(SkyrimMod mod, JsonElement args)
    {
        if (mod.ModHeader.Stats.NextFormID < 0x800)
        {
            mod.ModHeader.Stats.NextFormID = 0x800;
        }

        var baseStr = GetString(args, "base");
        var cellStr = GetString(args, "cell");
        var editorId = GetStringOrNull(args, "editorId");
        var x = GetFloat(args, "x", 0f);
        var y = GetFloat(args, "y", 0f);
        var z = GetFloat(args, "z", 0f);
        var rotX = GetFloat(args, "rotX", 0f);
        var rotY = GetFloat(args, "rotY", 0f);
        var rotZ = GetFloat(args, "rotZ", 0f);
        var persistent = GetBool(args, "persistent", true);
        var dataFolder = GetStringOrNull(args, "dataFolder");

        if (!FormKey.TryFactory(baseStr, out var baseFk))
            return Result<string>.Fail($"Invalid base FormKey: {baseStr}");
        if (!FormKey.TryFactory(cellStr, out var cellFk))
            return Result<string>.Fail($"Invalid cell FormKey: {cellStr}");

        // Locate the cell. First look in the plugin itself (a prior add-refr
        // in this batch may already have overridden the master cell). If not
        // found, fall back to the master-resolution path used by PluginService.AddRefr.
        ICell? targetCell = null;
        foreach (var block in mod.Cells)
        {
            foreach (var subBlock in block.SubBlocks)
            {
                foreach (var c in subBlock.Cells)
                {
                    if (c.FormKey == cellFk) { targetCell = c; break; }
                }
                if (targetCell != null) break;
            }
            if (targetCell != null) break;
        }

        if (targetCell == null)
        {
            if (string.IsNullOrEmpty(dataFolder))
                return Result<string>.Fail($"Cell {cellStr} not in plugin; need 'dataFolder' arg to resolve from masters");

            var linkCacheResult = new LinkCacheManager(_logger).CreateLinkCacheWithMod(dataFolder, mod);
            if (!linkCacheResult.Success)
                return Result<string>.Fail($"Failed to build link cache: {linkCacheResult.Error}");
            var linkCache = (Mutagen.Bethesda.Plugins.Cache.ILinkCache<ISkyrimMod, ISkyrimModGetter>)linkCacheResult.Value!;

            var cellLink = cellFk.ToLink<ICellGetter>();
            if (!cellLink.TryResolveContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter>(linkCache, out var cellContext))
                return Result<string>.Fail($"Cell {cellStr} not found in plugin or any master");

            targetCell = cellContext.GetOrAddAsOverride(mod);
        }

        var builder = new RefrBuilder(mod, targetCell, editorId)
            .WithBase(baseFk)
            .AtPosition(x, y, z)
            .WithRotation(rotX, rotY, rotZ);
        if (!persistent) builder.AsTemporary();

        var refr = builder.Build();
        _logger.Debug($"[batch] add-refr {editorId} = {refr.FormKey}");
        return Result<string>.Ok(refr.FormKey.ToString());
    }

    private Result<string> BatchAddPackage(SkyrimMod mod, JsonElement args)
    {
        if (mod.ModHeader.Stats.NextFormID < 0x800)
        {
            mod.ModHeader.Stats.NextFormID = 0x800;
        }

        var editorId = GetString(args, "editorId");
        var packageType = GetString(args, "type").ToLowerInvariant();

        var builder = new PackageBuilder(mod, editorId);

        // Only the types the unified slot pool uses today. Mirror the
        // corresponding case from PluginService.AddPackage when adding more.
        switch (packageType)
        {
            case "sandbox":
            {
                var radius = (uint)GetInt(args, "radius", 1024);
                builder.AsSandbox(radius);
                break;
            }

            case "travel":
            {
                var dest = GetStringOrNull(args, "destination");
                if (string.IsNullOrEmpty(dest))
                    return Result<string>.Fail("Travel package requires 'destination' (REFR FormKey)");
                if (!FormKey.TryFactory(dest, out var destFk))
                    return Result<string>.Fail($"Invalid destination FormKey: {dest}");
                builder.AsTravel(destFk);
                break;
            }

            case "follow":
            {
                var questEd = GetString(args, "targetAliasQuest");
                var aliasName = GetString(args, "targetAliasName");
                var minRadius = GetFloat(args, "minRadius", 128f);
                var maxRadius = GetFloat(args, "maxRadius", 256f);
                var goToLeader = GetBool(args, "goToLeadersGoal", true);
                var needLos = GetBool(args, "needLos", false);
                var rideHorse = GetBool(args, "rideHorse", false);

                var quest = mod.Quests.FirstOrDefault(q => q.EditorID == questEd);
                if (quest == null) return Result<string>.Fail($"Quest not found: {questEd}");
                var alias = quest.Aliases.FirstOrDefault(a => a.Name == aliasName);
                if (alias == null) return Result<string>.Fail($"Alias '{aliasName}' not found in quest '{questEd}'");

                builder.AsFollowToAlias((int)alias.ID, minRadius, maxRadius, goToLeader, needLos, rideHorse)
                       .WithOwnerQuest(quest.FormKey);
                break;
            }

            default:
                return Result<string>.Fail($"Batch add-package: unsupported type '{packageType}'. Supported in batch mode: sandbox, travel, follow.");
        }

        var package = builder.Build();
        _logger.Debug($"[batch] add-package {editorId} ({packageType}) = {package.FormKey}");
        return Result<string>.Ok(package.FormKey.ToString());
    }

    private Result<string> BatchAddCondition(SkyrimMod mod, JsonElement args)
    {
        // The CTDA find-record + reflection logic mirrors PluginService.AddCondition.
        // Differences from that method: takes the mod from caller, doesn't save.
        var editorId = GetStringOrNull(args, "editorId");
        var formId = GetStringOrNull(args, "formId");
        var recordType = GetStringOrNull(args, "type");
        var function = GetString(args, "function");
        var compareValue = GetFloat(args, "value", 0f);
        var compareOpStr = GetString(args, "operator");
        var param1 = GetStringOrNull(args, "param1");

        IMajorRecord? targetRecord = null;
        if (!string.IsNullOrEmpty(formId))
        {
            var findResult = _pluginService.FindRecordByFormKey(mod, formId);
            if (!findResult.Success || findResult.Value == null)
                return Result<string>.Fail($"Record not found by formId: {formId}");
            targetRecord = findResult.Value as IMajorRecord;
        }
        else if (!string.IsNullOrEmpty(editorId) && !string.IsNullOrEmpty(recordType))
        {
            var findResult = _pluginService.FindRecordByEditorId(mod, editorId, recordType);
            if (!findResult.Success || findResult.Value == null)
                return Result<string>.Fail($"Record not found by editorId: {editorId} (type {recordType})");
            targetRecord = findResult.Value as IMajorRecord;
        }
        else
        {
            return Result<string>.Fail("add-condition requires either 'formId' or both 'editorId' and 'type'");
        }

        if (targetRecord == null)
            return Result<string>.Fail("Record located but is not writable");

        if (!Enum.TryParse<Mutagen.Bethesda.Skyrim.CompareOperator>(compareOpStr, true, out var compareOp))
            return Result<string>.Fail($"Invalid operator: {compareOpStr}");

        FormKey? param1Fk = null;
        if (!string.IsNullOrEmpty(param1))
        {
            if (!FormKey.TryFactory(param1, out var parsed))
                return Result<string>.Fail($"Invalid param1 FormKey: {param1}");
            param1Fk = parsed;
        }

        var conditionResult = _pluginService.CreateConditionFromFunction(function, compareValue, compareOp, param1Fk);
        if (!conditionResult.Success || conditionResult.Value == null)
            return Result<string>.Fail(conditionResult.Error ?? "Failed to create condition");

        // Same reflection-driven Conditions list lookup PluginService.AddCondition uses.
        var conditionsProp = targetRecord.GetType().GetProperty("Conditions");
        if (conditionsProp == null)
            return Result<string>.Fail($"Record type '{targetRecord.GetType().Name}' has no Conditions property");
        var conditionsList = conditionsProp.GetValue(targetRecord) as System.Collections.IList;
        if (conditionsList == null)
            return Result<string>.Fail($"Record type '{targetRecord.GetType().Name}' Conditions is not a writable list");

        conditionsList.Add(conditionResult.Value);
        _logger.Debug($"[batch] add-condition {function} on {targetRecord.EditorID}");
        return Result<string>.Ok($"condition added to {targetRecord.EditorID}");
    }

    private Result<string> BatchAttachPackageToAlias(SkyrimMod mod, JsonElement args)
    {
        var questEd = GetString(args, "quest");
        var aliasName = GetString(args, "alias");
        var packageEd = GetString(args, "package");

        var quest = mod.Quests.FirstOrDefault(q => q.EditorID == questEd);
        if (quest == null) return Result<string>.Fail($"Quest not found: {questEd}");
        var alias = quest.Aliases.FirstOrDefault(a => a.Name == aliasName);
        if (alias == null) return Result<string>.Fail($"Alias '{aliasName}' not found in quest '{questEd}'");
        var package = mod.Packages.FirstOrDefault(p => p.EditorID == packageEd);
        if (package == null) return Result<string>.Fail($"Package not found: {packageEd}");

        alias.PackageData.Add(package.ToLink());
        _logger.Debug($"[batch] attach-package {packageEd} -> {aliasName}");
        return Result<string>.Ok($"attached {packageEd} to {aliasName}");
    }

    private Result<string> BatchAddAlias(SkyrimMod mod, JsonElement args)
    {
        var questEd = GetString(args, "quest");
        var aliasName = GetString(args, "name");
        var scriptName = GetStringOrNull(args, "script");
        var flagsStr = GetStringOrNull(args, "flags");

        var quest = mod.Quests.FirstOrDefault(q => q.EditorID == questEd);
        if (quest == null) return Result<string>.Fail($"Quest not found: {questEd}");

        QuestAlias.Flag? flags = null;
        if (!string.IsNullOrWhiteSpace(flagsStr))
        {
            if (!Enum.TryParse<QuestAlias.Flag>(flagsStr, ignoreCase: true, out var parsed))
                return Result<string>.Fail($"Invalid alias flags: '{flagsStr}'");
            flags = parsed;
        }

        // Existing service method is already mod-aware (takes Quest, doesn't save).
        var addResult = _pluginService.AddAliasToQuest(quest, aliasName, scriptName, flags);
        if (!addResult.Success || addResult.Value == null)
            return Result<string>.Fail(addResult.Error ?? "AddAliasToQuest failed");

        _logger.Debug($"[batch] add-alias {aliasName} (id {addResult.Value.ID}) -> {questEd}");
        return Result<string>.Ok($"alias {aliasName} added (id {addResult.Value.ID})");
    }

    private Result<string> BatchAttachAliasScript(SkyrimMod mod, JsonElement args)
    {
        var questEd = GetString(args, "quest");
        var aliasName = GetString(args, "alias");
        var scriptName = GetString(args, "script");

        var quest = mod.Quests.FirstOrDefault(q => q.EditorID == questEd);
        if (quest == null) return Result<string>.Fail($"Quest not found: {questEd}");

        var attachResult = _pluginService.AttachScriptToAliasByName(quest, aliasName, scriptName);
        if (!attachResult.Success)
            return Result<string>.Fail(attachResult.Error ?? "AttachScriptToAliasByName failed");

        _logger.Debug($"[batch] attach-alias-script {scriptName} -> {aliasName}");
        return Result<string>.Ok($"script {scriptName} attached to alias {aliasName}");
    }

    // ------------------------------------------------------------------------
    // JsonElement field-pull helpers. Centralized so the per-op methods stay
    // declarative. Numbers tolerate both JSON number and JSON string (the
    // PowerShell JSON emitter sometimes quotes numeric values).
    // ------------------------------------------------------------------------

    private static string GetString(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
            throw new InvalidOperationException($"Missing required arg: '{name}'");
        return prop.ValueKind == JsonValueKind.String ? prop.GetString()! : prop.ToString();
    }

    private static string? GetStringOrNull(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
    }

    private static int GetInt(JsonElement args, string name, int fallback)
    {
        if (!args.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return fallback;
        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetInt32(),
            JsonValueKind.String => int.TryParse(prop.GetString(), out var v) ? v : fallback,
            _ => fallback
        };
    }

    private static float GetFloat(JsonElement args, string name, float fallback)
    {
        if (!args.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return fallback;
        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetSingle(),
            JsonValueKind.String => float.TryParse(prop.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback,
            _ => fallback
        };
    }

    private static bool GetBool(JsonElement args, string name, bool fallback)
    {
        if (!args.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return fallback;
        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(prop.GetString(), out var v) ? v : fallback,
            _ => fallback
        };
    }
}
