using System.IO.Compression;
using System.Text;
using YttStudio.App;

namespace YttStudio.App.Tests;

public sealed class AppUpdateInstallerTests
{
    [Fact]
    public void ExecutionDetectorUsesInnoUninstallerAsTheInstalledMarker()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            Assert.Equal(
                AppUpdateExecutionForm.Portable,
                AppUpdateExecutionDetector.Detect("win-x64", root));
            File.WriteAllText(Path.Combine(root, "unins000.exe"), string.Empty);
            Assert.Equal(
                AppUpdateExecutionForm.Installed,
                AppUpdateExecutionDetector.Detect("win-x64", root));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void ExecutionDetectorUsesAppImageEnvironmentForLinux()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            Assert.Equal(
                AppUpdateExecutionForm.TarGz,
                AppUpdateExecutionDetector.Detect("linux-x64", root, string.Empty));
            Assert.Equal(
                AppUpdateExecutionForm.AppImage,
                AppUpdateExecutionDetector.Detect("linux-x64", root, Path.Combine(root, "yttStudio.AppImage")));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void ExecutionDetectorUsesAppBundlePathForMac()
    {
        string root = CreateTemporaryDirectory();
        string bundlePath = Path.Combine(root, "yttStudio.app", "Contents", "MacOS");
        Directory.CreateDirectory(bundlePath);
        try
        {
            Assert.Equal(
                AppUpdateExecutionForm.Installed,
                AppUpdateExecutionDetector.Detect("osx-arm64", bundlePath));
            Assert.Equal(
                AppUpdateExecutionForm.TarGz,
                AppUpdateExecutionDetector.Detect("osx-arm64", root));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void WindowsInstallerUsesOnlySupportedInnoSetupUnattendedArguments()
    {
        Assert.Equal(
            ["/VERYSILENT", "/CLOSEAPPLICATIONS", "/RESTARTAPPLICATIONS"],
            AppUpdateInstaller.BuildWindowsInstallerArguments());
    }

    [Fact]
    public async Task WindowsInstalledUpdateStartsSetupThroughProcessAbstraction()
    {
        string root = CreateTemporaryDirectory();
        string setupPath = Path.Combine(root, "yttStudio-setup.exe");
        await File.WriteAllTextAsync(setupPath, "setup", TestContext.Current.CancellationToken);
        RecordingProcessRunner runner = new();
        AppUpdateInstaller installer = new(
            runner,
            applicationDirectory: root,
            executablePath: setupPath,
            currentProcessId: 1234);

        try
        {
            await installer.InstallAsync(
                setupPath,
                "win-x64",
                AppUpdateExecutionForm.Installed,
                TestContext.Current.CancellationToken);

            AppUpdateProcessRequest request = Assert.Single(runner.Requests);
            Assert.Equal(setupPath, request.FileName);
            Assert.True(request.UseShellExecute);
            Assert.False(request.WaitForExit);
            Assert.Equal(
                ["/VERYSILENT", "/CLOSEAPPLICATIONS", "/RESTARTAPPLICATIONS"],
                request.Arguments);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public async Task WindowsPortableUpdateExtractsToStagingAndStartsReplacementHelper()
    {
        string root = CreateTemporaryDirectory();
        string applicationDirectory = Path.Combine(root, "current");
        Directory.CreateDirectory(applicationDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(applicationDirectory, "old.txt"),
            "old",
            TestContext.Current.CancellationToken);
        string zipPath = Path.Combine(root, "update.zip");
        CreateZip(zipPath, ("YttStudio.App.exe", "new"), ("new.txt", "new"));
        RecordingProcessRunner runner = new();
        AppUpdateInstaller installer = new(
            runner,
            applicationDirectory,
            Path.Combine(applicationDirectory, "YttStudio.App.exe"),
            1234);

        try
        {
            await installer.InstallAsync(
                zipPath,
                "win-x64",
                AppUpdateExecutionForm.Portable,
                TestContext.Current.CancellationToken);

            AppUpdateProcessRequest request = Assert.Single(runner.Requests);
            Assert.Equal("cmd.exe", request.FileName);
            Assert.Equal(["/d", "/c"], request.Arguments.Take(2));
            string scriptPath = request.Arguments[2];
            string script = await File.ReadAllTextAsync(
                scriptPath,
                TestContext.Current.CancellationToken);
            Assert.Contains("tasklist", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("move", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ytt_restore", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("start", script, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(applicationDirectory, "old.txt")));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public async Task InstallerRejectsAnAssetThatDoesNotMatchTheExecutionForm()
    {
        string root = CreateTemporaryDirectory();
        string packagePath = Path.Combine(root, "update.zip");
        await File.WriteAllTextAsync(packagePath, "zip", TestContext.Current.CancellationToken);
        AppUpdateInstaller installer = new(
            new RecordingProcessRunner(),
            root,
            Path.Combine(root, "YttStudio.App.exe"),
            1234);

        try
        {
            AppUpdateException exception = await Assert.ThrowsAsync<AppUpdateException>(
                () => installer.InstallAsync(
                    packagePath,
                    "win-x64",
                    AppUpdateExecutionForm.Installed,
                    TestContext.Current.CancellationToken));
            Assert.Equal(AppUpdateErrorKind.InstallationUnsupported, exception.Kind);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public async Task InstallerReportsAProcessStartFailure()
    {
        string root = CreateTemporaryDirectory();
        string setupPath = Path.Combine(root, "yttStudio-setup.exe");
        await File.WriteAllTextAsync(setupPath, "setup", TestContext.Current.CancellationToken);
        AppUpdateInstaller installer = new(
            new RecordingProcessRunner(new AppUpdateProcessResult(false, null)),
            root,
            setupPath,
            1234);

        try
        {
            AppUpdateException exception = await Assert.ThrowsAsync<AppUpdateException>(
                () => installer.InstallAsync(
                    setupPath,
                    "win-x64",
                    AppUpdateExecutionForm.Installed,
                    TestContext.Current.CancellationToken));
            Assert.Equal(AppUpdateErrorKind.InstallationFailed, exception.Kind);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void UnixHelpersContainWaitReplaceRollbackAndRelaunchSteps()
    {
        string directoryScript = AppUpdateInstallHelpers.BuildUnixDirectoryReplacementScript(
            9,
            "/opt/yttStudio",
            "/tmp/staging",
            "YttStudio.App",
            "/tmp/update.sh");
        string fileScript = AppUpdateInstallHelpers.BuildUnixFileReplacementScript(
            9,
            "/opt/yttStudio.AppImage",
            "/tmp/staging.AppImage",
            "/tmp/update.sh");

        Assert.Contains("kill -0", directoryScript, StringComparison.Ordinal);
        Assert.Contains("ytt_restore", directoryScript, StringComparison.Ordinal);
        Assert.Contains("mv", directoryScript, StringComparison.Ordinal);
        Assert.Contains("chmod +x", directoryScript, StringComparison.Ordinal);
        Assert.Contains("ytt_restore", fileScript, StringComparison.Ordinal);
        Assert.Contains("ytt_staging", fileScript, StringComparison.Ordinal);
        Assert.Contains("chmod +x", fileScript, StringComparison.Ordinal);
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "yttStudio-update-installer-tests",
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

    private static void CreateZip(string path, params (string Name, string Content)[] entries)
    {
        using FileStream stream = File.Create(path);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        foreach ((string name, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }

    private sealed class RecordingProcessRunner(
        AppUpdateProcessResult? result = null) : IAppUpdateProcessRunner
    {
        public List<AppUpdateProcessRequest> Requests { get; } = [];

        public Task<AppUpdateProcessResult> RunAsync(
            AppUpdateProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(result ?? new AppUpdateProcessResult(true, 0));
        }
    }
}
