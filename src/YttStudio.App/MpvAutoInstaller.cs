using System.Runtime.InteropServices;

namespace YttStudio.App;

/// <summary>자동 설치가 실패한 이유를 호출자가 구분할 수 있게 한다.</summary>
public enum MpvAutoInstallErrorKind
{
    UnsupportedPlatform,
    ReleaseMetadataRequestFailed,
    ReleaseAssetNotFound,
    DownloadFailed,
    ArchiveExtractionFailed,
    LibraryNotFound,
    InstallationFailed,
}

/// <summary>libmpv 자동 설치 과정에서 발생한 명시적 오류다.</summary>
public class MpvAutoInstallException : Exception
{
    public MpvAutoInstallException(
        MpvAutoInstallErrorKind kind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public MpvAutoInstallErrorKind Kind { get; }
}

/// <summary>자동 설치 진행 단계다.</summary>
public enum MpvInstallStage
{
    FetchingRelease,
    DownloadingArchive,
    ExtractingArchive,
    Installing,
    Completed,
}

/// <summary>자동 설치 호출자가 화면에 표시할 진행 정보다.</summary>
public sealed record MpvInstallProgress(
    MpvInstallStage Stage,
    string? AssetName,
    long BytesTransferred,
    long? TotalBytes)
{
    /// <summary>전체 크기를 알 수 있을 때 0부터 1 사이의 진행률을 반환한다.</summary>
    public double? Fraction
        => TotalBytes is > 0
            ? Math.Clamp((double)BytesTransferred / TotalBytes.Value, 0, 1)
            : null;
}

/// <summary>패키지 매니저 안내를 만들 때 사용할 운영체제 구분이다.</summary>
public enum MpvPackagePlatform
{
    Windows,
    MacOS,
    Linux,
    Other,
}

/// <summary>macOS 또는 Linux에서 사용자가 실행할 libmpv 설치 명령 안내다.</summary>
public sealed record MpvPackageInstallInstructions(
    MpvPackagePlatform Platform,
    IReadOnlyList<string> Commands,
    string DocumentationUrl)
{
    /// <summary>현재 플랫폼에서 자동 설치 백엔드를 사용할 수 있는지 나타낸다.</summary>
    public bool SupportsAutomaticInstallation => Platform == MpvPackagePlatform.Windows;
}

/// <summary>
/// Windows에서 공식 mpv 빌드의 최신 libmpv 개발 아카이브를 내려받아
/// 사용자 로컬 디렉터리에 설치한다. 저장소나 앱 배포물에는 바이너리를 넣지 않는다.
/// </summary>
public sealed class MpvAutoInstaller
{
    public const string GitHubLatestReleaseUrl =
        "https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/latest";

    public const string GithubLatestReleaseUrl = GitHubLatestReleaseUrl;

    public const string WindowsLibraryFileName = "libmpv-2.dll";

    public const string WindowsAssetPrefix = "mpv-dev-x86_64-";
    /// <summary>설치할 mpv 빌드의 릴리즈 태그다.</summary>
    /// <remarks>
    /// 최신 릴리즈를 따라가지 않고 특정 빌드에 못박는다. 내려받은 바이너리는 압축을 풀어
    /// 이 프로세스에 로드된다. 무엇이 올지 모르는 채로 그 일을 할 수는 없다. HTTPS 는 전송만
    /// 보호하고 산출물은 보증하지 않으므로 아래 해시로 확인한다.
    ///
    /// 버전을 올리려면 새 자산을 받아 SHA-256 을 다시 계산해 세 상수를 함께 고쳐야 한다.
    /// 번거롭지만 검증되지 않은 네이티브 코드를 로드하는 것보다 낫다. 지우지 마라.
    /// </remarks>
    public const string PinnedReleaseTag = "20260828";

    /// <summary>고정한 릴리즈에서 받을 자산 이름이다.</summary>
    public const string PinnedAssetName = "mpv-dev-x86_64-20260828-git-182fa6ca49.7z";

    /// <summary>고정한 자산의 SHA-256 이다. 받아서 직접 계산한 값이다.</summary>
    public const string PinnedAssetSha256 =
        "9efd04d351e09eca350d01da1b8b0c406537c037537111ba65ab43c91905635b";

    /// <summary>고정한 자산의 바이트 수다. 해시를 계산하기 전에 먼저 걸러낸다.</summary>
    public const long PinnedAssetLength = 31354441;

    /// <summary>다운로드를 허용하는 호스트다. 리디렉션이 이 밖으로 나가면 거부한다.</summary>
    public static readonly string[] AllowedDownloadHosts =
    [
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    ];

    /// <summary>고정한 자산의 내려받기 주소다.</summary>
    public static Uri PinnedAssetUri { get; } = new(
        $"https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/{PinnedReleaseTag}/{PinnedAssetName}");


    private const string DocumentationUrl = "https://mpv.io/installation/";
    private static readonly HttpClient SharedHttpClient = new();
    private static readonly SemaphoreSlim InstallationGate = new(1, 1);

    private readonly HttpClient httpClient;

    /// <summary>
    /// <paramref name="httpClient"/>를 지정하면 테스트나 호출자 소유의 전송 계층을 사용한다.
    /// 지정하지 않으면 앱 전체에서 공유하는 HttpClient를 사용한다.
    /// </summary>
    public MpvAutoInstaller(HttpClient? httpClient = null, string? installDirectory = null)
    {
        this.httpClient = httpClient ?? SharedHttpClient;
        string directory = string.IsNullOrWhiteSpace(installDirectory)
            ? MpvInstallerFileStore.GetDefaultInstallDirectory()
            : installDirectory!;
        InstallDirectory = Path.GetFullPath(directory);
    }

    /// <summary>압축을 풀고 설치할 사용자 로컬 디렉터리다.</summary>
    public string InstallDirectory { get; }

    /// <summary>현재 프로세스가 요구하는 Windows x64 자동 설치를 지원하는지 나타낸다.</summary>
    public static bool IsWindowsInstallationSupported
        => OperatingSystem.IsWindows()
            && Environment.Is64BitOperatingSystem
            && RuntimeInformation.OSArchitecture == Architecture.X64;

    /// <summary>
    /// 최신 Windows 빌드를 설치하고 설치된 <c>libmpv-2.dll</c>의 절대 경로를 반환한다.
    /// 취소 시 <see cref="OperationCanceledException"/>을 그대로 전달한다.
    /// </summary>
    public async Task<string> InstallAsync(
        IProgress<MpvInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWindowsInstallationSupported();
        await InstallationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await InstallAfterGateAsync(progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>현재 운영체제에 맞는 수동 패키지 설치 안내를 반환한다.</summary>
    public static MpvPackageInstallInstructions GetPackageManagerInstructions()
    {
        MpvPackagePlatform platform = OperatingSystem.IsMacOS()
            ? MpvPackagePlatform.MacOS
            : OperatingSystem.IsLinux()
                ? MpvPackagePlatform.Linux
                : OperatingSystem.IsWindows()
                    ? MpvPackagePlatform.Windows
                    : MpvPackagePlatform.Other;
        return GetPackageManagerInstructions(platform);
    }

    /// <summary>지정한 운영체제의 패키지 매니저 명령 안내를 반환한다.</summary>
    public static MpvPackageInstallInstructions GetPackageManagerInstructions(
        MpvPackagePlatform platform)
        => platform switch
        {
            MpvPackagePlatform.MacOS => new(platform, ["brew install mpv"], DocumentationUrl),
            MpvPackagePlatform.Linux => new(
                platform,
                ["sudo apt install libmpv2", "sudo dnf install mpv-libs", "sudo pacman -S mpv"],
                DocumentationUrl),
            MpvPackagePlatform.Windows => new(platform, [], DocumentationUrl),
            _ => new(platform, [], DocumentationUrl),
        };

    private async Task<string> InstallAfterGateAsync(
        IProgress<MpvInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        MpvInstallerWorkspace? workspace = null;
        try
        {
            workspace = MpvInstallerWorkspace.Create(InstallDirectory);
            Report(progress, new(MpvInstallStage.FetchingRelease, null, 0, null));
            GitHubAsset asset = await MpvInstallerTransport
                .GetLatestAssetAsync(httpClient, cancellationToken)
                .ConfigureAwait(false);
            await DownloadAndExtractAsync(workspace, asset, progress, cancellationToken)
                .ConfigureAwait(false);
            return await CommitAsync(workspace, asset, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MpvAutoInstallException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new MpvAutoInstallException(
                MpvAutoInstallErrorKind.InstallationFailed,
                "libmpv 설치에 실패했습니다.",
                exception);
        }
        finally
        {
            workspace?.Cleanup();
            InstallationGate.Release();
        }
    }

    private async Task DownloadAndExtractAsync(
        MpvInstallerWorkspace workspace,
        GitHubAsset asset,
        IProgress<MpvInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        await MpvInstallerTransport.DownloadArchiveAsync(
                httpClient,
                asset,
                workspace.ArchivePath,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        workspace.CreateStagingDirectory();
        Report(progress, new(MpvInstallStage.ExtractingArchive, asset.Name, 0, null));
        await MpvArchiveExtractor.ExtractAsync(
                workspace.StagingDirectory,
                asset.Name,
                workspace.ArchivePath,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static Task<string> CommitAsync(
        MpvInstallerWorkspace workspace,
        GitHubAsset asset,
        IProgress<MpvInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, new(MpvInstallStage.Installing, asset.Name, 0, null));
        string relativeLibraryPath = MpvArchiveExtractor.FindLibraryRelativePath(
            workspace.StagingDirectory);
        string installedPath = MpvInstallerFileStore.CommitInstallation(
            workspace.StagingDirectory,
            relativeLibraryPath,
            workspace.InstallDirectory);
        workspace.MarkCommitted();
        Report(progress, new(MpvInstallStage.Completed, asset.Name, 1, 1));
        return Task.FromResult(installedPath);
    }

    private static void EnsureWindowsInstallationSupported()
    {
        if (!IsWindowsInstallationSupported)
        {
            throw new MpvAutoInstallException(
                MpvAutoInstallErrorKind.UnsupportedPlatform,
                "libmpv 자동 설치는 Windows x64에서만 지원됩니다.");
        }
    }

    private static void Report(
        IProgress<MpvInstallProgress>? progress,
        MpvInstallProgress value)
        => progress?.Report(value);
}
