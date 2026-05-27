#pragma once

// ========================================
// Precompiled header for {{PROJECT_NAME}}
// ========================================
// Wired in via xmake.lua: set_pcxxheader("src/PCH.h"). Do NOT manually
// #include "PCH.h" in .cpp files — xmake auto-includes it.

// Standard library
#include <cstdint>
#include <cstddef>
#include <cstring>
#include <string>
#include <string_view>
#include <vector>
#include <unordered_map>
#include <memory>
#include <functional>
#include <algorithm>
#include <ranges>

using namespace std::literals;

// CommonLibSSE-NG
#include <RE/Skyrim.h>
#include <SKSE/SKSE.h>

#include <spdlog/sinks/basic_file_sink.h>

// Version macros (PLUGIN_NAME / PLUGIN_AUTHOR / PLUGIN_VERSION are -D'd
// from xmake.lua; defined here as fallbacks in case the build system
// hasn't injected them).
#ifndef PLUGIN_NAME
#define PLUGIN_NAME "{{PROJECT_NAME}}"
#endif
#ifndef PLUGIN_AUTHOR
#define PLUGIN_AUTHOR "{{AUTHOR}}"
#endif
#ifndef PLUGIN_VERSION
#define PLUGIN_VERSION "{{VERSION_MAJOR}}.{{VERSION_MINOR}}.{{VERSION_PATCH}}"
#endif
