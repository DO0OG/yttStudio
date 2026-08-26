using System.Text.Json;
using System.Text.Json.Serialization;
using SharpCompress.Archives;

namespace YttStudio.App;

internal static class MpvInstallerTransport
{
    private const string UserAgent = "YttStudio/1.0";

    public static async Task<GitHubAsset> GetLatestAssetAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(
                httpClient,
                MpvAutoInstaller.GitHubLatestReleaseUrl,
                MpvAutoInstallErrorKind.ReleaseMetadataRequestFailed,
                "GitHub에서 최신 mpv 릴리스 정보를 가져오지 못했습니다.",
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(
                response,
                MpvAutoInstallErrorKind.ReleaseMetadataRequestFailed,
                "GitHub 최신 mpv 릴리스 요청",
                cancellationToken)
            .ConfigureAwait(false);
        GitHubReleaseDto? release = await ReadReleaseAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return CreateAsset(SelectAsset(release));
    }

    public static async Task DownloadArchiveAsync(
        HttpClient httpClient,
        GitHubAsset asset,
        string destinationPath,
        IProgress<MpvInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(
                httpClient,
                asset.DownloadUri.ToString(),
                MpvAutoInstallErrorKind.DownloadFailed,
                "mpv libmpv 아카이브를 내려받지 못했습니다.",
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(
                response,
                MpvAutoInstallErrorKind.DownloadFailed,
                "mpv libmpv 아카이브 다운로드",
                cancellationToken)
            .ConfigureAwait(false);
        long? totalBytes = response.Content.Headers.ContentLength;
        ValidateArchiveLength(totalBytes);
        await SaveResponseAsync(response, asset.Name, destinationPath, totalBytes, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient httpClient,
        string url,
        MpvAutoInstallErrorKind errorKind,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        try
        {
            return await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new MpvAutoInstallException(errorKind, errorMessage, exception);
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        MpvAutoInstallErrorKind errorKind,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string detail = await ReadResponseDetailAsync(response, cancellationToken).ConfigureAwait(false);
        throw new MpvAutoInstallException(
            errorKind,
            $"{operation}이 HTTP {(int)response.StatusCode} ({response.ReasonPhrase})로 실패했습니다. {detail}");
    }

    private static async Task<GitHubReleaseDto?> ReadReleaseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using Stream responseStream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            return await JsonSerializer
                .DeserializeAsync<GitHubReleaseDto>(responseStream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new MpvAutoInstallException(
                MpvAutoInstallErrorKind.ReleaseMetadataRequestFailed,
                "GitHub 최신 mpv 릴리스 응답을 해석하지 못했습니다.",
                exception);
        }
    }

    private static GitHubAssetDto? SelectAsset(GitHubReleaseDto? release)
    {
        IEnumerable<GitHubAssetDto> candidates = release?.Assets?.Where(IsWindowsAsset)
            ?? Enumerable.Empty<GitHubAssetDto>();
        return candidates.FirstOrDefault(
                candidate => !candidate.Name!.Contains("-v3-", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();
    }

    private static GitHubAsset CreateAsset(GitHubAssetDto? asset)
    {
        if (asset is null
            || string.IsNullOrWhiteSpace(asset.Name)
            || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
        {
            throw new MpvAutoInstallException(
                MpvAutoInstallErrorKind.ReleaseAssetNotFound,
                "최신 mpv 릴리스에서 호환되는 mpv-dev-x86_64-*.7z 파일을 찾지 못했습니다.");
        }

        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out Uri? downloadUri)
            || downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new MpvAutoInstallException(
                MpvAutoInstallErrorKind.ReleaseAssetNotFound,
                "GitHub mpv 아카이브 URL이 올바른 HTTPS 주소가 아닙니다.");
        }

        return new GitHubAsset(asset.Name, downloadUri);
    }

    private static bool IsWindowsAsset(GitHubAssetDto asset)
        => !string.IsNullOrWhiteSpace(asset.Name)
            && asset.Name.StartsWith(MpvAutoInstaller.WindowsAssetPrefix, StringComparison.OrdinalIgnoreCase)
            && asset.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);

    private static void ValidateArchiveLength(long? totalBytes)
    {
        if (totalBytes is > MpvInstallerLimits.MaximumArchiveBytes)
        {
            throw new MpvAutoInstallException(
                MpvAutoInstallErrorKind.DownloadFailed,
                "mpv libmpv 아카이브 크기가 허용된 한도를 초과했습니다.");
        }
    }

    private static async Task SaveResponseAsync(
        HttpResponseMessage response,
        string assetName,
        string destinationPath,
        long? totalBytes,
        IProgress<MpvInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            await using Stream input = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using FileStream output = new(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                MpvInstallerLimits.BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyResponseAsync(input, output, assetName, totalBytes, progress, cancellationToken)
                .ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MpvAutoInstallException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new MpvAutoInstallException(
                MpvAutoInstallErrorKind.DownloadFailed,
                "내려받은 mpv 아카이브를 저장하지 못했습니다.",
                exception);
        }
    }

    private static async Task CopyResponseAsync(
        Stream input,
        FileStream output,
        string assetName,
        long? totalBytes,
        IProgress<MpvInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[MpvInstallerLimits.BufferSize];
        long transferred = 0;
        Report(progress, new(MpvInstallStage.DownloadingArchive, assetName, 0, totalBytes));
        while (true)
        {
            int read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            transferred += read;
            ValidateArchiveLength(transferred);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            Report(progress, new(MpvInstallStage.DownloadingArchive, assetName, transferred, totalBytes));
        }
    }

    private static async Task<string> ReadResponseDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(body) ? string.Empty : $"응답: {body[..Math.Min(body.Length, 300)]}";
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return string.Empty;
        }
    }

    private static void Report(
        IProgress<MpvInstallProgress>? progress,
        MpvInstallProgress value)
        => progress?.Report(value);
}

internal sealed record GitHubAsset(string Name, Uri DownloadUri);

internal sealed record GitHubReleaseDto(
    [property: JsonPropertyName("assets")] List<GitHubAssetDto>? Assets);

internal sealed record GitHubAssetDto(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl);

internal static class MpvInstallerLimits
{
    public const int BufferSize = 128 * 1024;
    public const long MaximumArchiveBytes = 512L * 1024 * 1024;
    public const long MaximumExtractedBytes = 2L * 1024 * 1024 * 1024;
    public const int MaximumEntryCount = 20_000;
}

internal sealed class MpvInstallerWorkspace
{
    private MpvInstallerWorkspace(string installDirectory, string workspaceDirectory)
    {
        InstallDirectory = installDirectory;
        WorkspaceDirectory = workspaceDirectory;
        ArchivePath = Path.Combine(workspaceDirectory, "mpv-dev.7z");
        StagingDirectory = Path.Combine(workspaceDirectory, "staging");
    }

    public string InstallDirectory { get; }

    public string WorkspaceDirectory { get; }

    public string ArchivePath { get; }

    public string StagingDirectory { get; }

    public static MpvInstallerWorkspace Create(string installDirectory)
    {
        string fullInstallDirectory = Path.GetFullPath(installDirectory);
        string? parent = Path.GetDirectoryName(fullInstallDirectory);
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new MpvAutoInstallException(
                MpvAutoInstallErrorKind.InstallationFailed,
                "libmpv 설치 디렉터리의 상위 경로를 확인할 수 없습니다.");
        }

        Directory.CreateDirectory(parent);
        string workspace = Path.Combine(parent, $".libmpv-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        return new(fullInstallDirectory, workspace);
    }

    public void CreateStagingDirectory() => Directory.CreateDirectory(StagingDirectory);

    public void MarkCommitted()
    {
    }

    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(WorkspaceDirectory))
            {
                Directory.Delete(WorkspaceDirectory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(exception, "libmpv 임시 설치 디렉터리를 정리하지 못했습니다: {Path}", WorkspaceDirectory);
        }
    }
}

internal static class MpvArchiveExtractor
{
    public static async Task ExtractAsync(
        string stagingDirectory,
        string assetName,
        string archivePath,
        IProgress<MpvInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using IArchive archive = ArchiveFactory.OpenArchive(archivePath);
            await ExtractEntriesAsync(archive, stagingDirectory, assetName, progress, cancellationToken)
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new MpvAutoInstallException(
                MpvAutoInstallErrorKind.ArchiveExtractionFailed,
                "mpv 압축 파일을 해제하지 못했습니다.",
                exception);
        }
    }

    public static string FindLibraryRelativePath(string stagingDirectory)
    {
        string? libraryPath = Directory
            .EnumerateFiles(stagingDirectory, MpvAutoInstaller.WindowsLibraryFileName, SearchOption.AllDirectories)
            .FirstOrDefault();
        if (libraryPath is null)
        {
            throw new MpvAutoInstallException(
                MpvAutoInstallErrorKind.LibraryNotFound,
                "압축 파일에서 libmpv-2.dll을 찾지 못했습니다.");
        }

        return Path.GetRelativePath(stagingDirectory, libraryPath);
    }

    private static async Task ExtractEntriesAsync(
        IArchive archive,
        string stagingDirectory,
        string assetName,
        IProgress<MpvInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        long extractedBytes = 0;
        int entryCount = 0;
        foreach (IArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateEntryCount(++entryCount);
            string destinationPath = GetSafeDestinationPath(stagingDirectory, entry.Key ?? string.Empty);
            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            extractedBytes = await ExtractFileAsync(entry, destinationPath, extractedBytes, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new(MpvInstallStage.ExtractingArchive, assetName, extractedBytes, null));
        }
    }

    private static async Task<long> ExtractFileAsync(
        IArchiveEntry entry,
        string destinationPath,
        long extractedBytes,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using Stream input = entry.OpenEntryStream();
        await using FileStream output = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[MpvInstallerLimits.BufferSize];
        while (true)
        {
            int read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return extractedBytes;
            }

            extractedBytes = ValidateExtractedLength(extractedBytes, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string GetSafeDestinationPath(string stagingDirectory, string entryKey)
    {
        if (string.IsNullOrWhiteSpace(entryKey) || Path.IsPathRooted(entryKey))
        {
            throw InvalidEntry(entryKey);
        }

        string root = Path.GetFullPath(stagingDirectory) + Path.DirectorySeparatorChar;
        string destination = Path.GetFullPath(Path.Combine(stagingDirectory, entryKey));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidEntry(entryKey);
        }

        return destination;
    }

    private static void ValidateEntryCount(int entryCount)
    {
        if (entryCount > MpvInstallerLimits.MaximumEntryCount)
        {
            throw InvalidEntry("항목 수 초과");
        }
    }

    private static long ValidateExtractedLength(long extractedBytes, int read)
    {
        long total = checked(extractedBytes + read);
        if (total > MpvInstallerLimits.MaximumExtractedBytes)
        {
            throw InvalidEntry("압축 해제 크기 초과");
        }

        return total;
    }

    private static MpvAutoInstallException InvalidEntry(string? entryKey)
        => new(
            MpvAutoInstallErrorKind.ArchiveExtractionFailed,
            $"안전하지 않은 압축 항목을 차단했습니다: {entryKey}");
}

internal static class MpvInstallerFileStore
{
    public static string GetDefaultInstallDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YttStudio",
            "libmpv");

    public static string CommitInstallation(
        string stagingDirectory,
        string relativeLibraryPath,
        string installDirectory)
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

            Directory.Move(stagingDirectory, installDirectory);
            DeleteBackup(backupDirectory, movedExisting);
            return Path.GetFullPath(Path.Combine(installDirectory, relativeLibraryPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RestoreBackup(installDirectory, backupDirectory, movedExisting);
            throw new MpvAutoInstallException(
                MpvAutoInstallErrorKind.InstallationFailed,
                "libmpv 설치 디렉터리를 교체하지 못했습니다.",
                exception);
        }
    }

    private static void DeleteBackup(string backupDirectory, bool movedExisting)
    {
        if (movedExisting && Directory.Exists(backupDirectory))
        {
            Directory.Delete(backupDirectory, recursive: true);
        }
    }

    private static void RestoreBackup(string installDirectory, string backupDirectory, bool movedExisting)
    {
        if (!movedExisting || Directory.Exists(installDirectory) || !Directory.Exists(backupDirectory))
        {
            return;
        }

        try
        {
            Directory.Move(backupDirectory, installDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Error(exception, "libmpv 기존 설치 복원에 실패했습니다: {Path}", backupDirectory);
        }
    }
}
