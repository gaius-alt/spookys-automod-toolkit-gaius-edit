# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),

and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.11.2] - 2026-05-20

### Fixed

- **Setup Wizard .NET 8 detection** — The wizard now correctly detects .NET 8 even when newer SDKs (9.x, 10.x) are also installed. Previously it used `dotnet --version` which only reports the highest/global SDK, causing the wizard to falsely claim ".NET 8 not found" for anyone with .NET 9 or 10 installed alongside 8. Now uses `dotnet --list-sdks` and checks for any 8.x entry. Reported by **Zol** and **knight24_**.

## [1.11.1] - 2026-04-07

### Fixed

- **`esp add-alias` and `esp attach-alias-script` commands now actually exist** - These were documented as part of v1.5.0 but were never implemented in the CLI. Now properly wired up with full support for alias flags, optional script attachment on creation, and dry-run mode. The script structure is correctly stored in `quest.VirtualMachineAdapter.Aliases[]` (QuestFragmentAlias) so Creation Kit recognizes it.

## [1.11.0] - 2026-03-12

### Added

- **nif-tool integration** - 8 new NIF commands powered by bundled Rust binary (`tools/nif-tool/`), contributed by **ColdSun**
- **Setup Wizard spinning indicator** - Replaced progress bar with animated spinner for "Working..." status
    - `nif list-textures` - Detailed texture listing with block index and slot number
    - `nif replace-textures` - Batch texture path rewriting (case-insensitive substring replacement)
    - `nif list-strings` - Show NIF string table entries (node/block names)
    - `nif rename-strings` - Batch rename node/block names in NIF string table
    - `nif shader-info` - Inspect BSLightingShaderProperty SF1/SF2 flags
    - `nif fix-eyes` - Fix eye ghosting bug in FaceGen NIFs
    - `nif verify` - Byte-perfect roundtrip verification
    - `nif restore` - Restore `.nif.bak` backups
    - All commands support `--dry-run`, `--backup`, `--json`, and recursive folder processing

### Removed

- **`nif textures` command** - Replaced by `nif list-textures` which provides accurate block/slot detail via nif-tool

## [1.10.0] - 2026-03-11

### Added

- **GUI Setup Wizard** - Standalone `SpookysAutomodSetup.exe` for one-click environment setup
    - Auto-detects Skyrim SE and VR installations
    - Links Papyrus script headers from Skyrim's Data folder
    - Downloads SKSE headers (both SE and VR) from skse.silverlock.org
    - Downloads SkyUI MCM script headers from GitHub
    - Downloads Papyrus compiler and Champollion decompiler
    - Detects CMake and MSVC build tools for SKSE development
    - Builds and verifies the toolkit automatically
    - Progress indicator during downloads and build steps
- **`skse build` command** - Build SKSE C++ projects directly from the toolkit
    - Wraps CMake configure + build in a single command
    - Supports `--config Release|Debug` and `--clean` options
    - Auto-detects CMake and MSVC availability with helpful error messages
- **`esp add-faction` command** - Create faction records with flags (HiddenFromPC, TrackCrime, SpecialCombat, CanBeOwner, Vendor)
- **`esp list-records` command** - List all records in a plugin with optional type filter
- **`esp remove-record` command** - Remove records by EditorID with dry-run support
- **`esp clone-record` command** - Deep-copy records with new EditorID/FormKey (17 record types)
- **Archive backup safety** - Repack operations now create `.bak` backup, auto-restore on failure

### Fixed

- **SKSE command error handling** - Fixed missing exit codes and inconsistent JSON output patterns
- **Async warnings** - Fixed CS1998 warnings in PapyrusCompilerWrapper, ChampollionWrapper, BsArchWrapper
- **Thread safety** - LinkCacheManager static cache now uses lock() for thread safety

- **`skse add-function` crash** - Fixed `JsonIgnoreCondition.WhenWritingNull` error on `Result<bool>` value types by switching to `WhenWritingDefault`
- **`papyrus decompile` reporting `decompiledCount: 0`** - Single-file decompilation now correctly reports count of 1
- **Documentation references to non-existent `esp analyze` command** - All references updated to use `esp info` across docs, skills, and error messages
- **MCM create syntax in docs** - Fixed incorrect `mcm create <file> --mod-name` to correct `mcm create <modName> <displayName> --output <file>`

## [1.9.1] - 2026-02-04

### Fixed

- **Console Window Suppression** - Archive operations no longer show console windows
    - Added `ProcessWindowStyle.Hidden` to BSArch process creation
    - Eliminates brief console window flashes during extract, pack, merge operations
    - Provides cleaner user experience during archive manipulation

### Documentation

- **Installation Guide** - Added NuGet source troubleshooting
    - Proactive note in installation section about potential NuGet restore errors
    - Troubleshooting entry with PowerShell command to add nuget.org source
    - Addresses fresh .NET installations or corporate environments without default NuGet config

## [1.9.0] - 2026-02-02

### Added

- **Complete NPC AI Package Support** - Full Skyrim AI behavior system with all 36 package types
    - `PackageBuilder` - Fluent builder for creating AI behavior packages with proper Mutagen API usage
        - **Basic Behaviors (7 types):** Sandbox, Travel, Sleep, Eat, Follow, Guard, Patrol
        - **Actions & Activities (8 types):** UseItemAt, Activate, Sit, UseIdleMarker, Wander, Wait, Relax, Acquire
        - **Combat & Magic (5 types):** Flee, Ambush, UseWeapon, UseMagic, CastMagic, Shout
        - **Social & Dialogue (4 types):** Dialogue, ForceGreet, Greet, Say
        - **Advanced Behaviors (8 types):** Accompany, Escort, FollowTo, Find, HoldPosition, KeepAnEyeOn, Hover, Orbit
        - **Utility (4 types):** LockDoors, UnlockDoors, Dismount
    - `esp add-package` command - Create package records with extensive options
        - All 36 package types supported via `--type` parameter
        - Schedule options: `--start-hour`, `--duration` (for time-based packages)
        - Reference options: `--bed`, `--furniture`, `--target`, `--marker`, `--location`, `--item-ref`, `--weapon-ref`, `--spell-ref`, `--shout-ref`, `--topic-ref`, `--door-ref`, `--object-ref`, `--escort-ref`, `--follow-ref`, `--location-ref`
        - Movement options: `--radius`, `--distance` (for area-based packages)
        - Optional parameters for target-based packages (flee-from, destination, etc.)
    - `esp attach-package` command - Attach packages to NPCs
        - Attach by package editor ID or FormKey
        - Multiple packages evaluated in attachment order (priority)
    - `NpcBuilder.WithPackage()` - Fluent API for attaching packages during NPC creation
        - By FormKey: `WithPackage(FormKey)`
        - By EditorID: `WithPackage(string)`
        - Multiple packages: `WithPackages(params FormKey[])`
    - Proper Package.Data dictionary population with PackageDataLocation and PackageDataTarget
    - ProcedureTree structure with PackageBranch and DataInputIndices
    - Schedule configuration with hour/duration settings
    - FormKey format validation ("FORMID:MODNAME" with 6 hex digits)
    - Support for optional targets (weapons, spells, shouts, topics, locations)
    - Flying creature packages (Hover, Orbit for dragons/flying NPCs)
    - Complete coverage of all documented Skyrim AI package types (100% coverage)

## [1.8.0] - 2026-01-30

### Added

- **Archive Editing Operations** - Comprehensive suite for modifying BSA/BA2 archives without full repackaging
    - `archive add-files` - Add new files to existing archives with directory structure preservation
        - `--base-dir` parameter for explicit path control
        - Auto-detection of common parent directory
        - Preserves folder hierarchy (e.g., meshes/mymod/weapon.nif)
    - `archive remove-files` - Remove files by pattern from archives
        - Filter patterns: `*.esp`, `scripts/*`, specific files
        - Preserves remaining archive structure
    - `archive replace-files` - Replace matching files from source directory
        - Optional filter patterns for selective replacement
        - Useful for script updates, texture patches
    - `archive update-file` - Convenience wrapper for single file updates
        - Faster than full replace-files for single operations
    - `archive extract-file` - Extract single file without unpacking entire archive
        - Significantly faster than full extraction for single file access

- **Archive Maintenance Operations**
    - `archive merge` - Combine multiple archives with conflict tracking
        - Reports conflicts when files exist in multiple archives
        - Later archives overwrite earlier ones
        - Tracks files per source archive
    - `archive validate` - Check archive integrity
        - Reports file count, size, archive type
        - Warns about mismatches or corruption
        - Useful before distribution
    - `archive optimize` - Repack with compression and report size savings
        - Defragments and compresses archives
        - Reports original vs optimized size
        - Percentage savings calculation
    - `archive diff` - Compare two archive versions
        - Shows added, removed, modified, and unchanged files
        - Useful for understanding what patches change

### Fixed

- **Critical archive editing bugs** - 5 major bugs preventing archive editing from working:
    1. **BSArch overwrite bug**: BSArch cannot pack to existing files
        - Added `File.Delete(archivePath)` before repacking in add-files, remove-files, replace-files
    2. **Temp directory cleanup bug**: BSArch created archives in temp directory (immediately deleted)
        - Added `Path.GetFullPath(archivePath)` to convert relative paths to absolute
        - Affects: add-files, remove-files, replace-files, merge, optimize
    3. **Merge extract directory bug**: Merge created temp path but didn't create directory
        - Added `Directory.CreateDirectory(tempExtractDir)` before extraction
    4. **Merge/Optimize output path bug**: Same temp directory issue as edit operations
        - Added `Path.GetFullPath(outputPath)` in MergeArchivesAsync and OptimizeAsync
    5. **ListFiles limit=0 bug**: limit=0 broke immediately instead of showing all files
        - Changed condition to check `limit.Value > 0` before breaking
        - Affected validate and diff operations which use limit=0 for "show all"

### Changed

- **Documentation comprehensively updated** for archive editing
    - `.claude/skills/skyrim-archive/skill.md` - Full command reference and workflows
    - `docs/llm-guide.md` - Complete Archive Operations section with examples
    - `docs/llm-init-prompt.md` - Updated quick reference for AI assistants
    - All version references removed to keep documentation version-agnostic

### Developer Notes

- Extract-modify-repack workflow pattern used for all editing operations
- All operations support `--preserve-compression` flag (defaults to true)
- JSON output for all operations with structured Result types
- Temp directory cleanup in finally blocks for safety
- ~500 lines of new code across ArchiveService and ArchiveCommands

### Known Limitations

- Archive editing requires BSArch tool (auto-downloads on first use)
- Edit operations take time for large archives (extract → modify → repack)
- BSArch only supports SSE, LE, FO4, FO76 archive formats

## [1.7.1] - 2026-01-29

### Fixed

- **`esp find-record` wildcard pattern bug** - The `--search "*"` parameter now correctly matches all records of the specified type
    - **Bug:** Wildcard pattern was treated as literal asterisk character, returning empty results even when records existed
    - **Impact:** Commands like `esp find-record --type spell --search "*"` would return `[]` despite `esp info` showing "Spells: 2"
    - **Fix:** Added wildcard handling - `*` now matches all records of filtered type while preserving substring search for other patterns
    - **Testing:** Verified with test plugin containing 2 spells - wildcard now returns both, partial search still works correctly

## [1.7.0] - 2026-01-29

### Added

- **Record Viewing and Override System** - Comprehensive tools for inspecting and modifying existing records
    - `esp view-record` - View detailed information about any record by EditorID or FormID
        - Supports 15 record types: Spell, Weapon, Armor, Quest, NPC, Perk, Faction, Book, MiscItem, Global, LeveledItem, FormList, Outfit, Location, EncounterZone
        - Extracts type-specific properties (spell effects, weapon damage, armor ratings, etc.)
        - Optional `--include-raw` flag for reflection-based property extraction
        - Full JSON output support for AI parsing
    - `esp create-override` - Create override patches for existing records
        - Automatically adds source plugin as master reference
        - Preserves all record data via DeepCopy
        - Creates patches that load after source in load order
        - Supports all major record types
    - `esp find-record` - Search for records across one or multiple plugins
        - Pattern matching on EditorID and Name fields
        - Filter by record type for faster searches
        - Search single plugin or all plugins in data folder
        - Returns plugin name, FormKey, EditorID, and record type
    - `esp batch-override` - Create overrides for multiple records at once
        - Batch by search pattern or explicit EditorID list
        - Filter by record type
        - All overrides in single patch plugin
        - Reports number of records modified
    - `esp compare-record` - Compare two versions of the same record
        - Side-by-side property comparison
        - Highlights differences between original and modified
        - Useful for understanding what patches change
        - Supports EditorID or FormID lookup
    - `esp conflicts` - Detect load order conflicts
        - Scans all plugins in data folder
        - Shows which plugins modify the same record
        - Reports load order positions
        - Identifies winning override (last in load order)

- **Condition Management System** - View, add, and remove conditions on records
    - `esp list-conditions` - View all conditions on a record (Perk, Package, IdleAnimation, MagicEffect)
        - Shows condition function, comparison value, parameters, flags
        - Numbered output for easy reference when removing
        - Full JSON support for AI parsing
    - `esp add-condition` - Add conditions to records
        - Supports Perk, Package, IdleAnimation, MagicEffect record types
        - Common condition functions: GetLevel, IsInCombat, GetActorValue, etc.
        - Creates override patch automatically
        - Hardcoded comparison value of 1.0 (suitable for most boolean checks)
    - `esp remove-condition` - Remove specific conditions by index
        - Remove single or multiple conditions (comma-separated indices)
        - Preserves all other record properties
        - Creates clean override patch
    - **Important:** Conditions supported on Perk, Package, IdleAnimation, MagicEffect only
    - Conditions NOT supported on Spell, Weapon, Armor (Mutagen API limitation)

- **Script Property Management**
    - `esp set-property` - Manually set script properties on quest or alias scripts
        - Set properties on quest scripts or alias scripts (via `--alias-target`)
        - Supports 6 property types: `object`, `alias`, `int`, `float`, `bool`, `string`
        - Object properties use FormKey format: `Plugin.esp|0xFormID`
        - Alias properties reference aliases within same quest
        - Full JSON output support
        - Complements `esp auto-fill` for manual property overrides
        - Ported from unmerged PR #1, resolves user-reported documentation issue

### Changed

- **Enhanced Error Messages** - All new commands provide helpful suggestions on failure
- **Improved JSON Output** - Consistent Result<T> pattern across all operations
- **Better FormKey Handling** - Support for both short and long FormKey formats

### Fixed

- **SKSE Template Modernization** - Completely updated SKSE plugin templates for CommonLibSSE-NG
    - Fixed deprecated `CompatibleVersions` API → Now uses `.RuntimeCompatibility(Independent)`
    - Added proper PCH configuration via CMake `target_precompile_headers()`
    - Fixed incomplete type errors with `RE::TESObjectREFR` by including `<RE/Skyrim.h>` in PCH
    - Fixed `NiPointer` conversion errors with correct `.get()` and `.As<T>()` usage
    - Fixed `LookupForm` API misuse → Now uses `LookupByEditorID()` for EditorID strings
    - Added comprehensive README with VCPKG and manual vendor/ setup instructions
    - Added `build.bat` script for easy Windows CMD building
    - Resolves 7 major user-reported compilation issues

### Developer Notes

- Added 8 new model classes in `SpookysAutomod.Core.Models`
- Extended PluginService with 6 major methods (~1,640 lines)
- Added 6 new CLI commands to EspCommands (~910 lines)
- Total new code: ~3,500 lines
- No new dependencies required

### Known Limitations

- Condition support temporarily disabled pending Mutagen API verification
- Conditions work on Perks but not Spells/Weapons/Armor in current Mutagen version
- Future versions will add full condition manipulation support

## [1.6.0] - 2026-01-20

### Added

- **LeveledItem support** for random loot and equipment lists
    - `esp add-leveled-item` - Create leveled item lists with entries and chance-none
    - Presets: low-treasure, medium-treasure, high-treasure, guaranteed-loot
    - Support for entry management with level and count parameters
- **FormList support** for script property collections
    - `esp add-form-list` - Create form lists for use in scripts and conditions
    - Accepts FormKeys or EditorIDs
- **EncounterZone support** for level scaling areas
    - `esp add-encounter-zone` - Create encounter zones with min/max levels
    - Presets: low-level (1-10), mid-level (10-30), high-level (30-50), scaling (1-unlimited)
    - Flags: never-resets, match-pc-below-min, disable-combat-boundary
- **Location support** for quest locations and fast travel
    - `esp add-location` - Create location records with keywords and parent locations
    - Presets: inn, city, dungeon, dwelling
    - Support for location keywords (LocTypeInn, LocTypeCity, etc.)
- **Outfit support** for NPC equipment sets
    - `esp add-outfit` - Create outfit records for NPC clothing/armor assignments
    - Presets: guard (iron armor + sword/shield), farmer (clothes), mage (robes), thief (leather)
    - Support for multiple armor and weapon items

### Fixed

- AutoFillService now includes Outfit type mapping for auto-fill functionality

## [1.5.0] - 2026-01-20

### Added

- **Quest alias system** for follower tracking and dynamic NPC management
    - `esp add-alias` - Create reference aliases with optional scripts and flags
    - `esp attach-alias-script` - Attach scripts to existing aliases
    - `esp set-property --alias-target` - Set properties on alias scripts
    - Alias flags: Optional, AllowReuseInQuest, AllowReserved, Essential, etc.
- **Type-aware auto-fill** for script properties
    - `esp auto-fill` - Automatically fill properties by parsing PSC files
    - `esp auto-fill-all` - Bulk auto-fill all scripts in a mod
    - Searches Skyrim.esm by EditorID with type filtering
    - Prevents wrong matches (e.g., Location vs Keyword with similar names)
    - Supports 40+ Papyrus type → Mutagen type mappings
    - **Complete array property support** using `ScriptObjectListProperty`
        - Auto-fill creates proper multi-element arrays for PSC array properties
        - ScriptBuilder.WithArrayProperty() supports multiple FormKeys
        - Currently fills arrays with single matching element (first match)
        - Future: Pattern matching and explicit multi-element lists
    - Cached link cache for 5x performance improvement on repeated operations
- **Type inspection tools** for debugging
    - `esp debug-types` - Show Mutagen type structures with reflection
    - Supports pattern matching (e.g., `Quest*`, `QuestAlias`)
    - Displays properties, types, nullability, and critical notes
    - Essential for understanding Mutagen's type system
- **Dry-run mode** for all add commands
    - `--dry-run` flag previews changes without saving
    - Works with: add-weapon, add-armor, add-spell, add-perk, add-book, add-quest, add-global, add-npc
    - Useful for testing and validation workflows
- **Faction support**
    - `esp add-faction` - Create faction records
    - Configure flags: HiddenFromPC, TrackCrime, etc.

### Changed

- Documentation significantly expanded with quest alias patterns
- LLM guide expanded with alias workflows and auto-fill usage
- **SKSE documentation clarified** - Removed misleading Visual Studio IDE requirement
    - Only CMake and MSVC Build Tools needed (no IDE required)
    - Added complete setup instructions to README
    - Clarified that LLMs can invoke CMake to build plugins end-to-end
    - SKSE moved from "limitations" to "fully supported" when tools installed

### Technical

- Services: AliasService, ScriptPropertyService, AutoFillService, BulkAutoFillService, TypeInspectionService, LinkCacheManager
- Builders: FactionBuilder, ScriptBuilder extended with WithObjectProperty() and WithArrayProperty()
- Array properties use Mutagen's ScriptObjectListProperty with ExtendedList<ScriptObjectProperty>
- Auto-fill always loads Skyrim.esm for vanilla record lookups
- QuestFragmentAlias.Property.Object correctly references quest FormKey
- Link cache caching with 5-minute timeout for performance optimization
- Reflection-based Mutagen type introspection for debugging
- Regex-based PSC property parsing with type detection

## [1.4.1] - 2026-01-12

### Fixed

- **Critical:** Fixed PapyrusService.CompileAsync() parameter mismatch causing compilation errors
    - Was passing `optimize` (bool) to position 4, which expects `additionalImports` (List<string>?)
    - Now correctly passes `null` for additionalImports parameter
    - Prevented the toolkit from compiling at all

### Changed

- Separated `.claude` skills from source repository
    - Skills now packaged with releases for end users
    - Clean source repository for developers
    - Releases have proper structure for Claude Code auto-detection

### Added

- Release build scripts (PowerShell and Bash) for automated packaging
- Scripts documentation in `scripts/README.md`

## [1.4.0] - 2026-01-03

### Fixed

- **Critical:** Champollion decompiler wrapper using wrong argument (`-o` → `--psc`)
- Decompiled files now written to specified output directory
- Command status now accurately reflects success/failure
- Error messages now include actual Champollion output
- Added helpful suggestions for common decompilation errors

### Changed

- Enhanced error context propagation in `ChampollionWrapper.cs`
- Added `ParseDecompilerSuggestions()` method for better error messages

### Impact

- `papyrus decompile` command now fully functional
- All decompilation tests pass

## [1.3.0] - 2026-01-03

### Added

- LLM initialization prompt for quick AI assistant onboarding
- Documentation for rapid setup with Claude, ChatGPT, etc.

### Security

- Removed personal file paths from public documentation

## [1.2.0] - 2026-01-02

### Added

- Papyrus script headers infrastructure
- Support for SKSE and SkyUI headers
- Comprehensive Papyrus compilation support

### Fixed

- Papyrus compilation error reporting
- Documentation references to script header directories

## [1.1.0] - 2025-12-30

### Added

- Test suite infrastructure
- Archive list command implementation
- Test project for archive functionality

### Fixed

- JSON output formatting
- Null checks in CLI commands
- .gitignore matching for SpookysAutomod.Esp directory

## [1.0.0] - 2025-12-30

### Added

- Initial release
- ESP/ESL plugin creation and modification
- Papyrus script compilation and decompilation
- BSA/BA2 archive handling
- NIF mesh inspection
- MCM Helper configuration generation
- Audio file processing (FUZ/XWM/WAV)
- SKSE C++ plugin project scaffolding
- Claude Code skills for all modules
- Comprehensive documentation
- JSON output support for all commands
- Auto-downloading of external tools

### Features

- Create weapons, armor, spells, perks, books, quests, NPCs
- Compile and decompile Papyrus scripts
- Extract and create BSA archives
- Inspect 3D meshes and textures
- Generate MCM configuration menus
- Process game audio files
- Generate SKSE plugin projects

[unreleased]: https://github.com/SpookyPirate/spookys-automod-toolkit/compare/v1.5.0...HEAD

[1.5.0]: https://github.com/SpookyPirate/spookys-automod-toolkit/compare/v1.4.1...v1.5.0

[1.4.1]: https://github.com/SpookyPirate/spookys-automod-toolkit/compare/v1.4.0...v1.4.1

[1.4.0]: https://github.com/SpookyPirate/spookys-automod-toolkit/compare/v1.3.0...v1.4.0

[1.3.0]: https://github.com/SpookyPirate/spookys-automod-toolkit/compare/v1.2.0...v1.3.0

[1.2.0]: https://github.com/SpookyPirate/spookys-automod-toolkit/compare/v1.1.0...v1.2.0

[1.1.0]: https://github.com/SpookyPirate/spookys-automod-toolkit/compare/v1.0.0...v1.1.0

[1.0.0]: https://github.com/SpookyPirate/spookys-automod-toolkit/releases/tag/v1.0.0