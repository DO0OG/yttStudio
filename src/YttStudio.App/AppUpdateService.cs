using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YttStudio.App;

/// <summary>앱 업데이트 처리 중 발생한 오류의 종류를 나타낸다.</summary>
public enum AppUpdateErrorKind
{
    /// <summary>네트워크 요청 자체가 실패한 경우다.</summary>
    Network,

    /// <summary>GitHub API 호출 횟수 제한에 도달한 경우다.</summary>
    RateLimited,

    /// <summary>릴리스 응답 또는 버전 메타데이터가 올바르지 않은 경우다.</summary>
    InvalidMetadata,

    /// <summary>현재 플랫폼에 맞는 릴리스 자산을 찾지 못한 경우다.</summary>
    AssetNotFound,

    /// <summary>현재 실행 환경이 지원되는 플랫폼 RID가 아닌 경우다.</summary>
    UnsupportedPlatform,

    /// <summary>릴리스 자산을 저장하지 못한 경우다.</summary>
    DownloadFailed,

    /// <summary>릴리스 자산 설치를 시작하거나 완료하지 못한 경우다.</summary>
    InstallationFailed,

    /// <summary>현재 실행 형태에서 릴리스 자산을 설치할 수 없는 경우다.</summary>
    InstallationUnsupported,
}

/// <summary>앱 업데이트 작업의 실패 원인과 종류를 함께 제공하는 예외다.</summary>
public sealed class AppUpdateException : Exception
{
    /// <summary>업데이트 예외를 생성한다.</summary>
    public AppUpdateException(
        AppUpdateErrorKind kind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    /// <summary>예외가 나타내는 업데이트 오류 종류다.</summary>
    public AppUpdateErrorKind Kind { get; }
}

/// <summary>GitHub 릴리스에서 선택된 앱 자산의 메타데이터다.</summary>
public sealed record AppUpdateAsset(
    string Name,
    Uri DownloadUri,
    long? SizeBytes,
    string? ContentType = null);

/// <summary>앱 업데이트 확인 결과다.</summary>
public sealed record AppUpdateCheckResult(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseTag,
    AppUpdateAsset? SelectedAsset);

/// <summary>앱 업데이트 자산 다운로드 진행 상태다.</summary>
public sealed record AppUpdateProgress(
    string AssetName,
    long BytesTransferred,
    long? TotalBytes)
{
    /// <summary>전체 크기를 알 수 있을 때의 다운로드 진행 비율이다.</summary>
    public double? Fraction
        => TotalBytes is > 0
            ? Math.Clamp((double)BytesTransferred / TotalBytes.Value, 0, 1)
            : null;
}

/// <summary>GitHub Releases를 사용해 앱 업데이트를 확인하고 실행 형태에 맞는 자산을 다운로드한다.</summary>
public sealed class AppUpdateService
{
    /// <summary>현재 앱 저장소의 최신 릴리스 API 주소다.</summary>
    public const string LatestReleaseUrl =
        "https://api.github.com/repos/DO0OG/yttStudio/releases/latest";

    private const string GitHubUserAgent = "yttStudio/1.0";
    private const string GitHubAccept = "application/vnd.github+json";
    private const int BufferSize = 128 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient httpClient;
    private readonly string runtimeIdentifier;
    private readonly AppUpdateExecutionForm executionForm;

    /// <summary>주입된 HTTP 클라이언트와 실행 환경 RID를 사용해 업데이트 서비스를 생성한다.</summary>
    /// <param name="httpClient">GitHub API와 자산 다운로드에 사용할 HTTP 클라이언트다.</param>
    /// <param name="runtimeIdentifier">플랫폼별 자산을 선택할 RID다. 생략하면 현재 런타임 RID를 사용한다.</param>
    public AppUpdateService(
        HttpClient httpClient,
        string? runtimeIdentifier = null)
        : this(httpClient, runtimeIdentifier, executionForm: null, detectExecutionForm: runtimeIdentifier is null)
    {
    }

    internal AppUpdateService(
        HttpClient httpClient,
        string? runtimeIdentifier,
        AppUpdateExecutionForm executionForm)
        : this(httpClient, runtimeIdentifier, executionForm, detectExecutionForm: false)
    {
    }

    private AppUpdateService(
        HttpClient httpClient,
        string? runtimeIdentifier,
        AppUpdateExecutionForm? executionForm,
        bool detectExecutionForm)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        this.runtimeIdentifier = runtimeIdentifier ?? RuntimeInformation.RuntimeIdentifier;
        ValidateRuntimeIdentifier(this.runtimeIdentifier);
        this.executionForm = executionForm
            ?? (detectExecutionForm
                ? AppUpdateExecutionDetector.Detect(this.runtimeIdentifier)
                : GetDefaultExecutionForm(this.runtimeIdentifier));
        AppUpdateExecutionDetector.Validate(this.runtimeIdentifier, this.executionForm);
    }

    /// <summary>서비스가 자산 선택에 사용하는 실행 환경 RID다.</summary>
    public string RuntimeIdentifier => runtimeIdentifier;

    /// <summary>서비스가 자산 선택에 사용하는 실행 형태다.</summary>
    internal AppUpdateExecutionForm ExecutionForm => executionForm;

    /// <summary>최신 릴리스를 조회하고 현재 버전보다 새로운 자산을 선택한다.</summary>
    public async Task<AppUpdateCheckResult> CheckForUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        SemanticVersion currentVersion = ParseCurrentVersion();
        using HttpResponseMessage response = await SendAsync(
                new Uri(LatestReleaseUrl, UriKind.Absolute),
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response, "최신 릴리스 조회");

        AppUpdateReleaseDto release = await ReadReleaseAsync(response, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(release.TagName) ||
            !SemanticVersion.TryParseTag(release.TagName, out SemanticVersion latestVersion))
        {
            throw Failure(
                AppUpdateErrorKind.InvalidMetadata,
                "GitHub 최신 릴리스의 태그가 v 접두사를 포함한 SemVer가 아니다.");
        }

        bool updateAvailable = latestVersion.CompareTo(currentVersion) > 0;
        AppUpdateAsset? selectedAsset = updateAvailable
            ? SelectAsset(release, release.TagName)
            : null;

        return new(
            updateAvailable,
            currentVersion.ToString(),
            latestVersion.ToString(),
            release.TagName,
            selectedAsset);
    }

    /// <summary>선택된 릴리스 자산을 지정한 디렉터리에 내려받고 저장된 경로를 반환한다.</summary>
    /// <param name="asset">최신 릴리스 확인 결과에서 선택된 자산이다.</param>
    /// <param name="destinationDirectory">사용자가 선택한 다운로드 대상 디렉터리다.</param>
    /// <param name="progress">다운로드 진행 상태를 받을 콜백이다.</param>
    /// <param name="cancellationToken">작업 취소 토큰이다.</param>
    /// <remarks>같은 디렉터리의 임시 파일에 검증하며 쓰고, 성공하면 기존 파일을 원자적으로 교체한다.</remarks>
    public async Task<string> DownloadAsync(
        AppUpdateAsset asset,
        string destinationDirectory,
        IProgress<AppUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ValidateAsset(asset);

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw Failure(
                AppUpdateErrorKind.DownloadFailed,
                "다운로드 대상 디렉터리가 비어 있다.");
        }

        string destinationPath = string.Empty;
        string? temporaryPath = null;
        bool temporaryFileCreated = false;
        try
        {
            string directory = Path.GetFullPath(destinationDirectory);
            Directory.CreateDirectory(directory);
            destinationPath = Path.Combine(directory, asset.Name);
            temporaryPath = Path.Combine(
                directory,
                $".{asset.Name}.{Guid.NewGuid():N}.download");

            using HttpResponseMessage response = await SendAsync(
                    asset.DownloadUri,
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureSuccess(response, "릴리스 자산 다운로드");

            long? contentLength = response.Content.Headers.ContentLength;
            long? totalBytes = contentLength ?? asset.SizeBytes;
            ReportProgress(progress, asset.Name, 0, totalBytes);

            await using Stream input = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            long transferred = 0;
            FileStream output = new(
                temporaryPath!,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            temporaryFileCreated = true;
            try
            {
                byte[] buffer = new byte[BufferSize];
                while (true)
                {
                    int read = await input
                        .ReadAsync(buffer.AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    transferred = checked(transferred + read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    ReportProgress(progress, asset.Name, transferred, totalBytes);
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await output.DisposeAsync().ConfigureAwait(false);
            }

            ValidateDownloadedLength(contentLength, asset.SizeBytes, transferred);
            File.Move(temporaryPath!, destinationPath, overwrite: true);
            temporaryFileCreated = false;
            return Path.GetFullPath(destinationPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AppUpdateException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw Failure(
                AppUpdateErrorKind.DownloadFailed,
                "릴리스 자산을 다운로드하지 못했다.",
                exception);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or
                OverflowException)
        {
            throw Failure(
                AppUpdateErrorKind.DownloadFailed,
                "릴리스 자산 파일을 저장하지 못했다.",
                exception);
        }
        finally
        {
            if (temporaryFileCreated && temporaryPath is not null)
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
    }

    private static void ValidateDownloadedLength(
        long? contentLength,
        long? assetSizeBytes,
        long transferred)
    {
        if (contentLength is long expectedContentLength && transferred != expectedContentLength)
        {
            throw Failure(
                AppUpdateErrorKind.DownloadFailed,
                $"다운로드 응답의 Content-Length와 실제 크기가 다르다. 예상 {expectedContentLength}, 실제 {transferred}바이트다.");
        }

        if (assetSizeBytes is > 0 && transferred != assetSizeBytes.Value)
        {
            throw Failure(
                AppUpdateErrorKind.DownloadFailed,
                $"릴리스 자산 메타데이터와 실제 크기가 다르다. 예상 {assetSizeBytes.Value}, 실제 {transferred}바이트다.");
        }
    }

    private static void TryDeleteTemporaryFile(string path)
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

    private static void ValidateRuntimeIdentifier(string value)
    {
        if (!IsSupportedRuntimeIdentifier(value))
        {
            throw Failure(
                AppUpdateErrorKind.UnsupportedPlatform,
                $"지원하지 않는 플랫폼 RID다: {value}");
        }
    }

    private static AppUpdateExecutionForm GetDefaultExecutionForm(string value)
        => value switch
        {
            "win-x64" or "osx-arm64" => AppUpdateExecutionForm.Installed,
            "linux-x64" => AppUpdateExecutionForm.AppImage,
            _ => throw Failure(
                AppUpdateErrorKind.UnsupportedPlatform,
                $"지원하지 않는 플랫폼 RID다: {value}"),
        };

    private static bool IsSupportedRuntimeIdentifier(string value)
        => value is "win-x64" or "osx-arm64" or "linux-x64";

    private SemanticVersion ParseCurrentVersion()
    {
        if (SemanticVersion.TryParse(AppVersion.Current, out SemanticVersion version))
        {
            return version;
        }

        throw Failure(
            AppUpdateErrorKind.InvalidMetadata,
            "현재 앱 버전이 SemVer가 아니다.");
    }

    private async Task<AppUpdateReleaseDto> ReadReleaseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using Stream stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            AppUpdateReleaseDto? release = await JsonSerializer
                .DeserializeAsync<AppUpdateReleaseDto>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return release ?? throw Failure(
                AppUpdateErrorKind.InvalidMetadata,
                "GitHub 최신 릴리스 응답이 비어 있다.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AppUpdateException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw Failure(
                AppUpdateErrorKind.Network,
                "GitHub 최신 릴리스 응답을 읽지 못했다.",
                exception);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw Failure(
                AppUpdateErrorKind.InvalidMetadata,
                "GitHub 최신 릴리스 응답의 JSON을 해석하지 못했다.",
                exception);
        }
    }

    private AppUpdateAsset SelectAsset(
        AppUpdateReleaseDto release,
        string releaseTag)
    {
        string[] candidateNames = runtimeIdentifier switch
        {
            "win-x64" when executionForm == AppUpdateExecutionForm.Installed =>
            [$"yttStudio-{releaseTag}-win-x64-setup.exe"],
            "win-x64" when executionForm == AppUpdateExecutionForm.Portable =>
            [$"yttStudio-{releaseTag}-win-x64.zip"],
            "osx-arm64" when executionForm == AppUpdateExecutionForm.Installed =>
            [$"yttStudio-{releaseTag}-osx-arm64.dmg"],
            "osx-arm64" when executionForm == AppUpdateExecutionForm.TarGz =>
            [$"yttStudio-{releaseTag}-osx-arm64.tar.gz"],
            "linux-x64" when executionForm == AppUpdateExecutionForm.AppImage =>
            [$"yttStudio-{releaseTag}-linux-x86_64.AppImage"],
            "linux-x64" when executionForm == AppUpdateExecutionForm.TarGz =>
            [$"yttStudio-{releaseTag}-linux-x64.tar.gz"],
            _ => throw Failure(
                AppUpdateErrorKind.UnsupportedPlatform,
                $"지원하지 않는 플랫폼 실행 형태다: {runtimeIdentifier}/{executionForm}"),
        };

        if (release.Assets is null)
        {
            throw Failure(
                AppUpdateErrorKind.AssetNotFound,
                $"릴리스에 {runtimeIdentifier} 자산이 없다.");
        }

        foreach (string candidateName in candidateNames)
        {
            AppUpdateReleaseAssetDto? candidate = release.Assets.FirstOrDefault(
                asset => asset is not null &&
                    string.Equals(asset.Name, candidateName, StringComparison.Ordinal));
            if (candidate is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate.BrowserDownloadUrl) ||
                !Uri.TryCreate(candidate.BrowserDownloadUrl, UriKind.Absolute, out Uri? downloadUri) ||
                downloadUri.Scheme != Uri.UriSchemeHttps)
            {
                throw Failure(
                    AppUpdateErrorKind.InvalidMetadata,
                    $"릴리스 자산 {candidateName}의 HTTPS 다운로드 주소가 올바르지 않다.");
            }

            if (candidate.Size is < 0)
            {
                throw Failure(
                    AppUpdateErrorKind.InvalidMetadata,
                    $"릴리스 자산 {candidateName}의 크기가 올바르지 않다.");
            }

            return new(candidateName, downloadUri, candidate.Size, candidate.ContentType);
        }

        throw Failure(
            AppUpdateErrorKind.AssetNotFound,
            $"릴리스에 {runtimeIdentifier}용 지원 자산이 없다.");
    }

    private static void ValidateAsset(AppUpdateAsset asset)
    {
        if (string.IsNullOrWhiteSpace(asset.Name) ||
            string.IsNullOrEmpty(Path.GetFileName(asset.Name)) ||
            !string.Equals(Path.GetFileName(asset.Name), asset.Name, StringComparison.Ordinal) ||
            asset.Name.Contains('/', StringComparison.Ordinal) ||
            asset.Name.Contains('\\', StringComparison.Ordinal) ||
            asset.DownloadUri.Scheme != Uri.UriSchemeHttps ||
            asset.SizeBytes is < 0)
        {
            throw Failure(
                AppUpdateErrorKind.InvalidMetadata,
                "다운로드 자산 메타데이터가 올바르지 않다.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(GitHubUserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GitHubAccept));
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
            throw Failure(
                AppUpdateErrorKind.Network,
                "GitHub 업데이트 요청이 네트워크 오류로 실패했다.",
                exception);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        AppUpdateErrorKind kind = response.StatusCode is HttpStatusCode.Forbidden or
            (HttpStatusCode)429
            ? AppUpdateErrorKind.RateLimited
            : AppUpdateErrorKind.Network;
        throw Failure(
            kind,
            $"{operation}이 HTTP {(int)response.StatusCode} 상태로 실패했다.");
    }

    private static void ReportProgress(
        IProgress<AppUpdateProgress>? progress,
        string assetName,
        long transferred,
        long? totalBytes)
        => progress?.Report(new(assetName, transferred, totalBytes));

    private static AppUpdateException Failure(
        AppUpdateErrorKind kind,
        string message,
        Exception? innerException = null)
        => new(kind, message, innerException);
}

internal sealed class AppUpdateReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("assets")]
    public List<AppUpdateReleaseAssetDto?>? Assets { get; set; }
}

internal sealed class AppUpdateReleaseAssetDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }
}
