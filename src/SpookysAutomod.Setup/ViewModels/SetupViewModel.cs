using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SpookysAutomod.Setup.Services;

namespace SpookysAutomod.Setup.ViewModels;

public class SetupViewModel : INotifyPropertyChanged
{
    private readonly SetupService _service;

    public SetupViewModel()
    {
        _service = new SetupService();
        Installations = new ObservableCollection<SkyrimInstallation>();

        NextCommand = new RelayCommand(async _ => await GoNextAsync(), _ => CanGoNext);
        BackCommand = new RelayCommand(_ => GoBack(), _ => CurrentStep > 0);
        BrowseSkyrimCommand = new RelayCommand(_ => BrowseSkyrim());
        CopyPromptCommand = new RelayCommand(_ => CopyInitPrompt());
        FinishCommand = new RelayCommand(_ => Application.Current.Shutdown());

        // Start detection on load
        _ = InitializeAsync();
    }

    #region Properties

    private int _currentStep;
    public int CurrentStep
    {
        get => _currentStep;
        set { _currentStep = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGoNext)); UpdateStepIndicators(); }
    }

    public int TotalSteps => 6;

    // Step 0: Welcome / Skyrim Detection
    private ObservableCollection<SkyrimInstallation> _installations = null!;
    public ObservableCollection<SkyrimInstallation> Installations
    {
        get => _installations;
        set { _installations = value; OnPropertyChanged(); }
    }

    private SkyrimInstallation? _selectedInstallation;
    public SkyrimInstallation? SelectedInstallation
    {
        get => _selectedInstallation;
        set { _selectedInstallation = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGoNext)); }
    }

    private string _customSkyrimPath = "";
    public string CustomSkyrimPath
    {
        get => _customSkyrimPath;
        set { _customSkyrimPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGoNext)); }
    }

    private bool _useCustomPath;
    public bool UseCustomPath
    {
        get => _useCustomPath;
        set { _useCustomPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGoNext)); }
    }

    private string _detectionStatus = "Searching for Skyrim installations...";
    public string DetectionStatus
    {
        get => _detectionStatus;
        set { _detectionStatus = value; OnPropertyChanged(); }
    }

    // Step 1: Script Headers
    private string _headersStatus = "";
    public string HeadersStatus
    {
        get => _headersStatus;
        set { _headersStatus = value; OnPropertyChanged(); }
    }

    private bool _headersSuccess;
    public bool HeadersSuccess
    {
        get => _headersSuccess;
        set { _headersSuccess = value; OnPropertyChanged(); }
    }

    private bool _headersNoSource;
    public bool HeadersNoSource
    {
        get => _headersNoSource;
        set { _headersNoSource = value; OnPropertyChanged(); }
    }

    // Step 2: Tool Downloads
    private string _compilerStatus = "Pending";
    public string CompilerStatus
    {
        get => _compilerStatus;
        set { _compilerStatus = value; OnPropertyChanged(); }
    }

    private int _compilerProgress;
    public int CompilerProgress
    {
        get => _compilerProgress;
        set { _compilerProgress = value; OnPropertyChanged(); }
    }

    private string _decompilerStatus = "Pending";
    public string DecompilerStatus
    {
        get => _decompilerStatus;
        set { _decompilerStatus = value; OnPropertyChanged(); }
    }

    private int _decompilerProgress;
    public int DecompilerProgress
    {
        get => _decompilerProgress;
        set { _decompilerProgress = value; OnPropertyChanged(); }
    }

    private string _skseHeadersStatus = "Pending";
    public string SkseHeadersStatus
    {
        get => _skseHeadersStatus;
        set { _skseHeadersStatus = value; OnPropertyChanged(); }
    }

    private int _skseHeadersProgress;
    public int SkseHeadersProgress
    {
        get => _skseHeadersProgress;
        set { _skseHeadersProgress = value; OnPropertyChanged(); }
    }

    private string _skyUiHeadersStatus = "Pending";
    public string SkyUiHeadersStatus
    {
        get => _skyUiHeadersStatus;
        set { _skyUiHeadersStatus = value; OnPropertyChanged(); }
    }

    private int _skyUiHeadersProgress;
    public int SkyUiHeadersProgress
    {
        get => _skyUiHeadersProgress;
        set { _skyUiHeadersProgress = value; OnPropertyChanged(); }
    }

    private bool _toolsComplete;
    public bool ToolsComplete
    {
        get => _toolsComplete;
        set { _toolsComplete = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGoNext)); }
    }

    // Step 3: .NET Check
    private string _dotNetStatus = "";
    public string DotNetStatus
    {
        get => _dotNetStatus;
        set { _dotNetStatus = value; OnPropertyChanged(); }
    }

    private bool _dotNetOk;
    public bool DotNetOk
    {
        get => _dotNetOk;
        set { _dotNetOk = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGoNext)); }
    }

    private string _xmakeStatus = "";
    public string XmakeStatus
    {
        get => _xmakeStatus;
        set { _xmakeStatus = value; OnPropertyChanged(); }
    }

    private bool _xmakeOk;
    public bool XmakeOk
    {
        get => _xmakeOk;
        set { _xmakeOk = value; OnPropertyChanged(); }
    }

    private string _msvcStatus = "";
    public string MsvcStatus
    {
        get => _msvcStatus;
        set { _msvcStatus = value; OnPropertyChanged(); }
    }

    private bool _msvcOk;
    public bool MsvcOk
    {
        get => _msvcOk;
        set { _msvcOk = value; OnPropertyChanged(); }
    }

    // Step 4: Build
    private string _buildStatus = "";
    public string BuildStatus
    {
        get => _buildStatus;
        set { _buildStatus = value; OnPropertyChanged(); }
    }

    private bool _buildSuccess;
    public bool BuildSuccess
    {
        get => _buildSuccess;
        set { _buildSuccess = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGoNext)); }
    }

    private bool _buildRunning;
    public bool BuildRunning
    {
        get => _buildRunning;
        set { _buildRunning = value; OnPropertyChanged(); }
    }

    // Step 5: Finish
    private string _initPromptPreview = "";
    public string InitPromptPreview
    {
        get => _initPromptPreview;
        set { _initPromptPreview = value; OnPropertyChanged(); }
    }

    private bool _promptCopied;
    public bool PromptCopied
    {
        get => _promptCopied;
        set { _promptCopied = value; OnPropertyChanged(); }
    }

    // Step indicators
    private string _step0State = "Current";
    public string Step0State { get => _step0State; set { _step0State = value; OnPropertyChanged(); } }
    private string _step1State = "Pending";
    public string Step1State { get => _step1State; set { _step1State = value; OnPropertyChanged(); } }
    private string _step2State = "Pending";
    public string Step2State { get => _step2State; set { _step2State = value; OnPropertyChanged(); } }
    private string _step3State = "Pending";
    public string Step3State { get => _step3State; set { _step3State = value; OnPropertyChanged(); } }
    private string _step4State = "Pending";
    public string Step4State { get => _step4State; set { _step4State = value; OnPropertyChanged(); } }
    private string _step5State = "Pending";
    public string Step5State { get => _step5State; set { _step5State = value; OnPropertyChanged(); } }

    // General
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGoNext)); }
    }

    public bool CanGoNext => !IsBusy && CurrentStep switch
    {
        0 => SelectedInstallation != null || (UseCustomPath && !string.IsNullOrWhiteSpace(CustomSkyrimPath)),
        1 => true, // Headers are optional (CK may not be installed)
        2 => ToolsComplete,
        3 => DotNetOk,
        4 => BuildSuccess,
        5 => true,
        _ => false
    };

    public string ToolkitPath => _service.ToolkitRoot;

    #endregion

    #region Commands

    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand BrowseSkyrimCommand { get; }
    public ICommand CopyPromptCommand { get; }
    public ICommand FinishCommand { get; }

    #endregion

    #region Step Logic

    private async Task InitializeAsync()
    {
        await Task.Run(() =>
        {
            var installs = _service.DetectSkyrimInstallations();
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var install in installs)
                    Installations.Add(install);

                if (Installations.Count > 0)
                {
                    SelectedInstallation = Installations[0];
                    DetectionStatus = $"Found {Installations.Count} installation(s)";
                }
                else
                {
                    DetectionStatus = "No Skyrim installations detected. Use 'Browse' to select manually.";
                    UseCustomPath = true;
                }
            });
        });
    }

    private async Task GoNextAsync()
    {
        IsBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            switch (CurrentStep)
            {
                case 0: await RunStep1_HeadersAsync(); break;
                case 1: await RunStep2_ToolsAsync(); break;
                case 2: RunStep3_DotNetCheck(); break;
                case 3: await RunStep4_BuildAsync(); break;
                case 4: RunStep5_Finish(); break;
            }
            CurrentStep++;
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void GoBack()
    {
        if (CurrentStep > 0)
            CurrentStep--;
    }

    private async Task RunStep1_HeadersAsync()
    {
        var skyrimPath = UseCustomPath ? CustomSkyrimPath : SelectedInstallation?.Path;
        if (string.IsNullOrEmpty(skyrimPath)) return;

        var scriptSource = _service.FindScriptSourceDir(skyrimPath);
        if (scriptSource == null)
        {
            HeadersNoSource = true;
            HeadersStatus = "Script headers not found. This usually means Creation Kit is not installed.\n\n" +
                           "You can install CK from Steam and re-run this wizard later, or set up headers manually.\n\n" +
                           "The toolkit will still work for most operations without headers.";
            return;
        }

        await Task.Run(() =>
        {
            var (success, message) = _service.SetupScriptHeaders(scriptSource);
            Application.Current.Dispatcher.Invoke(() =>
            {
                HeadersSuccess = success;
                HeadersStatus = success
                    ? $"Script headers linked successfully!\n\n{message}"
                    : $"Failed to link headers: {message}\n\nYou can set them up manually later.";
            });
        });
    }

    private async Task RunStep2_ToolsAsync()
    {
        var compilerProgress = new Progress<(int percent, string status)>(p =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CompilerProgress = p.percent;
                CompilerStatus = p.status;
            });
        });

        var decompilerProgress = new Progress<(int percent, string status)>(p =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                DecompilerProgress = p.percent;
                DecompilerStatus = p.status;
            });
        });

        var skseProgress = new Progress<(int percent, string status)>(p =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                SkseHeadersProgress = p.percent;
                SkseHeadersStatus = p.status;
            });
        });

        CompilerStatus = "Starting...";
        DecompilerStatus = "Starting...";
        SkseHeadersStatus = "Starting...";
        SkyUiHeadersStatus = "Starting...";

        // Get Skyrim path for SKSE header detection
        var skyrimPath = UseCustomPath ? CustomSkyrimPath : SelectedInstallation?.Path ?? "";

        var skyUiProgress = new Progress<(int percent, string status)>(p =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                SkyUiHeadersProgress = p.percent;
                SkyUiHeadersStatus = p.status;
            });
        });

        var compilerTask = _service.DownloadToolAsync(
            "russo-2025", "papyrus-compiler", "papyrus-compiler-windows.zip", "papyrus-compiler", compilerProgress);

        var decompilerTask = _service.DownloadToolAsync(
            "Orvid", "Champollion", "Champollion*.zip", "champollion", decompilerProgress);

        var skseTask = _service.SetupSkseHeadersAsync(skyrimPath, skseProgress);

        var skyUiTask = _service.SetupSkyUiHeadersAsync(skyUiProgress);

        await Task.WhenAll(compilerTask, decompilerTask, skseTask, skyUiTask);

        var compilerResult = await compilerTask;
        var decompilerResult = await decompilerTask;
        var skseResult = await skseTask;
        var skyUiResult = await skyUiTask;

        CompilerStatus = compilerResult.success ? "Installed" : $"Failed: {compilerResult.message}";
        CompilerProgress = compilerResult.success ? 100 : 0;
        DecompilerStatus = decompilerResult.success ? "Installed" : $"Failed: {decompilerResult.message}";
        DecompilerProgress = decompilerResult.success ? 100 : 0;
        SkseHeadersStatus = skseResult.success ? "Installed" : $"Skipped: {skseResult.message}";
        SkseHeadersProgress = skseResult.success ? 100 : 0;
        SkyUiHeadersStatus = skyUiResult.success ? "Installed" : $"Skipped: {skyUiResult.message}";
        SkyUiHeadersProgress = skyUiResult.success ? 100 : 0;

        ToolsComplete = true; // Allow proceeding even if downloads fail
        CommandManager.InvalidateRequerySuggested();
    }

    private void RunStep3_DotNetCheck()
    {
        var (installed, version) = _service.CheckDotNet();
        DotNetOk = installed;
        DotNetStatus = installed
            ? $".NET SDK {version} detected"
            : $".NET 8 SDK not found (detected: {version}).\n\nPlease install from: https://dotnet.microsoft.com/download/dotnet/8.0";

        // Also check xmake and MSVC (for SKSE plugin building)
        var (xmakeInstalled, xmakeVersion) = _service.CheckXmake();
        XmakeOk = xmakeInstalled;
        XmakeStatus = xmakeInstalled
            ? xmakeVersion
            : "Not found - needed for SKSE plugin building";

        var (msvcInstalled, msvcVersion) = _service.CheckMsvc();
        MsvcOk = msvcInstalled;
        MsvcStatus = msvcInstalled
            ? msvcVersion
            : "Not found - needed for SKSE plugin building";
    }

    private async Task RunStep4_BuildAsync()
    {
        BuildRunning = true;
        BuildStatus = "Building solution...";

        var progress = new Progress<string>(status =>
        {
            Application.Current.Dispatcher.Invoke(() => BuildStatus = status);
        });

        var (success, output) = await Task.Run(() => _service.BuildSolutionAsync(progress).Result);

        BuildRunning = false;
        BuildSuccess = success;
        BuildStatus = success ? "Build succeeded! The toolkit is ready to use." : $"Build failed:\n{output}";
        CommandManager.InvalidateRequerySuggested();
    }

    private void RunStep5_Finish()
    {
        // Save settings
        var skyrimPath = UseCustomPath ? CustomSkyrimPath : SelectedInstallation?.Path ?? "";
        var edition = UseCustomPath ? "SE" : (SelectedInstallation?.Edition.ToString() ?? "SE");

        _service.SaveSettings(new SetupSettings
        {
            SkyrimPath = skyrimPath,
            SkyrimEdition = edition,
            DataPath = Path.Combine(skyrimPath, "Data"),
            ScriptHeadersPath = Path.Combine(_service.ToolkitRoot, "skyrim-script-headers"),
            PapyrusCompilerInstalled = _service.IsToolInstalled("papyrus-compiler"),
            ChampollionInstalled = _service.IsToolInstalled("champollion"),
            DotNetVersion = _service.CheckDotNet().version,
            SetupDate = DateTime.Now
        });

        // Load init prompt preview
        var prompt = _service.GetInitPrompt();
        if (prompt != null)
        {
            // Insert the toolkit path
            prompt = prompt.Replace(
                "[USER WILL PROVIDE PATH - typically: C:\\...\\spookys-automod-toolkit]",
                _service.ToolkitRoot);

            InitPromptPreview = prompt.Length > 500
                ? prompt[..500] + "\n\n... (full prompt will be copied to clipboard)"
                : prompt;
        }
        else
        {
            InitPromptPreview = "Init prompt file not found at docs/llm-init-prompt.md";
        }
    }

    private void BrowseSkyrim()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select SkyrimSE.exe or SkyrimVR.exe",
            Filter = "Skyrim Executable|SkyrimSE.exe;SkyrimVR.exe",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            var dir = Path.GetDirectoryName(dialog.FileName);
            if (dir != null)
            {
                CustomSkyrimPath = dir;
                UseCustomPath = true;
            }
        }
    }

    private void CopyInitPrompt()
    {
        var prompt = _service.GetInitPrompt();
        if (prompt != null)
        {
            prompt = prompt.Replace(
                "[USER WILL PROVIDE PATH - typically: C:\\...\\spookys-automod-toolkit]",
                _service.ToolkitRoot);
            Clipboard.SetText(prompt);
            PromptCopied = true;
        }
    }

    private void UpdateStepIndicators()
    {
        var states = new[] { "Pending", "Pending", "Pending", "Pending", "Pending", "Pending" };
        for (int i = 0; i < 6; i++)
        {
            if (i < CurrentStep) states[i] = "Completed";
            else if (i == CurrentStep) states[i] = "Current";
        }
        Step0State = states[0];
        Step1State = states[1];
        Step2State = states[2];
        Step3State = states[3];
        Step4State = states[4];
        Step5State = states[5];
    }

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    #endregion
}

public class RelayCommand : ICommand
{
    private readonly Func<object?, Task> _executeAsync;
    private readonly Action<object?>? _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _executeAsync = _ => { execute(_); return Task.CompletedTask; };
    }

    public RelayCommand(Func<object?, Task> executeAsync, Func<object?, bool>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public async void Execute(object? parameter)
    {
        if (_execute != null)
            _execute(parameter);
        else
            await _executeAsync(parameter);
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
