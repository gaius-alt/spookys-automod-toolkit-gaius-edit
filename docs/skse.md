# SKSE Module Reference

The SKSE module handles creation and management of SKSE (Skyrim Script Extender) C++ plugin projects.

## Overview

SKSE plugins are DLL files that extend Skyrim's functionality at a native level. This module generates project scaffolding using **CommonLibSSE-NG**, which supports Skyrim SE, AE, GOG, and VR from a single codebase.

**Complete Workflow:** This module generates project files that can then be built using xmake. When build tools are installed, AI assistants can generate and build SKSE plugins end-to-end.

**Building Requirements:**
- MSVC Build Tools (no Visual Studio IDE needed; xmake also recognises VS 18 / 2026 preview)
- xmake 2.8+
- Internet connection (first build only - `commonlibsse-ng`, `fmt`, `spdlog` downloaded via xmake-repo)

## Commands

### templates

List available SKSE project templates.

```bash
skse templates
```

**Available Templates:**

| Template | Description |
|----------|-------------|
| `basic` | Minimal SKSE plugin with logging |
| `papyrus-native` | Plugin with Papyrus native function support |

**Example:**
```bash
skse templates
```

---

### create

Create a new SKSE plugin project.

```bash
skse create <name> [options]
```

**Arguments:**
| Argument | Description |
|----------|-------------|
| `name` | Project name |

**Options:**
| Option | Default | Description |
|--------|---------|-------------|
| `--template` | `basic` | Template to use |
| `--output` | `.` | Output directory |
| `--author` | `Unknown` | Author name |
| `--description` | - | Project description |

**Examples:**
```bash
# Basic plugin
skse create "MyPlugin" --output "./"

# Papyrus native functions plugin
skse create "MyNativePlugin" --template papyrus-native --author "YourName" --output "./"
```

**Generated Structure:**
```
MyPlugin/
  xmake.lua           # xmake build config; resolves commonlibsse-ng + deps via xmake-repo
  README.md           # Build instructions (basic template)
  skse-project.json   # Toolkit configuration
  src/
    PCH.h             # Precompiled header (basic template only)
    main.cpp          # Plugin entry point
    plugin.{cpp,h}    # Plugin module (papyrus-native template)
    papyrus.{cpp,h}   # Native function registration (papyrus-native template)
```

---

### info

Get information about an SKSE project.

```bash
skse info <path>
```

**Arguments:**
| Argument | Description |
|----------|-------------|
| `path` | Project directory (default: current) |

**Output includes:**
- Project name
- Author
- Version
- Template used
- Description
- Target Skyrim versions
- Papyrus functions (if any)

**Example:**
```bash
skse info "./MyPlugin"
```

---

### add-function

Add a Papyrus native function to a project.

```bash
skse add-function <project> --name <name> [options]
```

**Arguments:**
| Argument | Description |
|----------|-------------|
| `project` | Project directory |

**Required Options:**
| Option | Description |
|--------|-------------|
| `--name` | Function name |

**Optional:**
| Option | Default | Description |
|--------|---------|-------------|
| `--return` | `void` | Return type |
| `--param` | - | Parameters (format: `type:name`, can repeat) |

**Papyrus Types:**
| Papyrus | C++ |
|---------|-----|
| Int | int |
| Float | float |
| Bool | bool |
| String | std::string |
| Actor | RE::Actor* |
| ObjectReference | RE::TESObjectREFR* |
| Form | RE::TESForm* |

**Examples:**
```bash
# Simple function
skse add-function "./MyPlugin" --name "GetPluginVersion" --return "Int"

# Function with parameters
skse add-function "./MyPlugin" --name "SetActorSpeed" --return "void" --param "Actor:target" --param "Float:speed"

# Function returning Actor
skse add-function "./MyPlugin" --name "GetNearestActor" --return "Actor" --param "ObjectReference:origin" --param "Float:radius"
```

---

### build

Build an SKSE plugin project using xmake.

```bash
skse build <project> [options]
```

**Arguments:**
| Argument | Description |
|----------|-------------|
| `project` | Project directory (default: current) |

**Options:**
| Option | Default | Description |
|--------|---------|-------------|
| `--config` | `Release` | Build configuration (Release or Debug) |
| `--clean` | `false` | Clean build directory before building |

**Examples:**
```bash
# Build current project
skse build .

# Build with debug configuration
skse build "./MyPlugin" --config Debug

# Clean rebuild
skse build "./MyPlugin" --clean

# JSON output for AI parsing
skse build "./MyPlugin" --json
```

**Output includes:**
- Build success/failure
- Output DLL path
- Build directory
- Configure and build output

---

## Building Projects

### Using the `skse build` Command (Recommended)

The simplest way to build SKSE plugins:

```bash
# 1. Create project
skse create "MyPlugin" --output "./"

# 2. Build it
skse build "./MyPlugin"

# Output: MyPlugin/build/windows/x64/release/MyPlugin.dll
```

### Manual xmake Build

You can also build manually:

```bash
cd MyPlugin
xmake          # configures + builds (first run downloads deps via xmake-repo)
xmake build    # incremental subsequent builds
```

### Build Requirements

| Tool | Purpose | Installation |
|------|---------|--------------|
| MSVC Build Tools | C++ compiler (no IDE needed) | [Build Tools for Visual Studio](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022) |
| xmake 2.8+ | Build system | [Download](https://xmake.io) |

---

## Template Details

### basic

Minimal plugin with:
- SKSE plugin info via the modern `SKSEPluginInfo` macro
- Logging setup via spdlog
- Message handler (`kDataLoaded`, `kPostLoad`)
- One worked event-sink example (OnHit)

```cpp
SKSEPluginInfo(
    .Version = REL::Version{ 1, 0, 0, 0 },
    .Name    = PLUGIN_NAME,
    .Author  = PLUGIN_AUTHOR
)

SKSEPluginLoad(const SKSE::LoadInterface* skse) {
    SKSE::Init(skse);
    // Your code here
    return true;
}
```

### papyrus-native

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

Papyrus usage:
```papyrus
Int value = MyScript.MyFunction()
```

---

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

---

## Project Configuration

Projects store configuration in `skse_config.json`:

```json
{
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
```

---

## Capabilities and Limitations

This module **can**:
- Generate project scaffolding (xmake.lua, C++ source files)
- Add Papyrus native function stubs
- Manage project configuration
- Build projects end-to-end via `skse build` (when xmake and MSVC Build Tools are installed)
- Detect xmake and MSVC availability

This module **cannot**:
- Write custom C++ logic (generates stubs and templates only)
- Auto-install build tools (user must install xmake and MSVC manually)
- Debug plugins
- Generate complex event hooks

For advanced SKSE development:
- [CommonLibSSE-NG Wiki](https://github.com/CharmedBaryon/CommonLibSSE-NG/wiki)
- [SKSE Plugin Development Guide](https://www.creationkit.com/index.php?title=Category:SKSE)

---

## JSON Output

All commands support `--json` for machine-readable output:

```bash
skse info "./MyPlugin" --json
```

**Success response:**
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
