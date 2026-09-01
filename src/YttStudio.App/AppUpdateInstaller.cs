using System.Diagnostics;
using System.Text;

namespace YttStudio.App;

/// <summary>현재 앱이 실행된 배포 형태를 나타낸다.</summary>
internal enum AppUpdateExecutionForm
{
    Installed,
    Portable,
    AppImage,
    TarGz,
}

/// <summary>업데이트 설치에 사용할 한 번의 프로세스 실행 요청이다.</summary>
internal sealed record AppUpdateProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    bool UseShellExecute = false,
    string? WorkingDirectory = null,
    bool WaitForExit = false);

/// <summary>프로세스 실행 결과다.</summary>
internal sealed record AppUpdateProcessResult(bool Started, int? ExitCode);

/// <summary>업데이트 설치 프로세스를 실제 운영체제에서 실행하는 추상화다.</summary>
internal interface IAppUpdateProcessRunner
{
    Task<AppUpdateProcessResult> RunAsync(
        AppUpdateProcessRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>운영체제 프로세스 API를 업데이트 설치 추상화에 연결한다.</summary>
internal sealed class AppUpdateProcessRunner : IAppUpdateProcessRunner
{
    /// <inheritdoc />
    public async Task<AppUpdateProcessResult> RunAsync(
        AppUpdateProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException("프로세스 파일 이름이 비어 있다.", nameof(request));
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = request.FileName,
            UseShellExecute = request.UseShellExecute,
            CreateNoWindow = !request.UseShellExecute,
        };
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (string argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException(
                $"프로세스를 시작하지 못했다: {request.FileName}");
        }

        if (!request.WaitForExit)
        {
            process.Dispose();
            return new(true, null);
        }

        using (process)
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new(true, process.ExitCode);
        }
    }
}

/// <summary>실행 위치와 환경 변수로 업데이트 실행 형태를 판별한다.</summary>
internal static class AppUpdateExecutionDetector
{
    /// <summary>지정한 실행 환경의 업데이트 형태를 판별한다.</summary>
    public static AppUpdateExecutionForm Detect(
        string runtimeIdentifier,
        string? applicationDirectory = null,
        string? appImagePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        string directory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(applicationDirectory)
                ? AppContext.BaseDirectory
                : applicationDirectory);

        string? effectiveAppImagePath = string.IsNullOrWhiteSpace(appImagePath)
            ? Environment.GetEnvironmentVariable("APPIMAGE")
            : appImagePath;
        return runtimeIdentifier switch
        {
            "win-x64" => IsInnoSetupInstallation(directory)
                ? AppUpdateExecutionForm.Installed
                : AppUpdateExecutionForm.Portable,
            "osx-arm64" => IsMacAppBundlePath(directory)
                ? AppUpdateExecutionForm.Installed
                : AppUpdateExecutionForm.TarGz,
            "linux-x64" => !string.IsNullOrWhiteSpace(effectiveAppImagePath)
                ? AppUpdateExecutionForm.AppImage
                : AppUpdateExecutionForm.TarGz,
            _ => throw new AppUpdateException(
                AppUpdateErrorKind.UnsupportedPlatform,
                $"지원하지 않는 플랫폼 RID다: {runtimeIdentifier}"),
        };
    }

    /// <summary>플랫폼과 실행 형태의 조합이 지원되는지 확인한다.</summary>
    public static void Validate(
        string runtimeIdentifier,
        AppUpdateExecutionForm executionForm)
    {
        bool supported = runtimeIdentifier switch
        {
            "win-x64" => executionForm is
                AppUpdateExecutionForm.Installed or AppUpdateExecutionForm.Portable,
            "osx-arm64" => executionForm is
                AppUpdateExecutionForm.Installed or AppUpdateExecutionForm.TarGz,
            "linux-x64" => executionForm is
                AppUpdateExecutionForm.AppImage or AppUpdateExecutionForm.TarGz,
            _ => false,
        };
        if (!supported)
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationUnsupported,
                $"지원하지 않는 업데이트 실행 형태다: {runtimeIdentifier}/{executionForm}");
        }
    }

    private static bool IsInnoSetupInstallation(string applicationDirectory)
    {
        // Inno Setup은 설치 폴더에 unins*.exe를 남긴다. 이 파일이 있으면 설치본으로,
        // 없으면 압축을 풀어 실행하는 포터블로 판별한다.
        try
        {
            return Directory.EnumerateFiles(
                    applicationDirectory,
                    "unins*.exe",
                    SearchOption.TopDirectoryOnly)
                .Any();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException or
                System.Security.SecurityException or ArgumentException)
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationUnsupported,
                "설치 형태를 판별하는 중 현재 실행 디렉터리를 읽지 못했다.",
                exception);
        }
    }

    private static bool IsMacAppBundlePath(string applicationDirectory)
    {
        DirectoryInfo? directory = new(applicationDirectory);
        while (directory is not null)
        {
            if (directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }
}

/// <summary>다운로드된 릴리스 자산을 현재 앱 실행 형태에 맞게 설치한다.</summary>
internal interface IAppUpdateInstaller
{
    Task InstallAsync(
        string downloadedPath,
        string runtimeIdentifier,
        AppUpdateExecutionForm executionForm,
        CancellationToken cancellationToken = default);
}

internal sealed class AppUpdateInstaller : IAppUpdateInstaller
{
    private readonly IAppUpdateProcessRunner processRunner;
    private readonly string applicationDirectory;
    private readonly string executablePath;
    private readonly int currentProcessId;

    /// <summary>업데이트 설치기를 생성한다.</summary>
    public AppUpdateInstaller(
        IAppUpdateProcessRunner? processRunner = null,
        string? applicationDirectory = null,
        string? executablePath = null,
        int? currentProcessId = null)
    {
        this.processRunner = processRunner ?? new AppUpdateProcessRunner();
        this.applicationDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(
                string.IsNullOrWhiteSpace(applicationDirectory)
                    ? AppContext.BaseDirectory
                    : applicationDirectory));
        this.executablePath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(executablePath)
                ? Environment.ProcessPath ?? Path.Combine(this.applicationDirectory, "YttStudio.App")
                : executablePath);
        this.currentProcessId = currentProcessId ?? Environment.ProcessId;
    }

    /// <summary>다운로드된 자산의 설치를 시작한다.</summary>
    public async Task InstallAsync(
        string downloadedPath,
        string runtimeIdentifier,
        AppUpdateExecutionForm executionForm,
        CancellationToken cancellationToken = default)
    {
        AppUpdateExecutionDetector.Validate(runtimeIdentifier, executionForm);
        try
        {
            string packagePath = ValidateDownloadedPackage(downloadedPath, executionForm);
            switch (runtimeIdentifier, executionForm)
            {
                case ("win-x64", AppUpdateExecutionForm.Installed):
                    await InstallWindowsSetupAsync(packagePath, cancellationToken).ConfigureAwait(false);
                    return;
                case ("win-x64", AppUpdateExecutionForm.Portable):
                    await InstallWindowsPortableAsync(packagePath, cancellationToken).ConfigureAwait(false);
                    return;
                case ("osx-arm64", AppUpdateExecutionForm.Installed):
                    await InstallMacDmgAsync(packagePath, cancellationToken).ConfigureAwait(false);
                    return;
                case ("osx-arm64", AppUpdateExecutionForm.TarGz):
                    await InstallUnixTarballAsync(packagePath, macOS: true, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                case ("linux-x64", AppUpdateExecutionForm.AppImage):
                    await InstallLinuxAppImageAsync(packagePath, cancellationToken).ConfigureAwait(false);
                    return;
                case ("linux-x64", AppUpdateExecutionForm.TarGz):
                    await InstallUnixTarballAsync(packagePath, macOS: false, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                default:
                    throw new AppUpdateException(
                        AppUpdateErrorKind.InstallationUnsupported,
                        $"지원하지 않는 업데이트 설치 경로다: {runtimeIdentifier}/{executionForm}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AppUpdateException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                "업데이트 설치 중 파일 또는 권한 오류가 발생했다.",
                exception);
        }
    }

    /// <summary>Inno Setup 설치기에 전달할 무인 인자를 반환한다.</summary>
    internal static IReadOnlyList<string> BuildWindowsInstallerArguments()
        // yttStudio.iss의 [Setup] 항목은 앱 종료와 재실행을 Inno Setup에 맡기므로
        // 실제 Inno Setup 무인 실행 스위치만 사용한다.
        => ["/VERYSILENT", "/CLOSEAPPLICATIONS", "/RESTARTAPPLICATIONS"];

    private async Task InstallWindowsSetupAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        AppUpdateProcessResult result = await RunProcessAsync(
                new(
                    packagePath,
                    BuildWindowsInstallerArguments(),
                    UseShellExecute: true),
                waitForExit: false,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureStarted(result, "Windows 설치 프로그램");
    }

    private async Task InstallWindowsPortableAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        EnsureDirectory(applicationDirectory, "Windows 포터블 설치 위치");
        string parent = GetParentDirectory(applicationDirectory, "Windows 포터블 설치 위치");
        string token = Guid.NewGuid().ToString("N");
        string stagingDirectory = Path.Combine(parent, $".yttstudio-update-{token}");
        string helperPath = Path.Combine(parent, $".yttstudio-update-{token}.cmd");
        bool helperStarted = false;
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            await AppUpdateArchiveOperations.ExtractAsync(
                    packagePath,
                    stagingDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            string relativeExecutable = AppUpdateArchiveOperations.RequireFile(
                stagingDirectory,
                "YttStudio.App.exe");
            string script = AppUpdateInstallHelpers.BuildWindowsDirectoryReplacementScript(
                currentProcessId,
                applicationDirectory,
                stagingDirectory,
                relativeExecutable,
                helperPath,
                packagePath,
                AppUpdateInstallResultStore.GetResultPath(applicationDirectory));
            await File.WriteAllTextAsync(
                    helperPath,
                    script,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);
            AppUpdateProcessResult result = await RunProcessAsync(
                    new(
                        "cmd.exe",
                        ["/d", "/c", helperPath],
                        WorkingDirectory: parent),
                    waitForExit: false,
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureStarted(result, "Windows 포터블 교체 helper");
            helperStarted = true;
        }
        finally
        {
            if (!helperStarted)
            {
                AppUpdateArchiveOperations.TryDeleteDirectory(stagingDirectory);
                AppUpdateArchiveOperations.TryDeleteFile(helperPath);
            }
        }
    }

    private async Task InstallMacDmgAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        string currentApp = AppUpdatePathOperations.FindMacAppBundle(executablePath);
        string parent = GetParentDirectory(currentApp, "macOS 앱 설치 위치");
        string mountPoint = Path.Combine(
            Path.GetTempPath(),
            $"yttstudio-update-mount-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mountPoint);
        string? backupPath = null;
        string? stagedApp = null;
        bool mounted = false;
        try
        {
            await RunCheckedAsync(
                    new(
                        "hdiutil",
                        ["attach", "-nobrowse", "-readonly", "-mountpoint", mountPoint, packagePath]),
                    "macOS 디스크 이미지 연결",
                    cancellationToken)
                .ConfigureAwait(false);
            mounted = true;
            string mountedApp = AppUpdatePathOperations.FindSingleAppBundle(mountPoint);
            stagedApp = Path.Combine(parent, $".yttstudio-app-{Guid.NewGuid():N}.app");
            AppUpdateArchiveOperations.CopyDirectory(mountedApp, stagedApp);
            AppUpdatePathOperations.MakeExecutable(
                Path.Combine(stagedApp, "Contents", "MacOS", "YttStudio.App"));
            backupPath = AppUpdateArchiveOperations.MoveDirectoryWithBackup(currentApp, stagedApp);

            await RunCheckedAsync(
                    new("hdiutil", ["detach", mountPoint]),
                    "macOS 디스크 이미지 분리",
                    cancellationToken)
                .ConfigureAwait(false);
            mounted = false;

            await RunCheckedAsync(
                    new("open", BuildMacOpenArguments(currentApp)),
                    "macOS 새 앱 실행",
                    cancellationToken)
                .ConfigureAwait(false);
            AppUpdateInstallResultStore.Write(
                AppUpdateInstallResultStore.GetResultPath(currentApp),
                new(
                    AppUpdateInstallResultStore.SucceededStatus,
                    packagePath,
                    currentApp,
                    backupPath,
                    ExistingInstallationRestored: false,
                    Error: null));
        }
        catch (Exception exception)
        {
            if (mounted)
            {
                await TryDetachAsync(mountPoint).ConfigureAwait(false);
            }

            if (backupPath is not null)
            {
                AppUpdateRollbackResult rollback = AppUpdateArchiveOperations.RollbackDirectory(
                    currentApp,
                    backupPath);
                bool relaunched = false;
                Exception? relaunchException = null;
                if (rollback.Restored)
                {
                    try
                    {
                        await RunCheckedAsync(
                                new("open", BuildMacOpenArguments(currentApp)),
                                "macOS 기존 앱 재실행",
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        relaunched = true;
                        backupPath = null;
                    }
                    catch (Exception launchFailure)
                    {
                        relaunchException = launchFailure;
                    }
                }

                TryWriteInstallResult(
                    AppUpdateInstallResultStore.GetResultPath(currentApp),
                    new(
                        relaunched
                            ? AppUpdateInstallResultStore.RolledBackStatus
                            : AppUpdateInstallResultStore.FailedStatus,
                        packagePath,
                        currentApp,
                        rollback.BackupPath,
                        rollback.Restored,
                        relaunchException?.Message ?? rollback.Error ?? exception.Message));
            }

            throw;
        }
        finally
        {
            AppUpdateArchiveOperations.TryDeleteDirectory(mountPoint);
            if (stagedApp is not null)
            {
                AppUpdateArchiveOperations.TryDeleteDirectory(stagedApp);
            }
        }
    }

    private async Task InstallUnixTarballAsync(
        string packagePath,
        bool macOS,
        CancellationToken cancellationToken)
    {
        string targetDirectory = macOS
            ? AppUpdatePathOperations.FindMacAppBundle(executablePath)
            : applicationDirectory;
        string parent = GetParentDirectory(targetDirectory, "tar.gz 설치 위치");
        string token = Guid.NewGuid().ToString("N");
        string stagingRoot = Path.Combine(parent, $".yttstudio-update-{token}");
        string helperPath = Path.Combine(parent, $".yttstudio-update-{token}.sh");
        bool helperStarted = false;
        try
        {
            Directory.CreateDirectory(stagingRoot);
            await AppUpdateArchiveOperations.ExtractAsync(
                    packagePath,
                    stagingRoot,
                    cancellationToken)
                .ConfigureAwait(false);
            string replacementDirectory;
            string relativeExecutable;
            if (macOS)
            {
                // macOS tar.gz는 디스크 이미지 없이 .app을 배포하는 포터블 경로다.
                replacementDirectory = AppUpdatePathOperations.FindSingleAppBundle(stagingRoot);
                relativeExecutable = AppUpdateArchiveOperations.RequireFile(
                    replacementDirectory,
                    Path.Combine("Contents", "MacOS", "YttStudio.App"));
            }
            else
            {
                replacementDirectory = stagingRoot;
                relativeExecutable = AppUpdateArchiveOperations.RequireFile(
                    replacementDirectory,
                    "YttStudio.App");
                AppUpdatePathOperations.MakeExecutable(
                    Path.Combine(replacementDirectory, relativeExecutable));
            }

            string script = AppUpdateInstallHelpers.BuildUnixDirectoryReplacementScript(
                currentProcessId,
                targetDirectory,
                replacementDirectory,
                relativeExecutable,
                helperPath,
                packagePath,
                AppUpdateInstallResultStore.GetResultPath(targetDirectory));
            await File.WriteAllTextAsync(
                    helperPath,
                    script,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);
            AppUpdateProcessResult result = await RunProcessAsync(
                    new("/bin/sh", [helperPath], WorkingDirectory: parent),
                    waitForExit: false,
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureStarted(result, "tar.gz 교체 helper");
            helperStarted = true;
        }
        finally
        {
            if (!helperStarted)
            {
                AppUpdateArchiveOperations.TryDeleteDirectory(stagingRoot);
                AppUpdateArchiveOperations.TryDeleteFile(helperPath);
            }
        }
    }

    private async Task InstallLinuxAppImageAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        string? appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (string.IsNullOrWhiteSpace(appImagePath))
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationUnsupported,
                "APPIMAGE 실행 경로를 확인하지 못했다.");
        }

        string targetPath = Path.GetFullPath(appImagePath);
        if (!File.Exists(targetPath))
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                $"현재 AppImage 파일이 없다: {targetPath}");
        }

        string parent = GetParentDirectory(targetPath, "AppImage 설치 위치");
        string token = Guid.NewGuid().ToString("N");
        string stagingPath = Path.Combine(parent, $".yttstudio-update-{token}.AppImage");
        string helperPath = Path.Combine(parent, $".yttstudio-update-{token}.sh");
        bool helperStarted = false;
        try
        {
            File.Copy(packagePath, stagingPath, overwrite: false);
            AppUpdatePathOperations.MakeExecutable(stagingPath);
            string script = AppUpdateInstallHelpers.BuildUnixFileReplacementScript(
                currentProcessId,
                targetPath,
                stagingPath,
                helperPath,
                packagePath,
                AppUpdateInstallResultStore.GetResultPath(targetPath));
            await File.WriteAllTextAsync(
                    helperPath,
                    script,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);
            AppUpdateProcessResult result = await RunProcessAsync(
                    new("/bin/sh", [helperPath], WorkingDirectory: parent),
                    waitForExit: false,
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureStarted(result, "Linux AppImage 교체 helper");
            helperStarted = true;
        }
        finally
        {
            if (!helperStarted)
            {
                AppUpdateArchiveOperations.TryDeleteFile(stagingPath);
                AppUpdateArchiveOperations.TryDeleteFile(helperPath);
            }
        }
    }

    private async Task<AppUpdateProcessResult> RunProcessAsync(
        AppUpdateProcessRequest request,
        bool waitForExit,
        CancellationToken cancellationToken)
    {
        AppUpdateProcessRequest effectiveRequest = request with { WaitForExit = waitForExit };
        try
        {
            return await processRunner
                .RunAsync(effectiveRequest, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AppUpdateException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                $"{request.FileName} 프로세스를 시작하지 못했다.",
                exception);
        }
    }

    private async Task RunCheckedAsync(
        AppUpdateProcessRequest request,
        string operation,
        CancellationToken cancellationToken)
    {
        AppUpdateProcessResult result = await RunProcessAsync(
                request with { WaitForExit = true },
                waitForExit: true,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureStarted(result, operation);
        if (result.ExitCode is not 0)
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                $"{operation}이 종료 코드 {result.ExitCode}로 실패했다.");
        }
    }

    private static void EnsureStarted(AppUpdateProcessResult result, string operation)
    {
        if (!result.Started)
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                $"{operation}을 시작하지 못했다.");
        }
    }

    private static void TryWriteInstallResult(
        string resultPath,
        AppUpdateInstallResult result)
    {
        try
        {
            AppUpdateInstallResultStore.Write(resultPath, result);
        }
        catch (Exception exception)
        {
            Serilog.Log.Error(
                exception,
                "업데이트 설치 결과를 기록하지 못했다: {ResultPath}",
                resultPath);
        }
    }

    internal static IReadOnlyList<string> BuildMacOpenArguments(string appPath)
        => ["-n", appPath];

    private async Task TryDetachAsync(string mountPoint)
    {
        try
        {
            AppUpdateProcessResult result = await RunProcessAsync(
                    new("hdiutil", ["detach", mountPoint]),
                    waitForExit: true,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!result.Started || result.ExitCode is not 0)
            {
                Serilog.Log.Warning(
                    "macOS 디스크 이미지 분리 helper가 종료 코드 {ExitCode}로 끝났다: {MountPoint}",
                    result.ExitCode,
                    mountPoint);
            }
        }
        catch (Exception exception)
        {
            Serilog.Log.Warning(exception, "macOS 디스크 이미지 분리에 실패했다: {MountPoint}", mountPoint);
        }
    }

    private static string ValidateDownloadedPackage(
        string downloadedPath,
        AppUpdateExecutionForm executionForm)
    {
        if (string.IsNullOrWhiteSpace(downloadedPath))
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                "다운로드된 업데이트 경로가 비어 있다.");
        }

        string path = Path.GetFullPath(downloadedPath);
        if (!File.Exists(path))
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                $"다운로드된 업데이트 파일이 없다: {path}");
        }

        string extension = Path.GetExtension(path);
        bool valid = executionForm switch
        {
            AppUpdateExecutionForm.Installed => extension.Equals(".exe", StringComparison.OrdinalIgnoreCase),
            AppUpdateExecutionForm.Portable => extension.Equals(".zip", StringComparison.OrdinalIgnoreCase),
            AppUpdateExecutionForm.AppImage => extension.Equals(".AppImage", StringComparison.OrdinalIgnoreCase),
            AppUpdateExecutionForm.TarGz => path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
        if (!valid)
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationUnsupported,
                $"실행 형태와 자산 확장자가 맞지 않다: {executionForm}/{Path.GetFileName(path)}");
        }

        return path;
    }

    private static string GetParentDirectory(string path, string description)
        => Directory.GetParent(path)?.FullName
            ?? throw new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                $"{description}의 상위 경로를 확인하지 못했다.");

    private static void EnsureDirectory(string path, string description)
    {
        if (!Directory.Exists(path))
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                $"{description}가 없다: {path}");
        }
    }
}
