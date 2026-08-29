using YttStudio.Video;

namespace YttStudio.App;

/// <summary>YouTube 주소 입력과 재생 실패 안내를 담당한다.</summary>
public sealed partial class MainWindowViewModel
{
    private string? loadedVideoOriginalUrl;
    private readonly object videoLoadGate = new();
    private CancellationTokenSource? activeVideoLoadCancellation;
    private long videoLoadGeneration;

    /// <summary>현재 영상에 입력한 원문 YouTube 주소를 가져온다.</summary>
    internal string? LoadedVideoOriginalUrl => loadedVideoOriginalUrl;

    /// <summary>현재 영상 소스에 전달한 경로 또는 정규화 주소를 가져온다.</summary>
    internal string? LoadedVideoPath => loadedVideoPath;

    private async Task OpenVideoUrlAsync()
    {
        if (videoSource is null)
        {
            return;
        }

        string? input = await dialogs.OpenVideoUrlAsync(CreateVideoUrlDialogOptions());
        if (input is null)
        {
            return;
        }

        await OpenYouTubeVideoAsync(input);
    }

    private async Task OpenYouTubeVideoAsync(string input)
    {
        if (!TryNormalizeYouTubeUrl(input, out string normalized, out _))
        {
            Status = Loc["YouTubeUrlInvalid"];
            return;
        }

        if (!await ConfirmDocumentReplacementAsync())
        {
            return;
        }

        await LoadVideoAsync(input.Trim(), normalized);
    }

    private VideoUrlDialogOptions CreateVideoUrlDialogOptions()
        => new(
            Loc["OpenVideoUrlTitle"],
            Loc["OpenVideoUrlPrompt"],
            Loc["OpenVideoUrlPlaceholder"],
            Loc["OpenVideoUrlConfirm"],
            Loc["Cancel"]);

    private async Task EnsureYouTubePlayableAsync(
        string normalizedUrl,
        CancellationToken cancellationToken,
        long generation)
    {
        Status = Loc["YouTubePreflight"];
        await youtubeProbe(normalizedUrl, cancellationToken);
        if (!IsCurrentVideoLoad(generation))
        {
            return;
        }

        if (videoSourceFactory is not null || videoSource is not MpvVideoSource nativeSource)
        {
            return;
        }

        string? ytdlpPath = YtDlpLocator.Find();
        if (!ShouldRefreshYtDlpPath(nativeSource.YtDlpPath, ytdlpPath))
        {
            return;
        }

        if (!await RefreshMpvSourceForYtDlpAsync(ytdlpPath, generation)
            && IsCurrentVideoLoad(generation))
        {
            throw new YouTubePlaybackException(
                YouTubePlaybackFailureKind.ToolFailure,
                "libmpv 소스를 다시 만들 수 없습니다.");
        }
    }

    private CancellationTokenSource BeginVideoLoad(out long generation)
    {
        CancellationTokenSource next = new();
        CancellationTokenSource? previous;
        lock (videoLoadGate)
        {
            previous = activeVideoLoadCancellation;
            activeVideoLoadCancellation = next;
            generation = Interlocked.Increment(ref videoLoadGeneration);
        }

        previous?.Cancel();
        return next;
    }

    private void EndVideoLoad(CancellationTokenSource cancellation, long generation)
    {
        lock (videoLoadGate)
        {
            if (ReferenceEquals(activeVideoLoadCancellation, cancellation)
                && generation == videoLoadGeneration)
            {
                activeVideoLoadCancellation = null;
            }
        }

        cancellation.Dispose();
    }

    private void CancelActiveVideoLoad()
    {
        CancellationTokenSource? cancellation;
        lock (videoLoadGate)
        {
            cancellation = activeVideoLoadCancellation;
            activeVideoLoadCancellation = null;
            Interlocked.Increment(ref videoLoadGeneration);
        }

        cancellation?.Cancel();
    }

    private bool IsCurrentVideoLoad(long generation)
        => !disposed && Interlocked.Read(ref videoLoadGeneration) == generation;

    internal static bool ShouldRefreshYtDlpPath(string? currentPath, string? discoveredPath)
        => !string.Equals(currentPath, discoveredPath, StringComparison.OrdinalIgnoreCase);

    private static bool TryNormalizeYouTubeUrl(
        string? input,
        out string normalized,
        out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        if (!YouTubeUrlValidator.TryValidate(input, out Uri? uri, out string? validationError))
        {
            error = validationError ?? "YouTube 주소가 올바르지 않습니다.";
            return false;
        }

        normalized = uri!.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
        return true;
    }

    private static bool IsWebAddress(string value)
        => Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
            && uri.Scheme is "http" or "https";

    private string GetYouTubeFailureMessage(YouTubePlaybackException exception)
        => exception.Kind switch
        {
            YouTubePlaybackFailureKind.InvalidUrl => Loc["YouTubeUrlInvalid"],
            YouTubePlaybackFailureKind.YtDlpMissing => Loc["YouTubeYtDlpMissing"],
            YouTubePlaybackFailureKind.NetworkFailure => Loc["YouTubeNetworkFailure"],
            YouTubePlaybackFailureKind.Timeout => Loc["YouTubeNetworkFailure"],
            YouTubePlaybackFailureKind.Unplayable => Loc["YouTubeUnplayable"],
            _ => Loc["YouTubePlaybackFailed"],
        };

    private string GetVideoLoadFailureMessage(Exception exception, bool isUrl)
    {
        if (isUrl && exception is YouTubePlaybackException youtubeException)
        {
            return GetYouTubeFailureMessage(youtubeException);
        }

        if (isUrl && exception is TimeoutException)
        {
            return Loc["YouTubeNetworkFailure"];
        }

        return isUrl
            ? $"{Loc["YouTubePlaybackFailed"]}: {exception.Message}"
            : $"영상 열기 실패: {exception.Message}";
    }

    private static string GetVideoDisplayName(string input, bool isUrl)
    {
        if (!isUrl)
        {
            return Path.GetFileName(input);
        }

        return input.Trim();
    }
}
