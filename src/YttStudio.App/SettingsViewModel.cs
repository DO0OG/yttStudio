using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace YttStudio.App;

/// <summary>설정 창의 선택 항목을 현재 언어로 표시한다.</summary>
public sealed class SettingsOption<T> : INotifyPropertyChanged
{
    private readonly Localizer localizer;
    private readonly string resourceKey;

    public SettingsOption(T value, string resourceKey, Localizer localizer)
    {
        Value = value;
        this.resourceKey = resourceKey;
        this.localizer = localizer;
    }

    public T Value { get; }

    public string Label => localizer[resourceKey];

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Refresh() => PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs(nameof(Label)));
}

/// <summary>일반·모양·영상 탭이 공유하는 설정 창 상태를 보관한다.</summary>
public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppPreferences preferences;
    private readonly IFileDialogService dialogs;
    private readonly Action<AppLanguage> setLanguage;
    private readonly Action<AppThemeMode> setTheme;
    private readonly Func<string, Task<string>> applyMpvPath;
    private readonly Func<double> getSnapThreshold;
    private readonly Action<double> setSnapThreshold;
    private readonly Action<bool, int> applyAutosaveSettings;
    private readonly Func<string> getVideoStatus;
    private readonly Func<IProgress<MpvInstallProgress>, Task<string?>>? installMpv;
    private SettingsOption<AppLanguage>? selectedLanguage;
    private SettingsOption<AppThemeMode>? selectedTheme;
    private SettingsOption<int>? selectedAutosaveInterval;
    private bool autosaveEnabled;
    private string mpvPath;
    private double snapThreshold;
    private string status = string.Empty;
    private string videoStatus;
    private MpvInstallProgress? latestInstallProgress;
    private double installProgress;
    private string installStatus = string.Empty;
    private bool isInstalling;
    private bool disposed;

    public SettingsViewModel(
        Localizer localizer,
        AppPreferences preferences,
        IFileDialogService dialogs,
        Action<AppLanguage> setLanguage,
        Action<AppThemeMode> setTheme,
        Func<string, Task<string>> applyMpvPath,
        Func<double> getSnapThreshold,
        Action<double> setSnapThreshold,
        Action<bool, int> applyAutosaveSettings,
        Func<string> getVideoStatus,
        Func<IProgress<MpvInstallProgress>, Task<string?>>? installMpv = null)
    {
        Loc = localizer;
        this.preferences = preferences;
        this.dialogs = dialogs;
        this.setLanguage = setLanguage;
        this.setTheme = setTheme;
        this.applyMpvPath = applyMpvPath;
        this.getSnapThreshold = getSnapThreshold;
        this.setSnapThreshold = setSnapThreshold;
        this.applyAutosaveSettings = applyAutosaveSettings;
        this.getVideoStatus = getVideoStatus;
        this.installMpv = installMpv;
        mpvPath = NormalizePath(preferences.MpvPath);
        snapThreshold = Math.Clamp(getSnapThreshold(), 0, 64);
        autosaveEnabled = preferences.AutosaveEnabled;
        videoStatus = getVideoStatus();

        InitializeSettingsOptions();

        BrowseMpvCommand = new AsyncCommand(BrowseMpvAsync);
        ApplyMpvCommand = new AsyncCommand(ApplyMpvAsync);
        InstallMpvCommand = new AsyncCommand(InstallMpvAsync, () => CanInstallMpv);
        OpenMpvGuideCommand = new DelegateCommand(OpenMpvGuide);
        CloseCommand = new DelegateCommand(() => CloseRequested?.Invoke());
    }

    private void InitializeSettingsOptions()
    {
        LanguageOptions =
        [
            new SettingsOption<AppLanguage>(AppLanguage.Korean, "LanguageKorean", Loc),
            new SettingsOption<AppLanguage>(AppLanguage.English, "LanguageEnglish", Loc),
            new SettingsOption<AppLanguage>(AppLanguage.Japanese, "LanguageJapanese", Loc),
        ];
        ThemeOptions =
        [
            new SettingsOption<AppThemeMode>(AppThemeMode.Default, "ThemeDefault", Loc),
            new SettingsOption<AppThemeMode>(AppThemeMode.Light, "ThemeLight", Loc),
            new SettingsOption<AppThemeMode>(AppThemeMode.Dark, "ThemeDark", Loc),
        ];
        AutosaveIntervalOptions =
        [
            new SettingsOption<int>(15, "Autosave15Seconds", Loc),
            new SettingsOption<int>(30, "Autosave30Seconds", Loc),
            new SettingsOption<int>(60, "Autosave1Minute", Loc),
            new SettingsOption<int>(120, "Autosave2Minutes", Loc),
            new SettingsOption<int>(300, "Autosave5Minutes", Loc),
            new SettingsOption<int>(600, "Autosave10Minutes", Loc),
        ];
        selectedLanguage = LanguageOptions.FirstOrDefault(option => option.Value == preferences.Language);
        selectedTheme = ThemeOptions.FirstOrDefault(option => option.Value == preferences.Theme);
        selectedAutosaveInterval = AutosaveIntervalOptions.First(
            option => option.Value == NormalizeAutosaveInterval(preferences.AutosaveIntervalSeconds));
        MpvPackageInstallInstructions packageInstructions = MpvAutoInstaller.GetPackageManagerInstructions();
        PackageManagerCommands = packageInstructions.Commands;
        Loc.LanguageChanged += OnLanguageChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>창을 닫아 달라는 요청을 창 코드에 전달한다.</summary>
    public event Action? CloseRequested;

    public Localizer Loc { get; }

    public IReadOnlyList<SettingsOption<AppLanguage>> LanguageOptions { get; private set; } = [];

    public IReadOnlyList<SettingsOption<AppThemeMode>> ThemeOptions { get; private set; } = [];

    public IReadOnlyList<SettingsOption<int>> AutosaveIntervalOptions { get; private set; } = [];

    public SettingsOption<AppLanguage>? SelectedLanguage
    {
        get => selectedLanguage;
        set
        {
            if (value is null || ReferenceEquals(selectedLanguage, value))
            {
                return;
            }

            selectedLanguage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Language));
            setLanguage(value.Value);
        }
    }

    public SettingsOption<AppThemeMode>? SelectedTheme
    {
        get => selectedTheme;
        set
        {
            if (value is null || ReferenceEquals(selectedTheme, value))
            {
                return;
            }

            selectedTheme = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThemeMode));
            setTheme(value.Value);
        }
    }

    public AppLanguage Language
    {
        get => selectedLanguage?.Value ?? preferences.Language;
        set => SelectedLanguage = LanguageOptions.FirstOrDefault(option => option.Value == value);
    }

    public AppThemeMode ThemeMode
    {
        get => selectedTheme?.Value ?? preferences.Theme;
        set => SelectedTheme = ThemeOptions.FirstOrDefault(option => option.Value == value);
    }

    public bool AutosaveEnabled
    {
        get => autosaveEnabled;
        set
        {
            if (SetField(ref autosaveEnabled, value))
            {
                applyAutosaveSettings(value, SelectedAutosaveInterval?.Value ?? 60);
            }
        }
    }

    public SettingsOption<int>? SelectedAutosaveInterval
    {
        get => selectedAutosaveInterval;
        set
        {
            if (value is null || ReferenceEquals(selectedAutosaveInterval, value))
            {
                return;
            }

            selectedAutosaveInterval = value;
            OnPropertyChanged();
            applyAutosaveSettings(AutosaveEnabled, value.Value);
        }
    }

    public string MpvPath
    {
        get => mpvPath;
        set
        {
            string normalized = NormalizePath(value);
            if (SetField(ref mpvPath, normalized))
            {
                OnPropertyChanged(nameof(HasMpvPath));
            }
        }
    }

    public bool HasMpvPath => !string.IsNullOrWhiteSpace(mpvPath);

    public double SnapThreshold
    {
        get => snapThreshold;
        set
        {
            double clamped = Math.Clamp(value, 0, 64);
            if (SetField(ref snapThreshold, clamped))
            {
                setSnapThreshold(clamped);
            }
        }
    }

    public string Status
    {
        get => status;
        private set => SetField(ref status, value);
    }

    public string VideoStatus
    {
        get => videoStatus;
        private set => SetField(ref videoStatus, value);
    }

    public bool CanInstallMpv => installMpv is not null;

    public bool IsInstalling
    {
        get => isInstalling;
        private set
        {
            if (SetField(ref isInstalling, value))
            {
                OnPropertyChanged(nameof(IsInstallProgressIndeterminate));
            }
        }
    }

    public double InstallProgress
    {
        get => installProgress;
        private set => SetField(ref installProgress, value);
    }

    public bool IsInstallProgressIndeterminate
        => isInstalling && latestInstallProgress?.Fraction is null;

    public string InstallStatus
    {
        get => installStatus;
        private set => SetField(ref installStatus, value);
    }

    public IReadOnlyList<string> PackageManagerCommands { get; private set; } = [];

    public bool HasPackageManagerCommands => PackageManagerCommands.Count > 0;

    public AsyncCommand BrowseMpvCommand { get; }

    public AsyncCommand ApplyMpvCommand { get; }

    public AsyncCommand InstallMpvCommand { get; }

    public DelegateCommand OpenMpvGuideCommand { get; }

    public DelegateCommand CloseCommand { get; }

    public void RefreshVideoStatus()
    {
        VideoStatus = getVideoStatus();
    }

    private async Task BrowseMpvAsync()
    {
        string? selectedPath = await dialogs.OpenMpvLibraryAsync();
        if (selectedPath is null)
        {
            return;
        }

        MpvPath = selectedPath;
        await ApplyMpvAsync();
    }

    private async Task ApplyMpvAsync()
    {
        if (!string.IsNullOrWhiteSpace(MpvPath)
            && !File.Exists(MpvPath)
            && !Directory.Exists(MpvPath))
        {
            Status = Loc["MpvPathInvalid"];
            return;
        }

        Status = await applyMpvPath(MpvPath);
        RefreshVideoStatus();
    }

    private async Task InstallMpvAsync()
    {
        if (installMpv is null)
        {
            Status = Loc["MpvAutoInstallUnavailable"];
            return;
        }

        latestInstallProgress = null;
        InstallProgress = 0;
        InstallStatus = Loc["MpvInstallPreparing"];
        IsInstalling = true;
        try
        {
            IProgress<MpvInstallProgress> progress = new Progress<MpvInstallProgress>(OnInstallProgress);
            string? result = await installMpv(progress);
            Status = result ?? Loc["MpvReloadFailed"];
            RefreshVideoStatus();
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private void OnInstallProgress(MpvInstallProgress progress)
    {
        latestInstallProgress = progress;
        InstallProgress = progress.Fraction ?? 0;
        InstallStatus = FormatInstallProgress(progress);
        OnPropertyChanged(nameof(IsInstallProgressIndeterminate));
    }

    private string FormatInstallProgress(MpvInstallProgress progress)
    {
        string stage = progress.Stage switch
        {
            MpvInstallStage.FetchingRelease => Loc["MpvStageFetchingRelease"],
            MpvInstallStage.DownloadingArchive => Loc["MpvStageDownloadingArchive"],
            MpvInstallStage.ExtractingArchive => Loc["MpvStageExtractingArchive"],
            MpvInstallStage.Installing => Loc["MpvStageInstalling"],
            MpvInstallStage.Completed => Loc["MpvStageCompleted"],
            _ => Loc["MpvInstallPreparing"],
        };

        return progress.Fraction is double fraction
            ? $"{stage} · {fraction:P0}"
            : stage;
    }

    private void OpenMpvGuide()
    {
        if (!MpvInstallationGuide.TryOpen(out string? error))
        {
            Status = $"{Loc["MpvGuide"]}: {error}";
        }
    }

    private void OnLanguageChanged()
    {
        foreach (SettingsOption<AppLanguage> option in LanguageOptions)
        {
            option.Refresh();
        }

        foreach (SettingsOption<AppThemeMode> option in ThemeOptions)
        {
            option.Refresh();
        }

        foreach (SettingsOption<int> option in AutosaveIntervalOptions)
        {
            option.Refresh();
        }

        OnPropertyChanged(nameof(Loc));
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(ThemeMode));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(VideoStatus));
        InstallStatus = latestInstallProgress is null
            ? (isInstalling ? Loc["MpvInstallPreparing"] : string.Empty)
            : FormatInstallProgress(latestInstallProgress);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Loc.LanguageChanged -= OnLanguageChanged;
    }

    private static string NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Trim('"');

    private static int NormalizeAutosaveInterval(int seconds)
        => seconds is 15 or 30 or 60 or 120 or 300 or 600 ? seconds : 60;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
