---
name: skyrim-skse
description: Create, build, and manage SKSE C++ plugin projects. Use when the user wants to create native plugins, build them with xmake, add Papyrus native functions, or extend Skyrim's functionality at the native level.
---

# Skyrim SKSE Module

Create and manage SKSE (Skyrim Script Extender) C++ plugin projects using Spooky's AutoMod Toolkit.

## Prerequisites

Run all commands from the toolkit directory:
```bash
cd "<TOOLKIT_PATH>"
# Example: cd "C:\Tools\spookys-automod-toolkit"
```

### Build Requirements

**IMPORTANT:** Building SKSE plugins requires:

| Tool | Purpose | Installation |
|------|---------|--------------|
| **xmake 2.8+** | Build system | [Download](https://xmake.io) |
| **MSVC Build Tools** | C++ compiler | [MSVC Build Tools](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022) |
| xmake-repo | Dependencies | Auto-resolved on first build (`commonlibsse-ng`, `fmt`, `spdlog`) |

**Note:** You do NOT need the full Visual Studio IDE - only the MSVC Build Tools (C++ compiler). xmake auto-detects MSVC (including VS 18 / 2026 preview).

## Overview

SKSE plugins are DLL files that extend Skyrim's functionality at a native level. This module generates project scaffolding using **CommonLibSSE-NG**, which supports Skyrim SE, AE, GOG, and VR from a single codebase.

## Command Reference

### List Available Templates
```bash
dotnet run --project src/SpookysAutomod.Cli -- skse templates
```

**Available Templates:**
| Template | Description |
|----------|-------------|
| `basic` | Minimal SKSE plugin with logging |
| `papyrus-native` | Plugin with Papyrus native function support |

### Create SKSE Project
```bash
dotnet run --project src/SpookysAutomod.Cli -- skse create "<name>" [options]
```
| Option | Default | Description |
|--------|---------|-------------|
| `--template` | `basic` | Template to use |
| `--output` | `.` | Output directory |
| `--author` | `Unknown` | Author name |
| `--description` | - | Project description |

### Build SKSE Project
```bash
dotnet run --project src/SpookysAutomod.Cli -- skse build "<project>" [options]
```
| Option | Default | Description |
|--------|---------|-------------|
| `<project>` | `.` | Project directory |
| `--config` | `Release` | Build configuration (Release or Debug) |
| `--clean` | `false` | Clean build directory before building |

### Get Project Info
```bash
dotnet run --project src/SpookysAutomod.Cli -- skse info "<path>"
```
| Option | Description |
|--------|-------------|
| `<path>` | Project directory (default: current) |

### Add Papyrus Native Function
```bash
dotnet run --project src/SpookysAutomod.Cli -- skse add-function "<project>" --name "<name>" [options]
```
| Option | Default | Description |
|--------|---------|-------------|
| `--name` | Required | Function name |
| `--return` | `void` | Return type |
| `--param` | - | Parameters (format: `type:name`, repeatable) |

## Common Workflows

### Create Basic SKSE Plugin
```bash
# 1. Create project
dotnet run --project src/SpookysAutomod.Cli -- skse create "MyPlugin" --output "./" --author "YourName"

# 2. Build (requires xmake and MSVC)
dotnet run --project src/SpookysAutomod.Cli -- skse build "./MyPlugin"

# Output: MyPlugin/build/windows/x64/release/MyPlugin.dll
```

### Create Plugin with Papyrus Functions
```bash
# 1. Create project with papyrus-native template
dotnet run --project src/SpookysAutomod.Cli -- skse create "MyNativePlugin" --template papyrus-native --author "YourName" --output "./"

# 2. Add custom functions
dotnet run --project src/SpookysAutomod.Cli -- skse add-function "./MyNativePlugin" --name "GetActorSpeed" --return "Float" --param "Actor:target"

dotnet run --project src/SpookysAutomod.Cli -- skse add-function "./MyNativePlugin" --name "SetActorSpeed" --return "void" --param "Actor:target" --param "Float:speed"

dotnet run --project src/SpookysAutomod.Cli -- skse add-function "./MyNativePlugin" --name "GetPluginVersion" --return "Int"

# 3. Build
dotnet run --project src/SpookysAutomod.Cli -- skse build "./MyNativePlugin"
```

### Check Existing Project
```bash
# Get project info
dotnet run --project src/SpookysAutomod.Cli -- skse info "./MyPlugin"
```

## Papyrus Type Mapping

| Papyrus Type | C++ Type | Notes |
|--------------|----------|-------|
| Int | int | 32-bit integer |
| Float | float | 32-bit float |
| Bool | bool | Boolean |
| String | std::string | Text string |
| Actor | RE::Actor* | Actor reference |
| ObjectReference | RE::TESObjectREFR* | Object reference |
| Form | RE::TESForm* | Any form |

### Function Parameter Format
```
--param "Type:name"
```

Examples:
- `--param "Actor:target"` - Actor parameter named "target"
- `--param "Float:speed"` - Float parameter named "speed"
- `--param "Int:count"` - Integer parameter named "count"
- `--param "String:message"` - String parameter named "message"

## Generated Project Structure

```
MyPlugin/
  xmake.lua               # xmake build configuration
  skse-project.json       # Toolkit configuration
  README.md               # Per-project build instructions (basic template)
  src/
    PCH.h                 # Precompiled header (basic template)
    main.cpp              # SKSE plugin entry point
    plugin.cpp            # Plugin implementation (papyrus-native template)
    plugin.h              # Plugin header (papyrus-native template)
    papyrus.cpp           # Papyrus native registration (papyrus-native)
    papyrus.h             # Papyrus native header (papyrus-native)
```

`commonlibsse-ng`, `fmt`, and `spdlog` are resolved by xmake-repo on first build — they are not committed to the project directory.

## Template Details

### basic Template
Minimal plugin with:
- Modern CommonLibSSE-NG plugin metadata (`SKSEPluginInfo` + `SKSEPluginLoad` macros)
- Precompiled header (PCH) wired via xmake's `set_pcxxheader`
- One worked event-sink example (OnHit)
- Safe NiPointer and Actor casting patterns
- Logging via spdlog
- Message handler for game events (`kDataLoaded`, `kPostLoad`)

**Modern API Features:**
```cpp
// Modern plugin metadata (designated-initializer macro)
SKSEPluginInfo(
    .Version = REL::Version{ 1, 0, 0, 0 },
    .Name    = PLUGIN_NAME,
    .Author  = PLUGIN_AUTHOR
)

// Modern entry point
SKSEPluginLoad(const SKSE::LoadInterface* skse)
{
    SKSE::Init(skse);
    return true;
}

// Safe NiPointer handling
auto target = event->target.get();  // Get raw pointer
auto actor = target->As<RE::Actor>();  // Safe cast

// Correct form lookup
auto form = RE::TESForm::LookupByEditorID("MyForm"sv);  // By EditorID
auto form = RE::TESForm::LookupByID(0x00012EB7);  // By FormID
```

**Build path:**
- xmake-repo resolves `commonlibsse-ng v3.7.0`, `fmt`, `spdlog` automatically
- No vcpkg, no FetchContent, no manual vendor/ folder
- Run `xmake` from the project root (or use `skse build`)

### papyrus-native Template
Includes everything in `basic` plus:
- Papyrus native function registration
- Script interface
- Example function

```cpp
// Register functions
bool RegisterFunctions(RE::BSScript::IVirtualMachine* vm) {
    vm->RegisterFunction("MyFunction", "MyScript", MyFunction);
    return true;
}

// Example native function
int MyFunction(RE::StaticFunctionTag*) {
    return 42;
}
```

## Using Native Functions in Papyrus

After building the plugin, call native functions from Papyrus:
```papyrus
ScriptName MyScript

; Declare native functions
Int Function GetPluginVersion() global native
Float Function GetActorSpeed(Actor target) global native
Function SetActorSpeed(Actor target, Float speed) global native

; Usage
Event OnInit()
    Int version = GetPluginVersion()
    Debug.Notification("Plugin version: " + version)

    Actor player = Game.GetPlayer()
    Float speed = GetActorSpeed(player)
    SetActorSpeed(player, speed * 1.5)
EndEvent
```

## CommonLibSSE-NG

This toolkit uses **CommonLibSSE-NG** (Next Generation), which provides:

- **Multi-version support**: Single DLL works on SE, AE, GOG, VR
- **Address independence**: No hardcoded addresses
- **Modern C++**: Uses C++20 features
- **Complete API**: Covers most game functions

### Supported Skyrim Versions
| Version | Support |
|---------|---------|
| Skyrim SE 1.5.x | Full |
| Skyrim SE 1.6.x (AE) | Full |
| Skyrim GOG | Full |
| Skyrim VR | Partial |

## Building Projects

### Using `skse build` (Recommended)
```bash
# Standard build
dotnet run --project src/SpookysAutomod.Cli -- skse build "./MyPlugin"

# Debug build
dotnet run --project src/SpookysAutomod.Cli -- skse build "./MyPlugin" --config Debug

# Clean rebuild
dotnet run --project src/SpookysAutomod.Cli -- skse build "./MyPlugin" --clean

# JSON output
dotnet run --project src/SpookysAutomod.Cli -- skse build "./MyPlugin" --json
```

### Manual xmake Build
```bash
cd MyPlugin
xmake            # configures + builds (first run downloads deps via xmake-repo)
xmake build      # subsequent incremental builds
xmake config -m debug   # switch to Debug
xmake clean             # clean artifacts
```

### Output Location
- Release: `build/windows/x64/release/MyPlugin.dll`
- Debug: `build/windows/x64/debug/MyPlugin.dll`

## Installing SKSE Plugins

1. Copy DLL to `Data/SKSE/Plugins/`
2. Copy any config files to same folder
3. Launch game with SKSE loader

## Limitations

This module **CAN**:
- Generate project scaffolding
- Build projects end-to-end via `skse build` (requires xmake + MSVC)
- Add Papyrus native function stubs
- Manage project configuration

This module **CANNOT**:
- Write custom C++ logic (generates stubs/templates)
- Auto-install build tools (user must install xmake and MSVC)
- Debug plugins
- Generate complex game hooks

For advanced SKSE development:
- [CommonLibSSE-NG Wiki](https://github.com/CharmedBaryon/CommonLibSSE-NG/wiki)
- [SKSE Plugin Development Guide](https://www.creationkit.com/index.php?title=Category:SKSE)

## Important Notes

1. **Build tools required** - xmake and MSVC Build Tools needed (NOT full Visual Studio IDE)
2. **Single codebase, all versions** - CommonLibSSE-NG handles version differences (SE + AE by default; flip `skyrim_vr = true` in `xmake.lua` for VR)
3. **xmake-repo auto-resolves deps** - `commonlibsse-ng`, `fmt`, `spdlog` downloaded on first build (~5 minutes first time, cached at `%LOCALAPPDATA%\.xmake\packages\` after)
4. **Native functions need matching Papyrus declarations** - Script must declare functions as `native`
5. **Use `--json` flag** for machine-readable output when scripting
6. **LLMs can build plugins** - Use Bash tool to invoke `xmake` (or `skse build`) for end-to-end workflow

## JSON Output

All commands support `--json` for structured output:
```bash
dotnet run --project src/SpookysAutomod.Cli -- skse info "./MyPlugin" --json
```

Example response:
```json
{
  "success": true,
  "result": {
    "name": "MyPlugin",
    "author": "YourName",
    "version": "1.0.0",
    "template": "papyrus-native",
    "description": "My SKSE Plugin",
    "targetVersions": ["SE", "AE"],
    "papyrusFunctions": [
      {
        "name": "GetActorSpeed",
        "returnType": "Float",
        "parameters": [
          { "type": "Actor", "name": "target" }
        ]
      }
    ]
  }
}
```
