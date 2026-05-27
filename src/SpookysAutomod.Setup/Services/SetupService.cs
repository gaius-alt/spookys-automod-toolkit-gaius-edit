using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using SharpCompress.Archives.SevenZip;

namespace SpookysAutomod.Setup.Services;

public class SetupService
{
    private static readonly HttpClient _httpClient = new();

    public string ToolkitRoot { get; }

    public SetupService()
    {
        // Toolkit root is where SpookysAutomod.sln lives
        var exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        // 1. Check exe directory itself (dev environment)
        if (File.Exists(Path.Combine(exeDir, "SpookysAutomod.sln")))
        {
            ToolkitRoot = exeDir;
        }
        // 2. Check spookys-automod-toolkit/ subdirectory (release layout: exe is at root, sln is in subfolder)
        else if (File.Exists(Path.Combine(exeDir, "spookys-automod-toolkit", "SpookysAutomod.sln")))
        {
            ToolkitRoot = Path.Combine(exeDir, "spookys-automod-toolkit");
        }
        else
        {
            // 3. Running from bin/Debug/net8.0-windows/ - walk up to find .sln
            var dir = new DirectoryInfo(exeDir);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SpookysAutomod.sln")))
                dir = dir.Parent;
            ToolkitRoot = dir?.FullName ?? exeDir;
        }
    }

    #region Skyrim Detection

    public List<SkyrimInstallation> DetectSkyrimInstallations()
    {
        var installations = new List<SkyrimInstallation>();
        var candidates = new List<(string path, SkyrimEdition edition, string exe)>
        {
            (@"C:\Program Files (x86)\Steam\steamapps\common\Skyrim Special Edition", SkyrimEdition.SE, "SkyrimSE.exe"),
            (@"C:\Program Files\Steam\steamapps\common\Skyrim Special Edition", SkyrimEdition.SE, "SkyrimSE.exe"),
            (@"D:\SteamLibrary\steamapps\common\Skyrim Special Edition", SkyrimEdition.SE, "SkyrimSE.exe"),
            (@"D:\Steam\steamapps\common\Skyrim Special Edition", SkyrimEdition.SE, "SkyrimSE.exe"),
            (@"E:\SteamLibrary\steamapps\common\Skyrim Special Edition", SkyrimEdition.SE, "SkyrimSE.exe"),
            (@"D:\Games\Steam\steamapps\common\Skyrim Special Edition", SkyrimEdition.SE, "SkyrimSE.exe"),
            (@"E:\Games\Steam\steamapps\common\Skyrim Special Edition", SkyrimEdition.SE, "SkyrimSE.exe"),
            (@"C:\Program Files (x86)\Steam\steamapps\common\SkyrimVR", SkyrimEdition.VR, "SkyrimVR.exe"),
            (@"C:\Program Files\Steam\steamapps\common\SkyrimVR", SkyrimEdition.VR, "SkyrimVR.exe"),
            (@"D:\SteamLibrary\steamapps\common\SkyrimVR", SkyrimEdition.VR, "SkyrimVR.exe"),
            (@"D:\Steam\steamapps\common\SkyrimVR", SkyrimEdition.VR, "SkyrimVR.exe"),
            (@"E:\SteamLibrary\steamapps\common\SkyrimVR", SkyrimEdition.VR, "SkyrimVR.exe"),
        };

        // Parse Steam library folders for additional paths
        try
        {
            var steamPaths = new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam"
            };

            foreach (var steamPath in steamPaths)
            {
                var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdfPath)) continue;

                var content = File.ReadAllText(vdfPath);
                foreach (var line in content.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("\"path\"")) continue;

                    var parts = trimmed.Split('"');
                    if (parts.Length < 4) continue;

                    var libPath = parts[3].Replace("\\\\", "\\");
                    candidates.Add((Path.Combine(libPath, "steamapps", "common", "Skyrim Special Edition"), SkyrimEdition.SE, "SkyrimSE.exe"));
                    candidates.Add((Path.Combine(libPath, "steamapps", "common", "SkyrimVR"), SkyrimEdition.VR, "SkyrimVR.exe"));
                }
            }
        }
        catch { /* Ignore VDF parsing failures */ }

        foreach (var (path, edition, exe) in candidates)
        {
            if (!Directory.Exists(path) || !File.Exists(Path.Combine(path, exe))) continue;
            if (installations.Any(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;

            var scriptSource = FindScriptSourceDir(path);
            installations.Add(new SkyrimInstallation
            {
                Path = path,
                Edition = edition,
                HasScriptHeaders = scriptSource != null,
                ScriptSourcePath = scriptSource
            });
        }

        return installations;
    }

    public string? FindScriptSourceDir(string skyrimPath)
    {
        var candidates = new[]
        {
            Path.Combine(skyrimPath, "Data", "Scripts", "Source"),
            Path.Combine(skyrimPath, "Data", "Source", "Scripts"),
            Path.Combine(skyrimPath, "Data", "Scripts", "Source", "Base"),
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate) &&
                Directory.GetFiles(candidate, "*.psc", SearchOption.TopDirectoryOnly).Length > 0)
            {
                return candidate;
            }
        }
        return null;
    }

    #endregion

    #region Script Headers (Symlink)

    public (bool success, string message) SetupScriptHeaders(string scriptSourceDir)
    {
        try
        {
            var targetLink = Path.Combine(ToolkitRoot, "skyrim-script-headers");

            // Remove existing directory/link if it exists
            if (Directory.Exists(targetLink))
            {
                var info = new DirectoryInfo(targetLink);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    // It's already a symlink - remove it
                    Directory.Delete(targetLink, false);
                }
                else
                {
                    // It's a real directory with copied files - remove it
                    Directory.Delete(targetLink, true);
                }
            }

            // Create directory junction (doesn't require admin, unlike symlinks)
            var result = RunProcess("cmd.exe", $"/c mklink /J \"{targetLink}\" \"{scriptSourceDir}\"");
            if (result.exitCode != 0)
            {
                return (false, $"Failed to create junction: {result.output}");
            }

            // Verify the junction works
            var pscCount = Directory.GetFiles(targetLink, "*.psc", SearchOption.TopDirectoryOnly).Length;
            return (true, $"Linked to {scriptSourceDir} ({pscCount} script headers available)");
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}");
        }
    }

    #endregion

    #region SKSE Headers

    /// <summary>
    /// Check if SKSE script headers (.psc) are available in the linked headers directory.
    /// </summary>
    public bool HasSkseHeaders()
    {
        var headersDir = Path.Combine(ToolkitRoot, "skyrim-script-headers");
        if (!Directory.Exists(headersDir)) return false;

        // Check for known SKSE .psc files
        var skseFiles = new[] { "SKSE.psc", "StringUtil.psc", "MiscUtil.psc", "PapyrusUtil.psc" };
        return skseFiles.Any(f => File.Exists(Path.Combine(headersDir, f)));
    }

    /// <summary>
    /// Search the Skyrim Data folder for SKSE script headers and copy them to the headers location.
    /// If not found locally, download from SKSE release.
    /// </summary>
    public async Task<(bool success, string message)> SetupSkseHeadersAsync(
        string skyrimPath, IProgress<(int percent, string status)>? progress = null)
    {
        try
        {
            var skseHeadersDir = Path.Combine(ToolkitRoot, "tools", "papyrus-compiler", "headers", "skse");
            Directory.CreateDirectory(skseHeadersDir);

            // Check if already set up in the correct location
            if (Directory.Exists(skseHeadersDir) &&
                Directory.GetFiles(skseHeadersDir, "*.psc").Length > 0)
            {
                var count = Directory.GetFiles(skseHeadersDir, "*.psc").Length;
                progress?.Report((100, $"Already installed ({count} headers)"));
                return (true, $"SKSE headers already in tools/papyrus-compiler/headers/skse/ ({count} files)");
            }

            // Search for SKSE headers in Skyrim Data folder and linked headers
            // SKSE's "modified" scripts override vanilla ones with added functions,
            // so we look for SKSE-exclusive files (SKSE.psc, StringUtil.psc, etc.)
            // and if found, copy ALL .psc files from that directory since they're
            // SKSE's modified versions meant to override vanilla for compilation.
            var searchDirs = new List<string>
            {
                Path.Combine(skyrimPath, "Data", "Scripts", "Source"),
                Path.Combine(skyrimPath, "Data", "Source", "Scripts"),
                Path.Combine(ToolkitRoot, "skyrim-script-headers"),
            };

            foreach (var sourceDir in searchDirs)
            {
                if (!Directory.Exists(sourceDir)) continue;

                // Check for SKSE-exclusive files that confirm SKSE is installed
                var hasSkse = File.Exists(Path.Combine(sourceDir, "SKSE.psc")) ||
                              File.Exists(Path.Combine(sourceDir, "StringUtil.psc")) ||
                              File.Exists(Path.Combine(sourceDir, "ModEvent.psc"));
                if (!hasSkse) continue;

                progress?.Report((50, $"Found SKSE headers, copying to tools/papyrus-compiler/headers/skse/..."));

                // Copy ALL .psc files — SKSE distributes modified versions of vanilla scripts
                // with added SKSE functions, plus new SKSE-only scripts
                var copied = 0;
                foreach (var psc in Directory.GetFiles(sourceDir, "*.psc"))
                {
                    var fileName = Path.GetFileName(psc);
                    // Skip SkyUI files (they go in their own directory)
                    if (fileName.StartsWith("SKI_", StringComparison.OrdinalIgnoreCase)) continue;

                    File.Copy(psc, Path.Combine(skseHeadersDir, fileName), overwrite: true);
                    copied++;
                }

                if (copied > 0)
                {
                    progress?.Report((100, $"Installed ({copied} headers)"));
                    return (true, $"Copied {copied} SKSE headers to tools/papyrus-compiler/headers/skse/");
                }
            }

            // Not found locally - try downloading from SKSE release
            progress?.Report((10, "SKSE headers not found locally, downloading..."));
            return await DownloadSkseHeadersAsync(skyrimPath, skseHeadersDir, progress);
        }
        catch (Exception ex)
        {
            return (false, $"SKSE headers setup failed: {ex.Message}");
        }
    }

    private async Task<(bool success, string message)> DownloadSkseHeadersAsync(
        string skyrimPath, string targetDir, IProgress<(int percent, string status)>? progress)
    {
        // Download SKSE headers from skse.silverlock.org (.7z archives)
        // Both SE and VR headers are downloaded so users can develop for either platform
        var downloads = new[]
        {
            (url: "https://skse.silverlock.org/beta/skse64_2_02_06.7z", label: "SKSE64 (SE/AE)", subdir: ""),
            (url: "https://skse.silverlock.org/beta/sksevr_2_00_12.7z", label: "SKSEVR (VR)", subdir: "vr"),
        };

        var totalExtracted = 0;
        var results = new List<string>();

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SpookysAutomodSetup/1.0");

            for (int i = 0; i < downloads.Length; i++)
            {
                var (url, label, subdir) = downloads[i];
                var outputDir = string.IsNullOrEmpty(subdir) ? targetDir : Path.Combine(targetDir, subdir);
                Directory.CreateDirectory(outputDir);

                // Check if already populated
                if (Directory.GetFiles(outputDir, "*.psc").Length > 0)
                {
                    var existing = Directory.GetFiles(outputDir, "*.psc").Length;
                    results.Add($"{label}: already installed ({existing} files)");
                    totalExtracted += existing;
                    continue;
                }

                var basePercent = i * 50;
                progress?.Report((basePercent + 10, $"Downloading {label}..."));

                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var tempFile = Path.GetTempFileName();
                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tempFile, FileMode.Create))
                {
                    await contentStream.CopyToAsync(fileStream);
                }

                progress?.Report((basePercent + 35, $"Extracting {label} headers..."));

                // Extract .psc files from the .7z archive using SharpCompress
                // SKSE archives have scripts in Data/Scripts/Source/ or Scripts/Source/
                var extracted = 0;
                using (var archive = SevenZipArchive.OpenArchive(tempFile, null!))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.IsDirectory) continue;
                        var key = entry.Key ?? "";
                        if (!key.EndsWith(".psc", StringComparison.OrdinalIgnoreCase)) continue;

                        // SKSE archives store modified scripts in Scripts/Source/ within the archive
                        if (!key.Contains("Scripts/Source/", StringComparison.OrdinalIgnoreCase)) continue;

                        var fileName = Path.GetFileName(key);
                        // Skip SkyUI files
                        if (fileName.StartsWith("SKI_", StringComparison.OrdinalIgnoreCase)) continue;

                        var destPath = Path.Combine(outputDir, fileName);
                        using var entryStream = entry.OpenEntryStream();
                        using var outStream = new FileStream(destPath, FileMode.Create);
                        await entryStream.CopyToAsync(outStream);
                        extracted++;
                    }
                }

                try { File.Delete(tempFile); } catch { /* temp file cleanup is best-effort */ }
                totalExtracted += extracted;
                results.Add($"{label}: {extracted} headers");
            }

            if (totalExtracted > 0)
            {
                var summary = string.Join(", ", results);
                progress?.Report((100, $"Installed ({summary})"));
                return (true, $"Downloaded SKSE headers from skse.silverlock.org: {summary}");
            }

            return (false, "No .psc files found in SKSE archives. Install SKSE manually and re-run the wizard.");
        }
        catch (Exception ex)
        {
            return (false, $"Download failed: {ex.Message}\n\nInstall SKSE manually and re-run the wizard.");
        }
    }

    #endregion

    #region SkyUI Headers

    /// <summary>
    /// Download SkyUI script headers from GitHub and place them in tools/papyrus-compiler/headers/skyui/.
    /// </summary>
    public async Task<(bool success, string message)> SetupSkyUiHeadersAsync(
        IProgress<(int percent, string status)>? progress = null)
    {
        try
        {
            var skyuiHeadersDir = Path.Combine(ToolkitRoot, "tools", "papyrus-compiler", "headers", "skyui");
            Directory.CreateDirectory(skyuiHeadersDir);

            // Check if already set up
            if (Directory.GetFiles(skyuiHeadersDir, "*.psc").Length > 0)
            {
                var count = Directory.GetFiles(skyuiHeadersDir, "*.psc").Length;
                progress?.Report((100, $"Already installed ({count} headers)"));
                return (true, $"SkyUI headers already in tools/papyrus-compiler/headers/skyui/ ({count} files)");
            }

            // Check if SkyUI headers exist in the linked skyrim-script-headers junction
            var linkedHeaders = Path.Combine(ToolkitRoot, "skyrim-script-headers");
            if (Directory.Exists(linkedHeaders))
            {
                var skyuiFiles = Directory.GetFiles(linkedHeaders, "SKI_*.psc");
                if (skyuiFiles.Length > 0)
                {
                    progress?.Report((50, "Found SkyUI headers in linked headers, copying..."));
                    var copied = 0;
                    foreach (var psc in skyuiFiles)
                    {
                        File.Copy(psc, Path.Combine(skyuiHeadersDir, Path.GetFileName(psc)), overwrite: true);
                        copied++;
                    }
                    progress?.Report((100, $"Installed ({copied} headers)"));
                    return (true, $"Copied {copied} SkyUI headers to tools/papyrus-compiler/headers/skyui/");
                }
            }

            // Download from SkyUI GitHub
            progress?.Report((10, "Downloading SkyUI headers from GitHub..."));

            _httpClient.DefaultRequestHeaders.UserAgent.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SpookysAutomodSetup/1.0");

            // Use the default branch zipball
            var zipballUrl = "https://api.github.com/repos/schlangster/skyui/zipball/master";

            progress?.Report((30, "Downloading SkyUI source..."));

            var response = await _httpClient.GetAsync(zipballUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var tempFile = Path.GetTempFileName();
            using (var contentStream = await response.Content.ReadAsStreamAsync())
            using (var fileStream = new FileStream(tempFile, FileMode.Create))
            {
                await contentStream.CopyToAsync(fileStream);
            }

            progress?.Report((70, "Extracting SkyUI script headers..."));

            // Extract SKI_*.psc files from dist/Data/Scripts/Source/ and dist/Data/Scripts/Headers/
            var extracted = 0;
            using (var archive = ZipFile.OpenRead(tempFile))
            {
                foreach (var entry in archive.Entries)
                {
                    if (!entry.Name.EndsWith(".psc", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!entry.Name.StartsWith("SKI_", StringComparison.OrdinalIgnoreCase)) continue;
                    // Only from dist/Data/Scripts/ paths
                    if (!entry.FullName.Contains("dist/Data/Scripts/", StringComparison.OrdinalIgnoreCase)) continue;

                    var destPath = Path.Combine(skyuiHeadersDir, entry.Name);
                    // Prefer Source over Headers if both exist (Source has full implementations)
                    if (File.Exists(destPath) && !entry.FullName.Contains("/Source/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    entry.ExtractToFile(destPath, overwrite: true);
                    extracted++;
                }
            }

            File.Delete(tempFile);

            if (extracted > 0)
            {
                progress?.Report((100, $"Installed ({extracted} headers)"));
                return (true, $"Downloaded {extracted} SkyUI headers from GitHub");
            }

            return (false, "No SkyUI headers found in source. Install SkyUI manually.");
        }
        catch (Exception ex)
        {
            return (false, $"SkyUI headers setup failed: {ex.Message}");
        }
    }

    #endregion

    /// <summary>
    /// Check if a .psc filename is an SKSE/PapyrusUtil header (not a vanilla Bethesda script).
    /// </summary>
    private static bool IsSkseHeader(string fileName)
    {
        var skseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SKSE.psc", "StringUtil.psc", "MiscUtil.psc", "PapyrusUtil.psc",
            "StorageUtil.psc", "JsonUtil.psc", "ObjectUtil.psc", "ActorUtil.psc",
            "ModEvent.psc", "UI.psc", "UICallback.psc", "Input.psc",
            "NetImmerse.psc", "TreeObject.psc", "ColorForm.psc",
            "Math.psc", "SpellHelper.psc", "ActorBase.psc",
        };
        return skseNames.Contains(fileName);
    }

    #region Tool Downloads

    public async Task<(bool success, string message)> DownloadToolAsync(
        string owner, string repo, string assetPattern, string targetFolder,
        IProgress<(int percent, string status)>? progress = null)
    {
        try
        {
            var targetDir = Path.Combine(ToolkitRoot, "tools", targetFolder);

            // Check if already downloaded
            if (Directory.Exists(targetDir) && Directory.EnumerateFiles(targetDir, "*", SearchOption.AllDirectories).Any())
            {
                progress?.Report((100, "Already installed"));
                return (true, "Already installed");
            }

            progress?.Report((10, "Fetching release info..."));

            // Get latest release from GitHub
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            _httpClient.DefaultRequestHeaders.UserAgent.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SpookysAutomodSetup/1.0");

            var releaseJson = await _httpClient.GetStringAsync(apiUrl);
            var release = JsonSerializer.Deserialize<JsonElement>(releaseJson);

            string? downloadUrl = null;
            string? assetName = null;

            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (MatchesPattern(name, assetPattern))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    assetName = name;
                    break;
                }
            }

            if (downloadUrl == null)
            {
                return (false, $"No matching asset found for pattern: {assetPattern}");
            }

            progress?.Report((30, $"Downloading {assetName}..."));

            // Download the asset
            var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var tempFile = Path.GetTempFileName();

            using (var contentStream = await response.Content.ReadAsStreamAsync())
            using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var pct = (int)(30 + (totalRead * 60.0 / totalBytes));
                        progress?.Report((pct, $"Downloading... {totalRead / 1024:N0} KB"));
                    }
                }
            }

            progress?.Report((90, "Extracting..."));

            // Extract
            Directory.CreateDirectory(targetDir);
            if (assetName!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(tempFile, targetDir, overwriteFiles: true);
            }
            else
            {
                File.Copy(tempFile, Path.Combine(targetDir, assetName), overwrite: true);
            }

            File.Delete(tempFile);
            progress?.Report((100, "Installed"));
            return (true, $"Downloaded and extracted to {targetDir}");
        }
        catch (Exception ex)
        {
            return (false, $"Download failed: {ex.Message}");
        }
    }

    private static bool MatchesPattern(string name, string pattern)
    {
        if (pattern.Contains('*'))
        {
            var prefix = pattern.Split('*')[0];
            var suffix = pattern.Split('*').Last();
            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                   name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }
        return name.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsToolInstalled(string toolFolder)
    {
        var dir = Path.Combine(ToolkitRoot, "tools", toolFolder);
        return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any();
    }

    #endregion

    #region Build Tools Check

    public (bool installed, string version) CheckXmake()
    {
        try
        {
            var result = RunProcess("xmake", "--version");
            if (result.exitCode == 0)
            {
                var firstLine = result.output.Split('\n').FirstOrDefault()?.Trim() ?? result.output.Trim();
                return (true, firstLine);
            }
            return (false, "Not found");
        }
        catch
        {
            return (false, "Not found");
        }
    }

    [Obsolete("Use CheckXmake. CMake is no longer the build driver.")]
    public (bool installed, string version) CheckCMake() => CheckXmake();

    public (bool installed, string version) CheckMsvc()
    {
        try
        {
            // Try vswhere first (most reliable)
            var vswherePath = @"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe";
            if (File.Exists(vswherePath))
            {
                var vsResult = RunProcess(vswherePath,
                    "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property displayName");
                if (vsResult.exitCode == 0 && !string.IsNullOrWhiteSpace(vsResult.output))
                {
                    return (true, vsResult.output.Trim());
                }
            }

            // Fallback: try cl.exe
            var result = RunProcess("cl", "");
            if (result.output.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            {
                var firstLine = result.output.Split('\n').FirstOrDefault()?.Trim() ?? "Installed";
                return (true, firstLine);
            }

            return (false, "Not found");
        }
        catch
        {
            return (false, "Not found");
        }
    }

    #endregion

    #region .NET Check

    public (bool installed, string version) CheckDotNet()
    {
        try
        {
            var result = RunProcess("dotnet", "--version");
            if (result.exitCode == 0)
            {
                var version = result.output.Trim();
                var isNet8 = version.StartsWith("8.");
                return (isNet8, version);
            }
            return (false, "Not found");
        }
        catch
        {
            return (false, "Not found");
        }
    }

    #endregion

    #region Build Solution

    public Task<(bool success, string output)> BuildSolutionAsync(
        IProgress<string>? progress = null)
    {
        try
        {
            var cliProject = Path.Combine(ToolkitRoot, "src", "SpookysAutomod.Cli", "SpookysAutomod.Cli.csproj");
            if (!File.Exists(cliProject))
            {
                return Task.FromResult<(bool, string)>((false, $"CLI project not found: {cliProject}"));
            }

            progress?.Report("Restoring NuGet packages...");

            var restoreResult = RunProcess("dotnet", $"restore \"{cliProject}\"", ToolkitRoot);
            if (restoreResult.exitCode != 0)
            {
                // Try adding NuGet source first
                RunProcess("dotnet", "nuget add source https://api.nuget.org/v3/index.json -n nuget.org", ToolkitRoot);
                restoreResult = RunProcess("dotnet", $"restore \"{cliProject}\"", ToolkitRoot);
                if (restoreResult.exitCode != 0)
                {
                    return Task.FromResult<(bool, string)>((false, $"Restore failed:\n{restoreResult.output}"));
                }
            }

            progress?.Report("Building toolkit...");

            var buildResult = RunProcess("dotnet", $"build \"{cliProject}\" --no-restore --verbosity quiet", ToolkitRoot);
            if (buildResult.exitCode != 0)
            {
                return Task.FromResult<(bool, string)>((false, $"Build failed:\n{buildResult.output}"));
            }

            return Task.FromResult<(bool, string)>((true, "Build succeeded"));
        }
        catch (Exception ex)
        {
            return Task.FromResult<(bool, string)>((false, $"Build error: {ex.Message}"));
        }
    }

    #endregion

    #region Init Prompt

    public string? GetInitPrompt()
    {
        var promptPath = Path.Combine(ToolkitRoot, "docs", "llm-init-prompt.md");
        if (!File.Exists(promptPath)) return null;
        return File.ReadAllText(promptPath);
    }

    #endregion

    #region Settings

    public void SaveSettings(SetupSettings settings)
    {
        var path = Path.Combine(ToolkitRoot, "settings.json");
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(path, json);
    }

    public SetupSettings? LoadSettings()
    {
        var path = Path.Combine(ToolkitRoot, "settings.json");
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SetupSettings>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    #endregion

    #region Helpers

    private static (int exitCode, string output) RunProcess(string fileName, string arguments, string? workingDir = null)
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

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(60000);

        return (process.ExitCode, output + error);
    }

    #endregion
}

#region Models

public enum SkyrimEdition
{
    SE,
    VR
}

public class SkyrimInstallation
{
    public string Path { get; set; } = "";
    public SkyrimEdition Edition { get; set; }
    public bool HasScriptHeaders { get; set; }
    public string? ScriptSourcePath { get; set; }

    public string DisplayName => Edition switch
    {
        SkyrimEdition.SE => $"Skyrim Special Edition - {Path}",
        SkyrimEdition.VR => $"Skyrim VR - {Path}",
        _ => Path
    };
}

public class SetupSettings
{
    public string SkyrimPath { get; set; } = "";
    public string SkyrimEdition { get; set; } = "SE";
    public string DataPath { get; set; } = "";
    public string ScriptHeadersPath { get; set; } = "";
    public bool PapyrusCompilerInstalled { get; set; }
    public bool ChampollionInstalled { get; set; }
    public string DotNetVersion { get; set; } = "";
    public DateTime SetupDate { get; set; } = DateTime.Now;
}

#endregion
