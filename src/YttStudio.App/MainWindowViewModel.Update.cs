using YttStudio.Video;

namespace YttStudio.App;

/// <summary>앱 업데이트 확인, 다운로드, 설치 상태를 담당한다.</summary>
public sealed partial class MainWindowViewModel
{
    private static readonly HttpClient DefaultUpdateHttpClient = new();

    private readonly AppUpdateService updateService;
    private IAppUpdateCoordinator? updateCoordinator;
    private IAppUpdateInstaller updateInstaller = new AppUpdateInstaller();
    private Action requestShutdown = RequestShutdown;
    private readonly Action<string> openFileLocation;
    private readonly CancellationTokenSource updateCancellation = new();
    private readonly SemaphoreSlim updateGate = new(1, 1);
    private AppUpdateCheckResult? updateCheckResult;
    private AppUpdateProgress? latestUpdateProgress;
    private bool isDownloadingUpdate;
    private double updateProgress;
    private string updateProgressStatus = string.Empty;

    private static AppUpdateService CreateDefaultUpdateService()
        => new(DefaultUpdateHttpClient);

    internal MainWindowViewModel(
        IFileDialogService dialogs,
        PreferencesStore? preferencesStore,
        Func<IVideoSource?>? videoSourceFactory,
        Func<string, CancellationToken, Task>? youtubeProbe,
        AppUpdateService? updateService,
        Action<string>? openFileLocation,
        IAppUpdateInstaller? updateInstaller,
        Action? requestShutdown,
        IAppUpdateCoordinator? updateCoordinator = null)
        : this(
            dialogs,
            preferencesStore,
            videoSourceFactory,
            youtubeProbe,
            updateService,
            openFileLocation)
    {
        this.updateInstaller = updateInstaller ?? new AppUpdateInstaller();
        this.requestShutdown = requestShutdown ?? RequestShutdown;
        this.updateCoordinator = updateCoordinator ?? new AppUpdateCoordinator(this.updateService);
    }

    private IAppUpdateCoordinator CurrentUpdateCoordinator
        => updateCoordinator ??= new AppUpdateCoordinator(updateService);

    private static void OpenDownloadedFileLocation(string filePath)
    {
        string directory = Path.GetDirectoryName(filePath) ?? filePath;
        AppUpdateProcessRequest request;
        if (OperatingSystem.IsWindows())
        {
            request = new(
                "explorer.exe",
                [$"/select,\"{filePath}\""],
                UseShellExecute: true);
        }
        else if (OperatingSystem.IsMacOS())
        {
            request = new("open", ["-R", filePath]);
        }
        else if (OperatingSystem.IsLinux())
        {
            request = new("xdg-open", [directory]);
        }
        else
        {
            throw new PlatformNotSupportedException("현재 플랫폼의 파일 위치 열기를 지원하지 않는다.");
        }

        AppUpdateProcessResult result = new AppUpdateProcessRunner()
            .RunAsync(request)
            .GetAwaiter()
            .GetResult();
        if (!result.Started)
        {
            throw new InvalidOperationException("파일 위치를 여는 프로세스를 시작하지 못했다.");
        }
    }

    private void InitializeUpdateCommands()
    {
        CheckForUpdatesCommand = new AsyncCommand(
            () => CheckForUpdatesAsync(),
            () => !isDownloadingUpdate);
        DownloadUpdateCommand = new AsyncCommand(
            DownloadUpdateAsync,
            () => IsUpdateAvailable && !isDownloadingUpdate);
    }

    public bool IsUpdateAvailable
        => updateCheckResult?.IsUpdateAvailable == true &&
            updateCheckResult.SelectedAsset is not null;

    public bool IsDownloadingUpdate
    {
        get => isDownloadingUpdate;
        private set
        {
            if (SetField(ref isDownloadingUpdate, value))
            {
                CheckForUpdatesCommand.NotifyCanExecuteChanged();
                DownloadUpdateCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsUpdateProgressIndeterminate));
            }
        }
    }

    public double UpdateProgress
    {
        get => updateProgress;
        private set => SetField(ref updateProgress, value);
    }

    public bool IsUpdateProgressIndeterminate
        => IsDownloadingUpdate && latestUpdateProgress?.Fraction is null;

    public string UpdateProgressStatus
    {
        get => updateProgressStatus;
        private set => SetField(ref updateProgressStatus, value);
    }

    /// <summary>최신 버전을 확인한다. 자동 확인일 때만 사용자 설정으로 건너뛸 수 있다.</summary>
    public async Task CheckForUpdatesAsync(bool automatic = false)
    {
        if (automatic && !preferences.CheckForUpdatesEnabled)
        {
            return;
        }

        bool acquired = false;
        try
        {
            await updateGate.WaitAsync(updateCancellation.Token).ConfigureAwait(true);
            acquired = true;
            Status = Loc["UpdateChecking"];
            AppUpdateCheckResult result = await CurrentUpdateCoordinator
                .CheckForUpdateAsync(updateCancellation.Token)
                .ConfigureAwait(true);
            updateCheckResult = result;
            OnPropertyChanged(nameof(IsUpdateAvailable));
            DownloadUpdateCommand.NotifyCanExecuteChanged();

            if (result.IsUpdateAvailable && result.SelectedAsset is not null)
            {
                Status = string.Format(
                    Loc["UpdateAvailable"],
                    result.LatestVersion);
            }
            else
            {
                Status = string.Format(
                    Loc["UpdateNotAvailable"],
                    result.CurrentVersion);
            }
        }
        catch (OperationCanceledException) when (updateCancellation.IsCancellationRequested)
        {
        }
        catch (AppUpdateException exception)
        {
            updateCheckResult = null;
            OnPropertyChanged(nameof(IsUpdateAvailable));
            DownloadUpdateCommand.NotifyCanExecuteChanged();
            Status = FormatUpdateError(exception, checking: true);
            Serilog.Log.Warning(exception, "Unable to check for YttStudio updates");
        }
        catch (Exception exception)
        {
            updateCheckResult = null;
            OnPropertyChanged(nameof(IsUpdateAvailable));
            DownloadUpdateCommand.NotifyCanExecuteChanged();
            Status = $"{Loc["UpdateCheckError"]}: {Loc["UpdateUnexpectedError"]}";
            Serilog.Log.Error(exception, "Unexpected YttStudio update check failure");
        }
        finally
        {
            if (acquired)
            {
                updateGate.Release();
            }
        }
    }

    private async Task DownloadUpdateAsync()
    {
        AppUpdateAsset? asset = updateCheckResult?.SelectedAsset;
        if (!IsUpdateAvailable || asset is null)
        {
            Status = Loc["UpdateNotAvailableShort"];
            return;
        }

        bool acquired = false;
        try
        {
            await updateGate.WaitAsync(updateCancellation.Token).ConfigureAwait(true);
            acquired = true;
            try
            {
                IsDownloadingUpdate = true;
                latestUpdateProgress = null;
                UpdateProgress = 0;
                UpdateProgressStatus = Loc["UpdateDownloadPreparing"];
                Status = UpdateProgressStatus;
                IProgress<AppUpdateProgress> progress = new Progress<AppUpdateProgress>(
                    OnUpdateProgress);
                IAppUpdateCoordinator coordinator = CurrentUpdateCoordinator;
                string path = await coordinator.DownloadAsync(
                        asset,
                        GetDownloadsDirectory(),
                        progress,
                        updateCancellation.Token)
                    .ConfigureAwait(true);
                string downloadedStatus = string.Format(
                    Loc["UpdateDownloadCompleted"],
                    path);
                if (!await ConfirmDocumentReplacementAsync().ConfigureAwait(true))
                {
                    Status = string.Join(
                        Environment.NewLine,
                        downloadedStatus,
                        Loc["UpdateInstallCanceled"],
                        Loc["UpdateInstallInstructions"]);
                    OpenUpdateFallback(path);
                    return;
                }

                Status = Loc["UpdateInstalling"];
                try
                {
                    await updateInstaller.InstallAsync(
                            path,
                            coordinator.RuntimeIdentifier,
                            coordinator.ExecutionForm,
                            updateCancellation.Token)
                        .ConfigureAwait(true);
                    Status = Loc["UpdateInstallStarted"];
                    requestShutdown();
                }
                catch (AppUpdateException exception) when (
                    exception.Kind is AppUpdateErrorKind.InstallationFailed or
                        AppUpdateErrorKind.InstallationUnsupported)
                {
                    Status = string.Join(
                        Environment.NewLine,
                        downloadedStatus,
                        FormatUpdateInstallationError(exception),
                        Loc["UpdateInstallInstructions"]);
                    Serilog.Log.Warning(
                        exception,
                        "Unable to install downloaded YttStudio update {Path}",
                        path);
                    OpenUpdateFallback(path);
                }
            }
            finally
            {
                IsDownloadingUpdate = false;
            }
        }
        catch (OperationCanceledException) when (updateCancellation.IsCancellationRequested)
        {
        }
        catch (AppUpdateException exception)
        {
            Status = FormatUpdateError(exception, checking: false);
            Serilog.Log.Warning(exception, "Unable to download YttStudio update");
        }
        catch (Exception exception)
        {
            Status = $"{Loc["UpdateDownloadError"]}: {Loc["UpdateUnexpectedError"]}";
            Serilog.Log.Error(exception, "Unexpected YttStudio update download failure");
        }
        finally
        {
            if (acquired)
            {
                updateGate.Release();
            }
        }
    }

    internal bool ConsumeUpdateInstallResult(string? resultPath = null)
    {
        AppUpdateInstallResult? result;
        bool consumed;
        if (resultPath is null)
        {
            consumed = AppUpdateInstallResultStore.TryConsumeForCurrentProcess(out result);
        }
        else
        {
            consumed = AppUpdateInstallResultStore.TryConsume(resultPath, out result);
        }

        if (!consumed || result is null)
        {
            return false;
        }

        if (string.Equals(
                result.Status,
                AppUpdateInstallResultStore.SucceededStatus,
                StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(result.BackupPath))
            {
                AppUpdateArchiveOperations.DeleteBackup(result.BackupPath);
            }

            Status = Loc["UpdateInstallResultSucceeded"];
            return true;
        }

        string reason = string.IsNullOrWhiteSpace(result.Error)
            ? Loc["UpdateUnexpectedError"]
            : result.Error;
        if (string.Equals(
                result.Status,
                AppUpdateInstallResultStore.RolledBackStatus,
                StringComparison.OrdinalIgnoreCase))
        {
            Status = string.Join(
                Environment.NewLine,
                string.Format(Loc["UpdateInstallResultRolledBack"], reason),
                Loc["UpdateInstallInstructions"]);
        }
        else if (result.ExistingInstallationRestored)
        {
            Status = string.Join(
                Environment.NewLine,
                string.Format(Loc["UpdateInstallResultRestored"], reason),
                Loc["UpdateInstallInstructions"]);
        }
        else
        {
            string backupPath = string.IsNullOrWhiteSpace(result.BackupPath)
                ? result.TargetPath
                : result.BackupPath;
            Status = string.Join(
                Environment.NewLine,
                string.Format(Loc["UpdateInstallResultFailed"], backupPath, reason),
                Loc["UpdateInstallInstructions"]);
        }

        OpenUpdateFallback(result.DownloadedAssetPath);
        return true;
    }

    private void OnUpdateProgress(AppUpdateProgress progress)
    {
        latestUpdateProgress = progress;
        UpdateProgress = progress.Fraction ?? 0;
        UpdateProgressStatus = progress.Fraction is double fraction
            ? string.Format(Loc["UpdateDownloadProgress"], fraction.ToString("P0"))
            : string.Format(
                Loc["UpdateDownloadBytes"],
                progress.BytesTransferred,
                progress.TotalBytes?.ToString() ?? "?");
        Status = UpdateProgressStatus;
        OnPropertyChanged(nameof(IsUpdateProgressIndeterminate));
    }

    private void OpenUpdateFallback(string path)
    {
        try
        {
            openFileLocation(path);
        }
        catch (Exception exception)
        {
            Status = string.Join(
                Environment.NewLine,
                Status,
                Loc["UpdateOpenLocationFailed"]);
            Serilog.Log.Warning(
                exception,
                "Unable to open downloaded YttStudio update location {Path}",
                path);
        }
    }

    private string FormatUpdateInstallationError(AppUpdateException exception)
    {
        string key = exception.Kind == AppUpdateErrorKind.InstallationUnsupported
            ? "UpdateInstallUnsupported"
            : "UpdateInstallFailed";
        return string.Format(Loc[key], exception.Message);
    }

    private string FormatUpdateError(AppUpdateException exception, bool checking)
    {
        if (exception.Kind is AppUpdateErrorKind.InstallationFailed or
            AppUpdateErrorKind.InstallationUnsupported)
        {
            return FormatUpdateInstallationError(exception);
        }

        string detail = exception.Kind switch
        {
            AppUpdateErrorKind.Network => Loc["UpdateNetworkError"],
            AppUpdateErrorKind.RateLimited => Loc["UpdateRateLimited"],
            AppUpdateErrorKind.InvalidMetadata => Loc["UpdateMetadataError"],
            AppUpdateErrorKind.AssetNotFound => Loc["UpdateAssetNotFound"],
            AppUpdateErrorKind.UnsupportedPlatform => Loc["UpdateUnsupportedPlatform"],
            AppUpdateErrorKind.DownloadFailed => Loc["UpdateDownloadFailed"],
            _ => Loc["UpdateUnexpectedError"],
        };
        string operation = checking ? Loc["UpdateCheckError"] : Loc["UpdateDownloadError"];
        return $"{operation}: {detail}";
    }

    private static string GetDownloadsDirectory()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(
            string.IsNullOrWhiteSpace(profile) ? Environment.CurrentDirectory : profile,
            "Downloads");
    }

    private void CancelUpdateOperations()
    {
        if (!updateCancellation.IsCancellationRequested)
        {
            updateCancellation.Cancel();
        }
    }
}
