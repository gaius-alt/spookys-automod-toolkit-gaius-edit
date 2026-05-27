# {{PROJECT_NAME}}

**Author:** {{AUTHOR}}
**Version:** {{VERSION_MAJOR}}.{{VERSION_MINOR}}.{{VERSION_PATCH}}
**Description:** {{DESCRIPTION}}

## Build requirements

- **xmake 2.8+** — https://xmake.io
- **MSVC Build Tools** (Visual Studio 2022 BuildTools or newer) — xmake auto-detects.
- **Internet** (first build only — `commonlibsse-ng`, `fmt`, and `spdlog` are downloaded via xmake-repo).

## Building

```cmd
xmake
```

The first build resolves CommonLibSSE-NG + dependencies via xmake-repo; subsequent builds are incremental.

Other commands:

```cmd
xmake -y                Build (auto-confirm prompts)
xmake config -m debug   Switch to Debug mode
xmake config -m release Switch back to Release
xmake clean             Clean build artifacts
xmake -v                Verbose build
```

## Installation

After building, the DLL is at `build/windows/x64/release/{{PROJECT_NAME}}.dll`. Copy it to:

```
<Skyrim>/Data/SKSE/Plugins/{{PROJECT_NAME}}.dll
```

Or uncomment the `after_build` block in `xmake.lua` and set your Skyrim path for auto-deployment.

## Project structure

```
{{PROJECT_NAME}}/
├── xmake.lua          # Build config (commonlibsse-ng + deps resolved via xmake-repo)
├── README.md          # This file
└── src/
    ├── PCH.h          # Precompiled header (RE/SKSE/std)
    └── main.cpp       # Plugin entry + event-sink examples
```

## Troubleshooting

- **`xmake` not found** — install from https://xmake.io and add to PATH.
- **Compiler not found** — xmake auto-detects MSVC. If missing, install VS BuildTools with the "Desktop development with C++" workload.
- **First build hangs on `commonlibsse-ng` download** — needs internet; the package is ~50 MB and compiles from source.

## Resources

- **CommonLibSSE-NG**: https://github.com/CharmedBaryon/CommonLibSSE-NG
- **SKSE**: https://skse.silverlock.org/
- **Example plugins**: https://github.com/topics/commonlibsse
