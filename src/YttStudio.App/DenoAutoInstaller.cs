using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace YttStudio.App;

/// <summary>
/// YouTube 추출에 필요한 JavaScript challenge 실행용 Deno를 찾거나 사용자 영역에 설치한다.
/// yttStudio 배포물에는 Deno 바이너리를 포함하지 않는다.
/// </summary>
internal sealed class DenoAutoInstaller
{
    internal const string PinnedVersion = "2.9.6";
    private static readonly Version MinimumSupportedVersion = new(2, 3, 0);

    private const string WindowsAssetName = "deno-x86_64-pc-windows-msvc.zip";
    private const string MacAssetName = "deno-aarch64-apple-darwin.zip";
    private const string LinuxAssetName = "deno-x86_64-unknown-linux-gnu.zip";

    private const string WindowsSha256 = "15e5300b0ba3c3695a7621d90160a746ec9e710228cee639afa9d580f6e3cd11";
    private const string MacSha256 = "213a2f304f04d3c9cb5220669afad138f60a5aab1fe80962abdeb8f35807a472";
    private const string LinuxSha256 = "394f07f4da2bebe6ce6f1e7ce0fa16429b29b08c35e3fac3fe25972676dff4b2";

    private const long WindowsAssetLength = 42_601_047;
    private const long MacAssetLength = 38_446_106;
    private const long LinuxAssetLength = 41_582_794;
    private const long MaximumArchiveBytes = 128L * 1024 * 1024;
    private const long MaximumExecutableBytes = 256L * 1024 * 1024;
    private const int BufferSize = 128 * 1024;

    private static readonly string[] AllowedDownloadHosts =
    [
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    ];

    private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
    })
    {
        Timeout = TimeSpan.FromMinutes(2),
    };

    private static readonly SemaphoreSlim InstallationGate = new(1, 1);

    private readonly HttpClient httpClient;
    private readonly string installDirectory;

    internal DenoAutoInstaller(HttpClient? httpClient = null, string? installDirectory = null)
    {
        this.httpClient = httpClient ?? SharedHttpClient;
        this.installDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(installDirectory)
                ? GetDefaultInstallDirectory()
                : installDirectory);
    }

    /// <summary>
    /// 지원되는 기존 Deno가 있으면 우선 사용하고, 없으면 고정된 공식 릴리스 자산을 설치한다.
    /// 반환 경로는 yt-dlp 사전 확인과 libmpv ytdl_hook 자식 프로세스가 함께 찾을 수 있게
    /// 현재 프로세스 환경에도 등록한다.
    /// </summary>
    public async Task<string> EnsureAvailableAsync(CancellationToken cancellationToken = default)
    {
        string? existing = await FindSupportedExistingAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            RegisterRuntime(existing);
            return existing;
        }

        DenoAsset asset = GetCurrentAsset();
        await InstallationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            existing = await FindSupportedExistingAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                RegisterRuntime(existing);
                return existing;
            }

            string executablePath = Path.Combine(installDirectory, GetExecutableFileName());
            if (File.Exists(executablePath)
                && await IsSupportedDenoAsync(executablePath, cancellationToken).ConfigureAwait(false))
            {
                MakeExecutable(executablePath);
                RegisterRuntime(executablePath);
                return executablePath;
            }

            string parent = Path.GetDirectoryName(installDirectory)
                ?? throw new InvalidOperationException("Deno 설치 상위 경로를 확인할 수 없습니다.");
            Directory.CreateDirectory(parent);
            TryDeleteStaleWorkspaces(parent);
            string workspace = Path.Combine(parent, $".deno-install-{Guid.NewGuid():N}");
            string archivePath = Path.Combine(workspace, asset.AssetName);
            string stagingPath = Path.Combine(workspace, "staging");
            Directory.CreateDirectory(stagingPath);
            try
            {
                await DownloadAsync(asset, archivePath, cancellationToken).ConfigureAwait(false);
                string stagedExecutable = await ExtractExecutableAsync(
                    archivePath,
                    stagingPath,
                    cancellationToken).ConfigureAwait(false);
                MakeExecutable(stagedExecutable);
                if (!await IsSupportedDenoAsync(stagedExecutable, cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidDataException("설치한 Deno가 yt-dlp가 요구하는 최소 버전 2.3.0을 만족하지 않습니다.");
                }

                File.WriteAllText(
                    Path.Combine(stagingPath, "PROVENANCE.txt"),
                    $"upstream=https://github.com/denoland/deno\nversion={PinnedVersion}\nasset={asset.AssetName}\nsha256={asset.Sha256}\n");
                CommitInstallation(stagingPath, installDirectory);
                executablePath = Path.Combine(installDirectory, GetExecutableFileName());
                MakeExecutable(executablePath);
                RegisterRuntime(executablePath);
                return executablePath;
            }
            finally
            {
                TryDeleteDirectory(workspace);
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

        return Path.Combine(root, "yttStudio", "tools", "deno", PinnedVersion);
    }

    private async Task DownloadAsync(DenoAsset asset, string destinationPath, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, asset.DownloadUri);
        request.Headers.UserAgent.ParseAdd("yttStudio/0.2.5");
        using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Deno 런타임 다운로드에 실패했습니다: HTTP {(int)response.StatusCode}");
        }

        Uri finalUri = response.RequestMessage?.RequestUri ?? asset.DownloadUri;
        if (finalUri.Scheme != Uri.UriSchemeHttps
            || !AllowedDownloadHosts.Contains(finalUri.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"허용하지 않은 Deno 다운로드 호스트입니다: {finalUri.Host}");
        }

        long? contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > MaximumArchiveBytes
            || contentLength is long exactLength && exactLength != asset.AssetLength)
        {
            throw new InvalidDataException("Deno 런타임 파일 크기가 예상과 다릅니다.");
        }

        long transferred = 0;
        await using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (FileStream output = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            byte[] buffer = new byte[BufferSize];
            while (true)
            {
                int read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                transferred = checked(transferred + read);
                if (transferred > MaximumArchiveBytes || transferred > asset.AssetLength)
                {
                    throw new InvalidDataException("Deno 런타임 파일이 예상 크기를 초과했습니다.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (transferred != asset.AssetLength)
        {
            throw new InvalidDataException("Deno 런타임 파일 크기가 예상과 다릅니다.");
        }

        byte[] hash;
        await using (FileStream verify = new(destinationPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            hash = await SHA256.HashDataAsync(verify, cancellationToken).ConfigureAwait(false);
        }

        if (!string.Equals(Convert.ToHexStringLower(hash), asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Deno 런타임 SHA-256 검증에 실패했습니다.");
        }
    }

    private static async Task<string> ExtractExecutableAsync(
        string archivePath,
        string stagingPath,
        CancellationToken cancellationToken)
    {
        string executableName = GetExecutableFileName();
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        ZipArchiveEntry? executableEntry = archive.Entries.SingleOrDefault(entry =>
            string.Equals(entry.FullName.Replace('\\', '/'), executableName, StringComparison.Ordinal));
        if (executableEntry is null || executableEntry.Length <= 0 || executableEntry.Length > MaximumExecutableBytes)
        {
            throw new InvalidDataException("Deno 압축 파일에서 예상 실행 파일을 찾지 못했습니다.");
        }

        string destination = Path.GetFullPath(Path.Combine(stagingPath, executableName));
        string stagingRoot = Path.GetFullPath(stagingPath) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(stagingRoot, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Deno 압축 파일 경로가 설치 영역을 벗어납니다.");
        }

        await using Stream source = executableEntry.Open();
        await using FileStream target = new(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[BufferSize];
        long extracted = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            extracted = checked(extracted + read);
            if (extracted > MaximumExecutableBytes || extracted > executableEntry.Length)
            {
                throw new InvalidDataException("Deno 실행 파일 압축 해제 크기가 제한을 초과했습니다.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (extracted != executableEntry.Length)
        {
            throw new InvalidDataException("Deno 실행 파일 압축 해제 크기가 예상과 다릅니다.");
        }

        return destination;
    }

    private static async Task<string?> FindSupportedExistingAsync(CancellationToken cancellationToken)
    {
        IEnumerable<string> candidates = EnumerateCandidates();
        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate)
                && await IsSupportedDenoAsync(candidate, cancellationToken).ConfigureAwait(false))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        string? managed = Environment.GetEnvironmentVariable("YTTSTUDIO_DENO_PATH");
        if (!string.IsNullOrWhiteSpace(managed))
        {
            yield return managed;
        }

        string executableName = GetExecutableFileName();
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string normalized = directory.Trim('"');
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                yield return Path.Combine(normalized, executableName);
            }
        }
    }

    private static async Task<bool> IsSupportedDenoAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = path,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("--version");
            using Process process = new() { StartInfo = startInfo };
            if (!process.Start())
            {
                return false;
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            string output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode == 0 && TryReadVersion(output, out Version? version)
                && version >= MinimumSupportedVersion;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return false;
        }
    }

    private static bool TryReadVersion(string output, out Version? version)
    {
        version = null;
        string? firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.StartsWith("deno ", StringComparison.OrdinalIgnoreCase));
        if (firstLine is null)
        {
            return false;
        }

        string token = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? string.Empty;
        int separator = token.IndexOfAny(['-', '+']);
        if (separator >= 0)
        {
            token = token[..separator];
        }

        return Version.TryParse(token, out version);
    }

    private static void RegisterRuntime(string executablePath)
    {
        string fullPath = Path.GetFullPath(executablePath);
        Environment.SetEnvironmentVariable("YTTSTUDIO_DENO_PATH", fullPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        bool alreadyPresent = currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static item => item.Trim('"'))
            .Any(item => string.Equals(Path.GetFullPath(item), Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase));
        if (!alreadyPresent)
        {
            Environment.SetEnvironmentVariable("PATH", string.IsNullOrEmpty(currentPath)
                ? directory
                : directory + Path.PathSeparator + currentPath);
        }
    }

    private static DenoAsset GetCurrentAsset()
    {
        if (OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64)
        {
            return CreateAsset(WindowsAssetName, WindowsSha256, WindowsAssetLength);
        }

        if (OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64)
        {
            return CreateAsset(MacAssetName, MacSha256, MacAssetLength);
        }

        if (OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64)
        {
            return CreateAsset(LinuxAssetName, LinuxSha256, LinuxAssetLength);
        }

        throw new PlatformNotSupportedException(
            "이 플랫폼에는 yttStudio의 자동 Deno 설치 자산이 준비되어 있지 않습니다. Deno 2.3 이상을 PATH에 설치하세요.");
    }

    private static DenoAsset CreateAsset(string assetName, string sha256, long assetLength)
        => new(
            assetName,
            new Uri($"https://github.com/denoland/deno/releases/download/v{PinnedVersion}/{assetName}"),
            sha256,
            assetLength);

    private static string GetExecutableFileName() => OperatingSystem.IsWindows() ? "deno.exe" : "deno";

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

    private static void CommitInstallation(string stagingPath, string destinationPath)
    {
        string backupPath = destinationPath + ".previous";
        TryDeleteDirectory(backupPath);
        if (Directory.Exists(destinationPath))
        {
            Directory.Move(destinationPath, backupPath);
        }

        try
        {
            Directory.Move(stagingPath, destinationPath);
            TryDeleteDirectory(backupPath);
        }
        catch
        {
            if (!Directory.Exists(destinationPath) && Directory.Exists(backupPath))
            {
                Directory.Move(backupPath, destinationPath);
            }

            throw;
        }
    }

    /// <summary>
    /// 중단된 설치가 남긴 임시 작업 폴더를 정리한다. 실패해도 설치를 막지 않는다.
    /// </summary>
    private static void TryDeleteStaleWorkspaces(string parent)
    {
        try
        {
            foreach (string stale in Directory.EnumerateDirectories(parent, ".deno-install-*"))
            {
                TryDeleteDirectory(stale);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record DenoAsset(string AssetName, Uri DownloadUri, string Sha256, long AssetLength);
}
