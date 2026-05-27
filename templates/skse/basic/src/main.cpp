// ========================================
// {{PROJECT_NAME}} - SKSE plugin entry point.
// Author:      {{AUTHOR}}
// Version:     {{VERSION_MAJOR}}.{{VERSION_MINOR}}.{{VERSION_PATCH}}
// Description: {{DESCRIPTION}}
// ========================================
//
// Uses the modern CommonLibSSE-NG entry-point macros (SKSEPluginInfo +
// SKSEPluginLoad). Pre-3.7 SKSEPlugin_Query / SKSEPlugin_Version boilerplate
// is no longer needed.

#include "PCH.h"

// ========================================
// Plugin metadata
// ========================================

SKSEPluginInfo(
    .Version = REL::Version{ {{VERSION_MAJOR}}, {{VERSION_MINOR}}, {{VERSION_PATCH}}, 0 },
    .Name    = PLUGIN_NAME,
    .Author  = PLUGIN_AUTHOR
)

// ========================================
// Event sinks (examples)
// ========================================

class OnHitEventHandler : public RE::BSTEventSink<RE::TESHitEvent>
{
public:
    static OnHitEventHandler* GetSingleton()
    {
        static OnHitEventHandler singleton;
        return &singleton;
    }

    RE::BSEventNotifyControl ProcessEvent(
        const RE::TESHitEvent*                  event,
        RE::BSTEventSource<RE::TESHitEvent>*) override
    {
        if (!event) return RE::BSEventNotifyControl::kContinue;

        auto target = event->target.get();
        auto targetActor = target ? target->As<RE::Actor>() : nullptr;
        if (!targetActor) return RE::BSEventNotifyControl::kContinue;

        auto cause = event->cause.get();
        auto causeActor = cause ? cause->As<RE::Actor>() : nullptr;
        if (!causeActor) return RE::BSEventNotifyControl::kContinue;

        SKSE::log::info(
            "OnHit: {} hit {} (weapon=0x{:08X})",
            std::string_view{causeActor->GetName()},
            std::string_view{targetActor->GetName()},
            event->source);

        return RE::BSEventNotifyControl::kContinue;
    }

private:
    OnHitEventHandler() = default;
    OnHitEventHandler(const OnHitEventHandler&) = delete;
    OnHitEventHandler& operator=(const OnHitEventHandler&) = delete;
};

// ========================================
// Lifecycle
// ========================================

static void InitializeEventHandlers()
{
    if (auto* src = RE::ScriptEventSourceHolder::GetSingleton(); src) {
        src->AddEventSink<RE::TESHitEvent>(OnHitEventHandler::GetSingleton());
        SKSE::log::info("Registered OnHit event handler");
    } else {
        SKSE::log::error("ScriptEventSourceHolder unavailable; sinks not attached");
    }
}

static void MessageHandler(SKSE::MessagingInterface::Message* msg)
{
    switch (msg->type) {
        case SKSE::MessagingInterface::kDataLoaded:
            SKSE::log::info("kDataLoaded");
            InitializeEventHandlers();
            break;
        case SKSE::MessagingInterface::kPostLoad:
            SKSE::log::info("kPostLoad");
            break;
    }
}

// ========================================
// Entry point
// ========================================

SKSEPluginLoad(const SKSE::LoadInterface* skse)
{
    SKSE::Init(skse);

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

    if (auto* messaging = SKSE::GetMessagingInterface(); messaging) {
        messaging->RegisterListener(MessageHandler);
    }

    return true;
}
