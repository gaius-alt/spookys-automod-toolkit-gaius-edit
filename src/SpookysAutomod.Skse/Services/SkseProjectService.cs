using System.Diagnostics;
using System.Reflection;
using System.Text;
using SpookysAutomod.Core.Logging;
using SpookysAutomod.Core.Models;
using SpookysAutomod.Skse.Models;

namespace SpookysAutomod.Skse.Services;

/// <summary>
/// Service for creating and managing SKSE C++ plugin projects.
/// </summary>
public class SkseProjectService
{
    private readonly IModLogger _logger;
    private readonly Assembly _assembly;

    public SkseProjectService(IModLogger logger)
    {
        _logger = logger;
        _assembly = typeof(SkseProjectService).Assembly;
    }

    /// <summary>
    /// Creates a new SKSE plugin project from a template.
    /// </summary>
    public Result<string> CreateProject(SkseProjectConfig config, string outputPath)
    {
        try
        {
            var templateName = config.Template.ToLowerInvariant() switch
            {
                "basic" => "basic",
                "papyrus-native" => "papyrus-native",
                _ => "basic"
            };

            _logger.Info($"Creating SKSE project '{config.Name}' from template '{templateName}'");

            // Create output directory
            var projectDir = Path.Combine(outputPath, config.Name);
            if (Directory.Exists(projectDir))
            {
                return Result<string>.Fail(
                    $"Directory already exists: {projectDir}",
                    suggestions: new List<string> { "Choose a different project name", "Delete the existing directory first" });
            }

            Directory.CreateDirectory(projectDir);

            // Get all template files
            var templatePrefix = $"SpookysAutomod.Skse.Templates.{templateName.Replace("-", "_")}.";
            var resources = _assembly.GetManifestResourceNames()
                .Where(r => r.StartsWith(templatePrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (resources.Count == 0)
            {
                return Result<string>.Fail(
                    $"Template '{templateName}' not found",
                    suggestions: new List<string> { "Available templates: basic, papyrus-native" });
            }

            var replacements = BuildReplacements(config);

            foreach (var resourceName in resources)
            {
                // Resource name format: SpookysAutomod.Skse.Templates.basic.src.main.cpp
                // We need to convert to: src/main.cpp
                var relativePath = resourceName.Substring(templatePrefix.Length);

                // Split by dots and reconstruct the path
                var parts = relativePath.Split('.');
                if (parts.Length >= 2)
                {
                    // Last two parts are filename.extension (e.g., "main" and "cpp")
                    var fileName = parts[^2] + "." + parts[^1];
                    // Everything before that is the directory path
                    var dirParts = parts.Take(parts.Length - 2).ToArray();
                    relativePath = dirParts.Length > 0
                        ? Path.Combine(Path.Combine(dirParts), fileName)
                        : fileName;
                }

                var outputFilePath = Path.Combine(projectDir, relativePath);
                var outputFileDir = Path.GetDirectoryName(outputFilePath);
                if (!string.IsNullOrEmpty(outputFileDir))
                {
                    Directory.CreateDirectory(outputFileDir);
                }

                using var stream = _assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();

                // Apply replacements
                foreach (var (placeholder, value) in replacements)
                {
                    content = content.Replace(placeholder, value);
                }

                File.WriteAllText(outputFilePath, content);
                _logger.Debug($"Created: {relativePath}");
            }

            // Write project config
            var configPath = Path.Combine(projectDir, "skse-project.json");
            var configJson = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(configPath, configJson);

            _logger.Info($"SKSE project created at: {projectDir}");

            return Result<string>.Ok(projectDir);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail(
                $"Failed to create SKSE project: {ex.Message}",
                suggestions: new List<string> { "Check write permissions to output directory" });
        }
    }

    /// <summary>
    /// Gets information about an existing SKSE project.
    /// </summary>
    public Result<SkseProjectConfig> GetProjectInfo(string projectPath)
    {
        try
        {
            var configPath = Path.Combine(projectPath, "skse-project.json");
            if (!File.Exists(configPath))
            {
                // Try to infer from xmake.lua
                var xmakePath = Path.Combine(projectPath, "xmake.lua");
                if (!File.Exists(xmakePath))
                {
                    return Result<SkseProjectConfig>.Fail(
                        "Not a valid SKSE project directory",
                        suggestions: new List<string> { "Missing skse-project.json and xmake.lua" });
                }

                var config = InferConfigFromXmake(xmakePath);
                if (config != null)
                {
                    return Result<SkseProjectConfig>.Ok(config);
                }

                return Result<SkseProjectConfig>.Fail(
                    "Could not read project configuration",
                    suggestions: new List<string> { "Ensure skse-project.json exists or xmake.lua is valid" });
            }

            var json = File.ReadAllText(configPath);
            var projectConfig = System.Text.Json.JsonSerializer.Deserialize<SkseProjectConfig>(json);

            if (projectConfig == null)
            {
                return Result<SkseProjectConfig>.Fail("Invalid project configuration");
            }

            return Result<SkseProjectConfig>.Ok(projectConfig);
        }
        catch (Exception ex)
        {
            return Result<SkseProjectConfig>.Fail($"Failed to read project info: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a Papyrus native function to an existing project.
    /// </summary>
    public Result<bool> AddPapyrusFunction(string projectPath, PapyrusNativeFunction function)
    {
        try
        {
            var configResult = GetProjectInfo(projectPath);
            if (!configResult.Success || configResult.Value == null)
            {
                return Result<bool>.Fail("Could not read project configuration");
            }

            var config = configResult.Value;
            if (config.Template != "papyrus-native")
            {
                return Result<bool>.Fail(
                    "Project does not support Papyrus native functions",
                    suggestions: new List<string> { "Create a new project with --template papyrus-native" });
            }

            // Add to config
            config.PapyrusFunctions.Add(function);

            // Update papyrus.h
            var papyrusHeaderPath = Path.Combine(projectPath, "src", "papyrus.h");
            if (File.Exists(papyrusHeaderPath))
            {
                var header = File.ReadAllText(papyrusHeaderPath);
                var declaration = GenerateFunctionDeclaration(function);

                // Insert before closing brace of class
                var insertPoint = header.LastIndexOf("};", StringComparison.Ordinal);
                if (insertPoint > 0)
                {
                    header = header.Insert(insertPoint, $"        {declaration}\n    ");
                    File.WriteAllText(papyrusHeaderPath, header);
                }
            }

            // Update papyrus.cpp
            var papyrusCppPath = Path.Combine(projectPath, "src", "papyrus.cpp");
            if (File.Exists(papyrusCppPath))
            {
                var cpp = File.ReadAllText(papyrusCppPath);

                // Add registration
                var registrationLine = $"        a_vm->RegisterFunction(\"{function.Name}\", \"{config.Name}\", {function.Name});";
                var registerInsertPoint = cpp.IndexOf("SKSE::log::info(\"Registered Papyrus", StringComparison.Ordinal);
                if (registerInsertPoint > 0)
                {
                    cpp = cpp.Insert(registerInsertPoint, registrationLine + "\n\n        ");
                }

                // Add implementation
                var implementation = GenerateFunctionImplementation(config.Name, function);
                cpp += "\n" + implementation;

                File.WriteAllText(papyrusCppPath, cpp);
            }

            // Save updated config
            var configPath = Path.Combine(projectPath, "skse-project.json");
            var configJson = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(configPath, configJson);

            _logger.Info($"Added Papyrus function: {function.Name}");
            return Result<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Fail($"Failed to add Papyrus function: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds an SKSE plugin project using xmake.
    /// </summary>
    public async Task<Result<SkseProjectBuildResult>> BuildProjectAsync(
        string projectPath, string configuration = "Release", bool clean = false)
    {
        try
        {
            // Verify it's an SKSE project
            var xmakePath = Path.Combine(projectPath, "xmake.lua");
            if (!File.Exists(xmakePath))
            {
                return Result<SkseProjectBuildResult>.Fail(
                    "Not a valid SKSE project directory - xmake.lua not found",
                    suggestions: new List<string> { "Run 'skse create' first to scaffold a project" });
            }

            // Check for xmake
            var xmakeCheck = await RunProcessAsync("xmake", "--version", projectPath);
            if (xmakeCheck.exitCode != 0)
            {
                return Result<SkseProjectBuildResult>.Fail(
                    "xmake not found. xmake 2.8+ is required to build SKSE plugins.",
                    suggestions: new List<string>
                    {
                        "Install xmake from https://xmake.io",
                        "Ensure xmake is in your PATH",
                        "Run the Setup Wizard to check build tool status"
                    });
            }

            // Clean if requested
            var buildDir = Path.Combine(projectPath, "build");
            var xmakeCacheDir = Path.Combine(projectPath, ".xmake");
            if (clean)
            {
                _logger.Info("Cleaning build directory...");
                if (Directory.Exists(buildDir))      Directory.Delete(buildDir, true);
                if (Directory.Exists(xmakeCacheDir)) Directory.Delete(xmakeCacheDir, true);
            }

            // xmake uses lowercase mode names: release / debug.
            var xmakeMode = configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase) ? "debug" : "release";

            // Configure with xmake (resolves deps from xmake-repo on first run).
            _logger.Info("Configuring with xmake...");
            var configureResult = await RunProcessAsync(
                "xmake", $"config --yes -m {xmakeMode} -p windows -a x64", projectPath);

            if (configureResult.exitCode != 0)
            {
                var suggestions = new List<string>();
                if (configureResult.output.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase) &&
                    configureResult.output.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    suggestions.Add("MSVC Build Tools not found - install from https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022");
                    suggestions.Add("Select 'Desktop development with C++' workload during installation");
                }
                else
                {
                    suggestions.Add("Check the error output above for details");
                    suggestions.Add("Ensure you have internet access (first build downloads CommonLibSSE-NG, fmt, spdlog via xmake-repo)");
                    suggestions.Add("Try: skse build --clean for a fresh configure");
                }

                return Result<SkseProjectBuildResult>.Fail(
                    $"xmake configuration failed:\n{configureResult.output}",
                    suggestions: suggestions);
            }

            // Build
            _logger.Info($"Building ({configuration})...");
            var buildResult = await RunProcessAsync(
                "xmake", "build --yes", projectPath);

            if (buildResult.exitCode != 0)
            {
                return Result<SkseProjectBuildResult>.Fail(
                    $"Build failed:\n{buildResult.output}",
                    suggestions: new List<string>
                    {
                        "Check C++ compilation errors above",
                        "Try: skse build --clean for a fresh build",
                        "Ensure MSVC Build Tools are installed with C++ support"
                    });
            }

            // xmake output layout: build/windows/x64/<mode>/<target>.dll
            string? dllPath = null;
            var outputDir = Path.Combine(buildDir, "windows", "x64", xmakeMode);
            if (Directory.Exists(outputDir))
            {
                dllPath = Directory.GetFiles(outputDir, "*.dll").FirstOrDefault();
            }
            // Fallback: search anywhere under build/ (in case the layout differs).
            if (dllPath == null && Directory.Exists(buildDir))
            {
                dllPath = Directory.GetFiles(buildDir, "*.dll", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("CommonLib", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
            }

            var result = new SkseProjectBuildResult
            {
                Success = true,
                Configuration = configuration,
                OutputDll = dllPath,
                BuildDirectory = buildDir,
                ConfigureOutput = configureResult.output,
                BuildOutput = buildResult.output
            };

            _logger.Info($"Build succeeded! Output: {dllPath ?? "DLL not found in expected location"}");
            return Result<SkseProjectBuildResult>.Ok(result);
        }
        catch (Exception ex)
        {
            return Result<SkseProjectBuildResult>.Fail($"Build error: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if xmake is installed and returns version info.
    /// </summary>
    public async Task<Result<string>> CheckXmakeAsync()
    {
        try
        {
            var result = await RunProcessAsync("xmake", "--version", null);
            if (result.exitCode == 0)
            {
                var firstLine = result.output.Split('\n').FirstOrDefault()?.Trim() ?? result.output.Trim();
                return Result<string>.Ok(firstLine);
            }
            return Result<string>.Fail("xmake not found",
                suggestions: new List<string> { "Install from https://xmake.io" });
        }
        catch
        {
            return Result<string>.Fail("xmake not found",
                suggestions: new List<string> { "Install from https://xmake.io" });
        }
    }

    /// <summary>
    /// Backwards-compat shim - returns the xmake check result.
    /// </summary>
    [Obsolete("Use CheckXmakeAsync. CMake is no longer the build driver.")]
    public Task<Result<string>> CheckCMakeAsync() => CheckXmakeAsync();

    /// <summary>
    /// Checks if MSVC Build Tools are installed.
    /// </summary>
    public async Task<Result<string>> CheckMsvcAsync()
    {
        try
        {
            // Try cl.exe (MSVC compiler)
            var result = await RunProcessAsync("cl", "", null);
            // cl.exe with no args returns version info on stderr with exit code 0
            var output = result.output.Trim();
            if (output.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            {
                var firstLine = output.Split('\n').FirstOrDefault()?.Trim() ?? output;
                return Result<string>.Ok(firstLine);
            }

            // Try via vswhere
            var vswherePath = @"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe";
            if (File.Exists(vswherePath))
            {
                var vsResult = await RunProcessAsync(vswherePath,
                    "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property displayName", null);
                if (vsResult.exitCode == 0 && !string.IsNullOrWhiteSpace(vsResult.output))
                {
                    return Result<string>.Ok(vsResult.output.Trim());
                }
            }

            return Result<string>.Fail("MSVC Build Tools not found",
                suggestions: new List<string>
                {
                    "Install from https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022",
                    "Select 'Desktop development with C++' workload"
                });
        }
        catch
        {
            return Result<string>.Fail("MSVC Build Tools not found",
                suggestions: new List<string>
                {
                    "Install from https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022"
                });
        }
    }

    /// <summary>
    /// Lists available SKSE templates.
    /// </summary>
    public Result<IEnumerable<string>> ListTemplates()
    {
        var templates = new[]
        {
            "basic - Simple SKSE plugin with event handlers",
            "papyrus-native - SKSE plugin with Papyrus native function support"
        };

        return Result<IEnumerable<string>>.Ok(templates);
    }

    private Dictionary<string, string> BuildReplacements(SkseProjectConfig config)
    {
        // Parse version string
        var versionParts = config.Version.Split('.');
        var major = versionParts.Length > 0 ? versionParts[0] : "1";
        var minor = versionParts.Length > 1 ? versionParts[1] : "0";
        var patch = versionParts.Length > 2 ? versionParts[2] : "0";

        return new Dictionary<string, string>
        {
            { "{{PROJECT_NAME}}", config.Name },
            { "{{AUTHOR}}", config.Author },
            { "{{DESCRIPTION}}", config.Description },
            { "{{VERSION_MAJOR}}", major },
            { "{{VERSION_MINOR}}", minor },
            { "{{VERSION_PATCH}}", patch }
        };
    }

    private SkseProjectConfig? InferConfigFromXmake(string xmakePath)
    {
        try
        {
            var content = File.ReadAllText(xmakePath);
            var config = new SkseProjectConfig();

            // Extract project name from set_project("Foo") or target("Foo")
            var projectMatch = System.Text.RegularExpressions.Regex.Match(
                content, @"set_project\s*\(\s*[""']([^""']+)[""']\s*\)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (projectMatch.Success)
            {
                config.Name = projectMatch.Groups[1].Value;
            }
            else
            {
                var targetMatch = System.Text.RegularExpressions.Regex.Match(
                    content, @"target\s*\(\s*[""']([^""']+)[""']\s*\)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (targetMatch.Success)
                {
                    config.Name = targetMatch.Groups[1].Value;
                }
            }

            // Check if it has papyrus support
            if (content.Contains("papyrus", StringComparison.OrdinalIgnoreCase))
            {
                config.Template = "papyrus-native";
            }

            return config;
        }
        catch
        {
            return null;
        }
    }

    private string GenerateFunctionDeclaration(PapyrusNativeFunction function)
    {
        var paramList = new StringBuilder();
        paramList.Append("RE::StaticFunctionTag*");

        foreach (var param in function.Parameters)
        {
            paramList.Append($", {MapPapyrusTypeToCpp(param.Type)} {param.Name}");
        }

        return $"static {MapPapyrusTypeToCpp(function.ReturnType)} {function.Name}({paramList});";
    }

    private string GenerateFunctionImplementation(string projectName, PapyrusNativeFunction function)
    {
        var paramList = new StringBuilder();
        paramList.Append("RE::StaticFunctionTag*");

        foreach (var param in function.Parameters)
        {
            paramList.Append($", {MapPapyrusTypeToCpp(param.Type)} {param.Name}");
        }

        var defaultReturn = function.ReturnType.ToLowerInvariant() switch
        {
            "void" => "",
            "int" => "return 0;",
            "float" => "return 0.0f;",
            "bool" => "return false;",
            "string" => "return \"\";",
            _ => "return nullptr;"
        };

        return $@"    {MapPapyrusTypeToCpp(function.ReturnType)} Papyrus::{function.Name}({paramList})
    {{
        // TODO: Implement {function.Name}
        {defaultReturn}
    }}
";
    }

    private string MapPapyrusTypeToCpp(string papyrusType)
    {
        return papyrusType.ToLowerInvariant() switch
        {
            "int" => "int32_t",
            "float" => "float",
            "bool" => "bool",
            "string" => "RE::BSFixedString",
            "actor" => "RE::Actor*",
            "objectreference" => "RE::TESObjectREFR*",
            "form" => "RE::TESForm*",
            "void" => "void",
            _ => $"RE::{papyrusType}*"
        };
    }

    private static async Task<(int exitCode, string output)> RunProcessAsync(
        string fileName, string arguments, string? workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (workingDir != null)
            psi.WorkingDirectory = workingDir;

        using var process = Process.Start(psi);
        if (process == null) return (-1, "Failed to start process");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout + stderr);
    }
}

/// <summary>
/// Result of building an SKSE plugin project.
/// </summary>
public class SkseProjectBuildResult
{
    public bool Success { get; set; }
    public string Configuration { get; set; } = "Release";
    public string? OutputDll { get; set; }
    public string? BuildDirectory { get; set; }
    public string? ConfigureOutput { get; set; }
    public string? BuildOutput { get; set; }
}
