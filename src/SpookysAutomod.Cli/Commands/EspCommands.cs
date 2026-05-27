using System.CommandLine;
using System.Text.Json;
using SpookysAutomod.Core.Logging;
using SpookysAutomod.Core.Models;
using SpookysAutomod.Esp.Builders;
using SpookysAutomod.Esp.Services;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SpookysAutomod.Cli.Commands;

public static class EspCommands
{
    private static Option<bool> _jsonOption = null!;
    private static Option<bool> _verboseOption = null!;

    public static Command Create(Option<bool> jsonOption, Option<bool> verboseOption)
    {
        _jsonOption = jsonOption;
        _verboseOption = verboseOption;

        var espCommand = new Command("esp", "ESP/ESL plugin operations");

        espCommand.AddCommand(CreateCreateCommand());
        espCommand.AddCommand(CreateInfoCommand());
        espCommand.AddCommand(CreateAddQuestCommand());
        espCommand.AddCommand(CreateAddSpellCommand());
        espCommand.AddCommand(CreateAddGlobalCommand());
        espCommand.AddCommand(CreateAddWeaponCommand());
        espCommand.AddCommand(CreateAddArmorCommand());
        espCommand.AddCommand(CreateAddNpcCommand());
        espCommand.AddCommand(CreateAddBookCommand());
        espCommand.AddCommand(CreateAddPerkCommand());
        espCommand.AddCommand(CreateAddFactionCommand());
        espCommand.AddCommand(CreateAttachScriptCommand());
        espCommand.AddCommand(CreateAddAliasCommand());
        espCommand.AddCommand(CreateAttachAliasScriptCommand());
        espCommand.AddCommand(CreateSetPropertyCommand());
        espCommand.AddCommand(CreateGenerateSeqCommand());
        espCommand.AddCommand(CreateListMastersCommand());
        espCommand.AddCommand(CreateMergeCommand());
        espCommand.AddCommand(CreateDebugTypesCommand());
        espCommand.AddCommand(CreateAutoFillCommand());
        espCommand.AddCommand(CreateAutoFillAllCommand());
        espCommand.AddCommand(CreateAddLeveledItemCommand());
        espCommand.AddCommand(CreateAddFormListCommand());
        espCommand.AddCommand(CreateAddEncounterZoneCommand());
        espCommand.AddCommand(CreateAddLocationCommand());
        espCommand.AddCommand(CreateAddOutfitCommand());
        espCommand.AddCommand(CreateViewRecordCommand());
        espCommand.AddCommand(CreateOverrideCommand());
        espCommand.AddCommand(CreateFindRecordCommand());
        espCommand.AddCommand(CreateListRecordsCommand());
        espCommand.AddCommand(CreateBatchOverrideCommand());
        espCommand.AddCommand(CreateCompareRecordCommand());
        espCommand.AddCommand(CreateConflictsCommand());
        espCommand.AddCommand(CreateListConditionsCommand());
        espCommand.AddCommand(CreateRemoveConditionCommand());
        espCommand.AddCommand(CreateAddConditionCommand());
        espCommand.AddCommand(CreateAddPackageCommand());
        espCommand.AddCommand(CreateAttachPackageCommand());
        espCommand.AddCommand(CreateAddRefrCommand());
        espCommand.AddCommand(CreateRemoveRecordCommand());
        espCommand.AddCommand(CreateCloneRecordCommand());
        espCommand.AddCommand(CreateScriptCommand());

        return espCommand;
    }

    private static IModLogger CreateLogger(bool json, bool verbose) =>
        json ? new SilentLogger() : new ConsoleLogger(verbose);

    private static Command CreateCreateCommand()
    {
        var nameArg = new Argument<string>("name", "Name of the plugin file (e.g., MyMod.esp)");
        var outputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            getDefaultValue: () => ".",
            description: "Output directory");
        var lightOption = new Option<bool>("--light", "Create as a light plugin (ESL flagged)");
        var authorOption = new Option<string?>("--author", "Author name for the plugin header");
        var descOption = new Option<string?>("--description", "Description for the plugin header");

        var cmd = new Command("create", "Create a new ESP/ESL plugin")
        {
            nameArg,
            outputOption,
            lightOption,
            authorOption,
            descOption
        };

        cmd.SetHandler((name, output, light, author, desc, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var result = service.CreatePlugin(name, output, light, author, desc);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success)
            {
                Console.WriteLine($"Created plugin: {result.Value}");
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                Environment.ExitCode = 1;
            }
        }, nameArg, outputOption, lightOption, authorOption, descOption,
           _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateInfoCommand()
    {
        var pathArg = new Argument<string>("plugin", "Path to the plugin file");

        var cmd = new Command("info", "Get information about a plugin")
        {
            pathArg
        };

        cmd.SetHandler((path, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var result = service.GetPluginInfo(path);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success && result.Value != null)
            {
                var info = result.Value;
                Console.WriteLine($"Plugin: {info.FileName}");
                Console.WriteLine($"Path: {info.FilePath}");
                Console.WriteLine($"Size: {info.FileSize:N0} bytes");
                Console.WriteLine($"Light: {info.IsLight}");
                Console.WriteLine($"Master: {info.IsMaster}");

                if (!string.IsNullOrEmpty(info.Author))
                    Console.WriteLine($"Author: {info.Author}");

                if (info.MasterFiles.Count > 0)
                {
                    Console.WriteLine($"Masters: {string.Join(", ", info.MasterFiles)}");
                }

                Console.WriteLine($"\nRecords ({info.TotalRecords} total):");
                foreach (var (type, count) in info.RecordCounts.Where(kv => kv.Value > 0))
                {
                    Console.WriteLine($"  {type}: {count}");
                }
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                Environment.ExitCode = 1;
            }
        }, pathArg, _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateAddQuestCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editorId", "Editor ID for the quest");
        var nameOption = new Option<string?>("--name", "Display name for the quest");
        var startEnabledOption = new Option<bool>("--start-enabled", "Quest starts when game loads");
        var runOnceOption = new Option<bool>("--run-once", "Quest runs only once");
        var priorityOption = new Option<byte>("--priority", getDefaultValue: () => 50, description: "Quest priority (0-255)");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("add-quest", "Add a quest record to a plugin")
        {
            pluginArg,
            editorIdArg,
            nameOption,
            startEnabledOption,
            runOnceOption,
            priorityOption,
            dryRunOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var editorId = context.ParseResult.GetValueForArgument(editorIdArg);
            var name = context.ParseResult.GetValueForOption(nameOption);
            var startEnabled = context.ParseResult.GetValueForOption(startEnabledOption);
            var runOnce = context.ParseResult.GetValueForOption(runOnceOption);
            var priority = context.ParseResult.GetValueForOption(priorityOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success)
            {
                OutputError(loadResult.Error!, json);
                return;
            }

            var mod = loadResult.Value!;
            var builder = new QuestBuilder(mod, editorId);

            if (!string.IsNullOrEmpty(name))
                builder.WithName(name);

            if (startEnabled)
                builder.StartEnabled();

            if (runOnce)
                builder.RunOnce();

            builder.WithPriority(priority);

            var quest = builder.Build();

            // Conditional save based on dry-run
            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            editorId = quest.EditorID,
                            formId = quest.FormKey.ToString(),
                            name = quest.Name?.String,
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Added quest: {quest.EditorID} ({quest.FormKey})";
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateAddSpellCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editorId", "Editor ID for the spell");
        var nameOption = new Option<string?>("--name", "Display name for the spell");
        var typeOption = new Option<string>("--type", getDefaultValue: () => "spell",
            description: "Spell type: spell, power, lesser-power, ability");
        var costOption = new Option<uint>("--cost", getDefaultValue: () => 0, description: "Base magicka cost");
        var effectOption = new Option<string?>("--effect",
            description: "Effect preset: damage-health, restore-health, damage-magicka, restore-magicka, damage-stamina, restore-stamina, fortify-health, fortify-magicka, fortify-stamina, fortify-armor, fortify-attack");
        var magnitudeOption = new Option<float>("--magnitude", getDefaultValue: () => 25, description: "Effect magnitude (damage/heal amount or buff value)");
        var durationOption = new Option<int>("--duration", getDefaultValue: () => 0, description: "Effect duration in seconds (0 = instant)");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("add-spell", "Add a spell record to a plugin")
        {
            pluginArg, editorIdArg, nameOption, typeOption, costOption, effectOption, magnitudeOption, durationOption, dryRunOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var editorId = context.ParseResult.GetValueForArgument(editorIdArg);
            var name = context.ParseResult.GetValueForOption(nameOption);
            var type = context.ParseResult.GetValueForOption(typeOption) ?? "spell";
            var cost = context.ParseResult.GetValueForOption(costOption);
            var effect = context.ParseResult.GetValueForOption(effectOption);
            var magnitude = context.ParseResult.GetValueForOption(magnitudeOption);
            var duration = context.ParseResult.GetValueForOption(durationOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success)
            {
                OutputError(loadResult.Error!, json);
                return;
            }

            var mod = loadResult.Value!;
            var builder = new SpellBuilder(mod, editorId);

            if (!string.IsNullOrEmpty(name))
                builder.WithName(name);

            builder.WithBaseCost(cost);

            switch (type.ToLowerInvariant())
            {
                case "power":
                    builder.AsGreaterPower();
                    break;
                case "lesser-power":
                    builder.AsLesserPower();
                    break;
                case "ability":
                    builder.AsAbility();
                    break;
                default:
                    builder.WithType(SpellType.Spell);
                    break;
            }

            // Apply effect if specified
            bool hasEffect = false;
            if (!string.IsNullOrEmpty(effect))
            {
                hasEffect = true;
                switch (effect.ToLowerInvariant())
                {
                    case "damage-health": builder.WithDamageHealth(magnitude, duration); break;
                    case "restore-health": builder.WithRestoreHealth(magnitude, duration); break;
                    case "damage-magicka": builder.WithDamageMagicka(magnitude, duration); break;
                    case "restore-magicka": builder.WithRestoreMagicka(magnitude, duration); break;
                    case "damage-stamina": builder.WithDamageStamina(magnitude, duration); break;
                    case "restore-stamina": builder.WithRestoreStamina(magnitude, duration); break;
                    case "fortify-health": builder.WithFortifyHealth(magnitude, duration > 0 ? duration : 60); break;
                    case "fortify-magicka": builder.WithFortifyMagicka(magnitude, duration > 0 ? duration : 60); break;
                    case "fortify-stamina": builder.WithFortifyStamina(magnitude, duration > 0 ? duration : 60); break;
                    case "fortify-armor": builder.WithFortifyArmor(magnitude, duration > 0 ? duration : 60); break;
                    case "fortify-attack": builder.WithFortifyAttackDamage(magnitude / 100f, duration > 0 ? duration : 60); break;
                    default:
                        hasEffect = false;
                        break;
                }
            }

            var spell = builder.Build();

            // Conditional save based on dry-run
            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            var effectCount = spell.Effects.Count;
            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            editorId = spell.EditorID,
                            formId = spell.FormKey.ToString(),
                            name = spell.Name?.String,
                            effectCount,
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Added spell: {spell.EditorID} ({spell.FormKey})";
                if (hasEffect)
                    msg += $" [{effectCount} effect(s)]";
                else
                    msg += " [No effects - spell will do nothing! Use --effect to add one]";
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateAddGlobalCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editorId", "Editor ID for the global");
        var typeOption = new Option<string>("--type", getDefaultValue: () => "float",
            description: "Global type: short, long, float");
        var valueOption = new Option<float>("--value", getDefaultValue: () => 0, description: "Initial value");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("add-global", "Add a global variable to a plugin")
        {
            pluginArg,
            editorIdArg,
            typeOption,
            valueOption,
            dryRunOption
        };

        cmd.SetHandler((plugin, editorId, type, value, dryRun, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success)
            {
                OutputError(loadResult.Error!, json);
                return;
            }

            var mod = loadResult.Value!;
            var builder = new GlobalBuilder(mod, editorId);

            switch (type.ToLowerInvariant())
            {
                case "short":
                    builder.AsShort((short)value);
                    break;
                case "long":
                    builder.AsLong((int)value);
                    break;
                default:
                    builder.AsFloat(value);
                    break;
            }

            var global = builder.Build();

            // Conditional save based on dry-run
            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            // Get the value from the concrete global type
            var globalValue = global switch
            {
                GlobalFloat f => f.Data,
                GlobalInt i => i.Data,
                GlobalShort s => s.Data,
                _ => 0f
            };

            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            editorId = global.EditorID,
                            formId = global.FormKey.ToString(),
                            value = globalValue,
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Added global: {global.EditorID} ({global.FormKey}) = {globalValue}";
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        }, pluginArg, editorIdArg, typeOption, valueOption, dryRunOption,
           _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateAddFactionCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editorId", "Editor ID for the faction");
        var nameOption = new Option<string?>("--name", "Display name of the faction");
        var hiddenOption = new Option<bool>("--hidden-from-pc", "Hide faction from player's faction list");
        var trackCrimeOption = new Option<bool>("--track-crime", "Faction tracks crimes against its members");
        var specialCombatOption = new Option<bool>("--special-combat", "Enable special combat behavior");
        var canBeOwnerOption = new Option<bool>("--can-be-owner", "Faction can own items and properties");
        var vendorOption = new Option<bool>("--vendor", "Faction acts as vendor faction");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("add-faction", "Add a faction record to a plugin")
        {
            pluginArg, editorIdArg, nameOption, hiddenOption, trackCrimeOption,
            specialCombatOption, canBeOwnerOption, vendorOption, dryRunOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var editorId = context.ParseResult.GetValueForArgument(editorIdArg);
            var name = context.ParseResult.GetValueForOption(nameOption);
            var hiddenFromPC = context.ParseResult.GetValueForOption(hiddenOption);
            var trackCrime = context.ParseResult.GetValueForOption(trackCrimeOption);
            var specialCombat = context.ParseResult.GetValueForOption(specialCombatOption);
            var canBeOwner = context.ParseResult.GetValueForOption(canBeOwnerOption);
            var vendor = context.ParseResult.GetValueForOption(vendorOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success) { OutputError(loadResult.Error!, json); return; }

            var mod = loadResult.Value!;
            var builder = new FactionBuilder(mod, editorId);

            if (!string.IsNullOrEmpty(name)) builder.WithName(name);
            if (hiddenFromPC) builder.HiddenFromPC();
            if (trackCrime) builder.TrackCrime();
            if (specialCombat) builder.SpecialCombat();
            if (canBeOwner) builder.CanBeOwner();
            if (vendor) builder.AsVendor();

            var faction = builder.Build();

            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            editorId = faction.EditorID,
                            formId = faction.FormKey.ToString(),
                            flags = faction.Flags.ToString(),
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Added faction: {faction.EditorID} ({faction.FormKey})";
                if (faction.Flags != 0) msg += $" [Flags: {faction.Flags}]";
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateAttachScriptCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var questOption = new Option<string>("--quest", "Editor ID of the quest to attach to") { IsRequired = true };
        var scriptOption = new Option<string>("--script", "Name of the script to attach") { IsRequired = true };

        var cmd = new Command("attach-script", "Attach a script to a quest")
        {
            pluginArg,
            questOption,
            scriptOption
        };

        cmd.SetHandler((plugin, questId, scriptName, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success)
            {
                OutputError(loadResult.Error!, json);
                return;
            }

            var mod = loadResult.Value!;

            // Find the quest
            var quest = mod.Quests.FirstOrDefault(q => q.EditorID == questId);
            if (quest == null)
            {
                OutputError($"Quest not found: {questId}", json,
                    suggestions: new[] { "Check the quest editor ID is correct", "Use 'esp info' to list quests in the plugin" });
                return;
            }

            // Get or create adapter
            var adapter = quest.VirtualMachineAdapter as QuestAdapter ?? new QuestAdapter();

            // Check if script already attached
            if (adapter.Scripts.Any(s => s.Name == scriptName))
            {
                OutputError($"Script already attached: {scriptName}", json);
                return;
            }

            // Add script
            var scriptEntry = new ScriptEntry
            {
                Name = scriptName,
                Flags = ScriptEntry.Flag.Local
            };
            adapter.Scripts.Add(scriptEntry);
            quest.VirtualMachineAdapter = adapter;

            var saveResult = service.SavePlugin(mod, plugin);

            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            quest = questId,
                            script = scriptName
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                Console.WriteLine($"Attached script '{scriptName}' to quest '{questId}'");
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        }, pluginArg, questOption, scriptOption,
           _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateAddAliasCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var questOption = new Option<string>("--quest", "Editor ID of the quest") { IsRequired = true };
        var nameOption = new Option<string>("--name", "Name of the alias to add") { IsRequired = true };
        var scriptOption = new Option<string?>("--script", "Optional script to attach to the new alias");
        var flagsOption = new Option<string?>("--flags", "Comma-separated alias flags (e.g. 'Optional,AllowReuseInQuest,AllowReserved')");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("add-alias", "Add a reference alias to a quest, optionally with a script attached")
        {
            pluginArg, questOption, nameOption, scriptOption, flagsOption, dryRunOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var questId = context.ParseResult.GetValueForOption(questOption)!;
            var aliasName = context.ParseResult.GetValueForOption(nameOption)!;
            var scriptName = context.ParseResult.GetValueForOption(scriptOption);
            var flagsStr = context.ParseResult.GetValueForOption(flagsOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            // Parse flags
            QuestAlias.Flag? flags = null;
            if (!string.IsNullOrWhiteSpace(flagsStr))
            {
                if (!Enum.TryParse<QuestAlias.Flag>(flagsStr, ignoreCase: true, out var parsed))
                {
                    OutputError($"Invalid alias flags: '{flagsStr}'", json,
                        suggestions: new[] { "Valid flags: Optional, AllowReuseInQuest, AllowReserved, Essential, Protected, StoresText, AllowDeadActor, ClearsNameWhenRemoved, etc.", "Combine flags with commas: --flags 'Optional,AllowReuseInQuest'" });
                    return;
                }
                flags = parsed;
            }

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success) { OutputError(loadResult.Error!, json); return; }

            var mod = loadResult.Value!;
            var quest = mod.Quests.FirstOrDefault(q => q.EditorID == questId);
            if (quest == null)
            {
                OutputError($"Quest not found: {questId}", json,
                    suggestions: new[] { "Check the quest editor ID is correct", "Use 'esp info' to list quests in the plugin" });
                return;
            }

            var addResult = service.AddAliasToQuest(quest, aliasName, scriptName, flags);
            if (!addResult.Success) { OutputError(addResult.Error!, json); return; }
            var alias = addResult.Value!;

            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            quest = questId,
                            alias = alias.Name,
                            aliasId = alias.ID,
                            script = scriptName,
                            flags = alias.Flags?.ToString(),
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Added alias '{alias.Name}' (ID {alias.ID}) to quest '{questId}'";
                if (!string.IsNullOrEmpty(scriptName)) msg += $" with script '{scriptName}'";
                if (alias.Flags != null) msg += $" [Flags: {alias.Flags}]";
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateAttachAliasScriptCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var questOption = new Option<string>("--quest", "Editor ID of the quest") { IsRequired = true };
        var aliasOption = new Option<string>("--alias", "Name of the alias to attach the script to") { IsRequired = true };
        var scriptOption = new Option<string>("--script", "Name of the script to attach") { IsRequired = true };
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("attach-alias-script", "Attach a script to an existing quest alias")
        {
            pluginArg, questOption, aliasOption, scriptOption, dryRunOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var questId = context.ParseResult.GetValueForOption(questOption)!;
            var aliasName = context.ParseResult.GetValueForOption(aliasOption)!;
            var scriptName = context.ParseResult.GetValueForOption(scriptOption)!;
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success) { OutputError(loadResult.Error!, json); return; }

            var mod = loadResult.Value!;
            var quest = mod.Quests.FirstOrDefault(q => q.EditorID == questId);
            if (quest == null)
            {
                OutputError($"Quest not found: {questId}", json,
                    suggestions: new[] { "Check the quest editor ID is correct", "Use 'esp info' to list quests in the plugin" });
                return;
            }

            var attachResult = service.AttachScriptToAliasByName(quest, aliasName, scriptName);
            if (!attachResult.Success)
            {
                OutputError(attachResult.Error!, json,
                    suggestions: new[] { "Use 'esp add-alias' to create the alias first", "Alias names are case-sensitive" });
                return;
            }

            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            quest = questId,
                            alias = aliasName,
                            script = scriptName,
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Attached script '{scriptName}' to alias '{aliasName}' on quest '{questId}'";
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateSetPropertyCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var questOption = new Option<string>("--quest", "Editor ID of the quest") { IsRequired = true };
        var scriptOption = new Option<string>("--script", "Name of the script") { IsRequired = true };
        var propertyOption = new Option<string>("--property", "Property name") { IsRequired = true };
        var typeOption = new Option<string>("--type", "Property type: object, alias, int, float, bool, string") { IsRequired = true };
        var valueOption = new Option<string>("--value", "Property value (for object: 'Plugin.esp|0xFormID', for alias: alias name)") { IsRequired = true };
        var aliasTargetOption = new Option<string?>("--alias-target", "Target alias name (for setting properties on alias scripts instead of quest scripts)");

        var cmd = new Command("set-property", "Set a script property on a quest or alias script")
        {
            pluginArg, questOption, scriptOption, propertyOption, typeOption, valueOption, aliasTargetOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var questId = context.ParseResult.GetValueForOption(questOption)!;
            var scriptName = context.ParseResult.GetValueForOption(scriptOption)!;
            var propertyName = context.ParseResult.GetValueForOption(propertyOption)!;
            var propType = context.ParseResult.GetValueForOption(typeOption)!;
            var value = context.ParseResult.GetValueForOption(valueOption)!;
            var aliasTarget = context.ParseResult.GetValueForOption(aliasTargetOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var pluginService = new PluginService(logger);
            var propService = new ScriptPropertyService(logger);

            var loadResult = pluginService.LoadPluginForEdit(plugin);
            if (!loadResult.Success)
            {
                OutputError(loadResult.Error!, json);
                return;
            }

            var mod = loadResult.Value!;

            // Find the quest
            var quest = mod.Quests.FirstOrDefault(q => q.EditorID == questId);
            if (quest == null)
            {
                OutputError($"Quest not found: {questId}", json);
                return;
            }

            // Find the script (on quest or on alias)
            ScriptEntry? script = null;
            if (!string.IsNullOrEmpty(aliasTarget))
            {
                // Find script on alias (via QuestFragmentAlias)
                var alias = quest.Aliases.FirstOrDefault(a => a.Name == aliasTarget);
                if (alias == null)
                {
                    OutputError($"Alias not found: {aliasTarget}", json);
                    return;
                }
                script = propService.FindAliasScript(quest, aliasTarget, scriptName);
                if (script == null)
                {
                    OutputError($"Script '{scriptName}' not found on alias '{aliasTarget}'", json,
                        suggestions: new[] {
                            "Attach the script first with 'esp attach-script' or by creating the alias with a script",
                            "Note: Alias scripts are stored in QuestFragmentAlias within the Quest's VirtualMachineAdapter"
                        });
                    return;
                }
            }
            else
            {
                // Find script on quest
                script = propService.FindQuestScript(quest, scriptName);
                if (script == null)
                {
                    OutputError($"Script '{scriptName}' not found on quest '{questId}'", json,
                        suggestions: new[] { "Attach the script first with 'esp attach-script'" });
                    return;
                }
            }

            // Set the property based on type
            bool success = propType.ToLowerInvariant() switch
            {
                "object" => propService.SetObjectProperty(script, propertyName, value),
                "alias" => propService.SetAliasProperty(script, propertyName, quest, value),
                "int" => propService.SetIntProperty(script, propertyName, int.Parse(value)),
                "float" => propService.SetFloatProperty(script, propertyName, float.Parse(value)),
                "bool" => propService.SetBoolProperty(script, propertyName, bool.Parse(value)),
                "string" => propService.SetStringProperty(script, propertyName, value),
                _ => false
            };

            if (!success)
            {
                OutputError($"Failed to set property '{propertyName}'", json);
                return;
            }

            var saveResult = pluginService.SavePlugin(mod, plugin);

            if (json)
            {
                Console.WriteLine(new
                {
                    success = saveResult.Success,
                    result = new { quest = questId, script = scriptName, property = propertyName, type = propType, value },
                    error = saveResult.Error
                }.ToJson());
            }
            else if (saveResult.Success)
            {
                var target = !string.IsNullOrEmpty(aliasTarget) ? $"alias '{aliasTarget}'" : $"quest '{questId}'";
                Console.WriteLine($"Set {propType} property '{propertyName}' = '{value}' on script '{scriptName}' ({target})");
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateGenerateSeqCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var outputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            getDefaultValue: () => ".",
            description: "Output directory for SEQ file");

        var cmd = new Command("generate-seq", "Generate SEQ file for start-enabled quests")
        {
            pluginArg,
            outputOption
        };

        cmd.SetHandler((plugin, output, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var result = service.GenerateSeqFile(plugin, output);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success)
            {
                Console.WriteLine($"Generated SEQ file: {result.Value}");
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                if (result.Suggestions?.Count > 0)
                {
                    Console.Error.WriteLine("Suggestions:");
                    foreach (var suggestion in result.Suggestions)
                        Console.Error.WriteLine($"  - {suggestion}");
                }
                Environment.ExitCode = 1;
            }
        }, pluginArg, outputOption, _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateAddWeaponCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editorId", "Editor ID for the weapon");
        var nameOption = new Option<string?>("--name", "Display name for the weapon");
        var typeOption = new Option<string>("--type", getDefaultValue: () => "sword",
            description: "Weapon type: sword, greatsword, dagger, waraxe, battleaxe, mace, warhammer, bow, crossbow, staff");
        var damageOption = new Option<ushort>("--damage", getDefaultValue: () => 10, description: "Base damage");
        var valueOption = new Option<uint>("--value", getDefaultValue: () => 100, description: "Base value");
        var weightOption = new Option<float>("--weight", getDefaultValue: () => 5, description: "Weight");
        var modelOption = new Option<string?>("--model",
            description: "Model path relative to Data/Meshes (e.g., Weapons\\Iron\\IronSword.nif). Use 'iron-sword', 'steel-sword', 'iron-dagger', or 'hunting-bow' for vanilla presets.");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("add-weapon", "Add a weapon record to a plugin")
        {
            pluginArg, editorIdArg, nameOption, typeOption, damageOption, valueOption, weightOption, modelOption, dryRunOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var editorId = context.ParseResult.GetValueForArgument(editorIdArg);
            var name = context.ParseResult.GetValueForOption(nameOption);
            var type = context.ParseResult.GetValueForOption(typeOption) ?? "sword";
            var damage = context.ParseResult.GetValueForOption(damageOption);
            var value = context.ParseResult.GetValueForOption(valueOption);
            var weight = context.ParseResult.GetValueForOption(weightOption);
            var model = context.ParseResult.GetValueForOption(modelOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success) { OutputError(loadResult.Error!, json); return; }

            var mod = loadResult.Value!;
            var builder = new WeaponBuilder(mod, editorId)
                .WithDamage(damage)
                .WithValue(value)
                .WithWeight(weight);

            if (!string.IsNullOrEmpty(name)) builder.WithName(name);

            // Apply model if specified
            if (!string.IsNullOrEmpty(model))
            {
                switch (model.ToLowerInvariant())
                {
                    case "iron-sword": builder.WithIronSwordModel(); break;
                    case "steel-sword": builder.WithSteelSwordModel(); break;
                    case "iron-dagger": builder.WithIronDaggerModel(); break;
                    case "hunting-bow": builder.WithHuntingBowModel(); break;
                    default: builder.WithModel(model); break;
                }
            }

            switch (type.ToLowerInvariant())
            {
                case "greatsword": builder.AsGreatsword(); break;
                case "dagger": builder.AsDagger(); break;
                case "waraxe": builder.AsWarAxe(); break;
                case "battleaxe": builder.AsBattleaxe(); break;
                case "mace": builder.AsMace(); break;
                case "warhammer": builder.AsWarhammer(); break;
                case "bow": builder.AsBow(); break;
                case "crossbow": builder.AsCrossbow(); break;
                case "staff": builder.AsStaff(); break;
                default: builder.AsSword(); break;
            }

            var weapon = builder.Build();

            // Conditional save based on dry-run
            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            editorId = weapon.EditorID,
                            formId = weapon.FormKey.ToString(),
                            name = weapon.Name?.String,
                            model = weapon.Model?.File.DataRelativePath,
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Added weapon: {weapon.EditorID} ({weapon.FormKey})" + (weapon.Model != null ? $" [Model: {weapon.Model.File}]" : " [No model - weapon will be invisible!]");
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateAddArmorCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editorId", "Editor ID for the armor");
        var nameOption = new Option<string?>("--name", "Display name for the armor");
        var typeOption = new Option<string>("--type", getDefaultValue: () => "light",
            description: "Armor type: light, heavy, clothing");
        var slotOption = new Option<string>("--slot", getDefaultValue: () => "body",
            description: "Body slot: head, body, hands, feet, shield");
        var ratingOption = new Option<float>("--rating", getDefaultValue: () => 10, description: "Armor rating");
        var valueOption = new Option<uint>("--value", getDefaultValue: () => 100, description: "Base value");
        var modelOption = new Option<string?>("--model",
            description: "Model path relative to Data/Meshes. Presets: 'iron-cuirass', 'iron-helmet', 'iron-gauntlets', 'iron-boots', 'iron-shield'");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("add-armor", "Add an armor record to a plugin")
        {
            pluginArg, editorIdArg, nameOption, typeOption, slotOption, ratingOption, valueOption, modelOption, dryRunOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var editorId = context.ParseResult.GetValueForArgument(editorIdArg);
            var name = context.ParseResult.GetValueForOption(nameOption);
            var type = context.ParseResult.GetValueForOption(typeOption) ?? "light";
            var slot = context.ParseResult.GetValueForOption(slotOption) ?? "body";
            var rating = context.ParseResult.GetValueForOption(ratingOption);
            var value = context.ParseResult.GetValueForOption(valueOption);
            var model = context.ParseResult.GetValueForOption(modelOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success) { OutputError(loadResult.Error!, json); return; }

            var mod = loadResult.Value!;
            var builder = new ArmorBuilder(mod, editorId)
                .WithArmorRating(rating)
                .WithValue(value);

            if (!string.IsNullOrEmpty(name)) builder.WithName(name);

            // Apply model if specified
            if (!string.IsNullOrEmpty(model))
            {
                switch (model.ToLowerInvariant())
                {
                    case "iron-cuirass": builder.WithIronCuirassModel(); break;
                    case "iron-helmet": builder.WithIronHelmetModel(); break;
                    case "iron-gauntlets": builder.WithIronGauntletsModel(); break;
                    case "iron-boots": builder.WithIronBootsModel(); break;
                    case "iron-shield": builder.WithIronShieldModel(); break;
                    default: builder.WithWorldModel(model); break;
                }
            }

            switch (type.ToLowerInvariant())
            {
                case "heavy": builder.AsHeavyArmor(); break;
                case "clothing": builder.AsClothing(); break;
                default: builder.AsLightArmor(); break;
            }

            switch (slot.ToLowerInvariant())
            {
                case "head": builder.ForHead(); break;
                case "hands": builder.ForHands(); break;
                case "feet": builder.ForFeet(); break;
                case "shield": builder.ForShield(); break;
                default: builder.ForBody(); break;
            }

            var armor = builder.Build();

            // Conditional save based on dry-run
            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            var hasModel = armor.WorldModel?.Male?.Model?.File != null;
            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            editorId = armor.EditorID,
                            formId = armor.FormKey.ToString(),
                            name = armor.Name?.String,
                            model = armor.WorldModel?.Male?.Model?.File?.DataRelativePath,
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Added armor: {armor.EditorID} ({armor.FormKey})" + (hasModel ? $" [Model: {armor.WorldModel?.Male?.Model?.File}]" : " [No model - armor will be invisible!]");
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateAddNpcCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editorId", "Editor ID for the NPC");
        var nameOption = new Option<string?>("--name", "Display name for the NPC");
        var levelOption = new Option<short>("--level", getDefaultValue: () => 1, description: "NPC level");
        var femaleOption = new Option<bool>("--female", "NPC is female");
        var essentialOption = new Option<bool>("--essential", "NPC is essential");
        var uniqueOption = new Option<bool>("--unique", "NPC is unique");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("add-npc", "Add an NPC record to a plugin")
        {
            pluginArg, editorIdArg, nameOption, levelOption, femaleOption, essentialOption, uniqueOption, dryRunOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var editorId = context.ParseResult.GetValueForArgument(editorIdArg);
            var name = context.ParseResult.GetValueForOption(nameOption);
            var level = context.ParseResult.GetValueForOption(levelOption);
            var female = context.ParseResult.GetValueForOption(femaleOption);
            var essential = context.ParseResult.GetValueForOption(essentialOption);
            var unique = context.ParseResult.GetValueForOption(uniqueOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success) { OutputError(loadResult.Error!, json); return; }

            var mod = loadResult.Value!;
            var builder = new NpcBuilder(mod, editorId).WithLevel(level);

            if (!string.IsNullOrEmpty(name)) builder.WithName(name);
            if (female) builder.AsFemale();
            if (essential) builder.AsEssential();
            if (unique) builder.AsUnique();

            var npc = builder.Build();

            // Conditional save based on dry-run
            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            editorId = npc.EditorID,
                            formId = npc.FormKey.ToString(),
                            name = npc.Name?.String,
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Added NPC: {npc.EditorID} ({npc.FormKey})";
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateAddBookCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editorId", "Editor ID for the book");
        var nameOption = new Option<string?>("--name", "Display name for the book");
        var textOption = new Option<string?>("--text", "Book text content");
        var valueOption = new Option<uint>("--value", getDefaultValue: () => 10, description: "Base value");
        var weightOption = new Option<float>("--weight", getDefaultValue: () => 1, description: "Weight");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("add-book", "Add a book record to a plugin")
        {
            pluginArg, editorIdArg, nameOption, textOption, valueOption, weightOption, dryRunOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var editorId = context.ParseResult.GetValueForArgument(editorIdArg);
            var name = context.ParseResult.GetValueForOption(nameOption);
            var text = context.ParseResult.GetValueForOption(textOption);
            var value = context.ParseResult.GetValueForOption(valueOption);
            var weight = context.ParseResult.GetValueForOption(weightOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success) { OutputError(loadResult.Error!, json); return; }

            var mod = loadResult.Value!;
            var builder = new BookBuilder(mod, editorId)
                .WithValue(value)
                .WithWeight(weight);

            if (!string.IsNullOrEmpty(name)) builder.WithName(name);
            if (!string.IsNullOrEmpty(text)) builder.WithText(text);

            var book = builder.Build();

            // Conditional save based on dry-run
            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            editorId = book.EditorID,
                            formId = book.FormKey.ToString(),
                            name = book.Name?.String,
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Added book: {book.EditorID} ({book.FormKey})";
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateAddPerkCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editorId", "Editor ID for the perk");
        var nameOption = new Option<string?>("--name", "Display name for the perk");
        var descOption = new Option<string?>("--description", "Perk description");
        var playableOption = new Option<bool>("--playable", "Perk is playable");
        var hiddenOption = new Option<bool>("--hidden", "Perk is hidden");
        var effectOption = new Option<string?>("--effect",
            description: "Effect preset: weapon-damage, damage-reduction, armor, spell-cost, spell-power, spell-duration, sneak-attack, pickpocket, prices");
        var bonusOption = new Option<float>("--bonus", getDefaultValue: () => 25, description: "Bonus percentage (e.g., 25 for +25%)");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("add-perk", "Add a perk record to a plugin")
        {
            pluginArg, editorIdArg, nameOption, descOption, playableOption, hiddenOption, effectOption, bonusOption, dryRunOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var editorId = context.ParseResult.GetValueForArgument(editorIdArg);
            var name = context.ParseResult.GetValueForOption(nameOption);
            var desc = context.ParseResult.GetValueForOption(descOption);
            var playable = context.ParseResult.GetValueForOption(playableOption);
            var hidden = context.ParseResult.GetValueForOption(hiddenOption);
            var effect = context.ParseResult.GetValueForOption(effectOption);
            var bonus = context.ParseResult.GetValueForOption(bonusOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success) { OutputError(loadResult.Error!, json); return; }

            var mod = loadResult.Value!;
            var builder = new PerkBuilder(mod, editorId);

            if (!string.IsNullOrEmpty(name)) builder.WithName(name);
            if (!string.IsNullOrEmpty(desc)) builder.WithDescription(desc);
            if (playable) builder.AsPlayable();
            if (hidden) builder.AsHidden();

            // Apply effect if specified
            bool hasEffect = false;
            if (!string.IsNullOrEmpty(effect))
            {
                hasEffect = true;
                switch (effect.ToLowerInvariant())
                {
                    case "weapon-damage": builder.WithWeaponDamageBonus(bonus); break;
                    case "damage-reduction": builder.WithDamageReduction(bonus); break;
                    case "armor": builder.WithArmorBonus(bonus); break;
                    case "spell-cost": builder.WithSpellCostReduction(bonus); break;
                    case "spell-power": builder.WithSpellPowerBonus(bonus); break;
                    case "spell-duration": builder.WithSpellDurationBonus(bonus); break;
                    case "sneak-attack": builder.WithSneakAttackBonus(1.0f + bonus / 100f); break;
                    case "pickpocket": builder.WithPickpocketBonus(bonus); break;
                    case "prices": builder.WithBetterPrices(bonus); break;
                    default:
                        hasEffect = false;
                        break;
                }
            }

            var perk = builder.Build();

            // Conditional save based on dry-run
            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            var effectCount = perk.Effects.Count;
            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            editorId = perk.EditorID,
                            formId = perk.FormKey.ToString(),
                            name = perk.Name?.String,
                            effectCount,
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Added perk: {perk.EditorID} ({perk.FormKey})";
                if (hasEffect)
                    msg += $" [{effectCount} entry/entries]";
                else
                    msg += " [No entries - perk will do nothing! Use --effect to add one]";
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateListMastersCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");

        var cmd = new Command("list-masters", "List master file dependencies")
        {
            pluginArg
        };

        cmd.SetHandler((plugin, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var result = service.GetPluginInfo(plugin);

            if (json)
            {
                Console.WriteLine(new { success = result.Success, result = new { masters = result.Value?.MasterFiles }, error = result.Error }.ToJson());
            }
            else if (result.Success && result.Value != null)
            {
                Console.WriteLine($"Master files for {result.Value.FileName}:");
                if (result.Value.MasterFiles.Count == 0)
                    Console.WriteLine("  (none)");
                else
                    foreach (var master in result.Value.MasterFiles)
                        Console.WriteLine($"  - {master}");
            }
            else
            { Console.Error.WriteLine($"Error: {result.Error}"); Environment.ExitCode = 1; }
        }, pluginArg, _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateMergeCommand()
    {
        var sourceArg = new Argument<string>("source", "Source plugin to merge from");
        var targetArg = new Argument<string>("target", "Target plugin to merge into");
        var outputOption = new Option<string?>("--output", "Output path (defaults to overwriting target)");

        var cmd = new Command("merge", "Merge records from one plugin into another")
        {
            sourceArg, targetArg, outputOption
        };

        cmd.SetHandler((source, target, output, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var sourceResult = service.LoadPluginReadOnly(source);
            if (!sourceResult.Success) { OutputError(sourceResult.Error!, json); return; }

            var targetResult = service.LoadPluginForEdit(target);
            if (!targetResult.Success) { OutputError(targetResult.Error!, json); return; }

            var sourceMod = sourceResult.Value!;
            var targetMod = targetResult.Value!;

            int recordsCopied = 0;

            // Copy all major record types using DeepCopy
            foreach (var quest in sourceMod.Quests) { targetMod.Quests.Add(quest.DeepCopy()); recordsCopied++; }
            foreach (var spell in sourceMod.Spells) { targetMod.Spells.Add(spell.DeepCopy()); recordsCopied++; }
            foreach (var global in sourceMod.Globals) { targetMod.Globals.Add(global.DeepCopy()); recordsCopied++; }
            foreach (var weapon in sourceMod.Weapons) { targetMod.Weapons.Add(weapon.DeepCopy()); recordsCopied++; }
            foreach (var armor in sourceMod.Armors) { targetMod.Armors.Add(armor.DeepCopy()); recordsCopied++; }
            foreach (var npc in sourceMod.Npcs) { targetMod.Npcs.Add(npc.DeepCopy()); recordsCopied++; }
            foreach (var book in sourceMod.Books) { targetMod.Books.Add(book.DeepCopy()); recordsCopied++; }
            foreach (var perk in sourceMod.Perks) { targetMod.Perks.Add(perk.DeepCopy()); recordsCopied++; }

            var saveResult = service.SavePlugin(targetMod, output ?? target);

            if (json)
                Console.WriteLine(new { success = saveResult.Success, result = new { recordsCopied, outputPath = output ?? target }, error = saveResult.Error }.ToJson());
            else if (saveResult.Success)
                Console.WriteLine($"Merged {recordsCopied} records from {source} into {output ?? target}");
            else
            { Console.Error.WriteLine($"Error: {saveResult.Error}"); Environment.ExitCode = 1; }
        }, sourceArg, targetArg, outputOption, _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateDebugTypesCommand()
    {
        var patternArg = new Argument<string?>(
            "pattern",
            () => null,
            "Pattern to filter types (e.g., 'Quest*', 'QuestAlias')");
        var allOption = new Option<bool>("--all", "Show all Skyrim record types");

        var cmd = new Command("debug-types", "Show Mutagen type structures for debugging")
        {
            patternArg,
            allOption
        };

        cmd.SetHandler((pattern, all, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new TypeInspectionService(logger);

            var result = service.GetAllMutagenTypes(all ? "*" : pattern);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success)
            {
                foreach (var type in result.Value!)
                {
                    Console.WriteLine($"\n{type.Name} ({type.FullName})");
                    Console.WriteLine($"  Type: {(type.IsInterface ? "Interface" : "Class")}");

                    if (type.Properties.Count > 0)
                    {
                        Console.WriteLine("  Properties:");
                        foreach (var prop in type.Properties)
                        {
                            var nullable = prop.IsNullable ? "?" : "";
                            var collection = prop.IsCollection ? "[]" : "";
                            Console.WriteLine($"    {prop.Name}: {prop.Type}{nullable}{collection}");
                        }
                    }

                    if (type.Notes.Count > 0)
                    {
                        Console.WriteLine("  Notes:");
                        foreach (var note in type.Notes)
                            Console.WriteLine($"    - {note}");
                    }
                }

                Console.WriteLine($"\nFound {result.Value.Count} type(s)");
            }
            else
            {
                OutputError(result.Error!, json, result.Suggestions);
            }
        }, patternArg, allOption, _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateAutoFillCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var questOption = new Option<string>("--quest", "Quest EditorID") { IsRequired = true };
        var aliasOption = new Option<string?>("--alias", "Alias name (for alias scripts)");
        var scriptOption = new Option<string>("--script", "Script name") { IsRequired = true };
        var scriptDirOption = new Option<string>("--script-dir", "PSC source directory") { IsRequired = true };
        var dataFolderOption = new Option<string>("--data-folder", "Skyrim Data folder") { IsRequired = true };
        var noCacheOption = new Option<bool>("--no-cache", "Don't use cached link cache");

        var cmd = new Command("auto-fill", "Auto-fill script properties from PSC files")
        {
            pluginArg,
            questOption,
            aliasOption,
            scriptOption,
            scriptDirOption,
            dataFolderOption,
            noCacheOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var quest = context.ParseResult.GetValueForOption(questOption)!; // Required option
            var alias = context.ParseResult.GetValueForOption(aliasOption);
            var script = context.ParseResult.GetValueForOption(scriptOption)!; // Required option
            var scriptDir = context.ParseResult.GetValueForOption(scriptDirOption)!; // Required option
            var dataFolder = context.ParseResult.GetValueForOption(dataFolderOption)!; // Required option
            var noCache = context.ParseResult.GetValueForOption(noCacheOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var pluginService = new PluginService(logger);
            var linkCacheManager = new LinkCacheManager(logger);
            var autoFillService = new AutoFillService(logger, linkCacheManager);

            // Clear cache if requested
            if (noCache)
            {
                linkCacheManager.ClearCache();
            }

            // Load plugin
            var loadResult = pluginService.LoadPluginForEdit(plugin);
            if (!loadResult.Success)
            {
                OutputError(loadResult.Error!, json, loadResult.Suggestions);
                return;
            }

            var mod = loadResult.Value!;

            // Get PSC file path
            var pscPath = Path.Combine(scriptDir, $"{script}.psc");

            // Auto-fill based on whether alias is specified
            Result<AutoFillResult> result;
            if (!string.IsNullOrEmpty(alias))
            {
                result = autoFillService.AutoFillAliasScript(mod, quest, alias, script, pscPath, dataFolder);
            }
            else
            {
                result = autoFillService.AutoFillQuestScript(mod, quest, script, pscPath, dataFolder);
            }

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success)
            {
                var data = result.Value!;
                Console.WriteLine($"Auto-fill complete for {data.ScriptName}:");
                Console.WriteLine($"  Filled: {data.FilledCount} properties");
                Console.WriteLine($"  Skipped: {data.SkippedCount} properties (primitives or already set)");
                Console.WriteLine($"  Not found: {data.NotFoundCount} properties (not in Skyrim.esm)");

                if (data.FilledProperties.Count > 0)
                {
                    Console.WriteLine("\nFilled properties:");
                    foreach (var prop in data.FilledProperties)
                        Console.WriteLine($"  - {prop}");
                }

                if (data.NotFoundProperties.Count > 0)
                {
                    Console.WriteLine("\nNot found in Skyrim.esm:");
                    foreach (var prop in data.NotFoundProperties)
                        Console.WriteLine($"  - {prop}");
                }

                // Save plugin
                var saveResult = pluginService.SavePlugin(mod, plugin);
                if (saveResult.Success)
                {
                    Console.WriteLine($"\nSaved: {plugin}");
                }
                else
                {
                    OutputError(saveResult.Error!, json);
                }
            }
            else
            {
                OutputError(result.Error!, json, result.Suggestions);
            }
        });

        return cmd;
    }

    private static Command CreateAutoFillAllCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var scriptDirOption = new Option<string>("--script-dir", "PSC source directory") { IsRequired = true };
        var dataFolderOption = new Option<string>("--data-folder", "Skyrim Data folder") { IsRequired = true };
        var noCacheOption = new Option<bool>("--no-cache", "Don't use cached link cache");

        var cmd = new Command("auto-fill-all", "Auto-fill all scripts in the mod")
        {
            pluginArg,
            scriptDirOption,
            dataFolderOption,
            noCacheOption
        };

        cmd.SetHandler((plugin, scriptDir, dataFolder, noCache, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var pluginService = new PluginService(logger);
            var linkCacheManager = new LinkCacheManager(logger);
            var autoFillService = new AutoFillService(logger, linkCacheManager);
            var bulkService = new BulkAutoFillService(logger, autoFillService, linkCacheManager);

            // Clear cache if requested
            if (noCache)
            {
                linkCacheManager.ClearCache();
            }

            // Load plugin
            var loadResult = pluginService.LoadPluginForEdit(plugin);
            if (!loadResult.Success)
            {
                OutputError(loadResult.Error!, json, loadResult.Suggestions);
                return;
            }

            var mod = loadResult.Value!;

            // Perform bulk auto-fill
            var result = bulkService.AutoFillAll(mod, scriptDir, dataFolder);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success)
            {
                var data = result.Value!;
                Console.WriteLine($"Bulk auto-fill complete:");
                Console.WriteLine($"  Scripts processed: {data.TotalScripts}");
                Console.WriteLine($"  Scripts with filled properties: {data.FilledScripts}");
                Console.WriteLine($"  Scripts skipped (no PSC): {data.SkippedScripts}");
                Console.WriteLine($"  Total properties filled: {data.TotalPropertiesFilled}");

                if (data.Errors.Count > 0)
                {
                    Console.WriteLine("\nErrors:");
                    foreach (var error in data.Errors.Take(10))
                        Console.WriteLine($"  - {error}");
                    if (data.Errors.Count > 10)
                        Console.WriteLine($"  ... and {data.Errors.Count - 10} more");
                }

                if (data.Details.Count > 0 && verbose)
                {
                    Console.WriteLine("\nDetails:");
                    foreach (var detail in data.Details.Take(20))
                        Console.WriteLine($"  {detail}");
                    if (data.Details.Count > 20)
                        Console.WriteLine($"  ... and {data.Details.Count - 20} more");
                }

                // Save plugin
                var saveResult = pluginService.SavePlugin(mod, plugin);
                if (saveResult.Success)
                {
                    Console.WriteLine($"\nSaved: {plugin}");
                }
                else
                {
                    OutputError(saveResult.Error!, json);
                }
            }
            else
            {
                OutputError(result.Error!, json, result.Suggestions);
            }
        }, pluginArg, scriptDirOption, dataFolderOption, noCacheOption, _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateAddLeveledItemCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editorId", "Editor ID for the leveled item");
        var nameOption = new Option<string?>("--name", "Display name for the leveled item");
        var chanceNoneOption = new Option<byte>("--chance-none", getDefaultValue: () => 0,
            description: "Chance (0-100) that the list returns nothing");
        var addEntryOption = new Option<string[]?>("--add-entry",
            description: "Add entry in format: editorId,level,count (e.g., GoldBase,1,50). Can be used multiple times.");
        var presetOption = new Option<string?>("--preset",
            description: "Use preset: low-treasure, medium-treasure, high-treasure, guaranteed-loot");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("add-leveled-item", "Add a leveled item list record to a plugin")
        {
            pluginArg, editorIdArg, nameOption, chanceNoneOption, addEntryOption, presetOption, dryRunOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var editorId = context.ParseResult.GetValueForArgument(editorIdArg);
            var name = context.ParseResult.GetValueForOption(nameOption);
            var chanceNone = context.ParseResult.GetValueForOption(chanceNoneOption);
            var entries = context.ParseResult.GetValueForOption(addEntryOption);
            var preset = context.ParseResult.GetValueForOption(presetOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success) { OutputError(loadResult.Error!, json); return; }

            var mod = loadResult.Value!;
            var builder = new LeveledItemBuilder(mod, editorId);

            builder.WithChanceNone(chanceNone);

            // Apply preset
            if (!string.IsNullOrEmpty(preset))
            {
                switch (preset.ToLowerInvariant())
                {
                    case "low-treasure": builder.AsLowTreasure(); break;
                    case "medium-treasure": builder.AsMediumTreasure(); break;
                    case "high-treasure": builder.AsHighTreasure(); break;
                    case "guaranteed-loot": builder.AsGuaranteedLoot(); break;
                    default:
                        OutputError($"Unknown preset: {preset}. Use: low-treasure, medium-treasure, high-treasure, guaranteed-loot", json);
                        return;
                }
            }

            // Add entries
            if (entries != null && entries.Length > 0)
            {
                foreach (var entry in entries)
                {
                    var parts = entry.Split(',');
                    if (parts.Length < 2 || parts.Length > 3)
                    {
                        OutputError($"Invalid entry format: {entry}. Use: editorId,level[,count]", json);
                        return;
                    }

                    var itemEditorId = parts[0];
                    if (!short.TryParse(parts[1], out var level))
                    {
                        OutputError($"Invalid level in entry: {entry}", json);
                        return;
                    }

                    short count = 1;
                    if (parts.Length == 3 && !short.TryParse(parts[2], out count))
                    {
                        OutputError($"Invalid count in entry: {entry}", json);
                        return;
                    }

                    // Try to find the item in the mod (search weapons, armors, misc items, etc.)
                    FormKey? itemFormKey = null;
                    var weapon = mod.Weapons.FirstOrDefault(w => w.EditorID == itemEditorId);
                    if (weapon != null)
                    {
                        itemFormKey = weapon.FormKey;
                    }
                    else
                    {
                        var armor = mod.Armors.FirstOrDefault(a => a.EditorID == itemEditorId);
                        if (armor != null)
                        {
                            itemFormKey = armor.FormKey;
                        }
                        else
                        {
                            var miscItem = mod.MiscItems.FirstOrDefault(m => m.EditorID == itemEditorId);
                            if (miscItem != null)
                            {
                                itemFormKey = miscItem.FormKey;
                            }
                        }
                    }

                    if (!itemFormKey.HasValue)
                    {
                        OutputError($"Item not found: {itemEditorId}", json);
                        return;
                    }

                    builder.AddEntry(itemFormKey.Value, level, count);
                }
            }

            var leveledItem = builder.Build();

            // Conditional save based on dry-run
            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            editorId = leveledItem.EditorID,
                            formId = leveledItem.FormKey.ToString(),
                            chanceNone = leveledItem.ChanceNone,
                            entryCount = leveledItem.Entries?.Count ?? 0,
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Added leveled item: {leveledItem.EditorID} ({leveledItem.FormKey}) - {leveledItem.Entries?.Count ?? 0} entries";
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateAddFormListCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editorId", "Editor ID for the form list");
        var addFormOption = new Option<string[]?>("--add-form",
            description: "Add form in format: ModName.esp:0xFormId or EditorId. Can be used multiple times.");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("add-form-list", "Add a form list record to a plugin")
        {
            pluginArg, editorIdArg, addFormOption, dryRunOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var editorId = context.ParseResult.GetValueForArgument(editorIdArg);
            var forms = context.ParseResult.GetValueForOption(addFormOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success) { OutputError(loadResult.Error!, json); return; }

            var mod = loadResult.Value!;
            var builder = new FormListBuilder(mod, editorId);

            // Add forms
            if (forms != null && forms.Length > 0)
            {
                foreach (var formStr in forms)
                {
                    // Try to parse as FormKey first
                    if (Mutagen.Bethesda.Plugins.FormKey.TryFactory(formStr, out var formKey))
                    {
                        builder.AddForm(formKey);
                    }
                    else
                    {
                        // Try to find by EditorID in the mod
                        var record = mod.EnumerateMajorRecords().FirstOrDefault(r => r.EditorID == formStr);
                        if (record == null)
                        {
                            OutputError($"Form not found: {formStr}", json);
                            return;
                        }
                        builder.AddForm(record.FormKey);
                    }
                }
            }

            var formList = builder.Build();

            // Conditional save based on dry-run
            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            editorId = formList.EditorID,
                            formId = formList.FormKey.ToString(),
                            formCount = formList.Items?.Count ?? 0,
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Added form list: {formList.EditorID} ({formList.FormKey}) - {formList.Items?.Count ?? 0} forms";
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateAddEncounterZoneCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editorId", "Editor ID for the encounter zone");
        var minLevelOption = new Option<byte>("--min-level", getDefaultValue: () => 1,
            description: "Minimum level");
        var maxLevelOption = new Option<byte>("--max-level", getDefaultValue: () => 0,
            description: "Maximum level (0 = unlimited)");
        var neverResetsOption = new Option<bool>("--never-resets", "Zone never resets");
        var presetOption = new Option<string?>("--preset",
            description: "Use preset: low-level, mid-level, high-level, scaling");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("add-encounter-zone", "Add an encounter zone record to a plugin")
        {
            pluginArg, editorIdArg, minLevelOption, maxLevelOption, neverResetsOption, presetOption, dryRunOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var editorId = context.ParseResult.GetValueForArgument(editorIdArg);
            var minLevel = context.ParseResult.GetValueForOption(minLevelOption);
            var maxLevel = context.ParseResult.GetValueForOption(maxLevelOption);
            var neverResets = context.ParseResult.GetValueForOption(neverResetsOption);
            var preset = context.ParseResult.GetValueForOption(presetOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success) { OutputError(loadResult.Error!, json); return; }

            var mod = loadResult.Value!;
            var builder = new EncounterZoneBuilder(mod, editorId);

            // Apply preset
            if (!string.IsNullOrEmpty(preset))
            {
                switch (preset.ToLowerInvariant())
                {
                    case "low-level": builder.AsLowLevel(); break;
                    case "mid-level": builder.AsMidLevel(); break;
                    case "high-level": builder.AsHighLevel(); break;
                    case "scaling": builder.AsScaling(); break;
                    default:
                        OutputError($"Unknown preset: {preset}. Use: low-level, mid-level, high-level, scaling", json);
                        return;
                }
            }
            else
            {
                builder.WithMinLevel(minLevel);
                builder.WithMaxLevel(maxLevel);
            }

            if (neverResets) builder.NeverResets();

            var encounterZone = builder.Build();

            // Conditional save based on dry-run
            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            editorId = encounterZone.EditorID,
                            formId = encounterZone.FormKey.ToString(),
                            minLevel = encounterZone.MinLevel,
                            maxLevel = encounterZone.MaxLevel,
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Added encounter zone: {encounterZone.EditorID} ({encounterZone.FormKey}) - Level {encounterZone.MinLevel}-{(encounterZone.MaxLevel == 0 ? "unlimited" : encounterZone.MaxLevel.ToString())}";
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateAddLocationCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editorId", "Editor ID for the location");
        var nameOption = new Option<string?>("--name", "Display name for the location");
        var parentOption = new Option<string?>("--parent-location",
            description: "Parent location EditorID or FormKey");
        var keywordOption = new Option<string[]?>("--add-keyword",
            description: "Add keyword by EditorID or FormKey. Can be used multiple times.");
        var presetOption = new Option<string?>("--preset",
            description: "Use preset: inn, city, dungeon, dwelling");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("add-location", "Add a location record to a plugin")
        {
            pluginArg, editorIdArg, nameOption, parentOption, keywordOption, presetOption, dryRunOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var editorId = context.ParseResult.GetValueForArgument(editorIdArg);
            var name = context.ParseResult.GetValueForOption(nameOption);
            var parent = context.ParseResult.GetValueForOption(parentOption);
            var keywords = context.ParseResult.GetValueForOption(keywordOption);
            var preset = context.ParseResult.GetValueForOption(presetOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success) { OutputError(loadResult.Error!, json); return; }

            var mod = loadResult.Value!;
            var builder = new LocationBuilder(mod, editorId);

            if (!string.IsNullOrEmpty(name)) builder.WithName(name);

            // Apply preset
            if (!string.IsNullOrEmpty(preset))
            {
                switch (preset.ToLowerInvariant())
                {
                    case "inn": builder.AsInn(); break;
                    case "city": builder.AsCity(); break;
                    case "dungeon": builder.AsDungeon(); break;
                    case "dwelling": builder.AsDwelling(); break;
                    default:
                        OutputError($"Unknown preset: {preset}. Use: inn, city, dungeon, dwelling", json);
                        return;
                }
            }

            // Add parent location
            if (!string.IsNullOrEmpty(parent))
            {
                if (Mutagen.Bethesda.Plugins.FormKey.TryFactory(parent, out var parentFormKey))
                {
                    builder.WithParentLocation(parentFormKey);
                }
                else
                {
                    var parentLoc = mod.Locations.FirstOrDefault(l => l.EditorID == parent);
                    if (parentLoc == null)
                    {
                        OutputError($"Parent location not found: {parent}", json);
                        return;
                    }
                    builder.WithParentLocation(parentLoc.FormKey);
                }
            }

            // Add keywords
            if (keywords != null && keywords.Length > 0)
            {
                foreach (var keywordStr in keywords)
                {
                    if (Mutagen.Bethesda.Plugins.FormKey.TryFactory(keywordStr, out var keywordFormKey))
                    {
                        builder.AddKeyword(keywordFormKey);
                    }
                    else
                    {
                        var keyword = mod.Keywords.FirstOrDefault(k => k.EditorID == keywordStr);
                        if (keyword == null)
                        {
                            OutputError($"Keyword not found: {keywordStr}", json);
                            return;
                        }
                        builder.AddKeyword(keyword.FormKey);
                    }
                }
            }

            var location = builder.Build();

            // Conditional save based on dry-run
            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            editorId = location.EditorID,
                            formId = location.FormKey.ToString(),
                            name = location.Name?.String,
                            keywordCount = location.Keywords?.Count ?? 0,
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Added location: {location.EditorID} ({location.FormKey})";
                if (!string.IsNullOrEmpty(location.Name?.String)) msg += $" - {location.Name}";
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateAddOutfitCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editorId", "Editor ID for the outfit");
        var addItemOption = new Option<string[]?>("--add-item",
            description: "Add item (armor/weapon) by EditorID or FormKey. Can be used multiple times.");
        var presetOption = new Option<string?>("--preset",
            description: "Use preset: guard, farmer, mage, thief");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("add-outfit", "Add an outfit record to a plugin")
        {
            pluginArg, editorIdArg, addItemOption, presetOption, dryRunOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var editorId = context.ParseResult.GetValueForArgument(editorIdArg);
            var items = context.ParseResult.GetValueForOption(addItemOption);
            var preset = context.ParseResult.GetValueForOption(presetOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success) { OutputError(loadResult.Error!, json); return; }

            var mod = loadResult.Value!;
            var builder = new OutfitBuilder(mod, editorId);

            // Apply preset
            if (!string.IsNullOrEmpty(preset))
            {
                switch (preset.ToLowerInvariant())
                {
                    case "guard": builder.AsGuard(); break;
                    case "farmer": builder.AsFarmer(); break;
                    case "mage": builder.AsMage(); break;
                    case "thief": builder.AsThief(); break;
                    default:
                        OutputError($"Unknown preset: {preset}. Use: guard, farmer, mage, thief", json);
                        return;
                }
            }

            // Add items
            if (items != null && items.Length > 0)
            {
                foreach (var itemStr in items)
                {
                    if (Mutagen.Bethesda.Plugins.FormKey.TryFactory(itemStr, out var itemFormKey))
                    {
                        builder.AddItem(itemFormKey);
                    }
                    else
                    {
                        // Search in armor and weapon collections
                        var armor = mod.Armors.FirstOrDefault(a => a.EditorID == itemStr);
                        if (armor != null)
                        {
                            builder.AddItem(armor.FormKey);
                        }
                        else
                        {
                            var weapon = mod.Weapons.FirstOrDefault(w => w.EditorID == itemStr);
                            if (weapon == null)
                            {
                                OutputError($"Item not found: {itemStr}", json);
                                return;
                            }
                            builder.AddItem(weapon.FormKey);
                        }
                    }
                }
            }

            var outfit = builder.Build();

            // Conditional save based on dry-run
            Result saveResult;
            if (dryRun)
            {
                saveResult = Result.Ok($"{plugin} (DRY RUN - not saved)");
            }
            else
            {
                saveResult = service.SavePlugin(mod, plugin);
            }

            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            editorId = outfit.EditorID,
                            formId = outfit.FormKey.ToString(),
                            itemCount = outfit.Items?.Count ?? 0,
                            dryRun
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                var msg = $"Added outfit: {outfit.EditorID} ({outfit.FormKey}) - {outfit.Items?.Count ?? 0} items";
                if (dryRun) msg += " [DRY RUN - not saved]";
                Console.WriteLine(msg);
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static void OutputError(string error, bool json, IEnumerable<string>? suggestions = null)
    {
        if (json)
        {
            Console.WriteLine(Result.Fail(error, suggestions: suggestions?.ToList()).ToJson(true));
        }
        else
        {
            Console.Error.WriteLine($"Error: {error}");
            if (suggestions != null)
            {
                Console.Error.WriteLine("Suggestions:");
                foreach (var s in suggestions)
                    Console.Error.WriteLine($"  - {s}");
            }
        }
        Environment.ExitCode = 1;
    }

    private static Command CreateViewRecordCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdOption = new Option<string?>("--editor-id", "EditorID of the record to view");
        var formIdOption = new Option<string?>("--form-id", "FormID of the record to view (e.g., 0x000800)");
        var typeOption = new Option<string?>("--type", "Record type (required with --editor-id)");
        var includeRawOption = new Option<bool>("--include-raw", "Include raw properties via reflection");

        var cmd = new Command("view-record", "View detailed information about a record")
        {
            pluginArg,
            editorIdOption,
            formIdOption,
            typeOption,
            includeRawOption
        };

        cmd.SetHandler((plugin, editorId, formId, type, includeRaw, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var result = service.ViewRecord(plugin, editorId, formId, type, includeRaw);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success && result.Value != null)
            {
                var record = result.Value;
                Console.WriteLine($"Record: {record.EditorId}");
                Console.WriteLine($"FormKey: {record.FormKey}");
                Console.WriteLine($"Type: {record.RecordType}");
                Console.WriteLine();
                Console.WriteLine("Properties:");
                foreach (var (key, value) in record.Properties)
                {
                    if (value is List<Dictionary<string, object?>> list)
                    {
                        Console.WriteLine($"  {key}:");
                        foreach (var item in list)
                        {
                            Console.WriteLine($"    - {string.Join(", ", item.Select(kv => $"{kv.Key}={kv.Value}"))}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  {key}: {value}");
                    }
                }

                if (record.Conditions != null && record.Conditions.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Conditions:");
                    for (int i = 0; i < record.Conditions.Count; i++)
                    {
                        var cond = record.Conditions[i];
                        Console.WriteLine($"  [{i}] {cond.FunctionName} {cond.Operator} {cond.ComparisonValue}");
                        if (!string.IsNullOrEmpty(cond.ParameterA))
                            Console.WriteLine($"      ParamA: {cond.ParameterA}");
                        if (!string.IsNullOrEmpty(cond.ParameterB))
                            Console.WriteLine($"      ParamB: {cond.ParameterB}");
                        Console.WriteLine($"      Flags: {cond.Flags}, RunOn: {cond.RunOn}");
                    }
                }
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                if (result.ErrorContext != null)
                    Console.Error.WriteLine($"Context: {result.ErrorContext}");
                if (result.Suggestions != null)
                {
                    Console.Error.WriteLine("Suggestions:");
                    foreach (var s in result.Suggestions)
                        Console.Error.WriteLine($"  - {s}");
                }
                Environment.ExitCode = 1;
            }
        }, pluginArg, editorIdOption, formIdOption, typeOption, includeRawOption,
           _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateOverrideCommand()
    {
        var sourceArg = new Argument<string>("source", "Path to the source plugin");
        var outputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            description: "Name of the output patch plugin (e.g., Patch.esp)");
        var editorIdOption = new Option<string?>("--editor-id", "EditorID of the record to override");
        var formIdOption = new Option<string?>("--form-id", "FormID of the record to override");
        var typeOption = new Option<string?>("--type", "Record type (required with --editor-id)");
        var dataFolderOption = new Option<string?>("--data-folder", "Data folder path (defaults to source plugin directory)");

        outputOption.IsRequired = true;

        var cmd = new Command("create-override", "Create an override patch for a record")
        {
            sourceArg,
            outputOption,
            editorIdOption,
            formIdOption,
            typeOption,
            dataFolderOption
        };

        cmd.SetHandler((source, output, editorId, formId, type, dataFolder, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var result = service.CreateOverride(
                source,
                output,
                editorId,
                formId,
                type,
                false, // removeConditions - not yet implemented
                dataFolder);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success)
            {
                Console.WriteLine($"Created override patch: {result.Value}");
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                if (result.ErrorContext != null)
                    Console.Error.WriteLine($"Context: {result.ErrorContext}");
                if (result.Suggestions != null)
                {
                    Console.Error.WriteLine("Suggestions:");
                    foreach (var s in result.Suggestions)
                        Console.Error.WriteLine($"  - {s}");
                }
                Environment.ExitCode = 1;
            }
        }, sourceArg, outputOption, editorIdOption, formIdOption, typeOption,
           dataFolderOption, _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateFindRecordCommand()
    {
        var searchOption = new Option<string?>("--search", "Search pattern for EditorID or Name");
        var editorIdOption = new Option<string?>("--editor-id", "Exact EditorID to find");
        var typeOption = new Option<string?>("--type", "Record type to filter by");
        var pluginOption = new Option<string?>("--plugin", "Path to specific plugin to search");
        var dataFolderOption = new Option<string?>("--data-folder", "Data folder to search all plugins");
        var allPluginsOption = new Option<bool>("--all-plugins", "Search all plugins in data folder");

        var cmd = new Command("find-record", "Find records across plugins")
        {
            searchOption,
            editorIdOption,
            typeOption,
            pluginOption,
            dataFolderOption,
            allPluginsOption
        };

        cmd.SetHandler((search, editorId, type, plugin, dataFolder, allPlugins, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var result = service.FindRecords(search, editorId, type, plugin, dataFolder, allPlugins);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success && result.Value != null)
            {
                var records = result.Value;
                Console.WriteLine($"Found {records.Count} record(s):");
                Console.WriteLine();

                foreach (var record in records)
                {
                    Console.WriteLine($"Plugin: {record.PluginName}");
                    Console.WriteLine($"  EditorID: {record.EditorId}");
                    Console.WriteLine($"  FormKey: {record.FormKey}");
                    Console.WriteLine($"  Type: {record.RecordType}");
                    if (!string.IsNullOrEmpty(record.Name))
                        Console.WriteLine($"  Name: {record.Name}");
                    Console.WriteLine();
                }
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                if (result.ErrorContext != null)
                    Console.Error.WriteLine($"Context: {result.ErrorContext}");
                if (result.Suggestions != null)
                {
                    Console.Error.WriteLine("Suggestions:");
                    foreach (var s in result.Suggestions)
                        Console.Error.WriteLine($"  - {s}");
                }
                Environment.ExitCode = 1;
            }
        }, searchOption, editorIdOption, typeOption, pluginOption, dataFolderOption,
           allPluginsOption, _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateListRecordsCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var typeOption = new Option<string?>("--type", "Record type to filter (e.g., weapon, armor, npc, quest, spell)");
        var limitOption = new Option<int>("--limit", () => 100, "Maximum number of records to return");

        var cmd = new Command("list-records", "List all records in a plugin, optionally filtered by type")
        {
            pluginArg, typeOption, limitOption
        };

        cmd.SetHandler((plugin, type, limit, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            // Use FindRecords with wildcard pattern
            var result = service.FindRecords("*", null, type, plugin, null, false);

            if (json)
            {
                if (result.Success && result.Value != null)
                {
                    var records = result.Value.Take(limit).ToList();
                    Console.WriteLine(Result<List<RecordSearchResult>>.Ok(records).ToJson(true));
                }
                else
                {
                    Console.WriteLine(result.ToJson(true));
                }
            }
            else if (result.Success && result.Value != null)
            {
                var records = result.Value;
                var total = records.Count;
                var displayed = records.Take(limit).ToList();

                Console.WriteLine($"Records in {Path.GetFileName(plugin)}:");
                if (!string.IsNullOrEmpty(type))
                    Console.WriteLine($"Filter: {type}");
                Console.WriteLine($"Total: {total}{(total > limit ? $" (showing first {limit})" : "")}");
                Console.WriteLine();

                foreach (var record in displayed)
                {
                    var nameStr = !string.IsNullOrEmpty(record.Name) ? $" - {record.Name}" : "";
                    Console.WriteLine($"  [{record.RecordType}] {record.EditorId ?? "(no EditorID)"}{nameStr} ({record.FormKey})");
                }
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                if (result.Suggestions != null)
                {
                    foreach (var s in result.Suggestions)
                        Console.Error.WriteLine($"  - {s}");
                }
                Environment.ExitCode = 1;
            }
        }, pluginArg, typeOption, limitOption, _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateBatchOverrideCommand()
    {
        var sourceArg = new Argument<string>("source", "Path to the source plugin");
        var outputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            description: "Name of the output patch plugin");
        var typeOption = new Option<string?>("--type", "Record type to filter");
        var searchOption = new Option<string?>("--search", "Search pattern for EditorIDs");
        var editorIdsOption = new Option<string?>("--editor-ids", "Comma-separated list of EditorIDs");
        var dataFolderOption = new Option<string?>("--data-folder", "Data folder path");

        outputOption.IsRequired = true;

        var cmd = new Command("batch-override", "Create override patches for multiple records")
        {
            sourceArg,
            outputOption,
            typeOption,
            searchOption,
            editorIdsOption,
            dataFolderOption
        };

        cmd.SetHandler((source, output, type, search, editorIdsStr, dataFolder, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            string[]? editorIds = null;
            if (!string.IsNullOrEmpty(editorIdsStr))
            {
                editorIds = editorIdsStr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            }

            var result = service.BatchOverride(source, type, search, editorIds, output, dataFolder);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success && result.Value != null)
            {
                var batchResult = result.Value;
                Console.WriteLine($"Created batch override patch: {batchResult.PatchPath}");
                Console.WriteLine($"Modified {batchResult.RecordsModified} record(s):");
                foreach (var record in batchResult.ModifiedRecords)
                {
                    Console.WriteLine($"  - {record}");
                }
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                if (result.ErrorContext != null)
                    Console.Error.WriteLine($"Context: {result.ErrorContext}");
                if (result.Suggestions != null)
                {
                    Console.Error.WriteLine("Suggestions:");
                    foreach (var s in result.Suggestions)
                        Console.Error.WriteLine($"  - {s}");
                }
                Environment.ExitCode = 1;
            }
        }, sourceArg, outputOption, typeOption, searchOption, editorIdsOption,
           dataFolderOption, _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateCompareRecordCommand()
    {
        var plugin1Arg = new Argument<string>("plugin1", "Path to first plugin");
        var plugin2Arg = new Argument<string>("plugin2", "Path to second plugin");
        var editorIdOption = new Option<string?>("--editor-id", "EditorID of the record to compare");
        var formIdOption = new Option<string?>("--form-id", "FormID of the record to compare");
        var typeOption = new Option<string?>("--type", "Record type (required with --editor-id)");

        var cmd = new Command("compare-record", "Compare two versions of the same record")
        {
            plugin1Arg,
            plugin2Arg,
            editorIdOption,
            formIdOption,
            typeOption
        };

        cmd.SetHandler((plugin1, plugin2, editorId, formId, type, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var result = service.CompareRecords(plugin1, plugin2, editorId, formId, type);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success && result.Value != null)
            {
                var comparison = result.Value;

                Console.WriteLine($"Comparing: {comparison.Original.EditorId} ({comparison.Original.FormKey})");
                Console.WriteLine($"Plugin 1: {plugin1}");
                Console.WriteLine($"Plugin 2: {plugin2}");
                Console.WriteLine();

                if (comparison.Differences.Count == 0)
                {
                    Console.WriteLine("No differences found - records are identical");
                }
                else
                {
                    Console.WriteLine($"Found {comparison.Differences.Count} difference(s):");
                    Console.WriteLine();

                    foreach (var (field, diff) in comparison.Differences)
                    {
                        Console.WriteLine($"Field: {field}");
                        Console.WriteLine($"  Original:  {diff.OriginalValue ?? "(null)"}");
                        Console.WriteLine($"  Modified:  {diff.ModifiedValue ?? "(null)"}");
                        Console.WriteLine();
                    }
                }
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                if (result.ErrorContext != null)
                    Console.Error.WriteLine($"Context: {result.ErrorContext}");
                if (result.Suggestions != null)
                {
                    Console.Error.WriteLine("Suggestions:");
                    foreach (var s in result.Suggestions)
                        Console.Error.WriteLine($"  - {s}");
                }
                Environment.ExitCode = 1;
            }
        }, plugin1Arg, plugin2Arg, editorIdOption, formIdOption, typeOption,
           _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateConflictsCommand()
    {
        var dataFolderArg = new Argument<string>("data-folder", "Path to Skyrim Data folder");
        var pluginOption = new Option<string?>("--plugin", "Check conflicts for this plugin");
        var editorIdOption = new Option<string?>("--editor-id", "Check conflicts for this EditorID");
        var formIdOption = new Option<string?>("--form-id", "Check conflicts for this FormID");
        var typeOption = new Option<string?>("--type", "Record type (required with --editor-id)");

        var cmd = new Command("conflicts", "Detect load order conflicts")
        {
            dataFolderArg,
            pluginOption,
            editorIdOption,
            formIdOption,
            typeOption
        };

        cmd.SetHandler((dataFolder, plugin, editorId, formId, type, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var result = service.DetectConflicts(plugin, editorId, formId, type, dataFolder);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success && result.Value != null)
            {
                var report = result.Value;

                Console.WriteLine("Conflict Report:");
                if (!string.IsNullOrEmpty(report.EditorId))
                    Console.WriteLine($"EditorID: {report.EditorId}");
                if (!string.IsNullOrEmpty(report.FormKey))
                    Console.WriteLine($"FormKey: {report.FormKey}");
                Console.WriteLine();

                Console.WriteLine($"Found {report.Conflicts.Count} plugin(s) modifying this record:");
                Console.WriteLine();

                foreach (var conflict in report.Conflicts)
                {
                    var winner = conflict.IsWinner ? " [WINNER]" : "";
                    Console.WriteLine($"[{conflict.LoadOrder:D3}] {conflict.PluginName}{winner}");
                }

                Console.WriteLine();
                Console.WriteLine($"Winning override: {report.WinningPlugin}");
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                if (result.ErrorContext != null)
                    Console.Error.WriteLine($"Context: {result.ErrorContext}");
                if (result.Suggestions != null)
                {
                    Console.Error.WriteLine("Suggestions:");
                    foreach (var s in result.Suggestions)
                        Console.Error.WriteLine($"  - {s}");
                }
                Environment.ExitCode = 1;
            }
        }, dataFolderArg, pluginOption, editorIdOption, formIdOption, typeOption,
           _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateListConditionsCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdOption = new Option<string?>("--editor-id", "EditorID of the record");
        var formIdOption = new Option<string?>("--form-id", "FormID of the record");
        var typeOption = new Option<string?>("--type", "Record type (required with --editor-id)");

        var cmd = new Command("list-conditions", "List all conditions on a record")
        {
            pluginArg,
            editorIdOption,
            formIdOption,
            typeOption
        };

        cmd.SetHandler((plugin, editorId, formId, type, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var result = service.ListConditions(plugin, editorId, formId, type);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success && result.Value != null)
            {
                var conditions = result.Value;

                if (conditions.Count == 0)
                {
                    Console.WriteLine("No conditions found on this record");
                }
                else
                {
                    Console.WriteLine($"Found {conditions.Count} condition(s):");
                    Console.WriteLine();

                    for (int i = 0; i < conditions.Count; i++)
                    {
                        var cond = conditions[i];
                        Console.WriteLine($"[{i}] {cond.FunctionName}");
                        Console.WriteLine($"    Operator: {cond.Operator}");
                        Console.WriteLine($"    Comparison: {cond.ComparisonValue}");
                        if (!string.IsNullOrEmpty(cond.Flags))
                            Console.WriteLine($"    Flags: {cond.Flags}");
                        if (!string.IsNullOrEmpty(cond.RunOn))
                            Console.WriteLine($"    RunOn: {cond.RunOn}");
                        if (!string.IsNullOrEmpty(cond.ParameterA))
                            Console.WriteLine($"    ParamA: {cond.ParameterA}");
                        if (!string.IsNullOrEmpty(cond.ParameterB))
                            Console.WriteLine($"    ParamB: {cond.ParameterB}");
                        Console.WriteLine();
                    }
                }
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                if (result.ErrorContext != null)
                    Console.Error.WriteLine($"Context: {result.ErrorContext}");
                if (result.Suggestions != null)
                {
                    Console.Error.WriteLine("Suggestions:");
                    foreach (var s in result.Suggestions)
                        Console.Error.WriteLine($"  - {s}");
                }
                Environment.ExitCode = 1;
            }
        }, pluginArg, editorIdOption, formIdOption, typeOption, _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateRemoveConditionCommand()
    {
        var sourceArg = new Argument<string>("source", "Path to the source plugin");
        var outputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            description: "Name of the output patch plugin");
        var editorIdOption = new Option<string?>("--editor-id", "EditorID of the record");
        var formIdOption = new Option<string?>("--form-id", "FormID of the record");
        var typeOption = new Option<string?>("--type", "Record type (required with --editor-id)");
        var indicesOption = new Option<string>(
            "--indices",
            description: "Comma-separated indices of conditions to remove (e.g., \"0,2,5\")");

        outputOption.IsRequired = true;
        indicesOption.IsRequired = true;

        var cmd = new Command("remove-condition", "Remove specific conditions from a record")
        {
            sourceArg,
            outputOption,
            editorIdOption,
            formIdOption,
            typeOption,
            indicesOption
        };

        cmd.SetHandler((source, output, editorId, formId, type, indicesStr, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            // Parse indices
            int[] indices;
            try
            {
                indices = indicesStr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse)
                    .ToArray();
            }
            catch
            {
                Console.Error.WriteLine("Error: Invalid indices format. Use comma-separated numbers (e.g., \"0,2,5\")");
                Environment.ExitCode = 1;
                return;
            }

            var result = service.RemoveConditions(source, editorId, formId, type, indices, output, null);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success)
            {
                Console.WriteLine($"Created patch with {indices.Length} condition(s) removed:");
                Console.WriteLine(result.Value);
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                if (result.ErrorContext != null)
                    Console.Error.WriteLine($"Context: {result.ErrorContext}");
                if (result.Suggestions != null)
                {
                    Console.Error.WriteLine("Suggestions:");
                    foreach (var s in result.Suggestions)
                        Console.Error.WriteLine($"  - {s}");
                }
                Environment.ExitCode = 1;
            }
        }, sourceArg, outputOption, editorIdOption, formIdOption, typeOption,
           indicesOption, _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateAddConditionCommand()
    {
        // M7.12a: generalized in-place CTDA writer. Modifies the source
        // plugin directly (no patch ESP) and supports any record type
        // whose Mutagen class exposes a writable Conditions list
        // (Perk, Package, etc.). Plumbs comparison value + operator +
        // first-parameter FormKey through to the service so functions
        // like GetFactionRank(<faction>) == <rank> are reachable.
        var pluginArg = new Argument<string>("plugin", "Path to the plugin to modify in-place");
        var editorIdOption = new Option<string?>("--editor-id", "EditorID of the record");
        var formIdOption = new Option<string?>("--form-id", "FormID of the record");
        var typeOption = new Option<string?>("--type", "Record type (required with --editor-id; e.g. perk, package)");
        var functionOption = new Option<string>(
            "--function",
            description: "Condition function name (e.g. GetLevel, IsSneaking, GetFactionRank)");
        var valueOption = new Option<float>(
            "--value",
            getDefaultValue: () => 1.0f,
            description: "Comparison value (default 1.0)");
        var operatorOption = new Option<string>(
            "--operator",
            getDefaultValue: () => "GreaterThanOrEqualTo",
            description: "Comparison operator (EqualTo, NotEqualTo, GreaterThan, GreaterThanOrEqualTo, LessThan, LessThanOrEqualTo)");
        var param1Option = new Option<string?>(
            "--param1",
            description: "Optional FormKey for the function's first reference parameter (e.g. Faction for GetFactionRank). Format: 'FORMID:Plugin.esp'");

        functionOption.IsRequired = true;

        var cmd = new Command("add-condition", "Add a CTDA condition to a record (Perk, Package, etc.) in-place")
        {
            pluginArg,
            editorIdOption,
            formIdOption,
            typeOption,
            functionOption,
            valueOption,
            operatorOption,
            param1Option
        };

        // Use InvocationContext to read parsed values - System.CommandLine
        // SetHandler maxes at 8 typed args, the context-pull pattern is
        // unlimited.
        cmd.SetHandler(context =>
        {
            var parse = context.ParseResult;
            var plugin = parse.GetValueForArgument(pluginArg);
            var editorId = parse.GetValueForOption(editorIdOption);
            var formId = parse.GetValueForOption(formIdOption);
            var type = parse.GetValueForOption(typeOption);
            var function = parse.GetValueForOption(functionOption);
            var value = parse.GetValueForOption(valueOption);
            var op = parse.GetValueForOption(operatorOption);
            var param1 = parse.GetValueForOption(param1Option);
            var json = parse.GetValueForOption(_jsonOption);
            var verbose = parse.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var result = service.AddCondition(
                plugin, editorId, formId, type,
                function!, value, op!, param1, null);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success)
            {
                Console.WriteLine($"Added {function} condition to {result.Value}");
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                if (result.ErrorContext != null)
                    Console.Error.WriteLine($"Context: {result.ErrorContext}");
                if (result.Suggestions != null)
                {
                    Console.Error.WriteLine("Suggestions:");
                    foreach (var s in result.Suggestions)
                        Console.Error.WriteLine($"  - {s}");
                }
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateAddPackageCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editor-id", "Editor ID for the package");
        var typeOption = new Option<string>(
            aliases: new[] { "--type", "-t" },
            description: "Package type (sandbox, travel, sleep, eat, follow, guard, patrol, useitemat, sit, useidlemarker, flee, accompany, castmagic, dialogue, find, ambush, wander, wait, activate, relax, forcegreet, greet, useweapon, usemagic, lockdoors, unlockdoors, dismount, acquire, escortto, say, shout, followto, holdposition, keepaneyeon, hover, orbit)"
        ) { IsRequired = true };
        var radiusOption = new Option<ushort>(
            "--radius",
            getDefaultValue: () => 500,
            description: "Radius for sandbox packages (in units)");
        var locationOption = new Option<string?>(
            "--location",
            description: "FormKey of location reference");
        var bedRefOption = new Option<string?>(
            "--bed",
            description: "FormKey of bed reference (for sleep packages)");
        var furnitureRefOption = new Option<string?>(
            "--furniture",
            description: "FormKey of furniture reference (for eat packages)");
        var targetRefOption = new Option<string?>(
            "--target",
            description: "FormKey of target reference (for follow packages)");
        var markerRefOption = new Option<string?>(
            "--marker",
            description: "FormKey of marker reference (for guard packages)");
        var startHourOption = new Option<byte>(
            "--start-hour",
            getDefaultValue: () => 22,
            description: "Start hour for schedule (0-23)");
        var durationOption = new Option<byte>(
            "--duration",
            getDefaultValue: () => 8,
            description: "Duration in hours");
        var itemRefOption = new Option<string?>(
            "--item-ref",
            description: "FormKey of item/object reference (for useitemat packages)");
        var fleeFromOption = new Option<string?>(
            "--flee-from",
            description: "FormKey of reference to flee from (optional for flee packages)");
        var distanceOption = new Option<ushort>(
            "--distance",
            getDefaultValue: () => 1000,
            description: "Distance for flee packages (in units)");
        var destinationRefOption = new Option<string?>(
            "--destination",
            description: "FormKey of destination reference (for accompany packages)");
        var weaponRefOption = new Option<string?>(
            "--weapon-ref",
            description: "FormKey of weapon reference (for useweapon packages)");
        var spellRefOption = new Option<string?>(
            "--spell-ref",
            description: "FormKey of spell reference (for usemagic packages)");
        var doorRefOption = new Option<string?>(
            "--door-ref",
            description: "FormKey of door reference (for lockdoors/unlockdoors packages)");
        var objectRefOption = new Option<string?>(
            "--object-ref",
            description: "FormKey of object reference (for acquire packages)");
        var escortRefOption = new Option<string?>(
            "--escort-ref",
            description: "FormKey of actor to escort (for escortto packages)");
        var topicRefOption = new Option<string?>(
            "--topic-ref",
            description: "FormKey of dialogue topic (for say packages)");
        var shoutRefOption = new Option<string?>(
            "--shout-ref",
            description: "FormKey of shout (for shout packages)");
        var followRefOption = new Option<string?>(
            "--follow-ref",
            description: "FormKey of actor to follow (for followto packages)");
        var locationRefOption = new Option<string?>(
            "--location-ref",
            description: "FormKey of location (for say packages)");
        // Alias-target options for Travel and Follow packages. When both are
        // set, the package's target is the named alias rather than a fixed
        // REFR. The package's OwnerQuest is set automatically.
        var targetAliasQuestOption = new Option<string?>(
            "--target-alias-quest",
            description: "Editor ID of the quest containing the target alias (use with --target-alias-name for alias-targeted Travel or Follow packages)");
        var targetAliasNameOption = new Option<string?>(
            "--target-alias-name",
            description: "Name of the alias to target (use with --target-alias-quest for alias-targeted Travel or Follow packages)");
        // Follow procedure parameters. Match slot layout extracted from
        // Skyrim.esm:0x00019B2C ("Follow" vanilla template). Defaults mirror
        // the template's defaults so omitting all of these yields a package
        // that runs identically to the vanilla Follow template.
        var minRadiusOption = new Option<float?>(
            "--min-radius",
            description: "Follow: closest distance follower maintains (default: 128)");
        var maxRadiusOption = new Option<float?>(
            "--max-radius",
            description: "Follow: furthest distance before follower runs (default: 256)");
        var goToLeadersGoalOption = new Option<bool?>(
            "--go-to-leaders-goal",
            description: "Follow: walks WITH leader (caravan-style) rather than behind (default: true)");
        var needLosOption = new Option<bool?>(
            "--need-los",
            description: "Follow: requires line-of-sight to target to follow (default: false)");
        var rideHorseOption = new Option<bool?>(
            "--ride-horse",
            description: "Follow: mount a horse when possible (default: false)");

        var cmd = new Command("add-package", "Add an AI package to a plugin")
        {
            pluginArg,
            editorIdArg,
            typeOption,
            radiusOption,
            locationOption,
            bedRefOption,
            furnitureRefOption,
            targetRefOption,
            markerRefOption,
            startHourOption,
            durationOption,
            itemRefOption,
            fleeFromOption,
            distanceOption,
            destinationRefOption,
            weaponRefOption,
            spellRefOption,
            doorRefOption,
            objectRefOption,
            escortRefOption,
            topicRefOption,
            shoutRefOption,
            followRefOption,
            locationRefOption,
            targetAliasQuestOption,
            targetAliasNameOption,
            minRadiusOption,
            maxRadiusOption,
            goToLeadersGoalOption,
            needLosOption,
            rideHorseOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var editorId = context.ParseResult.GetValueForArgument(editorIdArg);
            var type = context.ParseResult.GetValueForOption(typeOption)!;
            var radius = context.ParseResult.GetValueForOption(radiusOption);
            var location = context.ParseResult.GetValueForOption(locationOption);
            var bedRef = context.ParseResult.GetValueForOption(bedRefOption);
            var furnitureRef = context.ParseResult.GetValueForOption(furnitureRefOption);
            var targetRef = context.ParseResult.GetValueForOption(targetRefOption);
            var markerRef = context.ParseResult.GetValueForOption(markerRefOption);
            var startHour = context.ParseResult.GetValueForOption(startHourOption);
            var duration = context.ParseResult.GetValueForOption(durationOption);
            var itemRef = context.ParseResult.GetValueForOption(itemRefOption);
            var fleeFrom = context.ParseResult.GetValueForOption(fleeFromOption);
            var distance = context.ParseResult.GetValueForOption(distanceOption);
            var destinationRef = context.ParseResult.GetValueForOption(destinationRefOption);
            var weaponRef = context.ParseResult.GetValueForOption(weaponRefOption);
            var spellRef = context.ParseResult.GetValueForOption(spellRefOption);
            var doorRef = context.ParseResult.GetValueForOption(doorRefOption);
            var objectRef = context.ParseResult.GetValueForOption(objectRefOption);
            var escortRef = context.ParseResult.GetValueForOption(escortRefOption);
            var topicRef = context.ParseResult.GetValueForOption(topicRefOption);
            var shoutRef = context.ParseResult.GetValueForOption(shoutRefOption);
            var followRef = context.ParseResult.GetValueForOption(followRefOption);
            var locationRef = context.ParseResult.GetValueForOption(locationRefOption);
            var targetAliasQuest = context.ParseResult.GetValueForOption(targetAliasQuestOption);
            var targetAliasName = context.ParseResult.GetValueForOption(targetAliasNameOption);
            var minRadius = context.ParseResult.GetValueForOption(minRadiusOption);
            var maxRadius = context.ParseResult.GetValueForOption(maxRadiusOption);
            var goToLeadersGoal = context.ParseResult.GetValueForOption(goToLeadersGoalOption);
            var needLos = context.ParseResult.GetValueForOption(needLosOption);
            var rideHorse = context.ParseResult.GetValueForOption(rideHorseOption);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            // Build options dictionary
            var options = new Dictionary<string, object>
            {
                ["radius"] = radius,
                ["startHour"] = startHour,
                ["duration"] = duration,
                ["distance"] = distance
            };

            if (!string.IsNullOrEmpty(location))
                options["location"] = location;
            if (!string.IsNullOrEmpty(bedRef))
                options["bedRef"] = bedRef;
            if (!string.IsNullOrEmpty(furnitureRef))
                options["furnitureRef"] = furnitureRef;
            if (!string.IsNullOrEmpty(targetRef))
                options["targetRef"] = targetRef;
            if (!string.IsNullOrEmpty(markerRef))
                options["markerRef"] = markerRef;
            if (!string.IsNullOrEmpty(itemRef))
                options["itemRef"] = itemRef;
            if (!string.IsNullOrEmpty(fleeFrom))
                options["fleeFrom"] = fleeFrom;
            if (!string.IsNullOrEmpty(destinationRef))
                options["destinationRef"] = destinationRef;
            if (!string.IsNullOrEmpty(weaponRef))
                options["weaponRef"] = weaponRef;
            if (!string.IsNullOrEmpty(spellRef))
                options["spellRef"] = spellRef;
            if (!string.IsNullOrEmpty(doorRef))
                options["doorRef"] = doorRef;
            if (!string.IsNullOrEmpty(objectRef))
                options["objectRef"] = objectRef;
            if (!string.IsNullOrEmpty(escortRef))
                options["escortRef"] = escortRef;
            if (!string.IsNullOrEmpty(topicRef))
                options["topicRef"] = topicRef;
            if (!string.IsNullOrEmpty(shoutRef))
                options["shoutRef"] = shoutRef;
            if (!string.IsNullOrEmpty(followRef))
                options["followRef"] = followRef;
            if (!string.IsNullOrEmpty(locationRef))
                options["locationRef"] = locationRef;
            if (!string.IsNullOrEmpty(targetAliasQuest))
                options["targetAliasQuest"] = targetAliasQuest;
            if (!string.IsNullOrEmpty(targetAliasName))
                options["targetAliasName"] = targetAliasName;
            // Follow procedure parameters. Only forwarded when the user
            // explicitly set them; PluginService applies the vanilla-template
            // defaults (128/256/Accompany=true/etc.) when these are absent.
            if (minRadius.HasValue)
                options["minRadius"] = minRadius.Value;
            if (maxRadius.HasValue)
                options["maxRadius"] = maxRadius.Value;
            if (goToLeadersGoal.HasValue)
                options["goToLeadersGoal"] = goToLeadersGoal.Value;
            if (needLos.HasValue)
                options["needLOS"] = needLos.Value;
            if (rideHorse.HasValue)
                options["rideHorse"] = rideHorse.Value;

            var result = service.AddPackage(plugin, editorId, type, options);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success)
            {
                Console.WriteLine($"Package added: {editorId} (FormKey: {result.Value})");
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                if (result.Suggestions != null && result.Suggestions.Count > 0)
                {
                    Console.Error.WriteLine("\nSuggestions:");
                    foreach (var suggestion in result.Suggestions)
                    {
                        Console.Error.WriteLine($"  - {suggestion}");
                    }
                }
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateAttachPackageCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var npcOption = new Option<string?>(
            aliases: new[] { "--npc", "-n" },
            description: "Editor ID of the NPC (use this OR --quest+--alias)");
        var questOption = new Option<string?>(
            aliases: new[] { "--quest", "-q" },
            description: "Editor ID of the quest containing the alias (use with --alias)");
        var aliasOption = new Option<string?>(
            aliases: new[] { "--alias", "-a" },
            description: "Name of the alias to attach the package to (use with --quest)");
        var packageOption = new Option<string>(
            aliases: new[] { "--package", "-p" },
            description: "Editor ID of the package to attach"
        ) { IsRequired = true };

        var cmd = new Command("attach-package", "Attach a package to an NPC or to a quest's reference alias")
        {
            pluginArg,
            npcOption,
            questOption,
            aliasOption,
            packageOption
        };

        cmd.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArg);
            var npc = context.ParseResult.GetValueForOption(npcOption);
            var quest = context.ParseResult.GetValueForOption(questOption);
            var alias = context.ParseResult.GetValueForOption(aliasOption);
            var package = context.ParseResult.GetValueForOption(packageOption)!;
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            // Resolve which attachment mode the caller wanted.
            var npcSet     = !string.IsNullOrEmpty(npc);
            var questAlias = !string.IsNullOrEmpty(quest) && !string.IsNullOrEmpty(alias);
            if (!npcSet && !questAlias)
            {
                Console.Error.WriteLine("Error: must provide --npc OR (--quest AND --alias)");
                Environment.ExitCode = 1;
                return;
            }
            if (npcSet && questAlias)
            {
                Console.Error.WriteLine("Error: provide only one of --npc OR (--quest AND --alias), not both");
                Environment.ExitCode = 1;
                return;
            }

            SpookysAutomod.Core.Models.Result<string> result;
            if (questAlias)
            {
                result = service.AttachPackageToAlias(plugin, quest!, alias!, package);
            }
            else
            {
                result = service.AttachPackageToNpc(plugin, npc!, package);
            }

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else if (result.Success)
            {
                if (questAlias)
                    Console.WriteLine($"Package '{package}' attached to alias '{alias}' in quest '{quest}'");
                else
                    Console.WriteLine($"Package '{package}' attached to NPC '{npc}'");
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                if (result.Suggestions != null && result.Suggestions.Count > 0)
                {
                    Console.Error.WriteLine("\nSuggestions:");
                    foreach (var suggestion in result.Suggestions)
                    {
                        Console.Error.WriteLine($"  - {suggestion}");
                    }
                }
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static Command CreateRemoveRecordCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var editorIdArg = new Argument<string>("editorId", "Editor ID of the record to remove");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("remove-record", "Remove a record from a plugin by EditorID")
        {
            pluginArg, editorIdArg, dryRunOption
        };

        cmd.SetHandler((plugin, editorId, dryRun, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success) { OutputError(loadResult.Error!, json); return; }

            var mod = loadResult.Value!;

            if (dryRun)
            {
                // Verify record exists without removing
                var findResult = service.FindRecords(null, editorId, null, plugin, null, false);
                if (findResult.Success && findResult.Value != null && findResult.Value.Count > 0)
                {
                    var record = findResult.Value[0];
                    if (json)
                    {
                        Console.WriteLine(new
                        {
                            success = true,
                            result = new { editorId = record.EditorId, formKey = record.FormKey, type = record.RecordType, dryRun = true }
                        }.ToJson());
                    }
                    else
                    {
                        Console.WriteLine($"Would remove: [{record.RecordType}] {record.EditorId} ({record.FormKey}) [DRY RUN - not saved]");
                    }
                }
                else
                {
                    OutputError($"Record not found: {editorId}", json);
                }
                return;
            }

            var removeResult = service.RemoveRecord(mod, editorId);
            if (!removeResult.Success) { OutputError(removeResult.Error!, json, removeResult.Suggestions); return; }

            var saveResult = service.SavePlugin(mod, plugin);

            if (json)
            {
                if (saveResult.Success)
                    Console.WriteLine(new { success = true, result = new { editorId, removed = true } }.ToJson());
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                Console.WriteLine($"Removed record: {editorId}");
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        }, pluginArg, editorIdArg, dryRunOption, _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateCloneRecordCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var sourceArg = new Argument<string>("source", "Editor ID of the record to clone");
        var newEditorIdArg = new Argument<string>("newEditorId", "Editor ID for the cloned record");
        var dryRunOption = new Option<bool>("--dry-run", "Preview changes without saving");

        var cmd = new Command("clone-record", "Clone a record with a new EditorID and FormKey")
        {
            pluginArg, sourceArg, newEditorIdArg, dryRunOption
        };

        cmd.SetHandler((plugin, source, newEditorId, dryRun, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var loadResult = service.LoadPluginForEdit(plugin);
            if (!loadResult.Success) { OutputError(loadResult.Error!, json); return; }

            var mod = loadResult.Value!;

            if (dryRun)
            {
                var findResult = service.FindRecords(null, source, null, plugin, null, false);
                if (findResult.Success && findResult.Value != null && findResult.Value.Count > 0)
                {
                    var record = findResult.Value[0];
                    if (json)
                    {
                        Console.WriteLine(new
                        {
                            success = true,
                            result = new { sourceEditorId = source, newEditorId, type = record.RecordType, dryRun = true }
                        }.ToJson());
                    }
                    else
                    {
                        Console.WriteLine($"Would clone: [{record.RecordType}] {source} -> {newEditorId} [DRY RUN - not saved]");
                    }
                }
                else
                {
                    OutputError($"Source record not found: {source}", json);
                }
                return;
            }

            var cloneResult = service.CloneRecord(mod, source, newEditorId);
            if (!cloneResult.Success) { OutputError(cloneResult.Error!, json, cloneResult.Suggestions); return; }

            var saveResult = service.SavePlugin(mod, plugin);

            if (json)
            {
                if (saveResult.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            sourceEditorId = source,
                            editorId = cloneResult.Value!.EditorId,
                            formKey = cloneResult.Value.FormKey,
                            type = cloneResult.Value.RecordType
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new { success = false, error = saveResult.Error }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (saveResult.Success)
            {
                Console.WriteLine($"Cloned: {source} -> {newEditorId} ({cloneResult.Value!.FormKey})");
            }
            else
            {
                Console.Error.WriteLine($"Error: {saveResult.Error}");
                Environment.ExitCode = 1;
            }
        }, pluginArg, sourceArg, newEditorIdArg, dryRunOption, _jsonOption, _verboseOption);

        return cmd;
    }

    private static Command CreateAddRefrCommand()
    {
        var pluginArg = new Argument<string>("plugin", "Path to the plugin file");
        var baseOption = new Option<string>(
            aliases: new[] { "--base", "-b" },
            description: "FormKey of the base form to place (e.g. 000033:Skyrim.esm for XMarker)")
        { IsRequired = true };
        var cellOption = new Option<string>(
            aliases: new[] { "--cell", "-c" },
            description: "FormKey of the cell to place into (must exist in the plugin or one of its masters)")
        { IsRequired = true };
        var xOption = new Option<float>(
            "--x", () => 0f, "X coordinate");
        var yOption = new Option<float>(
            "--y", () => 0f, "Y coordinate");
        var zOption = new Option<float>(
            "--z", () => 0f, "Z coordinate");
        var rotXOption = new Option<float>(
            "--rot-x", () => 0f, "X rotation (radians)");
        var rotYOption = new Option<float>(
            "--rot-y", () => 0f, "Y rotation (radians)");
        var rotZOption = new Option<float>(
            "--rot-z", () => 0f, "Z rotation (radians)");
        var editorIdOption = new Option<string?>(
            "--editor-id", "Editor ID for the REFR (optional)");
        var temporaryOption = new Option<bool>(
            "--temporary",
            "Place into the cell's Temporary collection instead of Persistent. " +
            "Temporary REFRs don't survive cell unload + cosave. Default is Persistent.");
        var scaleOption = new Option<float?>(
            "--scale", "Uniform scale (optional; vanilla default is 1.0, no XSCL emitted)");
        var dataFolderOption = new Option<string?>(
            "--data-folder",
            "Path to Skyrim Data folder. Required when --cell points at a cell defined " +
            "in a master file (not in the target plugin itself). Used to build a link " +
            "cache so the cell can be resolved and overridden into the target plugin.");

        var cmd = new Command(
            "add-refr",
            "Place a REFR (PlacedObject) into a cell. The cell must already exist " +
            "either in the plugin or in one of its master files. Persistent REFRs " +
            "(default) survive save/load and can be MoveTo'd at runtime by SKSE plugins.")
        {
            pluginArg,
            baseOption, cellOption,
            xOption, yOption, zOption,
            rotXOption, rotYOption, rotZOption,
            editorIdOption, temporaryOption, scaleOption,
            dataFolderOption,
        };

        cmd.SetHandler((context) =>
        {
            var plugin     = context.ParseResult.GetValueForArgument(pluginArg);
            var baseFk     = context.ParseResult.GetValueForOption(baseOption)!;
            var cellFk     = context.ParseResult.GetValueForOption(cellOption)!;
            var x          = context.ParseResult.GetValueForOption(xOption);
            var y          = context.ParseResult.GetValueForOption(yOption);
            var z          = context.ParseResult.GetValueForOption(zOption);
            var rotX       = context.ParseResult.GetValueForOption(rotXOption);
            var rotY       = context.ParseResult.GetValueForOption(rotYOption);
            var rotZ       = context.ParseResult.GetValueForOption(rotZOption);
            var editorId   = context.ParseResult.GetValueForOption(editorIdOption);
            var temporary  = context.ParseResult.GetValueForOption(temporaryOption);
            var scale      = context.ParseResult.GetValueForOption(scaleOption);
            var dataFolder = context.ParseResult.GetValueForOption(dataFolderOption);
            var json       = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose    = context.ParseResult.GetValueForOption(_verboseOption);

            var logger  = CreateLogger(json, verbose);
            var service = new PluginService(logger);

            var result = service.AddRefr(
                pluginPath:  plugin,
                baseFormKey: baseFk,
                cellFormKey: cellFk,
                x: x, y: y, z: z,
                rotX: rotX, rotY: rotY, rotZ: rotZ,
                editorId: editorId,
                persistent: !temporary,
                scale: scale,
                dataFolder: dataFolder);

            if (json)
            {
                if (result.Success)
                {
                    Console.WriteLine(new
                    {
                        success = true,
                        result = new
                        {
                            formKey    = result.Value,
                            editorId   = editorId,
                            baseForm   = baseFk,
                            cell       = cellFk,
                            position   = new { x, y, z },
                            rotation   = new { x = rotX, y = rotY, z = rotZ },
                            persistent = !temporary,
                            scale      = scale,
                        }
                    }.ToJson());
                }
                else
                {
                    Console.WriteLine(new
                    {
                        success     = false,
                        error       = result.Error,
                        suggestions = result.Suggestions,
                    }.ToJson());
                    Environment.ExitCode = 1;
                }
            }
            else if (result.Success)
            {
                Console.WriteLine($"Placed REFR {result.Value} (editorId={editorId ?? "(none)"}) " +
                                  $"base={baseFk} cell={cellFk} pos=({x:F1},{y:F1},{z:F1})");
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                if (result.Suggestions?.Any() == true)
                {
                    foreach (var s in result.Suggestions)
                    {
                        Console.Error.WriteLine($"  - {s}");
                    }
                }
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    // ========================================================================
    // esp script <ops.json>
    //
    // Runs a list of mutation ops against a single in-memory mod, saving once
    // at the end. Replaces N invocations of individual `esp add-*` commands
    // with one invocation that opens + saves the ESP a single time. Driven by
    // EspBatchService - which ops are supported is defined there.
    //
    // Ops JSON shape:
    //   {
    //     "plugin": "G:\\path\\to\\Plugin.esp",
    //     "ops": [
    //       { "id": "x1", "op": "add-refr",      "args": { ... } },
    //       {             "op": "add-package",   "args": { ... } },
    //       ...
    //     ]
    //   }
    //
    // Path "-" reads from stdin instead of a file (useful for piping from a
    // build script without a temp file).
    // ========================================================================
    private static Command CreateScriptCommand()
    {
        var opsFileArg = new Argument<string>(
            "ops-file",
            "Path to a JSON ops file ({ plugin, ops:[...] }). Use '-' to read JSON from stdin.");

        var cmd = new Command(
            "script",
            "Apply many ESP mutations from a JSON ops file against a single in-memory mod (one load, one save).")
        {
            opsFileArg
        };

        cmd.SetHandler((context) =>
        {
            var opsFile = context.ParseResult.GetValueForArgument(opsFileArg);
            var json = context.ParseResult.GetValueForOption(_jsonOption);
            var verbose = context.ParseResult.GetValueForOption(_verboseOption);

            var logger = CreateLogger(json, verbose);

            string opsText;
            try
            {
                opsText = opsFile == "-"
                    ? Console.In.ReadToEnd()
                    : File.ReadAllText(opsFile);
            }
            catch (Exception ex)
            {
                OutputError($"Failed to read ops file '{opsFile}': {ex.Message}", json);
                return;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(opsText);
            }
            catch (Exception ex)
            {
                OutputError($"Ops file is not valid JSON: {ex.Message}", json);
                return;
            }

            string? pluginPath;
            List<BatchOpEntry> ops;
            try
            {
                pluginPath = doc.RootElement.GetProperty("plugin").GetString();
                if (string.IsNullOrEmpty(pluginPath))
                {
                    OutputError("Ops file missing required 'plugin' field", json);
                    return;
                }

                var opsArr = doc.RootElement.GetProperty("ops");
                if (opsArr.ValueKind != JsonValueKind.Array)
                {
                    OutputError("Ops file 'ops' must be an array", json);
                    return;
                }

                ops = new List<BatchOpEntry>();
                foreach (var el in opsArr.EnumerateArray())
                {
                    ops.Add(new BatchOpEntry
                    {
                        Id = el.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null,
                        Op = el.GetProperty("op").GetString() ?? "",
                        // Clone the args subtree so it survives the JsonDocument scope.
                        // Cheap (small objects) and avoids lifetime bugs if the doc is
                        // disposed before ops execute.
                        Args = el.TryGetProperty("args", out var argsEl)
                            ? JsonDocument.Parse(argsEl.GetRawText()).RootElement
                            : JsonDocument.Parse("{}").RootElement
                    });
                }
            }
            catch (Exception ex)
            {
                OutputError($"Failed to parse ops: {ex.Message}", json);
                return;
            }

            var pluginService = new PluginService(logger);
            var loadResult = pluginService.LoadPluginForEdit(pluginPath);
            if (!loadResult.Success)
            {
                OutputError(loadResult.Error!, json);
                return;
            }
            var mod = loadResult.Value!;

            var batchService = new EspBatchService(logger, pluginService);
            var runResult = batchService.ExecuteOps(mod, ops);

            // Only save if every op succeeded. A partial save would leave the
            // ESP in an inconsistent state that the next build run would have
            // to clean up; bailing keeps the on-disk plugin matching the last
            // known-good state.
            string? saveError = null;
            if (runResult.Success)
            {
                var saveResult = pluginService.SavePlugin(mod, pluginPath);
                if (!saveResult.Success)
                {
                    saveError = saveResult.Error;
                }
            }

            if (json)
            {
                Console.WriteLine(new
                {
                    success = runResult.Success && saveError == null,
                    opCount = runResult.OpCount,
                    succeeded = runResult.Succeeded,
                    failed = runResult.Failed,
                    failedAt = runResult.FailedAt,
                    saveError,
                    results = runResult.Results.Select(r => new
                    {
                        index = r.Index,
                        id = r.Id,
                        op = r.Op,
                        success = r.Success,
                        value = r.Value,
                        error = r.Error
                    })
                }.ToJson());
                if (!runResult.Success || saveError != null) Environment.ExitCode = 1;
            }
            else
            {
                Console.WriteLine($"Script: {runResult.Succeeded}/{runResult.OpCount} ops succeeded.");
                if (!runResult.Success)
                {
                    var failed = runResult.Results.LastOrDefault();
                    Console.Error.WriteLine($"Failed at op #{runResult.FailedAt} ({failed?.Op}): {failed?.Error}");
                    Environment.ExitCode = 1;
                }
                else if (saveError != null)
                {
                    Console.Error.WriteLine($"All ops succeeded but save failed: {saveError}");
                    Environment.ExitCode = 1;
                }
                else
                {
                    Console.WriteLine($"Saved {pluginPath}.");
                }
            }
        });

        return cmd;
    }
}

// Extension for anonymous type JSON
internal static class JsonExtensions
{
    public static string ToJson(this object obj)
    {
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
