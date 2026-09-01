using YttStudio.App;

namespace YttStudio.App.Tests;

public sealed class AppUpdateInstallResultTests
{
    [Fact]
    public void ResultStoreWritesReadsAndConsumesTheHelperOutcome()
    {
        string root = CreateTemporaryDirectory();
        string target = Path.Combine(root, "current");
        string resultPath = AppUpdateInstallResultStore.GetResultPath(target);
        AppUpdateInstallResult expected = new(
            AppUpdateInstallResultStore.RolledBackStatus,
            Path.Combine(root, "download.zip"),
            target,
            target + ".backup-123",
            ExistingInstallationRestored: true,
            "new application launch failed");

        try
        {
            AppUpdateInstallResultStore.Write(resultPath, expected);

            Assert.True(AppUpdateInstallResultStore.TryRead(resultPath, out AppUpdateInstallResult? read));
            Assert.Equal(expected, read);
            Assert.True(AppUpdateInstallResultStore.TryConsume(resultPath, out AppUpdateInstallResult? consumed));
            Assert.Equal(expected, consumed);
            Assert.False(File.Exists(resultPath));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void RollbackFailureReturnsBackupPathAndLeavesBackupForManualRecovery()
    {
        string root = CreateTemporaryDirectory();
        string target = Path.Combine(root, "current");
        string backup = target + ".backup-123";
        File.WriteAllText(target, "a file blocks the directory target");
        Directory.CreateDirectory(backup);
        try
        {
            AppUpdateRollbackResult result = AppUpdateArchiveOperations.RollbackDirectory(target, backup);

            Assert.False(result.Restored);
            Assert.Equal(backup, result.BackupPath);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
            Assert.True(Directory.Exists(backup));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void WindowsDetectionFailureIsNotClassifiedAsPortable()
    {
        string root = CreateTemporaryDirectory();
        string filePath = Path.Combine(root, "not-a-directory");
        File.WriteAllText(filePath, "not a directory");
        try
        {
            AppUpdateException exception = Assert.Throws<AppUpdateException>(
                () => AppUpdateExecutionDetector.Detect("win-x64", filePath));

            Assert.Equal(AppUpdateErrorKind.InstallationUnsupported, exception.Kind);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void UnixHelpersPersistAllOutcomeStatesAndPaths()
    {
        string script = AppUpdateInstallHelpers.BuildUnixDirectoryReplacementScript(
            42,
            "/opt/YttStudio",
            "/tmp/staging",
            "YttStudio.App",
            "/tmp/helper.sh",
            "/tmp/download.tar.gz",
            "/opt/YttStudio.update-result.json");

        Assert.Contains("succeeded", script, StringComparison.Ordinal);
        Assert.Contains("rolled_back", script, StringComparison.Ordinal);
        Assert.Contains("failed", script, StringComparison.Ordinal);
        Assert.Contains("downloadedAssetPath", script, StringComparison.Ordinal);
        Assert.Contains("existingInstallationRestored", script, StringComparison.Ordinal);
        Assert.Contains("/opt/YttStudio.update-result.json", script, StringComparison.Ordinal);
        Assert.Contains("ytt_relaunch_existing", script, StringComparison.Ordinal);
    }

    [Fact]
    public void MacOpenRequestsCreateANewApplicationInstance()
    {
        Assert.Equal(
            ["-n", "/Applications/YttStudio.app"],
            AppUpdateInstaller.BuildMacOpenArguments("/Applications/YttStudio.app"));
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "yttStudio-update-result-tests",
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
}
