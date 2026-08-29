namespace YttStudio.Video;

/// <summary>YouTube 주소를 재생하기 전에 확인한 결과의 종류다.</summary>
public enum YouTubePlaybackFailureKind
{
    None,
    InvalidUrl,
    YtDlpMissing,
    NetworkFailure,
    Unplayable,
    Timeout,
    ToolFailure,
}

/// <summary>영상이 재생되지 않는 구체적인 이유다.</summary>
public enum YouTubeUnplayableReason
{
    None,
    Live,
    AgeRestricted,
    Private,
    RegionBlocked,
    Unavailable,
}

/// <summary>yt-dlp 사전 확인에서 얻은 재생 가능 여부와 메타데이터다.</summary>
public sealed record YouTubePreflightResult
{
    private YouTubePreflightResult(
        Uri? uri,
        bool isPlayable,
        YouTubePlaybackFailureKind failureKind,
        YouTubeUnplayableReason unplayableReason,
        string message,
        string? title,
        TimeSpan? duration,
        bool isLive,
        int? ageLimit,
        string? availability)
    {
        Uri = uri;
        IsPlayable = isPlayable;
        FailureKind = failureKind;
        UnplayableReason = unplayableReason;
        Message = message;
        Title = title;
        Duration = duration;
        IsLive = isLive;
        AgeLimit = ageLimit;
        Availability = availability;
    }

    /// <summary>검증된 YouTube 주소다.</summary>
    public Uri? Uri { get; }

    /// <summary>사전 확인이 성공했는지 가져온다.</summary>
    public bool IsPlayable { get; }

    /// <summary>실패했을 때의 안정적인 분류다.</summary>
    public YouTubePlaybackFailureKind FailureKind { get; }

    /// <summary>재생 불가 영상의 세부 이유다.</summary>
    public YouTubeUnplayableReason UnplayableReason { get; }

    /// <summary>로그와 사용자 안내에 사용할 짧은 설명이다.</summary>
    public string Message { get; }

    /// <summary>확인된 영상 제목이다.</summary>
    public string? Title { get; }

    /// <summary>확인된 영상 길이다. 라이브 영상은 값이 없을 수 있다.</summary>
    public TimeSpan? Duration { get; }

    /// <summary>라이브로 표시된 영상인지 가져온다.</summary>
    public bool IsLive { get; }

    /// <summary>yt-dlp가 보고한 연령 제한이다.</summary>
    public int? AgeLimit { get; }

    /// <summary>yt-dlp가 보고한 접근 상태다.</summary>
    public string? Availability { get; }

    internal static YouTubePreflightResult Success(
        Uri uri,
        string? title,
        TimeSpan? duration,
        bool isLive,
        int? ageLimit,
        string? availability)
        => new(uri, true, YouTubePlaybackFailureKind.None, YouTubeUnplayableReason.None,
            "YouTube 영상 정보를 확인했습니다.", title, duration, isLive, ageLimit, availability);

    internal static YouTubePreflightResult Failure(
        Uri? uri,
        YouTubePlaybackFailureKind kind,
        string message,
        YouTubeUnplayableReason reason = YouTubeUnplayableReason.None,
        bool isLive = false)
        => new(uri, false, kind, reason, message, null, null, isLive, null, null);
}

/// <summary>YouTube 영상 사전 확인 실패를 호출부가 분류할 수 있게 한다.</summary>
public sealed class YouTubePlaybackException : Exception
{
    public YouTubePlaybackException(
        YouTubePlaybackFailureKind kind,
        string message,
        YouTubeUnplayableReason reason = YouTubeUnplayableReason.None,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Reason = reason;
    }

    /// <summary>실패 종류를 가져온다.</summary>
    public YouTubePlaybackFailureKind Kind { get; }

    /// <summary>재생 불가 영상의 세부 이유를 가져온다.</summary>
    public YouTubeUnplayableReason Reason { get; }

    internal static YouTubePlaybackException InvalidUrl(string message)
        => new(YouTubePlaybackFailureKind.InvalidUrl, message);

    internal static YouTubePlaybackException MissingTool(string message)
        => new(YouTubePlaybackFailureKind.YtDlpMissing, message);

    internal static YouTubePlaybackException From(YouTubePreflightResult result)
        => new(result.FailureKind, result.Message, result.UnplayableReason);
}
