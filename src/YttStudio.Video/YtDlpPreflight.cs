using System.Text.Json;

namespace YttStudio.Video;

internal sealed record YtDlpProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);

internal interface IYtDlpProcessRunner
{
    Task<YtDlpProcessResult> RunAsync(
        string executablePath,
        Uri uri,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <summary>yt-dlp로 주소를 다운로드하지 않고 재생 가능 여부만 확인한다.</summary>
public sealed class YtDlpPreflight
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private readonly Func<(string? Path, string Diagnostic)> locate;
    private readonly IYtDlpProcessRunner processRunner;
    private readonly TimeSpan timeout;

    /// <summary>기본 탐색기와 프로세스 실행기로 사전 확인기를 만든다.</summary>
    public YtDlpPreflight(TimeSpan? timeout = null)
        : this(
            static () =>
            {
                bool found = YtDlpLocator.TryFind(out string? path, out string diagnostic);
                return (found ? path : null, diagnostic);
            },
            new YtDlpProcessRunner(),
            timeout ?? DefaultTimeout)
    {
    }

    internal YtDlpPreflight(
        Func<(string? Path, string Diagnostic)> locate,
        IYtDlpProcessRunner processRunner,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(locate);
        ArgumentNullException.ThrowIfNull(processRunner);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        this.locate = locate;
        this.processRunner = processRunner;
        this.timeout = timeout;
    }

    /// <summary>주소를 확인하고 yt-dlp 메타데이터만 읽는다.</summary>
    public async Task<YouTubePreflightResult> ProbeAsync(
        string? value,
        CancellationToken cancellationToken = default)
    {
        if (!YouTubeUrlValidator.TryValidate(value, out Uri? uri, out string? urlError))
        {
            return YouTubePreflightResult.Failure(
                null,
                YouTubePlaybackFailureKind.InvalidUrl,
                urlError ?? "YouTube 주소가 올바르지 않습니다.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        (string? executablePath, string diagnostic) = locate();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return YouTubePreflightResult.Failure(
                uri,
                YouTubePlaybackFailureKind.YtDlpMissing,
                diagnostic);
        }

        YtDlpProcessResult processResult;
        try
        {
            processResult = await processRunner.RunAsync(
                executablePath,
                uri!,
                timeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            return YouTubePreflightResult.Failure(
                uri,
                YouTubePlaybackFailureKind.YtDlpMissing,
                $"yt-dlp를 실행할 수 없습니다: {exception.Message}");
        }
        catch (Exception exception)
        {
            return YouTubePreflightResult.Failure(
                uri,
                YouTubePlaybackFailureKind.ToolFailure,
                $"yt-dlp 사전 확인에 실패했습니다: {exception.Message}");
        }

        return Interpret(uri!, processResult);
    }

    /// <summary>실패 결과를 예외로 바꾸는 호출부용 편의 메서드다.</summary>
    public async Task<YouTubePreflightResult> EnsurePlayableAsync(
        string? value,
        CancellationToken cancellationToken = default)
    {
        YouTubePreflightResult result = await ProbeAsync(value, cancellationToken).ConfigureAwait(false);
        if (!result.IsPlayable)
        {
            throw YouTubePlaybackException.From(result);
        }

        return result;
    }

    private static YouTubePreflightResult Interpret(Uri uri, YtDlpProcessResult processResult)
    {
        string diagnostic = JoinOutput(processResult.StandardError, processResult.StandardOutput);
        if (processResult.TimedOut)
        {
            return YouTubePreflightResult.Failure(
                uri,
                YouTubePlaybackFailureKind.Timeout,
                "yt-dlp 사전 확인 시간이 초과되었습니다.");
        }

        if (TryReadMetadata(processResult.StandardOutput, out VideoMetadata metadata))
        {
            YouTubePreflightResult? metadataFailure = ClassifyMetadata(uri, metadata);
            if (metadataFailure is not null)
            {
                return metadataFailure;
            }

            if (processResult.ExitCode == 0)
            {
                return YouTubePreflightResult.Success(
                    uri,
                    metadata.Title,
                    metadata.Duration,
                    metadata.IsLive,
                    metadata.AgeLimit,
                    metadata.Availability);
            }
        }

        return ClassifyOutput(uri, processResult.ExitCode, diagnostic);
    }

    private static YouTubePreflightResult? ClassifyMetadata(Uri uri, VideoMetadata metadata)
    {
        if (metadata.IsLive)
        {
            return YouTubePreflightResult.Failure(
                uri,
                YouTubePlaybackFailureKind.Unplayable,
                "생방송 영상은 프리뷰에서 재생할 수 없습니다.",
                YouTubeUnplayableReason.Live,
                isLive: true);
        }

        if (metadata.AgeLimit is >= 18 || metadata.AgeRestricted)
        {
            return YouTubePreflightResult.Failure(
                uri,
                YouTubePlaybackFailureKind.Unplayable,
                "연령 제한 영상은 프리뷰에서 재생할 수 없습니다.",
                YouTubeUnplayableReason.AgeRestricted);
        }

        YouTubeUnplayableReason availabilityReason = GetAvailabilityReason(metadata.Availability);
        if (availabilityReason is not YouTubeUnplayableReason.None)
        {
            return YouTubePreflightResult.Failure(
                uri,
                YouTubePlaybackFailureKind.Unplayable,
                MessageForReason(availabilityReason),
                availabilityReason);
        }

        return null;
    }

    private static YouTubePreflightResult ClassifyOutput(Uri uri, int exitCode, string diagnostic)
    {
        YouTubeUnplayableReason reason = FindUnplayableReason(diagnostic);
        if (reason is not YouTubeUnplayableReason.None)
        {
            return YouTubePreflightResult.Failure(
                uri,
                YouTubePlaybackFailureKind.Unplayable,
                MessageForReason(reason),
                reason,
                reason is YouTubeUnplayableReason.Live);
        }

        if (LooksLikeNetworkFailure(diagnostic))
        {
            return YouTubePreflightResult.Failure(
                uri,
                YouTubePlaybackFailureKind.NetworkFailure,
                "YouTube 네트워크 연결에 실패했습니다.");
        }

        string detail = string.IsNullOrWhiteSpace(diagnostic) ? $"종료 코드 {exitCode}" : diagnostic;
        return YouTubePreflightResult.Failure(
            uri,
            YouTubePlaybackFailureKind.ToolFailure,
            $"yt-dlp 사전 확인에 실패했습니다: {detail}");
    }

    private static bool TryReadMetadata(string output, out VideoMetadata metadata)
    {
        metadata = default;
        if (TryReadMetadataJson(output, out metadata))
        {
            return true;
        }

        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            if (TryReadMetadataJson(line, out metadata))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadMetadataJson(string value, out VideoMetadata metadata)
    {
        metadata = default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            metadata = ReadMetadata(document.RootElement);
            return true;
        }
        catch (JsonException)
        {
            // yt-dlp가 경고를 stdout에 섞어도 JSON 줄을 찾을 때까지 계속 읽는다.
            return false;
        }
    }

    private static VideoMetadata ReadMetadata(JsonElement root)
    {
        bool isLive = ReadBoolean(root, "is_live") ||
            ReadString(root, "live_status") is "is_live" or "is_upcoming";
        int? ageLimit = ReadInt(root, "age_limit");
        bool ageRestricted = ReadBoolean(root, "age_restricted");
        double? durationValue = ReadDouble(root, "duration");
        TimeSpan? duration = durationValue is > 0 ? TimeSpan.FromSeconds(durationValue.Value) : null;
        return new VideoMetadata(
            ReadString(root, "title"),
            duration,
            isLive,
            ageLimit,
            ageRestricted,
            ReadString(root, "availability"));
    }

    private static YouTubeUnplayableReason FindUnplayableReason(string diagnostic)
    {
        string value = diagnostic.ToLowerInvariant();
        if (ContainsAny(value, "private video", "this video is private", "video is private"))
        {
            return YouTubeUnplayableReason.Private;
        }

        if (ContainsAny(value, "age-restricted", "age restricted", "confirm your age", "age verification"))
        {
            return YouTubeUnplayableReason.AgeRestricted;
        }

        if (ContainsAny(value, "not available in your country", "not available in your location",
            "geo-restricted", "blocked in your country", "country restriction"))
        {
            return YouTubeUnplayableReason.RegionBlocked;
        }

        if (ContainsAny(value, "live stream", "live event", "this live event", " is live"))
        {
            return YouTubeUnplayableReason.Live;
        }

        return ContainsAny(value, "video unavailable", "video not found", "removed by the uploader",
            "has been removed", "content isn't available", "does not exist")
            ? YouTubeUnplayableReason.Unavailable
            : YouTubeUnplayableReason.None;
    }

    private static bool LooksLikeNetworkFailure(string diagnostic)
    {
        string value = diagnostic.ToLowerInvariant();
        return ContainsAny(value, "urlopen error", "timed out", "timeout", "connection reset",
            "connection refused", "network is unreachable", "temporary failure in name resolution",
            "name or service not known", "could not resolve host", "unable to download",
            "http error 4", "http error 429", "http error 5", "server returned 5", "proxy error",
            "sign in to confirm you're not a bot", "too many requests", "ssl:");
    }

    private static string MessageForReason(YouTubeUnplayableReason reason)
        => reason switch
        {
            YouTubeUnplayableReason.Live => "생방송 영상은 프리뷰에서 재생할 수 없습니다.",
            YouTubeUnplayableReason.AgeRestricted => "연령 제한 영상은 프리뷰에서 재생할 수 없습니다.",
            YouTubeUnplayableReason.Private => "비공개 영상은 재생할 수 없습니다.",
            YouTubeUnplayableReason.RegionBlocked => "현재 지역에서 재생할 수 없는 영상입니다.",
            _ => "재생할 수 없는 YouTube 영상입니다.",
        };

    private static YouTubeUnplayableReason GetAvailabilityReason(string? availability)
        => availability?.ToLowerInvariant() switch
        {
            "private" or "needs_auth" or "subscriber_only" => YouTubeUnplayableReason.Private,
            "geo_restricted" or "country_blocked" => YouTubeUnplayableReason.RegionBlocked,
            "unavailable" or "premium_only" or "needs_subscription" => YouTubeUnplayableReason.Unavailable,
            _ => YouTubeUnplayableReason.None,
        };

    private static bool ContainsAny(string value, params string[] patterns)
        => patterns.Any(value.Contains);

    private static string JoinOutput(string standardError, string standardOutput)
    {
        const int maxLength = 4096;
        string value = string.Join('\n', new[] { standardError, standardOutput }
            .Where(static item => !string.IsNullOrWhiteSpace(item)));
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static bool ReadBoolean(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static int? ReadInt(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : null;

    private static double? ReadDouble(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.TryGetDouble(out double result)
            ? result
            : null;

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private readonly record struct VideoMetadata(
        string? Title,
        TimeSpan? Duration,
        bool IsLive,
        int? AgeLimit,
        bool AgeRestricted,
        string? Availability);
}
