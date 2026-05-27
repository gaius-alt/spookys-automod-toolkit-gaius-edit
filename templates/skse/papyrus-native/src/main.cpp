// {{PROJECT_NAME}} - SKSE plugin entry point (Papyrus-native template).
// Author:      {{AUTHOR}}
// Version:     {{VERSION_MAJOR}}.{{VERSION_MINOR}}.{{VERSION_PATCH}}
// Description: {{DESCRIPTION}}
//
// Uses the modern CommonLibSSE-NG entry-point macros (SKSEPluginInfo +
// SKSEPluginLoad). Pre-3.7 SKSEPlugin_Query / SKSEPlugin_Version boilerplate
// is no longer needed.

#include "plugin.h"
#include "papyrus.h"

SKSEPluginInfo(
    .Version = REL::Version{ {{VERSION_MAJOR}}, {{VERSION_MINOR}}, {{VERSION_PATCH}}, 0 },
    .Name    = PLUGIN_NAME,
    .Author  = PLUGIN_AUTHOR
)

SKSEPluginLoad(const SKSE::LoadInterface* a_skse)
{
    SKSE::Init(a_skse);

    if (auto path = SKSE::log::log_directory(); path) {
        *path /= PLUGIN_NAME;
        *path += ".log";
        auto sink = std::make_shared<spdlog::sinks::basic_file_sink_mt>(path->string(), true);
        auto log  = std::make_shared<spdlog::logger>("global", std::move(sink));
        log->set_level(spdlog::level::info);
        log->flush_on(spdlog::level::info);
        spdlog::set_default_logger(std::move(log));
    }

    SKSE::log::info("{} v{} loaded", PLUGIN_NAME, PLUGIN_VERSION);

    if (auto* papyrus = SKSE::GetPapyrusInterface(); papyrus) {
        papyrus->Register({{PROJECT_NAME}}::Papyrus::RegisterFunctions);
        SKSE::log::info("Papyrus interface registered");
    }

    {{PROJECT_NAME}}::Plugin::Initialize();

    return true;
}
