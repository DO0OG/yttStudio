using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SharpCompress.Archives;

namespace YttStudio.App;

/// <summary>libmpv 자동 설치가 실패한 이유를 호출자가 구분할 수 있게 한다.</summary>
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
    public double? Fraction
        => TotalBytes is > 0
            ? Math.Clamp((double)BytesTransferred / TotalBytes.Value, 0, 1)
            : null;
}

public enum MpvPackagePlatform
{
    Windows,
    MacOS,
    Linux,
    Other,
}

public sealed record MpvPackageInstallInstructions(
    MpvPackagePlatform Platform,
    IReadOnlyList<string> Commands,
    string DocumentationUrl)
{
    public bool SupportsAutomaticInstallation
        => Platform is MpvPackagePlatform.Windows or MpvPackagePlatform.MacOS or MpvPackagePlatform.Linux;
}

internal sealed record MpvRuntimePackage(
    string InstallId,
    string AssetName,
    Uri DownloadUri,
    string Sha256,
    long AssetLength,
    string? EntryPrefix,
    IReadOnlyList<string> LibraryNames,
    string UpstreamUrl,
    string CorrespondingSourceUrl);

/// <summary>
/// 지원 데스크톱 플랫폼에서 라이선스 상태가 명확한 libmpv 런타임을 사용자 영역에 설치한다.
/// yttStudio 배포물에는 libmpv 바이너리를 포함하지 않는다.
/// </summary>
public sealed class MpvAutoInstaller
{
    public const string GitHubLatestReleaseUrl =
        "https://api.github.com/repos/zhongfly/mpv-winbuild/releases/latest";
    public const string GithubLatestReleaseUrl = GitHubLatestReleaseUrl;
    public const string WindowsLibraryFileName = "libmpv-2.dll";
    public const string WindowsAssetPrefix = "mpv-dev-lgpl-x86_64-";

    public const string PinnedReleaseTag = "2026-08-29-e8673660ab";
    public const string PinnedAssetName = "mpv-dev-lgpl-x86_64-20260829-git-e8673660ab.7z";
    public const string PinnedAssetSha256 =
        "78260166265fbc09b3bee75ee3464eb0f6bbaa8ecd172786e33c22bbf8a3cb47";
    public const long PinnedAssetLength = 27_984_604;

    internal const string KMediaVersion = "0.2.9";
    internal const string KMediaAssetName = "kmedia-mpv-0.2.9-runtime-desktop.jar";
    internal const string KMediaAssetSha256 =
        "4250b47144de085c7963f4bdbe99e995b9b2b0374e32a14ebe9d27fd38a67bef";
    internal const long KMediaAssetLength = 25_946_200;

    private const int BufferSize = 128 * 1024;
    private const long MaximumArchiveBytes = 512L * 1024 * 1024;
    private const long MaximumExtractedBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumEntryCount = 20_000;
    private const string DocumentationUrl = "https://mpv.io/installation/";
    private const string KMediaReleaseBase = "https://github.com/Shusek/KMediaMpv/releases/download/v0.2.9";
    private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
    });
    private static readonly SemaphoreSlim InstallationGate = new(1, 1);

    public static readonly string[] AllowedDownloadHosts =
    [
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    ];

    public static Uri PinnedAssetUri { get; } = new(
        $"https://github.com/zhongfly/mpv-winbuild/releases/download/{PinnedReleaseTag}/{PinnedAssetName}");

    private readonly HttpClient httpClient;

    public MpvAutoInstaller(HttpClient? httpClient = null, string? installDirectory = null)
    {
        this.httpClient = httpClient ?? SharedHttpClient;
        MpvRuntimePackage? package = TryGetCurrentPackage();
        string defaultDirectory = package is null
            ? Path.Combine(GetInstallRoot(), "unsupported")
            : Path.Combine(GetInstallRoot(), package.InstallId);
        InstallDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(installDirectory) ? defaultDirectory : installDirectory);
    }

    public string InstallDirectory { get; }

    /// <summary>현재 운영체제와 아키텍처에서 내부 자동 설치를 제공하는지 나타낸다.</summary>
    public static bool IsAutomaticInstallationSupported => TryGetCurrentPackage() is not null;

    /// <summary>
    /// 이전 호출부와의 호환 이름이다. 현재는 Windows뿐 아니라 지원 데스크톱 플랫폼 전체를 뜻한다.
    /// </summary>
    public static bool IsWindowsInstallationSupported => IsAutomaticInstallationSupported;

    public async Task<string> InstallAsync(
        IProgress<MpvInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        MpvRuntimePackage package = GetCurrentPackage();
        await InstallationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryFindInstalledLibrary(InstallDirectory, package, out string? installedPath))
            {
                progress?.Report(new(MpvInstallStage.Completed, package.AssetName, 1, 1));
                return installedPath!;
            }

            return await InstallPackageAsync(package, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            InstallationGate.Release();
        }
    }

    /// <summary>이미 내려받은 검증 런타임이 있으면 네트워크 없이 경로를 돌려준다.</summary>
    public static bool TryFindInstalledLibrary(out string? libraryPath)
    {
        MpvRuntimePackage? package = TryGetCurrentPackage();
        if (package is null)
        {
            libraryPath = null;
            return false;
        }

        string directory = Path.Combine(GetInstallRoot(), package.InstallId);
        return TryFindInstalledLibrary(directory, package, out libraryPath);
    }

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

    public static MpvPackageInstallInstructions GetPackageManagerInstructions(MpvPackagePlatform platform)
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

    private async Task<string> InstallPackageAsync(
        MpvRuntimePackage package,
        IProgress<MpvInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        string? parent = Path.GetDirectoryName(InstallDirectory);
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw Failure(MpvAutoInstallErrorKind.InstallationFailed, "libmpv 설치 경로를 확인할 수 없습니다.");
        }

        Directory.CreateDirectory(parent);
        string workspace = Path.Combine(parent, $".libmpv-install-{Guid.NewGuid():N}");
        string archivePath = Path.Combine(workspace, package.AssetName);
        string stagingPath = Path.Combine(workspace, "staging");
        Directory.CreateDirectory(workspace);
        try
        {
            progress?.Report(new(MpvInstallStage.FetchingRelease, package.AssetName, 0, package.AssetLength));
            await DownloadAsync(package, archivePath, progress, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(stagingPath);
            await ExtractAsync(package, archivePath, stagingPath, progress, cancellationToken).ConfigureAwait(false);
            string libraryPath = FindLibrary(stagingPath, package);
            WriteProvenance(stagingPath, package);
            progress?.Report(new(MpvInstallStage.Installing, package.AssetName, 0, null));
            string relativeLibraryPath = Path.GetRelativePath(stagingPath, libraryPath);
            CommitInstallation(stagingPath, InstallDirectory);
            string installedPath = Path.GetFullPath(Path.Combine(InstallDirectory, relativeLibraryPath));
            progress?.Report(new(MpvInstallStage.Completed, package.AssetName, 1, 1));
            return installedPath;
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    private async Task DownloadAsync(
        MpvRuntimePackage package,
        string destinationPath,
        IProgress<MpvInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, package.DownloadUri);
        request.Headers.UserAgent.ParseAdd("yttStudio/0.2.3");
        using HttpResponseMessage response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw Failure(MpvAutoInstallErrorKind.DownloadFailed,
                $"libmpv 런타임 다운로드에 실패했습니다: HTTP {(int)response.StatusCode}");
        }

        Uri finalUri = response.RequestMessage?.RequestUri ?? package.DownloadUri;
        if (finalUri.Scheme != Uri.UriSchemeHttps ||
            !AllowedDownloadHosts.Contains(finalUri.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw Failure(MpvAutoInstallErrorKind.DownloadFailed,
                $"허용하지 않은 다운로드 호스트입니다: {finalUri.Host}");
        }

        long? contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > MaximumArchiveBytes ||
            contentLength is long exactLength && exactLength != package.AssetLength)
        {
            throw Failure(MpvAutoInstallErrorKind.DownloadFailed, "libmpv 런타임 파일 크기가 예상과 다릅니다.");
        }

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream output = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[BufferSize];
        long transferred = 0;
        while (true)
        {
            int read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            transferred = checked(transferred + read);
            if (transferred > MaximumArchiveBytes || transferred > package.AssetLength)
            {
                throw Failure(MpvAutoInstallErrorKind.DownloadFailed, "libmpv 런타임 파일이 예상 크기를 초과했습니다.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            progress?.Report(new(MpvInstallStage.DownloadingArchive, package.AssetName, transferred, package.AssetLength));
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (transferred != package.AssetLength)
        {
            throw Failure(MpvAutoInstallErrorKind.DownloadFailed, "libmpv 런타임 파일 크기가 예상과 다릅니다.");
        }

        await using FileStream verifyStream = new(destinationPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = await SHA256.HashDataAsync(verifyStream, cancellationToken).ConfigureAwait(false);
        string actualHash = Convert.ToHexStringLower(hash);
        if (!string.Equals(actualHash, package.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(MpvAutoInstallErrorKind.DownloadFailed, "libmpv 런타임 SHA-256 검증에 실패했습니다.");
        }
    }

    private static async Task ExtractAsync(
        MpvRuntimePackage package,
        string archivePath,
        string stagingPath,
        IProgress<MpvInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using IArchive archive = ArchiveFactory.OpenArchive(archivePath);
            long extractedBytes = 0;
            int entryCount = 0;
            foreach (IArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++entryCount > MaximumEntryCount)
                {
                    throw Failure(MpvAutoInstallErrorKind.ArchiveExtractionFailed, "압축 항목 수가 제한을 초과했습니다.");
                }

                string key = (entry.Key ?? string.Empty).Replace('\\', '/');
                if (!TryGetRelativeEntry(package, key, out string relativePath))
                {
                    continue;
                }

                string destinationPath = GetSafeDestinationPath(stagingPath, relativePath);
                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                string? directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await using Stream input = entry.OpenEntryStream();
                await using FileStream output = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                byte[] buffer = new byte[BufferSize];
                while (true)
                {
                    int read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    extractedBytes = checked(extractedBytes + read);
                    if (extractedBytes > MaximumExtractedBytes)
                    {
                        throw Failure(MpvAutoInstallErrorKind.ArchiveExtractionFailed,
                            "압축 해제 크기가 제한을 초과했습니다.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                progress?.Report(new(MpvInstallStage.ExtractingArchive, package.AssetName, extractedBytes, null));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MpvAutoInstallException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw Failure(MpvAutoInstallErrorKind.ArchiveExtractionFailed, "libmpv 런타임 압축을 해제하지 못했습니다.", exception);
        }
    }

    private static bool TryGetRelativeEntry(MpvRuntimePackage package, string key, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith('/', StringComparison.Ordinal))
        {
            return false;
        }

        if (package.EntryPrefix is null)
        {
            relativePath = key;
            return true;
        }

        if (!key.StartsWith(package.EntryPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        relativePath = key[package.EntryPrefix.Length..];
        return relativePath.Length > 0;
    }

    private static string GetSafeDestinationPath(string stagingPath, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw Failure(MpvAutoInstallErrorKind.ArchiveExtractionFailed, "절대 경로 압축 항목을 차단했습니다.");
        }

        string root = Path.GetFullPath(stagingPath) + Path.DirectorySeparatorChar;
        string destination = Path.GetFullPath(Path.Combine(stagingPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!destination.StartsWith(root, comparison))
        {
            throw Failure(MpvAutoInstallErrorKind.ArchiveExtractionFailed, "설치 영역을 벗어나는 압축 항목을 차단했습니다.");
        }

        return destination;
    }

    private static string FindLibrary(string directory, MpvRuntimePackage package)
    {
        foreach (string name in package.LibraryNames)
        {
            string? match = Directory.EnumerateFiles(directory, name, SearchOption.AllDirectories).FirstOrDefault();
            if (match is not null)
            {
                return match;
            }
        }

        throw Failure(MpvAutoInstallErrorKind.LibraryNotFound, "설치 자산에서 호환되는 libmpv를 찾지 못했습니다.");
    }

    private static bool TryFindInstalledLibrary(
        string directory,
        MpvRuntimePackage package,
        out string? libraryPath)
    {
        if (Directory.Exists(directory))
        {
            foreach (string name in package.LibraryNames)
            {
                libraryPath = Directory.EnumerateFiles(directory, name, SearchOption.AllDirectories).FirstOrDefault();
                if (libraryPath is not null)
                {
                    return true;
                }
            }
        }

        libraryPath = null;
        return false;
    }

    private static void WriteProvenance(string stagingPath, MpvRuntimePackage package)
    {
        string text = $"""
            yttStudio libmpv runtime provenance

            Asset: {package.AssetName}
            SHA-256: {package.Sha256}
            Upstream: {package.UpstreamUrl}
            Corresponding source: {package.CorrespondingSourceUrl}

            This runtime remains subject to its upstream license. yttStudio does not relicense it under MIT.
            """;
        File.WriteAllText(Path.Combine(stagingPath, "YTTSTUDIO-RUNTIME-SOURCE.txt"), text);
    }

    private static void CommitInstallation(string stagingPath, string installDirectory)
    {
        string backupDirectory = installDirectory + $".backup-{Guid.NewGuid():N}";
        bool movedExisting = false;
        try
        {
            if (Directory.Exists(installDirectory))
            {
                Directory.Move(installDirectory, backupDirectory);
                movedExisting = true;
            }

            Directory.Move(stagingPath, installDirectory);
            if (movedExisting && Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (movedExisting && !Directory.Exists(installDirectory) && Directory.Exists(backupDirectory))
            {
                try
                {
                    Directory.Move(backupDirectory, installDirectory);
                }
                catch (Exception restoreException) when (restoreException is IOException or UnauthorizedAccessException)
                {
                    Serilog.Log.Error(restoreException, "libmpv 기존 설치 복원 실패: {Path}", backupDirectory);
                }
            }

            throw Failure(MpvAutoInstallErrorKind.InstallationFailed, "libmpv 설치 디렉터리를 교체하지 못했습니다.", exception);
        }
    }

    private static MpvRuntimePackage GetCurrentPackage()
        => TryGetCurrentPackage() ?? throw Failure(
            MpvAutoInstallErrorKind.UnsupportedPlatform,
            "이 운영체제 또는 아키텍처에서는 libmpv 자동 설치를 지원하지 않습니다.");

    private static MpvRuntimePackage? TryGetCurrentPackage()
    {
        if (OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64)
        {
            return new(
                $"windows-x64-{PinnedReleaseTag}",
                PinnedAssetName,
                PinnedAssetUri,
                PinnedAssetSha256,
                PinnedAssetLength,
                null,
                ["libmpv-2.dll", "mpv-2.dll"],
                $"https://github.com/zhongfly/mpv-winbuild/releases/tag/{PinnedReleaseTag}",
                "https://github.com/zhongfly/mpv-winbuild");
        }

        if (OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64)
        {
            return CreateKMediaPackage(
                "macos-arm64",
                "META-INF/kmediampv/native/macos-aarch64/",
                ["libmpv.2.dylib", "libmpv.dylib"]);
        }

        if (OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64)
        {
            return CreateKMediaPackage(
                "linux-x64",
                "META-INF/kmediampv/native/linux-x86_64/",
                ["libmpv.so.2", "libmpv.so"]);
        }

        return null;
    }

    private static MpvRuntimePackage CreateKMediaPackage(
        string platformId,
        string entryPrefix,
        IReadOnlyList<string> libraryNames)
        => new(
            $"{platformId}-kmediampv-{KMediaVersion}",
            KMediaAssetName,
            new Uri($"{KMediaReleaseBase}/{KMediaAssetName}"),
            KMediaAssetSha256,
            KMediaAssetLength,
            entryPrefix,
            libraryNames,
            "https://github.com/Shusek/KMediaMpv/releases/tag/v0.2.9",
            $"{KMediaReleaseBase}/kmedia-mpv-0.2.9-corresponding-source.tar.gz");

    private static string GetInstallRoot()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YttStudio",
            "libmpv");

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(exception, "libmpv 임시 설치 경로 정리 실패: {Path}", path);
        }
    }

    private static MpvAutoInstallException Failure(
        MpvAutoInstallErrorKind kind,
        string message,
        Exception? exception = null)
        => new(kind, message, exception);
}
