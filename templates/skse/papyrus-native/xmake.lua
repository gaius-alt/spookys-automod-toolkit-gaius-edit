-- {{PROJECT_NAME}} - SKSE plugin with Papyrus native function support.
-- Author:  {{AUTHOR}}
-- Version: {{VERSION_MAJOR}}.{{VERSION_MINOR}}.{{VERSION_PATCH}}
--
-- Build:    xmake
-- Output:   build artifacts under build/; copy to <Skyrim>/Data/SKSE/Plugins/
--
-- Dependencies (CommonLibSSE-NG, fmt, spdlog) are resolved automatically
-- via xmake-repo on first run.

set_project("{{PROJECT_NAME}}")
set_version("{{VERSION_MAJOR}}.{{VERSION_MINOR}}.{{VERSION_PATCH}}")
set_xmakever("2.8.0")

set_languages("c++23")
set_arch("x64")
set_warnings("allextra")
set_optimize("fastest")
set_runtimes("MD")

add_rules("mode.release", "mode.debug")

add_requires("commonlibsse-ng v3.7.0", {
    configs = {
        skyrim_se = true,
        skyrim_ae = true,
        skyrim_vr = false,
        runtimes  = "MD",
    }
})

target("{{PROJECT_NAME}}")
    set_kind("shared")
    set_extension(".dll")

    add_files("src/*.cpp")
    add_includedirs("src")
    add_headerfiles("src/*.h")

    add_packages("commonlibsse-ng")

    add_defines(
        'PLUGIN_NAME="{{PROJECT_NAME}}"',
        'PLUGIN_AUTHOR="{{AUTHOR}}"',
        'PLUGIN_VERSION="{{VERSION_MAJOR}}.{{VERSION_MINOR}}.{{VERSION_PATCH}}"'
    )

    -- Uncomment to auto-deploy the DLL after every build. Adjust the path:
    -- after_build(function (target)
    --     local outdir = "C:/Steam/steamapps/common/Skyrim Special Edition/Data/SKSE/Plugins"
    --     os.mkdir(outdir)
    --     os.cp(target:targetfile(), outdir)
    --     cprint("${bright magenta}Deployed:${clear} %s", path.join(outdir, "{{PROJECT_NAME}}.dll"))
    -- end)
target_end()
