using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using SpookysAutomod.Core.Logging;
using SpookysAutomod.Core.Models;
using SpookysAutomod.Esp.Builders;
using System.Reflection;

namespace SpookysAutomod.Esp.Services;

/// <summary>
/// Service for creating, loading, and saving ESP/ESM/ESL plugin files.
/// </summary>
public class PluginService
{
    private readonly IModLogger _logger;

    public PluginService(IModLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Create a new empty plugin.
    /// </summary>
    public Result<string> CreatePlugin(
        string name,
        string outputPath,
        bool isLight = false,
        string? author = null,
        string? description = null)
    {
        try
        {
            // Ensure .esp extension
            if (!name.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".esl", StringComparison.OrdinalIgnoreCase))
            {
                name += ".esp";
            }

            var modKey = ModKey.FromFileName(name);
            var mod = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);

            // Set header flags for light plugin (ESL flag = 0x200)
            if (isLight)
            {
                mod.ModHeader.Flags |= (SkyrimModHeader.HeaderFlag)0x200;
            }

            if (!string.IsNullOrEmpty(author))
            {
                mod.ModHeader.Author = author;
            }

            if (!string.IsNullOrEmpty(description))
            {
                mod.ModHeader.Description = description;
            }

            // Ensure output directory exists
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var fullPath = Path.IsPathRooted(outputPath)
                ? Path.Combine(outputPath, name)
                : Path.Combine(Directory.GetCurrentDirectory(), outputPath, name);

            mod.WriteToBinary(fullPath);

            _logger.Info($"Created plugin: {fullPath}");
            return Result<string>.Ok(fullPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail(
                $"Failed to create plugin: {ex.Message}",
                ex.StackTrace,
                new List<string>
                {
                    "Ensure the output path is writable",
                    "Check that the plugin name contains only valid characters"
                });
        }
    }

    /// <summary>
    /// Load an existing plugin for reading.
    /// </summary>
    public Result<ISkyrimModGetter> LoadPluginReadOnly(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return Result<ISkyrimModGetter>.Fail(
                    $"Plugin not found: {path}",
                    suggestions: new List<string>
                    {
                        "Check the file path is correct",
                        "Use an absolute path if relative path fails"
                    });
            }

            var mod = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
            _logger.Debug($"Loaded plugin (read-only): {path}");
            return Result<ISkyrimModGetter>.Ok(mod);
        }
        catch (Exception ex)
        {
            return Result<ISkyrimModGetter>.Fail(
                $"Failed to load plugin: {ex.Message}",
                ex.StackTrace,
                new List<string>
                {
                    "Ensure the file is a valid Skyrim plugin",
                    "Check if the file is corrupted",
                    "Verify the file is not locked by another process"
                });
        }
    }

    /// <summary>
    /// Load an existing plugin for editing.
    /// </summary>
    public Result<SkyrimMod> LoadPluginForEdit(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return Result<SkyrimMod>.Fail(
                    $"Plugin not found: {path}",
                    suggestions: new List<string>
                    {
                        "Check the file path is correct",
                        "Use an absolute path if relative path fails"
                    });
            }

            // Use import mask to properly initialize FormKey allocation
            var mod = SkyrimMod.CreateFromBinary(
                path,
                SkyrimRelease.SkyrimSE,
                new Mutagen.Bethesda.Plugins.Binary.Parameters.BinaryReadParameters
                {
                    // This ensures the FormKey allocator is properly initialized
                });

            // If the mod is empty or has low FormIDs, set a proper starting point
            // Skyrim mods should allocate FormIDs starting from 0x800
            if (mod.ModHeader.Stats.NextFormID < 0x800)
            {
                mod.ModHeader.Stats.NextFormID = 0x800;
            }

            _logger.Debug($"Loaded plugin (editable): {path}");
            return Result<SkyrimMod>.Ok(mod);
        }
        catch (Exception ex)
        {
            return Result<SkyrimMod>.Fail(
                $"Failed to load plugin: {ex.Message}",
                ex.StackTrace,
                new List<string>
                {
                    "Ensure the file is a valid Skyrim plugin",
                    "Check if the file is corrupted",
                    "Verify the file is not locked by another process"
                });
        }
    }

    /// <summary>
    /// Save a plugin to disk.
    /// </summary>
    public Result SavePlugin(SkyrimMod mod, string? outputPath = null)
    {
        try
        {
            var path = outputPath ?? Path.Combine(Directory.GetCurrentDirectory(), mod.ModKey.FileName);
            mod.WriteToBinary(path);
            _logger.Info($"Saved plugin: {path}");
            return Result.Ok($"Plugin saved to: {path}");
        }
        catch (Exception ex)
        {
            return Result.Fail(
                $"Failed to save plugin: {ex.Message}",
                ex.StackTrace,
                new List<string>
                {
                    "Ensure the output path is writable",
                    "Check that no other process has the file locked"
                });
        }
    }

    /// <summary>
    /// Get information about a plugin.
    /// </summary>
    public Result<PluginInfo> GetPluginInfo(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return Result<PluginInfo>.Fail($"Plugin not found: {path}");
            }

            var mod = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
            var fileInfo = new FileInfo(path);

            var info = new PluginInfo
            {
                FileName = mod.ModKey.FileName,
                FilePath = path,
                Author = mod.ModHeader.Author,
                Description = mod.ModHeader.Description,
                IsLight = mod.ModHeader.Flags.HasFlag((SkyrimModHeader.HeaderFlag)0x200),
                IsMaster = mod.ModKey.Type == ModType.Master,
                FileSize = fileInfo.Length
            };

            // Get master files
            foreach (var master in mod.ModHeader.MasterReferences)
            {
                info.MasterFiles.Add(master.Master.FileName);
            }

            // Count records by type
            info.RecordCounts["Quests"] = mod.Quests.Count;
            info.RecordCounts["Spells"] = mod.Spells.Count;
            info.RecordCounts["Globals"] = mod.Globals.Count;
            info.RecordCounts["NPCs"] = mod.Npcs.Count;
            info.RecordCounts["Weapons"] = mod.Weapons.Count;
            info.RecordCounts["Armors"] = mod.Armors.Count;
            info.RecordCounts["Books"] = mod.Books.Count;
            info.RecordCounts["Perks"] = mod.Perks.Count;
            info.RecordCounts["Factions"] = mod.Factions.Count;
            info.RecordCounts["MiscItems"] = mod.MiscItems.Count;
            info.RecordCounts["LeveledItems"] = mod.LeveledItems.Count;
            info.RecordCounts["FormLists"] = mod.FormLists.Count;
            info.RecordCounts["EncounterZones"] = mod.EncounterZones.Count;
            info.RecordCounts["Locations"] = mod.Locations.Count;
            info.RecordCounts["Outfits"] = mod.Outfits.Count;

            info.TotalRecords = info.RecordCounts.Values.Sum();

            return Result<PluginInfo>.Ok(info);
        }
        catch (Exception ex)
        {
            return Result<PluginInfo>.Fail(
                $"Failed to read plugin info: {ex.Message}",
                ex.StackTrace);
        }
    }

    /// <summary>
    /// Generate a SEQ file for quests that start enabled.
    /// </summary>
    public Result<string> GenerateSeqFile(string pluginPath, string outputDir)
    {
        try
        {
            if (!File.Exists(pluginPath))
            {
                return Result<string>.Fail($"Plugin not found: {pluginPath}");
            }

            var mod = SkyrimMod.CreateFromBinaryOverlay(pluginPath, SkyrimRelease.SkyrimSE);
            var startEnabledQuests = new List<uint>();

            foreach (var quest in mod.Quests)
            {
                if (quest.Flags.HasFlag(Quest.Flag.StartGameEnabled))
                {
                    startEnabledQuests.Add(quest.FormKey.ID);
                }
            }

            if (startEnabledQuests.Count == 0)
            {
                return Result<string>.Fail(
                    "No start-enabled quests found",
                    suggestions: new List<string>
                    {
                        "Add a quest with StartGameEnabled flag",
                        "SEQ files are only needed for quests that start on game load"
                    });
            }

            // Ensure output directory exists
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var seqFileName = Path.GetFileNameWithoutExtension(mod.ModKey.FileName) + ".seq";
            var seqPath = Path.Combine(outputDir, seqFileName);

            // Write SEQ file (simple format: count + FormIDs)
            using var writer = new BinaryWriter(File.Create(seqPath));
            writer.Write((uint)startEnabledQuests.Count);
            foreach (var formId in startEnabledQuests)
            {
                writer.Write(formId);
            }

            _logger.Info($"Generated SEQ file with {startEnabledQuests.Count} quest(s): {seqPath}");
            return Result<string>.Ok(seqPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail(
                $"Failed to generate SEQ file: {ex.Message}",
                ex.StackTrace);
        }
    }

    /// <summary>
    /// View detailed information about a record
    /// </summary>
    public Result<RecordInfo> ViewRecord(
        string pluginPath,
        string? editorId,
        string? formId,
        string? recordType,
        bool includeRaw = false)
    {
        try
        {
            if (!File.Exists(pluginPath))
            {
                return Result<RecordInfo>.Fail(
                    $"Plugin not found: {pluginPath}",
                    suggestions: new List<string>
                    {
                        "Check the file path is correct",
                        "Use an absolute path if relative path fails"
                    });
            }

            var mod = SkyrimMod.CreateFromBinaryOverlay(pluginPath, SkyrimRelease.SkyrimSE);

            IMajorRecordGetter? record = null;

            if (!string.IsNullOrEmpty(formId))
            {
                var findResult = FindRecordByFormKey(mod, formId);
                if (!findResult.Success)
                {
                    return Result<RecordInfo>.Fail(findResult.Error ?? "Record not found", findResult.ErrorContext);
                }
                record = findResult.Value;
            }
            else if (!string.IsNullOrEmpty(editorId) && !string.IsNullOrEmpty(recordType))
            {
                var findResult = FindRecordByEditorId(mod, editorId, recordType);
                if (!findResult.Success)
                {
                    return Result<RecordInfo>.Fail(findResult.Error ?? "Record not found", findResult.ErrorContext);
                }
                record = findResult.Value;
            }
            else
            {
                return Result<RecordInfo>.Fail(
                    "Must provide either FormID or both EditorID and RecordType",
                    suggestions: new List<string>
                    {
                        "Use --form-id for FormKey-based lookup",
                        "Use --editor-id and --type for EditorID-based lookup"
                    });
            }

            if (record == null)
            {
                return Result<RecordInfo>.Fail(
                    $"Record not found: {editorId ?? formId}",
                    suggestions: new List<string>
                    {
                        "Use 'esp info' to see available records",
                        "Check spelling of EditorID",
                        "Verify FormID is correct"
                    });
            }

            var recordInfo = new RecordInfo
            {
                EditorId = record.EditorID ?? string.Empty,
                FormKey = record.FormKey.ToString(),
                RecordType = record.GetType().Name.Replace("Getter", "").Replace("ReadOnly", "")
            };

            var propsResult = ExtractRecordProperties(record, includeRaw);
            if (!propsResult.Success)
            {
                return Result<RecordInfo>.Fail(propsResult.Error ?? "Failed to extract properties", propsResult.ErrorContext);
            }
            recordInfo.Properties = propsResult.Value ?? new Dictionary<string, object?>();

            var conditionsResult = ExtractConditions(record);
            if (conditionsResult.Success && conditionsResult.Value != null && conditionsResult.Value.Count > 0)
            {
                recordInfo.Conditions = conditionsResult.Value;
            }

            return Result<RecordInfo>.Ok(recordInfo);
        }
        catch (Exception ex)
        {
            return Result<RecordInfo>.Fail(
                $"Failed to view record: {ex.Message}",
                ex.StackTrace);
        }
    }

    /// <summary>
    /// Add a package (AI behavior) to a plugin.
    /// </summary>
    public Result<string> AddPackage(
        string pluginPath,
        string editorId,
        string packageType,
        Dictionary<string, object>? options = null)
    {
        try
        {
            if (!File.Exists(pluginPath))
            {
                return Result<string>.Fail($"Plugin not found: {pluginPath}");
            }

            var mod = SkyrimMod.CreateFromBinary(
                pluginPath,
                SkyrimRelease.SkyrimSE,
                new Mutagen.Bethesda.Plugins.Binary.Parameters.BinaryReadParameters());

            // Ensure proper FormID allocation
            if (mod.ModHeader.Stats.NextFormID < 0x800)
            {
                mod.ModHeader.Stats.NextFormID = 0x800;
            }

            var builder = new PackageBuilder(mod, editorId);

            // Configure package based on type
            switch (packageType.ToLowerInvariant())
            {
                case "sandbox":
                    var radius = options?.GetValueOrDefault("radius", 500) ?? 500;
                    builder.AsSandbox(Convert.ToUInt16(radius));
                    break;

                case "travel":
                    // New: alias-target Travel package. Resolves to whatever
                    // REFR fills the named alias at runtime. The package's
                    // OwnerQuest is set automatically so the engine can
                    // resolve the alias index.
                    if (options?.ContainsKey("targetAliasQuest") == true
                        && options.ContainsKey("targetAliasName"))
                    {
                        var questEd = options["targetAliasQuest"].ToString()!;
                        var aliasName = options["targetAliasName"].ToString()!;
                        var quest = mod.Quests.FirstOrDefault(q => q.EditorID == questEd);
                        if (quest == null)
                        {
                            return Result<string>.Fail(
                                $"Quest not found: {questEd}",
                                suggestions: new List<string>
                                {
                                    "Create the quest first with 'esp add-quest'",
                                    "Check the quest editor ID spelling"
                                });
                        }
                        var alias = quest.Aliases.FirstOrDefault(a => a.Name == aliasName);
                        if (alias == null)
                        {
                            return Result<string>.Fail(
                                $"Alias '{aliasName}' not found in quest '{questEd}'",
                                suggestions: new List<string>
                                {
                                    "Create the alias first with 'esp add-alias'",
                                    "Check the alias name spelling"
                                });
                        }
                        builder.AsTravelToAlias((int)alias.ID).WithOwnerQuest(quest.FormKey);
                    }
                    // Legacy: hardcoded-REFR target Travel package. The CLI
                    // option `--destination` maps to options["destinationRef"]
                    // (the shared key used by accompany packages too), not
                    // options["destination"]. Accept both for backward compat.
                    else if (options?.ContainsKey("destinationRef") == true ||
                             options?.ContainsKey("destination") == true)
                    {
                        var destStr = (options.ContainsKey("destinationRef")
                                       ? options["destinationRef"]
                                       : options["destination"])?.ToString();
                        if (!string.IsNullOrEmpty(destStr) && FormKey.TryFactory(destStr, out var destFormKey))
                        {
                            builder.AsTravel(destFormKey);
                        }
                    }
                    else
                    {
                        return Result<string>.Fail(
                            "Travel packages require either --destination (FormKey of REFR) or --target-alias-quest + --target-alias-name (quest alias)",
                            suggestions: new List<string>
                            {
                                "For static destinations: pass --destination <FormKey>",
                                "For dynamic destinations: pass --target-alias-quest <questEditorId> --target-alias-name <aliasName>"
                            });
                    }
                    break;

                case "sleep":
                    var startHour = Convert.ToByte(options?.GetValueOrDefault("startHour", 22) ?? 22);
                    var duration = Convert.ToByte(options?.GetValueOrDefault("duration", 8) ?? 8);

                    if (options?.ContainsKey("bedRef") == true)
                    {
                        var bedRefStr = options["bedRef"]?.ToString();
                        if (!string.IsNullOrEmpty(bedRefStr) && FormKey.TryFactory(bedRefStr, out var bedFormKey))
                        {
                            builder.AsSleep(bedFormKey, startHour, duration);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid bed reference FormKey: {bedRefStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Sleep packages require a valid bed reference (bedRef)");
                    }
                    break;

                case "eat":
                    var eatStartHour = Convert.ToByte(options?.GetValueOrDefault("startHour", 12) ?? 12);
                    var eatDuration = Convert.ToByte(options?.GetValueOrDefault("duration", 2) ?? 2);

                    if (options?.ContainsKey("furnitureRef") == true)
                    {
                        var furnitureRefStr = options["furnitureRef"]?.ToString();
                        if (!string.IsNullOrEmpty(furnitureRefStr) && FormKey.TryFactory(furnitureRefStr, out var furnitureFormKey))
                        {
                            builder.AsEat(furnitureFormKey, eatStartHour, eatDuration);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid furniture reference FormKey: {furnitureRefStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Eat packages require a valid furniture reference (furnitureRef)");
                    }
                    break;

                case "follow":
                {
                    // Shared Follow parameters. Slot/field names extracted from
                    // Skyrim.esm:0x00019B2C ("Follow" vanilla template) via the
                    // inspect_pack tool. Defaults mirror the template's defaults:
                    //   MinRadius=128, MaxRadius=256, Accompany=true,
                    //   NeedLOS=false, RideHorse=false.
                    // Backward-compat: legacy callers using `followDistance` get
                    // that value mapped onto MaxRadius (the closest single-value
                    // parameter to what the old code intended).
                    var legacyDistance = options?.GetValueOrDefault("followDistance");
                    var minRadius        = Convert.ToSingle(options?.GetValueOrDefault("minRadius", 128.0f) ?? 128.0f);
                    var maxRadius        = legacyDistance != null
                        ? Convert.ToSingle(legacyDistance)
                        : Convert.ToSingle(options?.GetValueOrDefault("maxRadius", 256.0f) ?? 256.0f);
                    var goToLeadersGoal  = Convert.ToBoolean(options?.GetValueOrDefault("goToLeadersGoal", true) ?? true);
                    var needLOS          = Convert.ToBoolean(options?.GetValueOrDefault("needLOS", false) ?? false);
                    var rideHorse        = Convert.ToBoolean(options?.GetValueOrDefault("rideHorse", false) ?? false);

                    // New: alias-target Follow package. The leader is resolved
                    // at runtime to whatever REFR fills the named alias on the
                    // owning quest. Matches the M5.6 framework's leader-slot
                    // architecture.
                    if (options?.ContainsKey("targetAliasQuest") == true
                        && options.ContainsKey("targetAliasName"))
                    {
                        var questEd = options["targetAliasQuest"].ToString()!;
                        var aliasName = options["targetAliasName"].ToString()!;
                        var quest = mod.Quests.FirstOrDefault(q => q.EditorID == questEd);
                        if (quest == null)
                        {
                            return Result<string>.Fail(
                                $"Quest not found: {questEd}",
                                suggestions: new List<string>
                                {
                                    "Create the quest first with 'esp add-quest'",
                                    "Check the quest editor ID spelling"
                                });
                        }
                        var alias = quest.Aliases.FirstOrDefault(a => a.Name == aliasName);
                        if (alias == null)
                        {
                            return Result<string>.Fail(
                                $"Alias '{aliasName}' not found in quest '{questEd}'",
                                suggestions: new List<string>
                                {
                                    "Create the alias first with 'esp add-alias'",
                                    "Check the alias name spelling"
                                });
                        }
                        builder.AsFollowToAlias((int)alias.ID, minRadius, maxRadius,
                                                goToLeadersGoal, needLOS, rideHorse)
                               .WithOwnerQuest(quest.FormKey);
                    }
                    // Legacy: static-REFR target Follow.
                    else if (options?.ContainsKey("targetRef") == true)
                    {
                        var targetRefStr = options["targetRef"]?.ToString();
                        if (!string.IsNullOrEmpty(targetRefStr) && FormKey.TryFactory(targetRefStr, out var targetFormKey))
                        {
                            builder.AsFollow(targetFormKey, minRadius, maxRadius,
                                             goToLeadersGoal, needLOS, rideHorse);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid target reference FormKey: {targetRefStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail(
                            "Follow packages require either --target-ref (FormKey of an actor) or --target-alias-quest + --target-alias-name (quest alias holding the leader)");
                    }
                    break;
                }

                case "guard":
                    if (options?.ContainsKey("markerRef") == true)
                    {
                        var markerRefStr = options["markerRef"]?.ToString();
                        if (!string.IsNullOrEmpty(markerRefStr) && FormKey.TryFactory(markerRefStr, out var markerFormKey))
                        {
                            builder.AsGuard(markerFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid marker reference FormKey: {markerRefStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Guard packages require a valid marker reference (markerRef)");
                    }
                    break;

                case "useitemat":
                case "activate":
                case "use":
                    if (options?.ContainsKey("itemRef") == true)
                    {
                        var itemRefStr = options["itemRef"]?.ToString();
                        if (!string.IsNullOrEmpty(itemRefStr) && FormKey.TryFactory(itemRefStr, out var itemFormKey))
                        {
                            builder.AsUseItemAt(itemFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid item reference FormKey: {itemRefStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("UseItemAt packages require a valid item reference (itemRef)");
                    }
                    break;

                case "sit":
                    if (options?.ContainsKey("furnitureRef") == true)
                    {
                        var sitFurnitureStr = options["furnitureRef"]?.ToString();
                        if (!string.IsNullOrEmpty(sitFurnitureStr) && FormKey.TryFactory(sitFurnitureStr, out var sitFurnitureFormKey))
                        {
                            builder.AsSit(sitFurnitureFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid furniture reference FormKey: {sitFurnitureStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Sit packages require a valid furniture reference (furnitureRef)");
                    }
                    break;

                case "useidlemarker":
                case "idle":
                    if (options?.ContainsKey("markerRef") == true)
                    {
                        var idleMarkerStr = options["markerRef"]?.ToString();
                        if (!string.IsNullOrEmpty(idleMarkerStr) && FormKey.TryFactory(idleMarkerStr, out var idleMarkerFormKey))
                        {
                            builder.AsUseIdleMarker(idleMarkerFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid idle marker reference FormKey: {idleMarkerStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("UseIdleMarker packages require a valid marker reference (markerRef)");
                    }
                    break;

                case "flee":
                    var fleeDistance = Convert.ToUInt16(options?.GetValueOrDefault("distance", 1000) ?? 1000);

                    if (options?.ContainsKey("fleeFrom") == true)
                    {
                        var fleeFromStr = options["fleeFrom"]?.ToString();
                        if (!string.IsNullOrEmpty(fleeFromStr) && FormKey.TryFactory(fleeFromStr, out var fleeFromFormKey))
                        {
                            builder.AsFlee(fleeFromFormKey, fleeDistance);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid flee-from reference FormKey: {fleeFromStr}");
                        }
                    }
                    else
                    {
                        // Flee from combat (no specific target)
                        builder.AsFlee(null, fleeDistance);
                    }
                    break;

                case "accompany":
                case "escort":
                    if (options?.ContainsKey("targetRef") == true && options?.ContainsKey("destinationRef") == true)
                    {
                        var accompanyTargetStr = options["targetRef"]?.ToString();
                        var accompanyDestStr = options["destinationRef"]?.ToString();

                        if (!string.IsNullOrEmpty(accompanyTargetStr) && FormKey.TryFactory(accompanyTargetStr, out var accompanyTargetFormKey) &&
                            !string.IsNullOrEmpty(accompanyDestStr) && FormKey.TryFactory(accompanyDestStr, out var accompanyDestFormKey))
                        {
                            builder.AsAccompany(accompanyTargetFormKey, accompanyDestFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail("Invalid FormKey for target or destination");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Accompany packages require both target reference (targetRef) and destination reference (destinationRef)");
                    }
                    break;

                case "castmagic":
                case "cast":
                    if (options?.ContainsKey("targetRef") == true)
                    {
                        var castTargetStr = options["targetRef"]?.ToString();
                        if (!string.IsNullOrEmpty(castTargetStr) && FormKey.TryFactory(castTargetStr, out var castTargetFormKey))
                        {
                            builder.AsCastMagic(castTargetFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid cast target FormKey: {castTargetStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("CastMagic packages require a valid target reference (targetRef)");
                    }
                    break;

                case "dialogue":
                    if (options?.ContainsKey("targetRef") == true)
                    {
                        var dialogueTargetStr = options["targetRef"]?.ToString();
                        if (!string.IsNullOrEmpty(dialogueTargetStr) && FormKey.TryFactory(dialogueTargetStr, out var dialogueTargetFormKey))
                        {
                            builder.AsDialogue(dialogueTargetFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid dialogue target FormKey: {dialogueTargetStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Dialogue packages require a valid target reference (targetRef)");
                    }
                    break;

                case "find":
                case "search":
                    if (options?.ContainsKey("targetRef") == true)
                    {
                        var findTargetStr = options["targetRef"]?.ToString();
                        if (!string.IsNullOrEmpty(findTargetStr) && FormKey.TryFactory(findTargetStr, out var findTargetFormKey))
                        {
                            builder.AsFind(findTargetFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid find target FormKey: {findTargetStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Find packages require a valid target reference (targetRef)");
                    }
                    break;

                case "ambush":
                    if (options?.ContainsKey("markerRef") == true)
                    {
                        var ambushMarkerStr = options["markerRef"]?.ToString();
                        if (!string.IsNullOrEmpty(ambushMarkerStr) && FormKey.TryFactory(ambushMarkerStr, out var ambushMarkerFormKey))
                        {
                            builder.AsAmbush(ambushMarkerFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid ambush marker FormKey: {ambushMarkerStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Ambush packages require a valid marker reference (markerRef)");
                    }
                    break;

                case "patrol":
                    if (options?.ContainsKey("markerRef") == true)
                    {
                        var patrolMarkerStr = options["markerRef"]?.ToString();
                        if (!string.IsNullOrEmpty(patrolMarkerStr) && FormKey.TryFactory(patrolMarkerStr, out var patrolMarkerFormKey))
                        {
                            builder.AsPatrol(patrolMarkerFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid patrol marker FormKey: {patrolMarkerStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Patrol packages require a valid marker reference (markerRef)");
                    }
                    break;

                case "wander":
                    var wanderRadius = Convert.ToUInt16(options?.GetValueOrDefault("radius", 1000) ?? 1000);
                    if (options?.ContainsKey("markerRef") == true)
                    {
                        var wanderMarkerStr = options["markerRef"]?.ToString();
                        if (!string.IsNullOrEmpty(wanderMarkerStr) && FormKey.TryFactory(wanderMarkerStr, out var wanderMarkerFormKey))
                        {
                            builder.AsWander(wanderMarkerFormKey, wanderRadius);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid wander marker FormKey: {wanderMarkerStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Wander packages require a valid marker reference (markerRef)");
                    }
                    break;

                case "wait":
                    if (options?.ContainsKey("markerRef") == true)
                    {
                        var waitMarkerStr = options["markerRef"]?.ToString();
                        if (!string.IsNullOrEmpty(waitMarkerStr) && FormKey.TryFactory(waitMarkerStr, out var waitMarkerFormKey))
                        {
                            builder.AsWait(waitMarkerFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid wait marker FormKey: {waitMarkerStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Wait packages require a valid marker reference (markerRef)");
                    }
                    break;

                case "relax":
                    if (options?.ContainsKey("markerRef") == true)
                    {
                        var relaxMarkerStr = options["markerRef"]?.ToString();
                        if (!string.IsNullOrEmpty(relaxMarkerStr) && FormKey.TryFactory(relaxMarkerStr, out var relaxMarkerFormKey))
                        {
                            builder.AsRelax(relaxMarkerFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid relax marker FormKey: {relaxMarkerStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Relax packages require a valid marker reference (markerRef)");
                    }
                    break;

                case "forcegreet":
                    if (options?.ContainsKey("targetRef") == true)
                    {
                        var forceGreetTargetStr = options["targetRef"]?.ToString();
                        if (!string.IsNullOrEmpty(forceGreetTargetStr) && FormKey.TryFactory(forceGreetTargetStr, out var forceGreetTargetFormKey))
                        {
                            builder.AsForceGreet(forceGreetTargetFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid force greet target FormKey: {forceGreetTargetStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("ForceGreet packages require a valid target reference (targetRef)");
                    }
                    break;

                case "greet":
                    if (options?.ContainsKey("targetRef") == true)
                    {
                        var greetTargetStr = options["targetRef"]?.ToString();
                        if (!string.IsNullOrEmpty(greetTargetStr) && FormKey.TryFactory(greetTargetStr, out var greetTargetFormKey))
                        {
                            builder.AsGreet(greetTargetFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid greet target FormKey: {greetTargetStr}");
                        }
                    }
                    else
                    {
                        // Greet with no specific target
                        builder.AsGreet();
                    }
                    break;

                case "useweapon":
                    if (options?.ContainsKey("weaponRef") == true)
                    {
                        var weaponRefStr = options["weaponRef"]?.ToString();
                        if (!string.IsNullOrEmpty(weaponRefStr) && FormKey.TryFactory(weaponRefStr, out var weaponFormKey))
                        {
                            FormKey? weaponTarget = null;
                            if (options?.ContainsKey("targetRef") == true)
                            {
                                var targetStr = options["targetRef"]?.ToString();
                                if (!string.IsNullOrEmpty(targetStr) && FormKey.TryFactory(targetStr, out var targetFormKey))
                                {
                                    weaponTarget = targetFormKey;
                                }
                            }
                            builder.AsUseWeapon(weaponFormKey, weaponTarget);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid weapon FormKey: {weaponRefStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("UseWeapon packages require a valid weapon reference (weaponRef)");
                    }
                    break;

                case "usemagic":
                    if (options?.ContainsKey("spellRef") == true)
                    {
                        var spellRefStr = options["spellRef"]?.ToString();
                        if (!string.IsNullOrEmpty(spellRefStr) && FormKey.TryFactory(spellRefStr, out var spellFormKey))
                        {
                            FormKey? magicTarget = null;
                            if (options?.ContainsKey("targetRef") == true)
                            {
                                var targetStr = options["targetRef"]?.ToString();
                                if (!string.IsNullOrEmpty(targetStr) && FormKey.TryFactory(targetStr, out var targetFormKey))
                                {
                                    magicTarget = targetFormKey;
                                }
                            }
                            builder.AsUseMagic(spellFormKey, magicTarget);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid spell FormKey: {spellRefStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("UseMagic packages require a valid spell reference (spellRef)");
                    }
                    break;

                case "lockdoors":
                    if (options?.ContainsKey("doorRef") == true)
                    {
                        var lockDoorStr = options["doorRef"]?.ToString();
                        if (!string.IsNullOrEmpty(lockDoorStr) && FormKey.TryFactory(lockDoorStr, out var lockDoorFormKey))
                        {
                            builder.AsLockDoors(lockDoorFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid door FormKey: {lockDoorStr}");
                        }
                    }
                    else
                    {
                        // Lock all owned doors
                        builder.AsLockDoors();
                    }
                    break;

                case "unlockdoors":
                    if (options?.ContainsKey("doorRef") == true)
                    {
                        var unlockDoorStr = options["doorRef"]?.ToString();
                        if (!string.IsNullOrEmpty(unlockDoorStr) && FormKey.TryFactory(unlockDoorStr, out var unlockDoorFormKey))
                        {
                            builder.AsUnlockDoors(unlockDoorFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid door FormKey: {unlockDoorStr}");
                        }
                    }
                    else
                    {
                        // Unlock all owned doors
                        builder.AsUnlockDoors();
                    }
                    break;

                case "dismount":
                    builder.AsDismount();
                    break;

                case "acquire":
                    if (options?.ContainsKey("objectRef") == true)
                    {
                        var acquireObjStr = options["objectRef"]?.ToString();
                        if (!string.IsNullOrEmpty(acquireObjStr) && FormKey.TryFactory(acquireObjStr, out var acquireObjFormKey))
                        {
                            builder.AsAcquire(acquireObjFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid object FormKey: {acquireObjStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Acquire packages require a valid object reference (objectRef)");
                    }
                    break;

                case "escortto":
                    if (options?.ContainsKey("escortRef") == true && options?.ContainsKey("destinationRef") == true)
                    {
                        var escortRefStr = options["escortRef"]?.ToString();
                        var escortDestStr = options["destinationRef"]?.ToString();

                        if (!string.IsNullOrEmpty(escortRefStr) && FormKey.TryFactory(escortRefStr, out var escortFormKey) &&
                            !string.IsNullOrEmpty(escortDestStr) && FormKey.TryFactory(escortDestStr, out var escortDestFormKey))
                        {
                            builder.AsEscort(escortFormKey, escortDestFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail("Invalid FormKey for escort target or destination");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Escort packages require both escort reference (escortRef) and destination reference (destinationRef)");
                    }
                    break;

                case "say":
                    if (options?.ContainsKey("topicRef") == true)
                    {
                        var topicRefStr = options["topicRef"]?.ToString();
                        if (!string.IsNullOrEmpty(topicRefStr) && FormKey.TryFactory(topicRefStr, out var topicFormKey))
                        {
                            FormKey? sayLocation = null;
                            if (options?.ContainsKey("locationRef") == true)
                            {
                                var locStr = options["locationRef"]?.ToString();
                                if (!string.IsNullOrEmpty(locStr) && FormKey.TryFactory(locStr, out var locFormKey))
                                {
                                    sayLocation = locFormKey;
                                }
                            }
                            builder.AsSay(topicFormKey, sayLocation);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid topic FormKey: {topicRefStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Say packages require a valid topic reference (topicRef)");
                    }
                    break;

                case "shout":
                    if (options?.ContainsKey("shoutRef") == true)
                    {
                        var shoutRefStr = options["shoutRef"]?.ToString();
                        if (!string.IsNullOrEmpty(shoutRefStr) && FormKey.TryFactory(shoutRefStr, out var shoutFormKey))
                        {
                            FormKey? shoutTarget = null;
                            if (options?.ContainsKey("targetRef") == true)
                            {
                                var targetStr = options["targetRef"]?.ToString();
                                if (!string.IsNullOrEmpty(targetStr) && FormKey.TryFactory(targetStr, out var targetFormKey))
                                {
                                    shoutTarget = targetFormKey;
                                }
                            }
                            builder.AsShout(shoutFormKey, shoutTarget);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid shout FormKey: {shoutRefStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Shout packages require a valid shout reference (shoutRef)");
                    }
                    break;

                case "followto":
                    if (options?.ContainsKey("followRef") == true && options?.ContainsKey("destinationRef") == true)
                    {
                        var followRefStr = options["followRef"]?.ToString();
                        var followDestStr = options["destinationRef"]?.ToString();

                        if (!string.IsNullOrEmpty(followRefStr) && FormKey.TryFactory(followRefStr, out var followFormKey) &&
                            !string.IsNullOrEmpty(followDestStr) && FormKey.TryFactory(followDestStr, out var followDestFormKey))
                        {
                            builder.AsFollowTo(followFormKey, followDestFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail("Invalid FormKey for follow target or destination");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("FollowTo packages require both follow reference (followRef) and destination reference (destinationRef)");
                    }
                    break;

                case "holdposition":
                    if (options?.ContainsKey("markerRef") == true)
                    {
                        var holdPosStr = options["markerRef"]?.ToString();
                        if (!string.IsNullOrEmpty(holdPosStr) && FormKey.TryFactory(holdPosStr, out var holdPosFormKey))
                        {
                            builder.AsHoldPosition(holdPosFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid position marker FormKey: {holdPosStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("HoldPosition packages require a valid marker reference (markerRef)");
                    }
                    break;

                case "keepaneyeon":
                    if (options?.ContainsKey("targetRef") == true)
                    {
                        var watchTargetStr = options["targetRef"]?.ToString();
                        if (!string.IsNullOrEmpty(watchTargetStr) && FormKey.TryFactory(watchTargetStr, out var watchTargetFormKey))
                        {
                            builder.AsKeepAnEyeOn(watchTargetFormKey);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid watch target FormKey: {watchTargetStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("KeepAnEyeOn packages require a valid target reference (targetRef)");
                    }
                    break;

                case "hover":
                    var hoverRadius = Convert.ToUInt16(options?.GetValueOrDefault("radius", 1000) ?? 1000);
                    if (options?.ContainsKey("markerRef") == true)
                    {
                        var hoverMarkerStr = options["markerRef"]?.ToString();
                        if (!string.IsNullOrEmpty(hoverMarkerStr) && FormKey.TryFactory(hoverMarkerStr, out var hoverMarkerFormKey))
                        {
                            builder.AsHover(hoverMarkerFormKey, hoverRadius);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid hover marker FormKey: {hoverMarkerStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Hover packages require a valid marker reference (markerRef)");
                    }
                    break;

                case "orbit":
                    var orbitRadius = Convert.ToUInt16(options?.GetValueOrDefault("radius", 500) ?? 500);
                    if (options?.ContainsKey("markerRef") == true)
                    {
                        var orbitMarkerStr = options["markerRef"]?.ToString();
                        if (!string.IsNullOrEmpty(orbitMarkerStr) && FormKey.TryFactory(orbitMarkerStr, out var orbitMarkerFormKey))
                        {
                            builder.AsOrbit(orbitMarkerFormKey, orbitRadius);
                        }
                        else
                        {
                            return Result<string>.Fail($"Invalid orbit marker FormKey: {orbitMarkerStr}");
                        }
                    }
                    else
                    {
                        return Result<string>.Fail("Orbit packages require a valid marker reference (markerRef)");
                    }
                    break;

                default:
                    return Result<string>.Fail(
                        $"Unknown package type: {packageType}",
                        suggestions: new List<string>
                        {
                            "Valid types: sandbox, travel, sleep, eat, follow, guard, patrol, useitemat, activate, sit, useidlemarker, idle, flee, accompany, escort, castmagic, cast, dialogue, find, search, ambush, wander, wait, relax, forcegreet, greet, useweapon, usemagic, lockdoors, unlockdoors, dismount, acquire, escortto, say, shout, followto, holdposition, keepaneyeon, hover, orbit"
                        });
            }

            // Apply common options
            if (options?.ContainsKey("location") == true)
            {
                var locationStr = options["location"].ToString();
                if (!string.IsNullOrEmpty(locationStr) && FormKey.TryFactory(locationStr, out var locationFormKey))
                {
                    builder.WithLocation(locationFormKey);
                }
            }

            var package = builder.Build();
            mod.WriteToBinary(pluginPath);

            _logger.Info($"Added package: {editorId} (FormKey: {package.FormKey})");
            return Result<string>.Ok(package.FormKey.ToString());
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to add package: {ex.Message}", ex.StackTrace);
        }
    }

    /// <summary>
    /// Attach an existing package to an NPC.
    /// </summary>
    /// <summary>
    /// Attach a package to a quest's reference alias. The package will run
    /// for whichever actor fills the alias at runtime. This is the canonical
    /// pattern for dynamic-target Travel packages (alias-target packages
    /// applied to alias-attached actors).
    /// </summary>
    public Result<string> AttachPackageToAlias(
        string pluginPath,
        string questEditorId,
        string aliasName,
        string packageEditorId)
    {
        try
        {
            if (!File.Exists(pluginPath))
            {
                return Result<string>.Fail($"Plugin not found: {pluginPath}");
            }

            var mod = SkyrimMod.CreateFromBinary(
                pluginPath,
                SkyrimRelease.SkyrimSE,
                new Mutagen.Bethesda.Plugins.Binary.Parameters.BinaryReadParameters());

            if (mod.ModHeader.Stats.NextFormID < 0x800)
            {
                mod.ModHeader.Stats.NextFormID = 0x800;
            }

            var quest = mod.Quests.FirstOrDefault(q => q.EditorID == questEditorId);
            if (quest == null)
            {
                return Result<string>.Fail(
                    $"Quest not found: {questEditorId}",
                    suggestions: new List<string>
                    {
                        "Create the quest first with 'esp add-quest'",
                        "Check the quest editor ID spelling"
                    });
            }

            var alias = quest.Aliases.FirstOrDefault(a => a.Name == aliasName);
            if (alias == null)
            {
                return Result<string>.Fail(
                    $"Alias '{aliasName}' not found in quest '{questEditorId}'",
                    suggestions: new List<string>
                    {
                        "Create the alias first with 'esp add-alias'",
                        "Check the alias name spelling"
                    });
            }

            var package = mod.Packages.FirstOrDefault(p => p.EditorID == packageEditorId);
            if (package == null)
            {
                return Result<string>.Fail(
                    $"Package not found: {packageEditorId}",
                    suggestions: new List<string>
                    {
                        "Create the package first with 'esp add-package'",
                        "Check the package editor ID spelling"
                    });
            }

            alias.PackageData.Add(package.ToLink());

            mod.WriteToBinary(pluginPath);

            _logger.Info($"Attached package '{packageEditorId}' to alias '{aliasName}' in quest '{questEditorId}'");
            return Result<string>.Ok($"Package attached to alias");
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to attach package to alias: {ex.Message}", ex.StackTrace);
        }
    }

    public Result<string> AttachPackageToNpc(
        string pluginPath,
        string npcEditorId,
        string packageEditorId)
    {
        try
        {
            if (!File.Exists(pluginPath))
            {
                return Result<string>.Fail($"Plugin not found: {pluginPath}");
            }

            var mod = SkyrimMod.CreateFromBinary(
                pluginPath,
                SkyrimRelease.SkyrimSE,
                new Mutagen.Bethesda.Plugins.Binary.Parameters.BinaryReadParameters());

            // Ensure proper FormID allocation
            if (mod.ModHeader.Stats.NextFormID < 0x800)
            {
                mod.ModHeader.Stats.NextFormID = 0x800;
            }

            // Find NPC
            var npc = mod.Npcs.FirstOrDefault(n => n.EditorID == npcEditorId);
            if (npc == null)
            {
                return Result<string>.Fail(
                    $"NPC not found: {npcEditorId}",
                    suggestions: new List<string>
                    {
                        "Use 'esp info' to list NPCs in the plugin",
                        "Check the NPC editor ID spelling"
                    });
            }

            // Find Package
            var package = mod.Packages.FirstOrDefault(p => p.EditorID == packageEditorId);
            if (package == null)
            {
                return Result<string>.Fail(
                    $"Package not found: {packageEditorId}",
                    suggestions: new List<string>
                    {
                        "Create the package first with 'esp add-package'",
                        "Check the package editor ID spelling"
                    });
            }

            // Attach package to NPC
            npc.Packages.Add(package.ToLink());

            mod.WriteToBinary(pluginPath);

            _logger.Info($"Attached package '{packageEditorId}' to NPC '{npcEditorId}'");
            return Result<string>.Ok($"Package attached successfully");
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to attach package: {ex.Message}", ex.StackTrace);
        }
    }

    /// <summary>
    /// Find a record by FormKey string
    /// </summary>
    internal Result<IMajorRecordGetter> FindRecordByFormKey(ISkyrimModGetter mod, string formKeyStr)
    {
        try
        {
            if (!Mutagen.Bethesda.Plugins.FormKey.TryFactory(formKeyStr, out var formKey))
            {
                return Result<IMajorRecordGetter>.Fail(
                    $"Invalid FormKey format: {formKeyStr}",
                    suggestions: new List<string>
                    {
                        "Use format: 0x000800 or PluginName.esp:0x000800",
                        "Use --editor-id if you know the EditorID instead"
                    });
            }

            foreach (var record in mod.EnumerateMajorRecords())
            {
                if (record.FormKey == formKey)
                {
                    return Result<IMajorRecordGetter>.Ok(record);
                }
            }

            return Result<IMajorRecordGetter>.Fail(
                $"Record with FormKey {formKey} not found in plugin",
                suggestions: new List<string>
                {
                    "Verify the FormKey exists in this plugin",
                    "Use 'esp info' to see record counts"
                });
        }
        catch (Exception ex)
        {
            return Result<IMajorRecordGetter>.Fail($"Error finding record: {ex.Message}", ex.StackTrace);
        }
    }

    /// <summary>
    /// Find a record by EditorID and type
    /// </summary>
    internal Result<IMajorRecordGetter> FindRecordByEditorId(ISkyrimModGetter mod, string editorId, string recordType)
    {
        try
        {
            IMajorRecordGetter? found = null;

            switch (recordType.ToLowerInvariant())
            {
                case "spell":
                    found = mod.Spells.FirstOrDefault(s => s.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                case "weapon":
                    found = mod.Weapons.FirstOrDefault(w => w.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                case "armor":
                    found = mod.Armors.FirstOrDefault(a => a.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                case "quest":
                    found = mod.Quests.FirstOrDefault(q => q.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                case "npc":
                    found = mod.Npcs.FirstOrDefault(n => n.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                case "perk":
                    found = mod.Perks.FirstOrDefault(p => p.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                case "package":
                    found = mod.Packages.FirstOrDefault(p => p.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                case "faction":
                    found = mod.Factions.FirstOrDefault(f => f.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                case "book":
                    found = mod.Books.FirstOrDefault(b => b.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                case "miscitem":
                    found = mod.MiscItems.FirstOrDefault(m => m.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                case "global":
                    found = mod.Globals.FirstOrDefault(g => g.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                case "leveleditem":
                    found = mod.LeveledItems.FirstOrDefault(l => l.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                case "formlist":
                    found = mod.FormLists.FirstOrDefault(f => f.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                case "outfit":
                    found = mod.Outfits.FirstOrDefault(o => o.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                case "location":
                    found = mod.Locations.FirstOrDefault(l => l.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                case "encounterzone":
                    found = mod.EncounterZones.FirstOrDefault(e => e.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
                    break;
                default:
                    return Result<IMajorRecordGetter>.Fail(
                        $"Unsupported record type: {recordType}",
                        suggestions: new List<string>
                        {
                            "Supported types: spell, weapon, armor, quest, npc, perk, faction, book, miscitem, global, leveleditem, formlist, outfit, location, encounterzone",
                            "Use --form-id instead for other record types"
                        });
            }

            if (found == null)
            {
                return Result<IMajorRecordGetter>.Fail(
                    $"{recordType} with EditorID '{editorId}' not found",
                    suggestions: new List<string>
                    {
                        "Check EditorID spelling",
                        "Use 'esp info' to see record counts",
                        "Try --form-id if you know the FormKey"
                    });
            }

            return Result<IMajorRecordGetter>.Ok(found);
        }
        catch (Exception ex)
        {
            return Result<IMajorRecordGetter>.Fail($"Error finding record: {ex.Message}", ex.StackTrace);
        }
    }

    /// <summary>
    /// Extract properties from a record based on its type
    /// </summary>
    private Result<Dictionary<string, object?>> ExtractRecordProperties(IMajorRecordGetter record, bool includeRaw)
    {
        try
        {
            return record switch
            {
                ISpellGetter spell => ExtractSpellProperties(spell),
                IWeaponGetter weapon => ExtractWeaponProperties(weapon),
                IArmorGetter armor => ExtractArmorProperties(armor),
                IQuestGetter quest => ExtractQuestProperties(quest),
                INpcGetter npc => ExtractNpcProperties(npc),
                IPerkGetter perk => ExtractPerkProperties(perk),
                _ => includeRaw ? ExtractPropertiesViaReflection(record) : Result<Dictionary<string, object?>>.Ok(new Dictionary<string, object?>())
            };
        }
        catch (Exception ex)
        {
            return Result<Dictionary<string, object?>>.Fail($"Failed to extract properties: {ex.Message}", ex.StackTrace);
        }
    }

    /// <summary>
    /// Extract spell-specific properties
    /// </summary>
    private Result<Dictionary<string, object?>> ExtractSpellProperties(ISpellGetter spell)
    {
        var props = new Dictionary<string, object?>
        {
            ["Name"] = spell.Name?.String,
            ["Type"] = spell.Type.ToString(),
            ["BaseCost"] = spell.BaseCost,
            ["CastType"] = spell.CastType.ToString(),
            ["TargetType"] = spell.TargetType.ToString(),
            ["CastDuration"] = spell.CastDuration,
            ["Range"] = spell.Range,
            ["EffectCount"] = spell.Effects.Count
        };

        if (spell.EquipmentType != null && !spell.EquipmentType.IsNull)
        {
            props["EquipmentType"] = spell.EquipmentType.FormKey.ToString();
        }

        var effects = new List<Dictionary<string, object?>>();
        foreach (var effect in spell.Effects)
        {
            var effectProps = new Dictionary<string, object?>
            {
                ["BaseEffect"] = effect.BaseEffect.FormKey.ToString(),
                ["Magnitude"] = effect.Data?.Magnitude ?? 0,
                ["Duration"] = effect.Data?.Duration ?? 0,
                ["Area"] = effect.Data?.Area ?? 0
            };
            effects.Add(effectProps);
        }
        props["Effects"] = effects;

        return Result<Dictionary<string, object?>>.Ok(props);
    }

    /// <summary>
    /// Extract weapon-specific properties
    /// </summary>
    private Result<Dictionary<string, object?>> ExtractWeaponProperties(IWeaponGetter weapon)
    {
        var props = new Dictionary<string, object?>
        {
            ["Name"] = weapon.Name?.String,
            ["Damage"] = weapon.BasicStats?.Damage ?? 0,
            ["Weight"] = weapon.BasicStats?.Weight ?? 0,
            ["Value"] = weapon.BasicStats?.Value ?? 0,
            ["CriticalDamage"] = weapon.Critical?.Damage ?? 0,
            ["Speed"] = weapon.Data?.Speed ?? 0,
            ["Reach"] = weapon.Data?.Reach ?? 0,
            ["AnimationType"] = weapon.Data?.AnimationType.ToString()
        };

        if (weapon.Keywords != null)
        {
            props["Keywords"] = weapon.Keywords.Select(k => k.FormKey.ToString()).ToList();
        }

        if (weapon.Template != null && !weapon.Template.IsNull)
        {
            props["Template"] = weapon.Template.FormKey.ToString();
        }

        return Result<Dictionary<string, object?>>.Ok(props);
    }

    /// <summary>
    /// Extract armor-specific properties
    /// </summary>
    private Result<Dictionary<string, object?>> ExtractArmorProperties(IArmorGetter armor)
    {
        var props = new Dictionary<string, object?>
        {
            ["Name"] = armor.Name?.String,
            ["ArmorRating"] = armor.ArmorRating,
            ["Weight"] = armor.Weight,
            ["Value"] = armor.Value,
            ["BodyTemplate"] = armor.BodyTemplate?.ToString()
        };

        if (armor.Keywords != null)
        {
            props["Keywords"] = armor.Keywords.Select(k => k.FormKey.ToString()).ToList();
        }

        return Result<Dictionary<string, object?>>.Ok(props);
    }

    /// <summary>
    /// Extract quest-specific properties
    /// </summary>
    private Result<Dictionary<string, object?>> ExtractQuestProperties(IQuestGetter quest)
    {
        var props = new Dictionary<string, object?>
        {
            ["Name"] = quest.Name?.String,
            ["Priority"] = quest.Priority,
            ["Flags"] = quest.Flags.ToString(),
            ["StageCount"] = quest.Stages.Count,
            ["AliasCount"] = quest.Aliases.Count
        };

        // TODO: Add Event property once API is confirmed
        // if (quest.Event != null && !quest.Event.IsNull)
        // {
        //     props["Event"] = quest.Event.FormKey.ToString();
        // }

        return Result<Dictionary<string, object?>>.Ok(props);
    }

    /// <summary>
    /// Extract NPC-specific properties
    /// </summary>
    private Result<Dictionary<string, object?>> ExtractNpcProperties(INpcGetter npc)
    {
        var props = new Dictionary<string, object?>
        {
            ["Name"] = npc.Name?.String,
            ["Race"] = npc.Race.FormKey.ToString(),
            ["Level"] = npc.Configuration?.Level?.ToString(),
            ["Health"] = npc.Configuration?.HealthOffset ?? 0,
            ["Magicka"] = npc.Configuration?.MagickaOffset ?? 0,
            ["Stamina"] = npc.Configuration?.StaminaOffset ?? 0
        };

        if (npc.Class != null && !npc.Class.IsNull)
        {
            props["Class"] = npc.Class.FormKey.ToString();
        }

        if (npc.Keywords != null)
        {
            props["Keywords"] = npc.Keywords.Select(k => k.FormKey.ToString()).ToList();
        }

        return Result<Dictionary<string, object?>>.Ok(props);
    }

    /// <summary>
    /// Extract perk-specific properties
    /// </summary>
    private Result<Dictionary<string, object?>> ExtractPerkProperties(IPerkGetter perk)
    {
        var props = new Dictionary<string, object?>
        {
            ["Name"] = perk.Name?.String,
            ["Description"] = perk.Description?.String,
            ["EffectCount"] = perk.Effects.Count
        };

        return Result<Dictionary<string, object?>>.Ok(props);
    }

    /// <summary>
    /// Extract properties via reflection (fallback for unknown types)
    /// </summary>
    private Result<Dictionary<string, object?>> ExtractPropertiesViaReflection(IMajorRecordGetter record)
    {
        var props = new Dictionary<string, object?>();
        var type = record.GetType();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            try
            {
                if (prop.Name is "EditorID" or "FormKey")
                    continue;

                var value = prop.GetValue(record);
                if (value != null)
                {
                    props[prop.Name] = value.ToString();
                }
            }
            catch
            {
                // Skip properties that throw exceptions
            }
        }

        return Result<Dictionary<string, object?>>.Ok(props);
    }

    /// <summary>
    /// Extract conditions from a record
    /// Verified to work with: Perk, Package, IdleAnimation, MagicEffect
    /// </summary>
    private Result<List<ConditionInfo>> ExtractConditions(IMajorRecordGetter record)
    {
        var conditions = new List<ConditionInfo>();

        try
        {
            IReadOnlyList<IConditionGetter>? conditionList = null;

            switch (record)
            {
                case IPerkGetter perk:
                    conditionList = perk.Conditions;
                    break;
                case IPackageGetter package:
                    conditionList = package.Conditions;
                    break;
                case IIdleAnimationGetter idle:
                    conditionList = idle.Conditions;
                    break;
                // Note: MagicEffect conditions exist but are more complex
                // Spells, Weapons, Armor do NOT have direct Conditions properties
            }

            if (conditionList == null || conditionList.Count == 0)
            {
                return Result<List<ConditionInfo>>.Ok(conditions);
            }

            foreach (var condition in conditionList)
            {
                if (condition.Data == null)
                    continue;

                var functionName = condition.Data.GetType().Name.Replace("ConditionData", "");

                var condInfo = new ConditionInfo
                {
                    FunctionName = functionName,
                    Operator = condition.CompareOperator.ToString()
                };

                // Handle ConditionFloat (most common) - use getter interface for binary overlay compatibility
                if (condition is IConditionFloatGetter condFloat)
                {
                    condInfo.ComparisonValue = condFloat.ComparisonValue;
                }
                // Handle ConditionGlobal (uses global variable)
                else if (condition is IConditionGlobalGetter condGlobal)
                {
                    condInfo.ComparisonValue = 0; // Global comparison
                    if (condGlobal.ComparisonValue.FormKey != null)
                    {
                        condInfo.ParameterA = condGlobal.ComparisonValue.FormKey.ToString();
                    }
                }

                // Extract flags and run-on type (common to all conditions)
                condInfo.Flags = condition.Flags.ToString();

                // RunOnType varies by ConditionData subclass - try to get it via reflection
                var runOnProp = condition.Data.GetType().GetProperty("RunOnType");
                if (runOnProp != null)
                {
                    var runOnValue = runOnProp.GetValue(condition.Data);
                    condInfo.RunOn = runOnValue?.ToString() ?? "Unknown";
                }

                conditions.Add(condInfo);
            }

            return Result<List<ConditionInfo>>.Ok(conditions);
        }
        catch (Exception ex)
        {
            return Result<List<ConditionInfo>>.Fail($"Failed to extract conditions: {ex.Message}", ex.StackTrace);
        }
    }

    /// <summary>
    /// Create an override patch for a record
    /// </summary>
    public Result<string> CreateOverride(
        string sourcePluginPath,
        string outputPluginName,
        string? editorId,
        string? formId,
        string? recordType,
        bool removeConditions = false,
        string? dataFolder = null)
    {
        try
        {
            if (!File.Exists(sourcePluginPath))
            {
                return Result<string>.Fail(
                    $"Source plugin not found: {sourcePluginPath}",
                    suggestions: new List<string>
                    {
                        "Check the file path is correct",
                        "Use an absolute path if relative path fails"
                    });
            }

            var sourceMod = SkyrimMod.CreateFromBinaryOverlay(sourcePluginPath, SkyrimRelease.SkyrimSE);

            IMajorRecordGetter? sourceRecord = null;

            if (!string.IsNullOrEmpty(formId))
            {
                var findResult = FindRecordByFormKey(sourceMod, formId);
                if (!findResult.Success)
                {
                    return Result<string>.Fail(findResult.Error ?? "Record not found", findResult.ErrorContext);
                }
                sourceRecord = findResult.Value;
            }
            else if (!string.IsNullOrEmpty(editorId) && !string.IsNullOrEmpty(recordType))
            {
                var findResult = FindRecordByEditorId(sourceMod, editorId, recordType);
                if (!findResult.Success)
                {
                    return Result<string>.Fail(findResult.Error ?? "Record not found", findResult.ErrorContext);
                }
                sourceRecord = findResult.Value;
            }
            else
            {
                return Result<string>.Fail(
                    "Must provide either FormID or both EditorID and RecordType",
                    suggestions: new List<string>
                    {
                        "Use --form-id for FormKey-based lookup",
                        "Use --editor-id and --type for EditorID-based lookup"
                    });
            }

            if (sourceRecord == null)
            {
                return Result<string>.Fail(
                    $"Record not found: {editorId ?? formId}",
                    suggestions: new List<string>
                    {
                        "Use 'esp info' to see available records",
                        "Check spelling of EditorID",
                        "Verify FormID is correct"
                    });
            }

            if (!outputPluginName.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) &&
                !outputPluginName.EndsWith(".esl", StringComparison.OrdinalIgnoreCase))
            {
                outputPluginName += ".esp";
            }

            var outputModKey = ModKey.FromFileName(outputPluginName);
            var patchMod = new SkyrimMod(outputModKey, SkyrimRelease.SkyrimSE);

            patchMod.ModHeader.MasterReferences.Add(new MasterReference
            {
                Master = sourceMod.ModKey
            });

            var overrideResult = CreateOverrideRecord(patchMod, sourceRecord, removeConditions);
            if (!overrideResult.Success)
            {
                return Result<string>.Fail(overrideResult.Error ?? "Failed to create override", overrideResult.ErrorContext);
            }

            var outputDir = !string.IsNullOrEmpty(dataFolder)
                ? dataFolder
                : Path.GetDirectoryName(sourcePluginPath) ?? Directory.GetCurrentDirectory();

            var outputPath = Path.Combine(outputDir, outputPluginName);

            patchMod.WriteToBinary(outputPath);

            _logger.Info($"Created override patch: {outputPath}");
            return Result<string>.Ok(outputPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail(
                $"Failed to create override: {ex.Message}",
                ex.StackTrace);
        }
    }

    /// <summary>
    /// Create an override record with optional modifications
    /// Overrides keep the same FormKey as the source record
    /// </summary>
    private Result<IMajorRecord> CreateOverrideRecord(
        SkyrimMod targetMod,
        IMajorRecordGetter sourceRecord,
        bool removeConditions)
    {
        try
        {
            switch (sourceRecord)
            {
                case ISpellGetter spell:
                    var spellOverride = (Spell)spell.DeepCopy();
                    // TODO: Add condition removal once Mutagen API is confirmed
                    // if (removeConditions && spellOverride.Conditions != null)
                    // {
                    //     spellOverride.Conditions.Clear();
                    // }
                    targetMod.Spells.Add(spellOverride);
                    return Result<IMajorRecord>.Ok(spellOverride);

                case IWeaponGetter weapon:
                    var weaponOverride = (Weapon)weapon.DeepCopy();
                    // TODO: Add condition removal once Mutagen API is confirmed
                    // if (removeConditions && weaponOverride.Conditions != null)
                    // {
                    //     weaponOverride.Conditions.Clear();
                    // }
                    targetMod.Weapons.Add(weaponOverride);
                    return Result<IMajorRecord>.Ok(weaponOverride);

                case IArmorGetter armor:
                    var armorOverride = (Armor)armor.DeepCopy();
                    // TODO: Add condition removal once Mutagen API is confirmed
                    // if (removeConditions && armorOverride.Conditions != null)
                    // {
                    //     armorOverride.Conditions.Clear();
                    // }
                    targetMod.Armors.Add(armorOverride);
                    return Result<IMajorRecord>.Ok(armorOverride);

                case IQuestGetter quest:
                    var questOverride = (Quest)quest.DeepCopy();
                    targetMod.Quests.Add(questOverride);
                    return Result<IMajorRecord>.Ok(questOverride);

                case INpcGetter npc:
                    var npcOverride = (Npc)npc.DeepCopy();
                    targetMod.Npcs.Add(npcOverride);
                    return Result<IMajorRecord>.Ok(npcOverride);

                case IPerkGetter perk:
                    var perkOverride = (Perk)perk.DeepCopy();
                    if (removeConditions && perkOverride.Conditions != null)
                    {
                        perkOverride.Conditions.Clear();
                    }
                    targetMod.Perks.Add(perkOverride);
                    return Result<IMajorRecord>.Ok(perkOverride);

                case IFactionGetter faction:
                    var factionOverride = (Faction)faction.DeepCopy();
                    targetMod.Factions.Add(factionOverride);
                    return Result<IMajorRecord>.Ok(factionOverride);

                case IBookGetter book:
                    var bookOverride = (Book)book.DeepCopy();
                    targetMod.Books.Add(bookOverride);
                    return Result<IMajorRecord>.Ok(bookOverride);

                case IMiscItemGetter miscItem:
                    var miscOverride = (MiscItem)miscItem.DeepCopy();
                    targetMod.MiscItems.Add(miscOverride);
                    return Result<IMajorRecord>.Ok(miscOverride);

                case IGlobalGetter global:
                    var globalOverride = (Global)global.DeepCopy();
                    targetMod.Globals.Add(globalOverride);
                    return Result<IMajorRecord>.Ok(globalOverride);

                case ILeveledItemGetter leveledItem:
                    var leveledItemOverride = (LeveledItem)leveledItem.DeepCopy();
                    targetMod.LeveledItems.Add(leveledItemOverride);
                    return Result<IMajorRecord>.Ok(leveledItemOverride);

                case IFormListGetter formList:
                    var formListOverride = (FormList)formList.DeepCopy();
                    targetMod.FormLists.Add(formListOverride);
                    return Result<IMajorRecord>.Ok(formListOverride);

                case IOutfitGetter outfit:
                    var outfitOverride = (Outfit)outfit.DeepCopy();
                    targetMod.Outfits.Add(outfitOverride);
                    return Result<IMajorRecord>.Ok(outfitOverride);

                case ILocationGetter location:
                    var locationOverride = (Location)location.DeepCopy();
                    targetMod.Locations.Add(locationOverride);
                    return Result<IMajorRecord>.Ok(locationOverride);

                case IEncounterZoneGetter encounterZone:
                    var encounterZoneOverride = (EncounterZone)encounterZone.DeepCopy();
                    targetMod.EncounterZones.Add(encounterZoneOverride);
                    return Result<IMajorRecord>.Ok(encounterZoneOverride);

                default:
                    return Result<IMajorRecord>.Fail(
                        $"Unsupported record type for override: {sourceRecord.GetType().Name}",
                        suggestions: new List<string>
                        {
                            "Supported types: Spell, Weapon, Armor, Quest, NPC, Perk, Faction, Book, MiscItem, Global, LeveledItem, FormList, Outfit, Location, EncounterZone"
                        });
            }
        }
        catch (Exception ex)
        {
            return Result<IMajorRecord>.Fail($"Failed to create override record: {ex.Message}", ex.StackTrace);
        }
    }

    /// <summary>
    /// Find records across plugins by search pattern
    /// </summary>
    public Result<List<RecordSearchResult>> FindRecords(
        string? searchPattern,
        string? editorId,
        string? recordType,
        string? pluginPath,
        string? dataFolder,
        bool allPlugins = false)
    {
        try
        {
            var results = new List<RecordSearchResult>();
            var pluginsToSearch = new List<string>();

            if (allPlugins && !string.IsNullOrEmpty(dataFolder))
            {
                if (!Directory.Exists(dataFolder))
                {
                    return Result<List<RecordSearchResult>>.Fail(
                        $"Data folder not found: {dataFolder}",
                        suggestions: new List<string>
                        {
                            "Check the data folder path is correct",
                            "Use an absolute path"
                        });
                }

                pluginsToSearch.AddRange(Directory.GetFiles(dataFolder, "*.esp"));
                pluginsToSearch.AddRange(Directory.GetFiles(dataFolder, "*.esm"));
                pluginsToSearch.AddRange(Directory.GetFiles(dataFolder, "*.esl"));
            }
            else if (!string.IsNullOrEmpty(pluginPath))
            {
                if (!File.Exists(pluginPath))
                {
                    return Result<List<RecordSearchResult>>.Fail($"Plugin not found: {pluginPath}");
                }
                pluginsToSearch.Add(pluginPath);
            }
            else
            {
                return Result<List<RecordSearchResult>>.Fail(
                    "Must provide either --plugin or --data-folder with --all-plugins",
                    suggestions: new List<string>
                    {
                        "Use --plugin to search a specific plugin",
                        "Use --data-folder --all-plugins to search all plugins"
                    });
            }

            foreach (var plugin in pluginsToSearch)
            {
                try
                {
                    var mod = SkyrimMod.CreateFromBinaryOverlay(plugin, SkyrimRelease.SkyrimSE);
                    var pluginName = Path.GetFileName(plugin);

                    IEnumerable<IMajorRecordGetter> recordsToSearch = mod.EnumerateMajorRecords();

                    if (!string.IsNullOrEmpty(recordType))
                    {
                        recordsToSearch = FilterByRecordType(mod, recordType);
                    }

                    foreach (var record in recordsToSearch)
                    {
                        bool matches = false;

                        if (!string.IsNullOrEmpty(editorId))
                        {
                            matches = record.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true;
                        }
                        else if (!string.IsNullOrEmpty(searchPattern))
                        {
                            // Handle wildcard pattern
                            if (searchPattern == "*")
                            {
                                matches = true; // Match all records when wildcard is used
                            }
                            else
                            {
                                matches = (record.EditorID?.Contains(searchPattern, StringComparison.OrdinalIgnoreCase) == true) ||
                                         (GetRecordName(record)?.Contains(searchPattern, StringComparison.OrdinalIgnoreCase) == true);
                            }
                        }

                        if (matches)
                        {
                            results.Add(new RecordSearchResult
                            {
                                PluginName = pluginName,
                                EditorId = record.EditorID ?? string.Empty,
                                FormKey = record.FormKey.ToString(),
                                RecordType = record.GetType().Name.Replace("Getter", "").Replace("ReadOnly", ""),
                                Name = GetRecordName(record)
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Skipping plugin {plugin}: {ex.Message}");
                    continue;
                }
            }

            return Result<List<RecordSearchResult>>.Ok(results);
        }
        catch (Exception ex)
        {
            return Result<List<RecordSearchResult>>.Fail(
                $"Failed to search records: {ex.Message}",
                ex.StackTrace);
        }
    }

    /// <summary>
    /// Batch create overrides for multiple records
    /// </summary>
    public Result<BatchOverrideResult> BatchOverride(
        string sourcePluginPath,
        string? recordType,
        string? searchPattern,
        string[]? editorIds,
        string outputPluginName,
        string? dataFolder = null)
    {
        try
        {
            if (!File.Exists(sourcePluginPath))
            {
                return Result<BatchOverrideResult>.Fail($"Source plugin not found: {sourcePluginPath}");
            }

            var sourceMod = SkyrimMod.CreateFromBinaryOverlay(sourcePluginPath, SkyrimRelease.SkyrimSE);

            if (!outputPluginName.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) &&
                !outputPluginName.EndsWith(".esl", StringComparison.OrdinalIgnoreCase))
            {
                outputPluginName += ".esp";
            }

            var outputModKey = ModKey.FromFileName(outputPluginName);
            var patchMod = new SkyrimMod(outputModKey, SkyrimRelease.SkyrimSE);

            patchMod.ModHeader.MasterReferences.Add(new MasterReference
            {
                Master = sourceMod.ModKey
            });

            var recordsToOverride = new List<IMajorRecordGetter>();

            if (editorIds != null && editorIds.Length > 0)
            {
                foreach (var editorId in editorIds)
                {
                    var findResult = FindRecordByEditorId(sourceMod, editorId, recordType ?? "");
                    if (findResult.Success && findResult.Value != null)
                    {
                        recordsToOverride.Add(findResult.Value);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(searchPattern))
            {
                IEnumerable<IMajorRecordGetter> allRecords = string.IsNullOrEmpty(recordType)
                    ? sourceMod.EnumerateMajorRecords()
                    : FilterByRecordType(sourceMod, recordType);

                foreach (var record in allRecords)
                {
                    if (record.EditorID?.Contains(searchPattern, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        recordsToOverride.Add(record);
                    }
                }
            }

            var modifiedRecords = new List<string>();
            foreach (var record in recordsToOverride)
            {
                var overrideResult = CreateOverrideRecord(patchMod, record, false);
                if (overrideResult.Success)
                {
                    modifiedRecords.Add(record.EditorID ?? record.FormKey.ToString());
                }
            }

            var outputDir = !string.IsNullOrEmpty(dataFolder)
                ? dataFolder
                : Path.GetDirectoryName(sourcePluginPath) ?? Directory.GetCurrentDirectory();

            var outputPath = Path.Combine(outputDir, outputPluginName);

            patchMod.WriteToBinary(outputPath);

            _logger.Info($"Created batch override patch: {outputPath}");

            return Result<BatchOverrideResult>.Ok(new BatchOverrideResult
            {
                RecordsModified = modifiedRecords.Count,
                ModifiedRecords = modifiedRecords,
                PatchPath = outputPath
            });
        }
        catch (Exception ex)
        {
            return Result<BatchOverrideResult>.Fail(
                $"Failed to create batch override: {ex.Message}",
                ex.StackTrace);
        }
    }

    /// <summary>
    /// Filter records by type
    /// </summary>
    private IEnumerable<IMajorRecordGetter> FilterByRecordType(ISkyrimModGetter mod, string recordType)
    {
        return recordType.ToLowerInvariant() switch
        {
            "spell" => mod.Spells,
            "weapon" => mod.Weapons,
            "armor" => mod.Armors,
            "quest" => mod.Quests,
            "npc" => mod.Npcs,
            "perk" => mod.Perks,
            "faction" => mod.Factions,
            "book" => mod.Books,
            "miscitem" => mod.MiscItems,
            "global" => mod.Globals,
            "leveleditem" => mod.LeveledItems,
            "formlist" => mod.FormLists,
            "outfit" => mod.Outfits,
            "location" => mod.Locations,
            "encounterzone" => mod.EncounterZones,
            _ => mod.EnumerateMajorRecords()
        };
    }

    /// <summary>
    /// Get the display name of a record
    /// </summary>
    private string? GetRecordName(IMajorRecordGetter record)
    {
        return record switch
        {
            ISpellGetter spell => spell.Name?.String,
            IWeaponGetter weapon => weapon.Name?.String,
            IArmorGetter armor => armor.Name?.String,
            IQuestGetter quest => quest.Name?.String,
            INpcGetter npc => npc.Name?.String,
            IPerkGetter perk => perk.Name?.String,
            IBookGetter book => book.Name?.String,
            _ => null
        };
    }

    /// <summary>
    /// Compare two versions of the same record
    /// </summary>
    public Result<RecordComparison> CompareRecords(
        string plugin1Path,
        string plugin2Path,
        string? editorId,
        string? formId,
        string? recordType)
    {
        try
        {
            if (!File.Exists(plugin1Path))
            {
                return Result<RecordComparison>.Fail($"Plugin 1 not found: {plugin1Path}");
            }

            if (!File.Exists(plugin2Path))
            {
                return Result<RecordComparison>.Fail($"Plugin 2 not found: {plugin2Path}");
            }

            var mod1 = SkyrimMod.CreateFromBinaryOverlay(plugin1Path, SkyrimRelease.SkyrimSE);
            var mod2 = SkyrimMod.CreateFromBinaryOverlay(plugin2Path, SkyrimRelease.SkyrimSE);

            IMajorRecordGetter? record1 = null;
            IMajorRecordGetter? record2 = null;

            if (!string.IsNullOrEmpty(formId))
            {
                var findResult1 = FindRecordByFormKey(mod1, formId);
                var findResult2 = FindRecordByFormKey(mod2, formId);

                if (!findResult1.Success || !findResult2.Success)
                {
                    return Result<RecordComparison>.Fail(
                        "Record not found in one or both plugins",
                        suggestions: new List<string>
                        {
                            "Verify the FormKey exists in both plugins",
                            "Use --editor-id if the record has different FormKeys"
                        });
                }

                record1 = findResult1.Value;
                record2 = findResult2.Value;
            }
            else if (!string.IsNullOrEmpty(editorId) && !string.IsNullOrEmpty(recordType))
            {
                var findResult1 = FindRecordByEditorId(mod1, editorId, recordType);
                var findResult2 = FindRecordByEditorId(mod2, editorId, recordType);

                if (!findResult1.Success || !findResult2.Success)
                {
                    return Result<RecordComparison>.Fail(
                        "Record not found in one or both plugins",
                        suggestions: new List<string>
                        {
                            "Verify the EditorID exists in both plugins",
                            "Check the record type is correct"
                        });
                }

                record1 = findResult1.Value;
                record2 = findResult2.Value;
            }
            else
            {
                return Result<RecordComparison>.Fail(
                    "Must provide either FormID or both EditorID and RecordType");
            }

            if (record1 == null || record2 == null)
            {
                return Result<RecordComparison>.Fail("Failed to load records for comparison");
            }

            var info1Result = ExtractRecordInfo(record1, false);
            var info2Result = ExtractRecordInfo(record2, false);

            if (!info1Result.Success || !info2Result.Success)
            {
                return Result<RecordComparison>.Fail("Failed to extract record properties");
            }

            var info1 = info1Result.Value!;
            var info2 = info2Result.Value!;

            var differences = new Dictionary<string, FieldDifference>();

            var allKeys = info1.Properties.Keys.Union(info2.Properties.Keys).ToHashSet();

            foreach (var key in allKeys)
            {
                var value1 = info1.Properties.GetValueOrDefault(key);
                var value2 = info2.Properties.GetValueOrDefault(key);

                var value1Str = value1?.ToString() ?? "";
                var value2Str = value2?.ToString() ?? "";

                if (value1Str != value2Str)
                {
                    differences[key] = new FieldDifference
                    {
                        Field = key,
                        OriginalValue = value1,
                        ModifiedValue = value2
                    };
                }
            }

            return Result<RecordComparison>.Ok(new RecordComparison
            {
                Original = info1,
                Modified = info2,
                Differences = differences
            });
        }
        catch (Exception ex)
        {
            return Result<RecordComparison>.Fail(
                $"Failed to compare records: {ex.Message}",
                ex.StackTrace);
        }
    }

    /// <summary>
    /// Detect load order conflicts for a record or plugin
    /// </summary>
    public Result<ConflictReport> DetectConflicts(
        string? pluginPath,
        string? editorId,
        string? formId,
        string? recordType,
        string dataFolder)
    {
        try
        {
            if (!Directory.Exists(dataFolder))
            {
                return Result<ConflictReport>.Fail($"Data folder not found: {dataFolder}");
            }

            if (string.IsNullOrEmpty(pluginPath) && string.IsNullOrEmpty(formId) && string.IsNullOrEmpty(editorId))
            {
                return Result<ConflictReport>.Fail(
                    "Must provide either --plugin, --form-id, or --editor-id");
            }

            var allPlugins = new List<string>();
            allPlugins.AddRange(Directory.GetFiles(dataFolder, "*.esm").OrderBy(f => f));
            allPlugins.AddRange(Directory.GetFiles(dataFolder, "*.esp").OrderBy(f => f));
            allPlugins.AddRange(Directory.GetFiles(dataFolder, "*.esl").OrderBy(f => f));

            Mutagen.Bethesda.Plugins.FormKey? targetFormKey = null;
            string targetEditorId = editorId ?? "";

            if (!string.IsNullOrEmpty(formId))
            {
                if (!Mutagen.Bethesda.Plugins.FormKey.TryFactory(formId, out var parsedFormKey))
                {
                    return Result<ConflictReport>.Fail($"Invalid FormID format: {formId}");
                }
                targetFormKey = parsedFormKey;
            }

            var conflicts = new List<ConflictingPlugin>();
            int loadOrder = 0;

            foreach (var plugin in allPlugins)
            {
                try
                {
                    var mod = SkyrimMod.CreateFromBinaryOverlay(plugin, SkyrimRelease.SkyrimSE);

                    bool hasRecord = false;

                    if (targetFormKey.HasValue)
                    {
                        hasRecord = mod.EnumerateMajorRecords().Any(r => r.FormKey == targetFormKey.Value);
                    }
                    else if (!string.IsNullOrEmpty(targetEditorId) && !string.IsNullOrEmpty(recordType))
                    {
                        var findResult = FindRecordByEditorId(mod, targetEditorId, recordType);
                        hasRecord = findResult.Success;

                        if (hasRecord && !targetFormKey.HasValue && findResult.Value != null)
                        {
                            targetFormKey = findResult.Value.FormKey;
                            targetEditorId = findResult.Value.EditorID ?? targetEditorId;
                        }
                    }
                    else if (!string.IsNullOrEmpty(pluginPath))
                    {
                        hasRecord = Path.GetFileName(plugin).Equals(
                            Path.GetFileName(pluginPath),
                            StringComparison.OrdinalIgnoreCase);
                    }

                    if (hasRecord)
                    {
                        conflicts.Add(new ConflictingPlugin
                        {
                            PluginName = Path.GetFileName(plugin),
                            LoadOrder = loadOrder,
                            IsWinner = false
                        });
                    }

                    loadOrder++;
                }
                catch
                {
                    loadOrder++;
                    continue;
                }
            }

            if (conflicts.Count == 0)
            {
                return Result<ConflictReport>.Fail(
                    "No conflicts found",
                    suggestions: new List<string>
                    {
                        "Record may not exist in any loaded plugins",
                        "Verify the FormKey or EditorID is correct"
                    });
            }

            conflicts.Last().IsWinner = true;

            return Result<ConflictReport>.Ok(new ConflictReport
            {
                FormKey = targetFormKey?.ToString() ?? "",
                EditorId = targetEditorId,
                Conflicts = conflicts,
                WinningPlugin = conflicts.Last().PluginName
            });
        }
        catch (Exception ex)
        {
            return Result<ConflictReport>.Fail(
                $"Failed to detect conflicts: {ex.Message}",
                ex.StackTrace);
        }
    }

    /// <summary>
    /// Extract RecordInfo from a record (helper for compare)
    /// </summary>
    private Result<RecordInfo> ExtractRecordInfo(IMajorRecordGetter record, bool includeRaw)
    {
        var recordInfo = new RecordInfo
        {
            EditorId = record.EditorID ?? string.Empty,
            FormKey = record.FormKey.ToString(),
            RecordType = record.GetType().Name.Replace("Getter", "").Replace("ReadOnly", "")
        };

        var propsResult = ExtractRecordProperties(record, includeRaw);
        if (!propsResult.Success)
        {
            return Result<RecordInfo>.Fail(propsResult.Error ?? "Failed to extract properties");
        }

        recordInfo.Properties = propsResult.Value ?? new Dictionary<string, object?>();

        return Result<RecordInfo>.Ok(recordInfo);
    }

    /// <summary>
    /// List all conditions on a record
    /// </summary>
    public Result<List<ConditionInfo>> ListConditions(
        string pluginPath,
        string? editorId,
        string? formId,
        string? recordType)
    {
        try
        {
            if (!File.Exists(pluginPath))
            {
                return Result<List<ConditionInfo>>.Fail($"Plugin not found: {pluginPath}");
            }

            var mod = SkyrimMod.CreateFromBinaryOverlay(pluginPath, SkyrimRelease.SkyrimSE);

            IMajorRecordGetter? record = null;

            if (!string.IsNullOrEmpty(formId))
            {
                var findResult = FindRecordByFormKey(mod, formId);
                if (!findResult.Success || findResult.Value == null)
                {
                    return Result<List<ConditionInfo>>.Fail("Record not found");
                }
                record = findResult.Value;
            }
            else if (!string.IsNullOrEmpty(editorId) && !string.IsNullOrEmpty(recordType))
            {
                var findResult = FindRecordByEditorId(mod, editorId, recordType);
                if (!findResult.Success || findResult.Value == null)
                {
                    return Result<List<ConditionInfo>>.Fail("Record not found");
                }
                record = findResult.Value;
            }
            else
            {
                return Result<List<ConditionInfo>>.Fail("Must provide either FormID or both EditorID and RecordType");
            }

            return ExtractConditions(record);
        }
        catch (Exception ex)
        {
            return Result<List<ConditionInfo>>.Fail($"Failed to list conditions: {ex.Message}", ex.StackTrace);
        }
    }

    /// <summary>
    /// Remove specific conditions from a record by index
    /// </summary>
    public Result<string> RemoveConditions(
        string sourcePluginPath,
        string? editorId,
        string? formId,
        string? recordType,
        int[] conditionIndices,
        string outputPluginName,
        string? dataFolder = null)
    {
        try
        {
            if (!File.Exists(sourcePluginPath))
            {
                return Result<string>.Fail($"Source plugin not found: {sourcePluginPath}");
            }

            var sourceMod = SkyrimMod.CreateFromBinaryOverlay(sourcePluginPath, SkyrimRelease.SkyrimSE);

            IMajorRecordGetter? sourceRecord = null;

            if (!string.IsNullOrEmpty(formId))
            {
                var findResult = FindRecordByFormKey(sourceMod, formId);
                if (!findResult.Success || findResult.Value == null)
                {
                    return Result<string>.Fail("Record not found");
                }
                sourceRecord = findResult.Value;
            }
            else if (!string.IsNullOrEmpty(editorId) && !string.IsNullOrEmpty(recordType))
            {
                var findResult = FindRecordByEditorId(sourceMod, editorId, recordType);
                if (!findResult.Success || findResult.Value == null)
                {
                    return Result<string>.Fail("Record not found");
                }
                sourceRecord = findResult.Value;
            }
            else
            {
                return Result<string>.Fail("Must provide either FormID or both EditorID and RecordType");
            }

            if (!outputPluginName.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) &&
                !outputPluginName.EndsWith(".esl", StringComparison.OrdinalIgnoreCase))
            {
                outputPluginName += ".esp";
            }

            var outputModKey = ModKey.FromFileName(outputPluginName);
            var patchMod = new SkyrimMod(outputModKey, SkyrimRelease.SkyrimSE);

            patchMod.ModHeader.MasterReferences.Add(new MasterReference
            {
                Master = sourceMod.ModKey
            });

            // Only Perks are currently supported for condition manipulation
            if (sourceRecord is not IPerkGetter)
            {
                return Result<string>.Fail(
                    "Condition manipulation currently only supported for Perk records",
                    suggestions: new List<string>
                    {
                        "Use --type perk",
                        "Other record types (Package, IdleAnimation) coming soon"
                    });
            }

            var perkSource = (IPerkGetter)sourceRecord;
            var perkOverride = (Perk)perkSource.DeepCopy();

            if (perkOverride.Conditions == null || perkOverride.Conditions.Count == 0)
            {
                return Result<string>.Fail("Record has no conditions to remove");
            }

            // Sort indices in descending order to avoid index shifting issues
            var sortedIndices = conditionIndices.OrderByDescending(i => i).ToArray();

            foreach (var index in sortedIndices)
            {
                if (index < 0 || index >= perkOverride.Conditions.Count)
                {
                    return Result<string>.Fail($"Invalid condition index: {index}. Record has {perkOverride.Conditions.Count} conditions.");
                }
                perkOverride.Conditions.RemoveAt(index);
            }

            patchMod.Perks.Add(perkOverride);

            var outputDir = !string.IsNullOrEmpty(dataFolder)
                ? dataFolder
                : Path.GetDirectoryName(sourcePluginPath) ?? Directory.GetCurrentDirectory();

            var outputPath = Path.Combine(outputDir, outputPluginName);

            patchMod.WriteToBinary(outputPath);

            _logger.Info($"Created patch with {conditionIndices.Length} condition(s) removed: {outputPath}");
            return Result<string>.Ok(outputPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to remove conditions: {ex.Message}", ex.StackTrace);
        }
    }

    /// <summary>
    /// Add a condition to a record
    /// </summary>
    /// <summary>
    /// Add a CTDA condition to a record's Conditions list. Modifies the
    /// source plugin in-place (no patch ESP). Supports any record type
    /// whose Mutagen class exposes a writable `Conditions` IList&lt;Condition&gt;
    /// (Perk, Package, and others - resolved dynamically via reflection).
    ///
    /// `parameter1FormKey` is the optional reference target for condition
    /// functions whose ConditionData has a FormLink property (e.g.
    /// GetFactionRank's Faction parameter, GetIsID's Object parameter).
    /// The first FormLink property on the ConditionData receives this
    /// value; pass null to leave it at the type default (FormID 0).
    /// </summary>
    public Result<string> AddCondition(
        string pluginPath,
        string? editorId,
        string? formId,
        string? recordType,
        string conditionFunction,
        float comparisonValue,
        string comparisonOperator,
        string? parameter1FormKey = null,
        string? dataFolder = null)
    {
        try
        {
            if (!File.Exists(pluginPath))
            {
                return Result<string>.Fail($"Plugin not found: {pluginPath}");
            }

            var mod = SkyrimMod.CreateFromBinary(
                pluginPath,
                SkyrimRelease.SkyrimSE,
                new Mutagen.Bethesda.Plugins.Binary.Parameters.BinaryReadParameters());

            // Locate the record we're attaching the condition to. The
            // CreateFromBinary load returns the writable mod, so the
            // returned IMajorRecordGetter is actually the writable
            // instance via Mutagen's interface dispatch.
            IMajorRecord? targetRecord = null;

            if (!string.IsNullOrEmpty(formId))
            {
                var findResult = FindRecordByFormKey(mod, formId);
                if (!findResult.Success || findResult.Value == null)
                {
                    return Result<string>.Fail("Record not found");
                }
                targetRecord = findResult.Value as IMajorRecord;
            }
            else if (!string.IsNullOrEmpty(editorId) && !string.IsNullOrEmpty(recordType))
            {
                var findResult = FindRecordByEditorId(mod, editorId, recordType);
                if (!findResult.Success || findResult.Value == null)
                {
                    return Result<string>.Fail("Record not found");
                }
                targetRecord = findResult.Value as IMajorRecord;
            }
            else
            {
                return Result<string>.Fail("Must provide either FormID or both EditorID and RecordType");
            }

            if (targetRecord == null)
            {
                return Result<string>.Fail("Record located but is not writable");
            }

            // Parse comparison operator.
            if (!Enum.TryParse<CompareOperator>(comparisonOperator, true, out var compareOp))
            {
                return Result<string>.Fail(
                    $"Invalid comparison operator: {comparisonOperator}",
                    suggestions: new List<string>
                    {
                        "Valid operators: EqualTo, NotEqualTo, GreaterThan, GreaterThanOrEqualTo, LessThan, LessThanOrEqualTo"
                    });
            }

            // Parse parameter1 FormKey if provided.
            FormKey? param1 = null;
            if (!string.IsNullOrEmpty(parameter1FormKey))
            {
                if (!FormKey.TryFactory(parameter1FormKey, out var parsed))
                {
                    return Result<string>.Fail(
                        $"Invalid parameter1 FormKey: {parameter1FormKey}",
                        suggestions: new List<string>
                        {
                            "FormKey format: 'FORMID:Plugin.esp' (e.g. '000800:MyMod.esp')"
                        });
                }
                param1 = parsed;
            }

            // Build the Condition.
            var conditionResult = CreateConditionFromFunction(
                conditionFunction, comparisonValue, compareOp, param1);
            if (!conditionResult.Success || conditionResult.Value == null)
            {
                return Result<string>.Fail(conditionResult.Error ?? "Failed to create condition");
            }

            // Locate the record's writable Conditions list via reflection.
            // Any Mutagen record exposing `IList<Condition> Conditions` is
            // supported - we don't enumerate record types statically. If a
            // record has no Conditions property the dispatch fails cleanly.
            var conditionsProp = targetRecord.GetType().GetProperty("Conditions");
            if (conditionsProp == null)
            {
                return Result<string>.Fail(
                    $"Record type '{targetRecord.GetType().Name}' has no Conditions property",
                    suggestions: new List<string>
                    {
                        "Verify the record type supports conditions (Perk, Package, etc.)",
                        "Use 'esp list-conditions' to confirm the record exposes Conditions"
                    });
            }
            var conditionsList = conditionsProp.GetValue(targetRecord)
                as System.Collections.IList;
            if (conditionsList == null)
            {
                return Result<string>.Fail(
                    $"Record type '{targetRecord.GetType().Name}' Conditions property is not a writable list");
            }
            conditionsList.Add(conditionResult.Value);

            // Write the (modified) source mod back in-place.
            var outputDir = !string.IsNullOrEmpty(dataFolder)
                ? dataFolder
                : Path.GetDirectoryName(pluginPath) ?? Directory.GetCurrentDirectory();
            var outputPath = Path.Combine(outputDir, Path.GetFileName(pluginPath));

            mod.WriteToBinary(outputPath);

            _logger.Info(
                $"Added {conditionFunction} condition (operator={compareOp}, " +
                $"value={comparisonValue}, param1={parameter1FormKey ?? "<none>"}) " +
                $"to {targetRecord.GetType().Name} '{targetRecord.EditorID}'");
            return Result<string>.Ok(outputPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to add condition: {ex.Message}", ex.StackTrace);
        }
    }

    /// <summary>
    /// Create a condition from a function name. Uses reflection to
    /// resolve the ConditionData subclass (e.g. GetFactionRankConditionData)
    /// from the function name + Mutagen assembly.
    ///
    /// `parameter1FormKey` (optional) populates the first FormLink-typed
    /// property on the ConditionData (e.g. Faction for GetFactionRank).
    /// This avoids per-function-name special casing - any condition
    /// function whose data type has a single FormLink reference works
    /// generically.
    /// </summary>
    internal Result<Condition> CreateConditionFromFunction(
        string functionName,
        float comparisonValue,
        CompareOperator compareOperator,
        FormKey? parameter1FormKey = null)
    {
        try
        {
            // Build the ConditionData type name.
            var typeName = $"{functionName}ConditionData";
            var fullTypeName = $"Mutagen.Bethesda.Skyrim.{typeName}";

            var conditionDataType = typeof(ISkyrimMod).Assembly.GetType(fullTypeName);
            if (conditionDataType == null)
            {
                return Result<Condition>.Fail(
                    $"Unknown condition function: {functionName}",
                    suggestions: new List<string>
                    {
                        "Use exact Mutagen function names (e.g., GetLevel, GetActorValue, GetFactionRank)",
                        "Type name is built as '<Function>ConditionData'",
                        "See Mutagen documentation for full list of condition functions"
                    });
            }

            var conditionData = System.Activator.CreateInstance(conditionDataType) as ConditionData;
            if (conditionData == null)
            {
                return Result<Condition>.Fail($"Failed to create instance of {typeName}");
            }

            // If parameter1FormKey was provided, set the first FormLink-
            // shaped property on the ConditionData.
            //
            // Mutagen's writable ConditionData subclasses model record-
            // reference parameters as one of several generic interface
            // shapes:
            //   - `IFormLink<TGetter>`           - direct FormKey reference
            //   - `IFormLinkOrIndex<TGetter>`    - FormKey OR alias index
            //   - `IFormLinkGetter<TGetter>`     - readonly view
            //
            // The matching concrete impl name varies (FormLink<T>,
            // FormLinkOrIndex<T>, ...). Rather than hard-coding pairs we
            // discover the concrete impl at runtime: walk loaded Mutagen
            // assemblies, find a non-abstract generic type whose closed
            // form is assignment-compatible with the property type AND
            // has a constructor taking FormKey. This auto-handles every
            // shape Mutagen exposes without per-function special cases.
            if (parameter1FormKey.HasValue)
            {
                var formLinkProp = conditionDataType.GetProperties()
                    .FirstOrDefault(p =>
                        p.PropertyType.Name.Contains("FormLink", StringComparison.Ordinal) &&
                        p.CanWrite);
                if (formLinkProp == null)
                {
                    return Result<Condition>.Fail(
                        $"{typeName} has no FormLink property to apply parameter1 to",
                        suggestions: new List<string>
                        {
                            "Function does not take a record reference - omit --param1",
                            "If the function does accept a record reference but isn't picking up, file an issue with the data type name"
                        });
                }
                if (!formLinkProp.PropertyType.IsGenericType)
                {
                    return Result<Condition>.Fail(
                        $"{typeName}.{formLinkProp.Name} is not generic; cannot construct a typed FormLink");
                }

                var propType = formLinkProp.PropertyType;
                var genericArg = propType.GetGenericArguments()[0];

                // First try the obvious pair: IFormLink<T> -> FormLink<T>.
                object? formLinkValue = TryConstructFormLinkOf(
                    typeof(FormLink<>), genericArg, parameter1FormKey.Value, propType);

                // If FormLink<T> isn't assignment-compatible, discover
                // the right concrete impl via reflection across all
                // loaded assemblies (Mutagen splits types across
                // Mutagen.Bethesda.Plugins, .Skyrim, .Core, etc).
                if (formLinkValue == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type[] types;
                        try { types = asm.GetTypes(); }
                        catch (ReflectionTypeLoadException ex)
                        {
                            types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
                        }
                        foreach (var candidate in types)
                        {
                            if (!candidate.IsGenericTypeDefinition) continue;
                            if (candidate.IsAbstract || candidate.IsInterface) continue;
                            if (candidate.GetGenericArguments().Length != 1) continue;
                            formLinkValue = TryConstructFormLinkOf(
                                candidate, genericArg, parameter1FormKey.Value, propType);
                            if (formLinkValue != null) break;
                        }
                        if (formLinkValue != null) break;
                    }
                }

                if (formLinkValue == null)
                {
                    return Result<Condition>.Fail(
                        $"Could not find a concrete impl assignable to {propType.FullName} that accepts FormKey",
                        suggestions: new List<string>
                        {
                            "Mutagen likely exposes a non-(FormKey) constructor for this property type",
                            "Inspect the property type in Mutagen + add a construction pathway in TryConstructFormLinkOf"
                        });
                }

                formLinkProp.SetValue(conditionData, formLinkValue);
            }

            var condition = new ConditionFloat
            {
                ComparisonValue = comparisonValue,
                CompareOperator = compareOperator,
                Data = conditionData
            };

            return Result<Condition>.Ok(condition);
        }
        catch (Exception ex)
        {
            return Result<Condition>.Fail($"Failed to create condition: {ex.Message}", ex.StackTrace);
        }
    }

    /// <summary>
    /// Construct a closed generic instance of `genericTypeDef`
    /// (e.g. typeof(FormLink&lt;&gt;)) bound to `genericArg`, with FormKey
    /// passed to its constructor. Returns null if the constructed type
    /// isn't assignment-compatible with `targetPropertyType` or if no
    /// FormKey-accepting constructor exists.
    /// </summary>
    private static object? TryConstructFormLinkOf(
        Type genericTypeDef, Type genericArg, FormKey formKey, Type targetPropertyType)
    {
        try
        {
            var closed = genericTypeDef.MakeGenericType(genericArg);
            if (!targetPropertyType.IsAssignableFrom(closed)) return null;

            // Path 1: single-FormKey constructor (FormLink<T>, FormLinkNullable<T>).
            var ctor1 = closed.GetConstructor(new[] { typeof(FormKey) });
            if (ctor1 != null)
            {
                return ctor1.Invoke(new object[] { formKey });
            }
            // Path 2: constructor accepting FormLink<T> (wrapper types).
            var formLinkClosed = typeof(FormLink<>).MakeGenericType(genericArg);
            var ctorFL = closed.GetConstructor(new[] { formLinkClosed });
            if (ctorFL != null)
            {
                var inner = System.Activator.CreateInstance(formLinkClosed, formKey);
                return ctorFL.Invoke(new[] { inner! });
            }
            // Path 3: default ctor + writable property assignable from
            // a FormLink-shaped value, OR a 2-arg ctor (flag, FormKey).
            // The (flag, FormKey) form is what FormLinkOrIndex<T> exposes
            // - its `()` ctor exists but is internal so reflection can't
            // call it with default BindingFlags.
            var defaultCtor = closed.GetConstructor(Type.EmptyTypes);
            if (defaultCtor != null)
            {
                var instance = defaultCtor.Invoke(null);
                foreach (var p in closed.GetProperties())
                {
                    if (!p.CanWrite) continue;
                    var built = BuildFormLinkAssignableTo(p.PropertyType, genericArg, formKey);
                    if (built != null)
                    {
                        p.SetValue(instance, built);
                        return instance;
                    }
                    if (p.PropertyType == typeof(FormKey))
                    {
                        p.SetValue(instance, formKey);
                        return instance;
                    }
                }
            }
            // Path 4: 2-param ctor where the 2nd is FormKey. Used for
            // FormLinkOrIndex<T>(IFormLinkOrIndexFlagGetter, FormKey).
            // The flag arg is the owning record's context (e.g. condition
            // data carries this in the parent record). For our purposes
            // (writing a fresh CTDA), passing null is acceptable - the
            // serializer writes the FormKey regardless of flag state.
            foreach (var ctor2 in closed.GetConstructors())
            {
                var ps = ctor2.GetParameters();
                if (ps.Length != 2) continue;
                if (ps[1].ParameterType != typeof(FormKey)) continue;
                if (ps[0].ParameterType.IsValueType) continue;  // flag must be ref/interface
                try
                {
                    return ctor2.Invoke(new object?[] { null, formKey });
                }
                catch { /* try next ctor */ }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Build a FormLink-shaped instance assignment-compatible with
    /// `targetType` (typically a Mutagen interface like IFormLink&lt;T&gt;
    /// or IFormLinkNullable&lt;T&gt;), wrapping `formKey`. Returns null if
    /// no candidate Mutagen wrapper produces an assignable value.
    /// </summary>
    private static object? BuildFormLinkAssignableTo(Type targetType, Type genericArg, FormKey formKey)
    {
        // Try the obvious concrete impls first. These cover IFormLink<T>
        // (closed FormLink<T>) + IFormLinkNullable<T> (closed
        // FormLinkNullable<T>).
        Type[] candidates =
        {
            typeof(FormLink<>),
            typeof(FormLinkNullable<>),
        };
        foreach (var def in candidates)
        {
            try
            {
                var closed = def.MakeGenericType(genericArg);
                if (!targetType.IsAssignableFrom(closed)) continue;

                // Path A: direct (FormKey) constructor.
                var ctor = closed.GetConstructor(new[] { typeof(FormKey) });
                if (ctor != null)
                {
                    return ctor.Invoke(new object[] { formKey });
                }
                // Path B: (Nullable<FormKey>) constructor (FormLinkNullable<T>).
                var nullableFormKeyType = typeof(System.Nullable<>).MakeGenericType(typeof(FormKey));
                var ctorNullable = closed.GetConstructor(new[] { nullableFormKeyType });
                if (ctorNullable != null)
                {
                    var nullable = System.Activator.CreateInstance(nullableFormKeyType, formKey);
                    return ctorNullable.Invoke(new[] { nullable! });
                }
                // Path C: default ctor + writable FormKey property.
                var defaultCtor = closed.GetConstructor(Type.EmptyTypes);
                if (defaultCtor != null)
                {
                    var instance = defaultCtor.Invoke(null);
                    var formKeyProp = closed.GetProperty("FormKey");
                    if (formKeyProp != null && formKeyProp.CanWrite &&
                        formKeyProp.PropertyType == typeof(FormKey))
                    {
                        formKeyProp.SetValue(instance, formKey);
                        return instance;
                    }
                }
            }
            catch { /* try next */ }
        }
        return null;
    }

    /// <summary>
    /// Remove a record from a plugin by EditorID.
    /// </summary>
    public Result RemoveRecord(SkyrimMod mod, string editorId)
    {
        try
        {
            // Search all record groups for the EditorID
            bool removed = TryRemoveFromGroup(mod.Weapons, editorId)
                || TryRemoveFromGroup(mod.Armors, editorId)
                || TryRemoveFromGroup(mod.Spells, editorId)
                || TryRemoveFromGroup(mod.Quests, editorId)
                || TryRemoveFromGroup(mod.Npcs, editorId)
                || TryRemoveFromGroup(mod.Perks, editorId)
                || TryRemoveFromGroup(mod.Factions, editorId)
                || TryRemoveFromGroup(mod.Books, editorId)
                || TryRemoveFromGroup(mod.MiscItems, editorId)
                || TryRemoveFromGroup(mod.Globals, editorId)
                || TryRemoveFromGroup(mod.LeveledItems, editorId)
                || TryRemoveFromGroup(mod.FormLists, editorId)
                || TryRemoveFromGroup(mod.Outfits, editorId)
                || TryRemoveFromGroup(mod.Locations, editorId)
                || TryRemoveFromGroup(mod.EncounterZones, editorId)
                || TryRemoveFromGroup(mod.Packages, editorId);

            if (!removed)
            {
                return Result.Fail(
                    $"Record not found: {editorId}",
                    suggestions: new List<string>
                    {
                        "Check the EditorID is correct (case-insensitive)",
                        "Use 'esp list-records' to see available records",
                        "Use 'esp find-record --search' to search by pattern"
                    });
            }

            _logger.Info($"Removed record: {editorId}");
            return Result.Ok($"Removed record: {editorId}");
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to remove record: {ex.Message}", ex.StackTrace);
        }
    }

    private static bool TryRemoveFromGroup<T>(IGroup<T> group, string editorId)
        where T : class, IMajorRecord, IMajorRecordGetter
    {
        var record = group.FirstOrDefault(r =>
            r.EditorID?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true);
        if (record != null)
        {
            group.Remove(record.FormKey);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clone a record within a plugin, creating a copy with a new EditorID and FormKey.
    /// </summary>
    public Result<RecordSearchResult> CloneRecord(SkyrimMod mod, string sourceEditorId, string newEditorId)
    {
        try
        {
            // Find the source record
            var sourceRecord = mod.EnumerateMajorRecords()
                .FirstOrDefault(r => r.EditorID?.Equals(sourceEditorId, StringComparison.OrdinalIgnoreCase) == true);

            if (sourceRecord == null)
            {
                return Result<RecordSearchResult>.Fail(
                    $"Source record not found: {sourceEditorId}",
                    suggestions: new List<string>
                    {
                        "Check the EditorID is correct",
                        "Use 'esp find-record --search' to search for the record"
                    });
            }

            // Deep copy and add to the appropriate group with new FormKey
            var clone = sourceRecord.DeepCopy();
            var mutableClone = (IMajorRecord)clone;
            mutableClone.EditorID = newEditorId;

            // Add clone to the correct group based on type
            var formKey = AddCloneToGroup(mod, mutableClone);
            if (formKey == null)
            {
                return Result<RecordSearchResult>.Fail(
                    $"Unsupported record type for cloning: {sourceRecord.GetType().Name}",
                    suggestions: new List<string>
                    {
                        "Supported types: Weapon, Armor, Spell, Quest, NPC, Perk, Faction, Book, Global, LeveledItem, FormList, Outfit, Location, EncounterZone, Package"
                    });
            }

            _logger.Info($"Cloned record: {sourceEditorId} -> {newEditorId} ({formKey})");
            return Result<RecordSearchResult>.Ok(new RecordSearchResult
            {
                EditorId = newEditorId,
                FormKey = formKey.Value.ToString(),
                RecordType = sourceRecord.GetType().Name.Replace("Getter", ""),
                PluginName = mod.ModKey.FileName,
                Name = GetRecordName(sourceRecord)
            });
        }
        catch (Exception ex)
        {
            return Result<RecordSearchResult>.Fail($"Failed to clone record: {ex.Message}", ex.StackTrace);
        }
    }

    private FormKey? AddCloneToGroup(SkyrimMod mod, IMajorRecord clone)
    {
        // Mutagen's FormKey is immutable after creation, so we need to use
        // the Duplicate method which creates a new record with a new FormKey
        var newFormKey = mod.GetNextFormKey();
        switch (clone)
        {
            case Weapon w: var nw = mod.Weapons.AddNew(); CopyRecordFields(w, nw); nw.EditorID = clone.EditorID; return nw.FormKey;
            case Armor a: var na = mod.Armors.AddNew(); CopyRecordFields(a, na); na.EditorID = clone.EditorID; return na.FormKey;
            case Spell s: var ns = mod.Spells.AddNew(); CopyRecordFields(s, ns); ns.EditorID = clone.EditorID; return ns.FormKey;
            case Quest q: var nq = mod.Quests.AddNew(); CopyRecordFields(q, nq); nq.EditorID = clone.EditorID; return nq.FormKey;
            case Npc n: var nn = mod.Npcs.AddNew(); CopyRecordFields(n, nn); nn.EditorID = clone.EditorID; return nn.FormKey;
            case Perk p: var np = mod.Perks.AddNew(); CopyRecordFields(p, np); np.EditorID = clone.EditorID; return np.FormKey;
            case Faction f: var nf = new Faction(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE); CopyRecordFields(f, nf); nf.EditorID = clone.EditorID; mod.Factions.Add(nf); return nf.FormKey;
            case Book b: var nb = mod.Books.AddNew(); CopyRecordFields(b, nb); nb.EditorID = clone.EditorID; return nb.FormKey;
            case Global g: var ng = mod.Globals.AddNewFloat(clone.EditorID ?? "Clone"); CopyRecordFields(g, ng); ng.EditorID = clone.EditorID; return ng.FormKey;
            case LeveledItem li: var nli = mod.LeveledItems.AddNew(); CopyRecordFields(li, nli); nli.EditorID = clone.EditorID; return nli.FormKey;
            case FormList fl: var nfl = mod.FormLists.AddNew(); CopyRecordFields(fl, nfl); nfl.EditorID = clone.EditorID; return nfl.FormKey;
            case Outfit o: var no = mod.Outfits.AddNew(); CopyRecordFields(o, no); no.EditorID = clone.EditorID; return no.FormKey;
            case Location l: var nl = mod.Locations.AddNew(); CopyRecordFields(l, nl); nl.EditorID = clone.EditorID; return nl.FormKey;
            case EncounterZone ez: var nez = mod.EncounterZones.AddNew(); CopyRecordFields(ez, nez); nez.EditorID = clone.EditorID; return nez.FormKey;
            case Package pk: var npk = mod.Packages.AddNew(); CopyRecordFields(pk, npk); npk.EditorID = clone.EditorID; return npk.FormKey;
            default: return null;
        }
    }

    /// <summary>
    /// Copy writable properties from source to destination using reflection.
    /// Skips FormKey and EditorID (set separately).
    /// </summary>
    private static void CopyRecordFields(IMajorRecord source, IMajorRecord dest)
    {
        var type = source.GetType();
        foreach (var prop in type.GetProperties())
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (prop.Name is "FormKey" or "EditorID") continue;

            try
            {
                var value = prop.GetValue(source);
                prop.SetValue(dest, value);
            }
            catch
            {
                // Skip properties that can't be copied (readonly, indexers, etc.)
            }
        }
    }

    /// <summary>
    /// Add a reference alias to a quest, optionally with a script attached.
    /// Sets ID, Name, and Flags. If a script is provided, it's stored in
    /// QuestFragmentAlias within the quest's VirtualMachineAdapter (the only
    /// way Creation Kit recognizes alias scripts).
    /// </summary>
    public Result<QuestAlias> AddAliasToQuest(
        Quest quest,
        string aliasName,
        string? scriptName = null,
        QuestAlias.Flag? flags = null)
    {
        if (quest.Aliases.Any(a => a.Name == aliasName))
        {
            return Result<QuestAlias>.Fail($"Alias already exists: {aliasName}");
        }

        // Pick the next available alias ID (uint, monotonic)
        var nextId = quest.Aliases.Count == 0 ? 0u : quest.Aliases.Max(a => a.ID) + 1;

        var alias = new QuestAlias
        {
            ID = nextId,
            Name = aliasName,
            Flags = flags
        };
        quest.Aliases.Add(alias);

        if (!string.IsNullOrEmpty(scriptName))
        {
            var scriptResult = AttachScriptToAlias(quest, alias, scriptName);
            if (!scriptResult.Success)
            {
                // Roll back the alias we just added so the plugin isn't left half-modified
                quest.Aliases.Remove(alias);
                return Result<QuestAlias>.Fail(scriptResult.Error!);
            }
        }

        return Result<QuestAlias>.Ok(alias);
    }

    /// <summary>
    /// Attach a script to an existing alias by name. Creates a QuestFragmentAlias
    /// entry in the quest's VirtualMachineAdapter if one doesn't exist. The
    /// fragment alias's Property.Object MUST reference the quest's FormKey or
    /// the Creation Kit won't see the script.
    /// </summary>
    public Result<ScriptEntry> AttachScriptToAliasByName(Quest quest, string aliasName, string scriptName)
    {
        var alias = quest.Aliases.FirstOrDefault(a => a.Name == aliasName);
        if (alias == null)
        {
            return Result<ScriptEntry>.Fail($"Alias not found: {aliasName}");
        }
        return AttachScriptToAlias(quest, alias, scriptName);
    }

    private Result<ScriptEntry> AttachScriptToAlias(Quest quest, QuestAlias alias, string scriptName)
    {
        var adapter = quest.VirtualMachineAdapter as QuestAdapter ?? new QuestAdapter();
        var aliasIndex = (short)alias.ID;

        // Find or create the QuestFragmentAlias for this alias
        var fragAlias = adapter.Aliases!.FirstOrDefault(fa => fa.Property?.Alias == aliasIndex);
        if (fragAlias == null)
        {
            fragAlias = new QuestFragmentAlias
            {
                Property = new ScriptObjectProperty
                {
                    Object = quest.FormKey.ToLink<ISkyrimMajorRecordGetter>(),
                    Alias = aliasIndex,
                    Flags = ScriptProperty.Flag.Edited
                }
            };
            adapter.Aliases!.Add(fragAlias);
        }

        if (fragAlias.Scripts!.Any(s => string.Equals(s.Name, scriptName, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<ScriptEntry>.Fail($"Script already attached to alias '{alias.Name}': {scriptName}");
        }

        var script = new ScriptEntry
        {
            Name = scriptName,
            Flags = ScriptEntry.Flag.Local
        };
        fragAlias.Scripts!.Add(script);

        // Make sure the adapter is wired back to the quest in case we created a new one
        quest.VirtualMachineAdapter = adapter;

        return Result<ScriptEntry>.Ok(script);
    }

    /// <summary>
    /// Place a PlacedObject (REFR) into a target Cell in the plugin.
    ///
    /// The target Cell can be:
    ///   - A cell defined in the plugin itself (rare for esps adding new
    ///     content; the cell would have been added via a separate command
    ///     that doesn't exist yet).
    ///   - A cell defined in one of the plugin's masters. In that case we
    ///     resolve via a link cache built from the data folder, override
    ///     the cell into the target plugin, and add the REFR to the
    ///     override's Persistent/Temporary collection.
    ///
    /// Skyrim.esm needs to be in the data folder for the override path.
    /// </summary>
    public Result<string> AddRefr(
        string pluginPath,
        string baseFormKey,
        string cellFormKey,
        float x, float y, float z,
        float rotX = 0f, float rotY = 0f, float rotZ = 0f,
        string? editorId = null,
        bool persistent = true,
        float? scale = null,
        string? dataFolder = null)
    {
        try
        {
            if (!File.Exists(pluginPath))
            {
                return Result<string>.Fail($"Plugin not found: {pluginPath}");
            }

            if (!FormKey.TryFactory(baseFormKey, out var baseFk))
            {
                return Result<string>.Fail(
                    $"Invalid --base FormKey: {baseFormKey}",
                    suggestions: new List<string>
                    {
                        "Format: XXXXXX:PluginName.esp (6 hex digits, plugin filename)",
                        "Example: 000033:Skyrim.esm (vanilla XMarker base)"
                    });
            }

            if (!FormKey.TryFactory(cellFormKey, out var cellFk))
            {
                return Result<string>.Fail(
                    $"Invalid --cell FormKey: {cellFormKey}",
                    suggestions: new List<string>
                    {
                        "Format: XXXXXX:PluginName.esp (6 hex digits, plugin filename)",
                        "The cell must exist in the plugin itself or in one of its masters"
                    });
            }

            var mod = SkyrimMod.CreateFromBinary(
                pluginPath,
                SkyrimRelease.SkyrimSE);
            if (mod.ModHeader.Stats.NextFormID < 0x800)
            {
                mod.ModHeader.Stats.NextFormID = 0x800;
            }

            // Find the target cell. Two paths: in-plugin or via masters.
            ICell? targetCell = null;

            // Path 1: cell already exists in the plugin (e.g. previous add-refr
            // call already overrode it - reuse the override).
            foreach (var block in mod.Cells)
            {
                foreach (var subBlock in block.SubBlocks)
                {
                    foreach (var c in subBlock.Cells)
                    {
                        if (c.FormKey == cellFk)
                        {
                            targetCell = c;
                            break;
                        }
                    }
                    if (targetCell != null) break;
                }
                if (targetCell != null) break;
            }

            // Path 2: cell comes from a master. Need a link cache + override.
            if (targetCell == null)
            {
                if (string.IsNullOrEmpty(dataFolder))
                {
                    return Result<string>.Fail(
                        $"Cell {cellFormKey} not found in plugin; need --data-folder to resolve from masters",
                        suggestions: new List<string>
                        {
                            "Pass --data-folder pointing at the Skyrim Data directory so the cell can be resolved from a master ESM/ESP",
                            "Example: --data-folder 'C:/Skyrim Special Edition/Data'"
                        });
                }

                var linkCacheResult = new LinkCacheManager(_logger).CreateLinkCacheWithMod(dataFolder, mod);
                if (!linkCacheResult.Success)
                {
                    return Result<string>.Fail(
                        $"Failed to build link cache: {linkCacheResult.Error}",
                        linkCacheResult.ErrorContext,
                        linkCacheResult.Suggestions);
                }
                var linkCache = (ILinkCache<ISkyrimMod, ISkyrimModGetter>)linkCacheResult.Value!;

                var cellLink = cellFk.ToLink<ICellGetter>();
                if (!cellLink.TryResolveContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter>(linkCache, out var cellContext))
                {
                    return Result<string>.Fail(
                        $"Cell {cellFormKey} not found in plugin or any master",
                        suggestions: new List<string>
                        {
                            "Verify the FormKey is correct (format XXXXXX:Plugin.esp)",
                            "Verify the master containing the cell is in --data-folder",
                            "If the master is the plugin itself, the cell must already exist (use a different command to create it)"
                        });
                }

                // Override the master cell into our mod so we can write to it.
                targetCell = cellContext.GetOrAddAsOverride(mod);
            }

            // Build and place the REFR.
            var builder = new RefrBuilder(mod, targetCell, editorId)
                .WithBase(baseFk)
                .AtPosition(x, y, z)
                .WithRotation(rotX, rotY, rotZ);
            if (scale.HasValue) builder.WithScale(scale.Value);
            if (!persistent) builder.AsTemporary();

            var refr = builder.Build();

            mod.WriteToBinary(pluginPath);

            _logger.Info(
                $"Added REFR {refr.EditorID ?? "(no editor ID)"} = {refr.FormKey} " +
                $"base={baseFormKey} cell={cellFormKey} pos=({x:F1},{y:F1},{z:F1})");

            return Result<string>.Ok(refr.FormKey.ToString());
        }
        catch (Exception ex)
        {
            return Result<string>.Fail(
                "Failed to add REFR",
                ex.Message,
                new List<string>
                {
                    "Verify the plugin file is writable and not in use by another process",
                    "Verify --base FormKey points at a placeable object (Static, Activator, Furniture, etc.)",
                    "If using a cell from a master, verify --data-folder is correct"
                });
        }
    }
}
