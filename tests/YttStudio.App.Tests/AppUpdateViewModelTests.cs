using Avalonia.Headless.XUnit;
using YttStudio.App;

namespace YttStudio.App.Tests;

public sealed class AppUpdateViewModelTests
{
    [AvaloniaFact]
    public async Task CancelingTheExistingDocumentPromptDoesNotInstallOrShutdown()
    {
        string root = CreateTemporaryDirectory();
        string? downloadedPath = null;
        try
        {
            TestFileDialogService dialogs = new();
            dialogs.UnsavedChoices.Enqueue(UnsavedChangesChoice.Cancel);
            FakeInstaller installer = new();
            bool shutdown = false;
            using MainWindowViewModel viewModel = CreateViewModel(
                root,
                dialogs,
                installer,
                () => shutdown = true,
                out _,
                out downloadedPath);

            await viewModel.CheckForUpdatesAsync();
            viewModel.AddCueCommand.Execute(null);
            await viewModel.DownloadUpdateCommand.ExecuteAsync();
            Assert.Equal(0, installer.Calls);
            Assert.False(shutdown);
            Assert.Equal(1, dialogs.UnsavedChoiceCalls);
            Assert.Contains("취소", viewModel.Status, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
            DeleteDownloadedAsset(downloadedPath);
        }
    }

    [AvaloniaFact]
    public async Task InstallationFailureReportsErrorAndOpensDownloadedLocation()
    {
        string root = CreateTemporaryDirectory();
        string? downloadedPath = null;
        try
        {
            TestFileDialogService dialogs = new();
            FakeInstaller installer = new(new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                "fake installation failure"));
            List<string> opened = [];
            using MainWindowViewModel viewModel = CreateViewModel(
                root,
                dialogs,
                installer,
                () => { },
                out _,
                out downloadedPath,
                opened.Add);

            await viewModel.CheckForUpdatesAsync();
            await viewModel.DownloadUpdateCommand.ExecuteAsync();
            Assert.Equal(1, installer.Calls);
            Assert.Contains("fake installation failure", viewModel.Status, StringComparison.Ordinal);
            Assert.Contains("자동 설치", viewModel.Status, StringComparison.Ordinal);
            Assert.Single(opened);
            Assert.Equal(downloadedPath, opened[0]);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
            DeleteDownloadedAsset(downloadedPath);
        }
    }

    [AvaloniaFact]
    public async Task SuccessfulInstallationHandsOffThenRequestsShutdown()
    {
        string root = CreateTemporaryDirectory();
        string? downloadedPath = null;
        try
        {
            FakeInstaller installer = new();
            bool shutdown = false;
            using MainWindowViewModel viewModel = CreateViewModel(
                root,
                new TestFileDialogService(),
                installer,
                () => shutdown = true,
                out _,
                out downloadedPath);

            await viewModel.CheckForUpdatesAsync();
            await viewModel.DownloadUpdateCommand.ExecuteAsync();
            Assert.Equal(1, installer.Calls);
            Assert.True(installer.HandoffCompleted);
            Assert.True(shutdown);
            Assert.Contains("다시", viewModel.Status, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
            DeleteDownloadedAsset(downloadedPath);
        }
    }

    [AvaloniaFact]
    public async Task FailedAttemptReleasesGateForTheNextAttempt()
    {
        string root = CreateTemporaryDirectory();
        string? downloadedPath = null;
        try
        {
            FakeInstaller installer = new(
                new AppUpdateException(AppUpdateErrorKind.InstallationFailed, "first attempt"));
            installer.NextException = null;
            using MainWindowViewModel viewModel = CreateViewModel(
                root,
                new TestFileDialogService(),
                installer,
                () => { },
                out _,
                out downloadedPath);

            await viewModel.CheckForUpdatesAsync();
            await viewModel.DownloadUpdateCommand.ExecuteAsync();
            Assert.Equal(1, installer.Calls);
            Assert.True(viewModel.DownloadUpdateCommand.CanExecute(null));

            await viewModel.DownloadUpdateCommand.ExecuteAsync();
            Assert.Equal(2, installer.Calls);
            Assert.True(installer.HandoffCompleted);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
            DeleteDownloadedAsset(downloadedPath);
        }
    }

    [AvaloniaFact]
    public void StartupConsumesFailureResultAndKeepsManualRecoveryPathVisible()
    {
        string root = CreateTemporaryDirectory();
        string resultPath = Path.Combine(root, "update-result.json");
        string downloadedPath = Path.Combine(root, "download.zip");
        string backupPath = Path.Combine(root, "current.backup");
        File.WriteAllText(downloadedPath, "download");
        Directory.CreateDirectory(backupPath);
        try
        {
            AppUpdateInstallResultStore.Write(
                resultPath,
                new(
                    AppUpdateInstallResultStore.FailedStatus,
                    downloadedPath,
                    Path.Combine(root, "current"),
                    backupPath,
                    ExistingInstallationRestored: false,
                    "old application could not be restored"));
            List<string> opened = [];
            using MainWindowViewModel viewModel = CreateViewModel(
                root,
                new TestFileDialogService(),
                new FakeInstaller(),
                () => { },
                out _,
                out _,
                opened.Add);

            Assert.True(viewModel.ConsumeUpdateInstallResult(resultPath));
            Assert.Contains("복원", viewModel.Status, StringComparison.Ordinal);
            Assert.Contains(backupPath, viewModel.Status, StringComparison.Ordinal);
            Assert.Contains("다운로드한 파일", viewModel.Status, StringComparison.Ordinal);
            Assert.Single(opened);
            Assert.Equal(downloadedPath, opened[0]);
            Assert.False(File.Exists(resultPath));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    private static MainWindowViewModel CreateViewModel(
        string root,
        TestFileDialogService dialogs,
        FakeInstaller installer,
        Action shutdown,
        out AppUpdateService service,
        out string downloadedPath,
        Action<string>? openFileLocation = null)
    {
        string tag = "v99.0.0";
        string assetName = $"yttStudio-{tag}-win-x64-setup.exe";
        downloadedPath = Path.Combine(root, assetName);
        AppUpdateAsset asset = new(
            assetName,
            new Uri("https://example.test/" + assetName),
            3);
        service = new AppUpdateService(
            new HttpClient(),
            "win-x64");
        return new MainWindowViewModel(
            dialogs,
            new PreferencesStore(Path.Combine(root, "preferences.json")),
            videoSourceFactory: () => null,
            youtubeProbe: (_, _) => Task.CompletedTask,
            updateService: service,
            openFileLocation: openFileLocation ?? (_ => { }),
            updateInstaller: installer,
            requestShutdown: shutdown,
            updateCoordinator: new FakeCoordinator(asset, downloadedPath));
    }

    private static void DeleteDownloadedAsset(string? path)
    {
        if (path is not null && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "yttStudio-update-vm-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class FakeInstaller(Exception? firstException = null) : IAppUpdateInstaller
    {
        public int Calls { get; private set; }
        public bool HandoffCompleted { get; private set; }
        public Exception? NextException { get; set; } = firstException;

        public Task InstallAsync(
            string downloadedPath,
            string runtimeIdentifier,
            AppUpdateExecutionForm executionForm,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (NextException is Exception exception)
            {
                Exception? next = NextException;
                NextException = null;
                throw next;
            }

            HandoffCompleted = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCoordinator(AppUpdateAsset asset, string downloadPath) : IAppUpdateCoordinator
    {
        public string RuntimeIdentifier => "win-x64";
        public AppUpdateExecutionForm ExecutionForm => AppUpdateExecutionForm.Installed;

        public Task<AppUpdateCheckResult> CheckForUpdateAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AppUpdateCheckResult(
                IsUpdateAvailable: true,
                CurrentVersion: "0.1.0",
                LatestVersion: "99.0.0",
                ReleaseTag: "v99.0.0",
                SelectedAsset: asset));

        public Task<string> DownloadAsync(
            AppUpdateAsset requestedAsset,
            string destinationDirectory,
            IProgress<AppUpdateProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(downloadPath, "new");
            return Task.FromResult(downloadPath);
        }
    }

    private sealed class TestFileDialogService : IFileDialogService
    {
        public Queue<UnsavedChangesChoice> UnsavedChoices { get; } = [];
        public int UnsavedChoiceCalls { get; private set; }

        public Task<string?> OpenSubtitleAsync() => Task.FromResult<string?>(null);
        public Task<string?> OpenVideoAsync() => Task.FromResult<string?>(null);
        public Task<string?> OpenVideoUrlAsync(VideoUrlDialogOptions? options = null)
            => Task.FromResult<string?>(null);
        public Task<string?> SaveYttAsync(string? suggestedName) => Task.FromResult<string?>(null);
        public Task<UnsavedChangesChoice> ConfirmUnsavedChangesAsync(
            string title,
            string message,
            string saveLabel = "저장",
            string discardLabel = "버리기",
            string cancelLabel = "취소")
        {
            UnsavedChoiceCalls++;
            return Task.FromResult(UnsavedChoices.Count > 0
                ? UnsavedChoices.Dequeue()
                : UnsavedChangesChoice.Discard);
        }

        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "삭제")
            => Task.FromResult(false);
        public Task<string?> OpenProjectAsync() => Task.FromResult<string?>(null);
        public Task<string?> SaveProjectAsync(string? suggestedName) => Task.FromResult<string?>(null);
        public Task<string?> OpenMpvLibraryAsync() => Task.FromResult<string?>(null);
        public Task<string?> RelinkVideoAsync(string missingPath) => Task.FromResult<string?>(null);
    }
}
