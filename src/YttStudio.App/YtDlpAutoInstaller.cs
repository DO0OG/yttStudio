using System.Runtime.InteropServices;
using System.Security.Cryptography;
using YttStudio.Video;

namespace YttStudio.App;

/// <summary>
/// YouTube URL 프리뷰에 필요한 yt-dlp를 사용자가 별도로 준비하지 않았을 때
/// 공식 yt-dlp GitHub 릴리스에서 고정된 바이너리를 내려받아 사용자 영역에 설치한다.
/// yttStudio 릴리스 패키지에는 yt-dlp 바이너리를 포함하지 않는다.
/// </summary>
internal sealed class YtDlpAutoInstaller
{
    internal const string PinnedVersion = "2026.08.19";

    private const string WindowsAssetName = "yt-dlp.exe";
    private const string MacAssetName = "yt-dlp_macos";
    private const string LinuxAssetName = "yt-dlp_linux";

    private const string WindowsSha256 = "66674953fe251b89f4d08c5f0e35e0728679bd67ab3d7d05c0562af101dd3e7a";
    private const string MacSha256 = "0f192b7ec147ab6288885d6351d9ab67367640029b4377576ef46dd79cf7b202";
    private const string LinuxSha256 = "58162f9bfdc27458ea47bfcb311cf47028f17d8154a8bf7d689861d46399230a";

    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(2),
    };

    private static readonly SemaphoreSlim InstallationGate = new(1, 1);

    private readonly HttpClient httpClient;
    private readonly string installDirectory;

    internal YtDlpAutoInstaller(HttpClient? httpClient = null, string? installDirectory = null)
    {
        this.httpClient = httpClient ?? SharedHttpClient;
        this.installDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(installDirectory)
                ? GetDefaultInstallDirectory()
                : installDirectory);
    }

    /// <summary>
    /// 기존 설치본을 우선 사용하고, 찾지 못한 경우에만 공식 릴리스에서 설치한다.
    /// 반환된 경로는 현재 프로세스의 yt-dlp override로도 등록한다.
    /// </summary>
    public async Task<string> EnsureAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (YtDlpLocator.TryFind(out string? existing, out _)
            && !string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        Asset asset = GetCurrentAsset();
        await InstallationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (YtDlpLocator.TryFind(out existing, out _)
                && !string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }

            Directory.CreateDirectory(installDirectory);
            string destination = Path.Combine(installDirectory, GetInstalledFileName());
            if (File.Exists(destination)
                && await HasExpectedHashAsync(destination, asset.Sha256, cancellationToken)
                    .ConfigureAwait(false))
            {
                MakeExecutable(destination);
                SetOverride(destination);
                return destination;
            }

            string temporary = destination + ".download";
            try
            {
                await DownloadAsync(asset, temporary, cancellationToken).ConfigureAwait(false);
                if (!await HasExpectedHashAsync(temporary, asset.Sha256, cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw new InvalidDataException("다운로드한 yt-dlp의 SHA-256이 고정된 값과 일치하지 않습니다.");
                }

                MakeExecutable(temporary);
                File.Move(temporary, destination, overwrite: true);
                MakeExecutable(destination);
                SetOverride(destination);
                return destination;
            }
            finally
            {
                TryDelete(temporary);
            }
        }
        finally
        {
            InstallationGate.Release();
        }
    }

    internal static string GetDefaultInstallDirectory()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(root, "yttStudio", "tools", "yt-dlp", PinnedVersion);
    }

    private async Task DownloadAsync(Asset asset, string destination, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, asset.Uri);
        request.Headers.UserAgent.ParseAdd("yttStudio/1.0");
        using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using FileStream target = new(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> HasExpectedHashAsync(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(hash), expectedSha256,
            StringComparison.OrdinalIgnoreCase);
    }

    private static Asset GetCurrentAsset()
    {
        string assetName;
        string sha256;
        if (OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64)
        {
            assetName = WindowsAssetName;
            sha256 = WindowsSha256;
        }
        else if (OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64)
        {
            assetName = MacAssetName;
            sha256 = MacSha256;
        }
        else if (OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64)
        {
            assetName = LinuxAssetName;
            sha256 = LinuxSha256;
        }
        else
        {
            throw new PlatformNotSupportedException(
                "이 플랫폼에는 yttStudio의 자동 yt-dlp 설치 바이너리가 준비되어 있지 않습니다. PATH 또는 YTTSTUDIO_YTDLP_PATH로 직접 설치한 yt-dlp를 지정하세요.");
        }

        return new Asset(
            new Uri($"https://github.com/yt-dlp/yt-dlp/releases/download/{PinnedVersion}/{assetName}"),
            sha256);
    }

    private static string GetInstalledFileName()
        => OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";

    private static void SetOverride(string path)
        => Environment.SetEnvironmentVariable("YTTSTUDIO_YTDLP_PATH", path);

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record Asset(Uri Uri, string Sha256);
}
