using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render;

/// <summary>편집기 뷰포트에 맞춰 활성 자막을 렌더하고 측정한다.</summary>
public interface ISubtitleRenderer
{
    /// <summary>측정이나 렌더 중 관찰한 폰트 해석 결과를 가져온다.</summary>
    IReadOnlyList<FontResolution> FontResolutions { get; }

    /// <summary>요청한 시각의 활성 큐를 렌더한다.</summary>
    void Render(SKCanvas canvas, PlayerViewport viewport, SubtitleProject project, TimeSpan time, RenderOptions options);

    /// <summary>편집기 히트 테스트를 위해 활성 큐의 경계를 측정한다.</summary>
    IReadOnlyList<CueHitBox> Measure(PlayerViewport viewport, SubtitleProject project, TimeSpan time);
}

/// <summary>실제 플레이어와 대조해 측정한 프리뷰 기하를 식별한다.</summary>
public enum PreviewViewportMode
{
    /// <summary>영상 프레임 자체를 플레이어이자 자막 좌표 공간으로 쓴다.</summary>
    VideoFrame,

    /// <summary>유튜브 일반 플레이어 영역이다. 크기는 실측한 한 표본에서 가져왔다.</summary>
    YouTubeDefault,

    /// <summary>유튜브 극장 플레이어 영역이다. 크기는 실측한 한 표본에서 가져왔다.</summary>
    YouTubeTheater,

    /// <summary>호출자가 실제 플레이어 크기를 제공하는 유튜브 전체화면 영역이다.</summary>
    YouTubeFullscreen,

    /// <summary>유튜브 세로 모바일 플레이어 영역이다. 실측 대기 중이다.</summary>
    MobilePortrait,
}

/// <summary>
/// 플레이어와 자막 배치에 관여하는 두 좌표 공간을 기술한다.
/// </summary>
/// <remarks>
/// 일반·극장 모드는 실측한 플레이어 크기를 사용한다. 전체화면 크기는 실측값을
/// 추정하지 않고 호출자가 제공하며, 모바일 세로는 실측 전까지 팩터리를 만들지 않는다.
/// </remarks>
public sealed record PlayerViewport
{
    // 아래 네 값은 docs/viewport-modes.md 측정 당시의 플레이어 크기다. 유튜브가 정한
    // 고정 규격이 아니라 창 크기에 딸린 한 표본이며, 브라우저 창이 다르면 값도 달라진다.
    // 실측에서 확인된 것은 크기 자체가 아니라 글자 크기가 너비에 비례한다는 관계이고,
    // 데스크톱 모드끼리는 서로 닮음이라 어느 표본을 쓰든 그림은 같다. 대표값으로만 쓴다.
    private const float YouTubeDefaultPlayerWidth = 794f;
    private const float YouTubeDefaultPlayerHeight = 437.5f;
    private const float YouTubeTheaterPlayerWidth = 1162f;
    private const float YouTubeTheaterPlayerHeight = 634f;

    public PlayerViewport(SKSize playerSize, SKRect videoContentRect, SKRect subtitleSpace,
        PreviewViewportMode mode)
    {
        ValidateSize(playerSize);
        ValidateRect(videoContentRect, nameof(videoContentRect));
        ValidateRect(subtitleSpace, nameof(subtitleSpace));

        PlayerSize = playerSize;
        VideoContentRect = videoContentRect;
        SubtitleSpace = subtitleSpace;
        Mode = mode;
    }

    /// <summary>기본 영상 프레임 뷰포트를 만든다.</summary>
    public PlayerViewport(SKSize playerSize)
        : this(playerSize, IdentityRect(playerSize), IdentityRect(playerSize), PreviewViewportMode.VideoFrame)
    {
    }

    /// <summary>
    /// 이전에 그리기 영역의 너비와 높이를 넘기던 호출자를 위한 호환 생성자다.
    /// 의도적으로 항등 VideoFrame 뷰포트다.
    /// </summary>
    public PlayerViewport(float width, float height)
        : this(new SKSize(width, height))
    {
    }

    public SKSize PlayerSize { get; }
    public SKRect VideoContentRect { get; }
    public SKRect SubtitleSpace { get; }
    public PreviewViewportMode Mode { get; }

    /// <summary>이전 API 와의 소스 호환을 위해 남겨둔 플레이어 너비를 가져온다.</summary>
    public float Width => PlayerSize.Width;

    /// <summary>이전 API 와의 소스 호환을 위해 남겨둔 플레이어 높이를 가져온다.</summary>
    public float Height => PlayerSize.Height;

    /// <summary>자막 공간이 프레임 경계와 정확히 같은 VideoFrame 뷰포트를 만든다.</summary>
    public static PlayerViewport VideoFrame(SKSize playerSize)
        => new(playerSize, IdentityRect(playerSize), IdentityRect(playerSize), PreviewViewportMode.VideoFrame);

    /// <summary>픽셀 크기로 VideoFrame 뷰포트를 만든다.</summary>
    public static PlayerViewport VideoFrame(float width, float height)
        => VideoFrame(new SKSize(width, height));

    /// <summary>팩터리 형태 이름을 선호하는 호출자를 위한 별칭이다.</summary>
    public static PlayerViewport ForVideoFrame(SKSize playerSize) => VideoFrame(playerSize);

    /// <summary>문서에서 측정한 유튜브 일반 플레이어 뷰포트를 만든다.</summary>
    public static PlayerViewport YouTubeDefault()
        => CreateMeasuredViewport(
            new SKSize(YouTubeDefaultPlayerWidth, YouTubeDefaultPlayerHeight),
            PreviewViewportMode.YouTubeDefault,
            videoAspectRatio: null);

    /// <summary>유튜브 일반 플레이어에 영상 종횡비를 적용한 뷰포트를 만든다.</summary>
    public static PlayerViewport YouTubeDefault(SKSize videoSize)
        => CreateMeasuredViewport(
            new SKSize(YouTubeDefaultPlayerWidth, YouTubeDefaultPlayerHeight),
            PreviewViewportMode.YouTubeDefault,
            GetAspectRatio(videoSize));

    /// <summary>유튜브 극장 플레이어 뷰포트를 만든다.</summary>
    public static PlayerViewport YouTubeTheater()
        => CreateMeasuredViewport(
            new SKSize(YouTubeTheaterPlayerWidth, YouTubeTheaterPlayerHeight),
            PreviewViewportMode.YouTubeTheater,
            videoAspectRatio: null);

    /// <summary>유튜브 극장 플레이어에 영상 종횡비를 적용한 뷰포트를 만든다.</summary>
    public static PlayerViewport YouTubeTheater(SKSize videoSize)
        => CreateMeasuredViewport(
            new SKSize(YouTubeTheaterPlayerWidth, YouTubeTheaterPlayerHeight),
            PreviewViewportMode.YouTubeTheater,
            GetAspectRatio(videoSize));

    /// <summary>
    /// 호출자가 제공한 플레이어 크기로 유튜브 전체화면 뷰포트를 만든다.
    /// 전체화면 크기는 실측되지 않았으므로 기본값을 두지 않는다.
    /// </summary>
    public static PlayerViewport YouTubeFullscreen(SKSize playerSize)
        => CreateMeasuredViewport(playerSize, PreviewViewportMode.YouTubeFullscreen, videoAspectRatio: null);

    /// <summary>호출자가 제공한 플레이어와 영상 크기로 전체화면 뷰포트를 만든다.</summary>
    public static PlayerViewport YouTubeFullscreen(SKSize playerSize, SKSize videoSize)
        => CreateMeasuredViewport(playerSize, PreviewViewportMode.YouTubeFullscreen, GetAspectRatio(videoSize));

    private static PlayerViewport CreateMeasuredViewport(
        SKSize playerSize,
        PreviewViewportMode mode,
        float? videoAspectRatio)
    {
        ValidateSize(playerSize);
        SKRect playerRect = IdentityRect(playerSize);
        SKRect videoRect = videoAspectRatio is float aspect
            ? AspectFit(playerRect, aspect)
            : playerRect;
        return new PlayerViewport(playerSize, videoRect, playerRect, mode);
    }

    private static float GetAspectRatio(SKSize videoSize)
    {
        ValidateSize(videoSize);
        return videoSize.Width / videoSize.Height;
    }

    private static float ToSingleAspectRatio(double videoAspectRatio)
    {
        if (!double.IsFinite(videoAspectRatio) || videoAspectRatio <= 0 ||
            videoAspectRatio > float.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(videoAspectRatio),
                "Video aspect ratio must be finite and positive.");
        }

        return (float)videoAspectRatio;
    }

    private static SKRect AspectFit(SKRect playerRect, float videoAspectRatio)
    {
        if (!float.IsFinite(videoAspectRatio) || videoAspectRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(videoAspectRatio),
                "Video aspect ratio must be finite and positive.");
        }

        float playerAspectRatio = playerRect.Width / playerRect.Height;
        if (playerAspectRatio > videoAspectRatio)
        {
            float height = playerRect.Height;
            float width = height * videoAspectRatio;
            float left = playerRect.Left + ((playerRect.Width - width) / 2);
            return SKRect.Create(left, playerRect.Top, width, height);
        }

        float widthToFit = playerRect.Width;
        float heightToFit = widthToFit / videoAspectRatio;
        float top = playerRect.Top + ((playerRect.Height - heightToFit) / 2);
        return SKRect.Create(playerRect.Left, top, widthToFit, heightToFit);
    }

    private static SKRect IdentityRect(SKSize size) => SKRect.Create(size.Width, size.Height);

    private static void ValidateSize(SKSize size)
    {
        if (!float.IsFinite(size.Width) || !float.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Player dimensions must be finite and positive.");
        }
    }

    private static void ValidateRect(SKRect rect, string parameterName)
    {
        if (!float.IsFinite(rect.Left) || !float.IsFinite(rect.Top) ||
            !float.IsFinite(rect.Right) || !float.IsFinite(rect.Bottom) ||
            rect.Width <= 0 || rect.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName,
                "Viewport rectangles must be finite and have positive dimensions.");
        }
    }
}

/// <summary>측정된 큐와 그 화면 공간 기하를 기술한다.</summary>
public sealed record CueHitBox(Cue Cue, SKRect Bounds, SKPoint AnchorScreenPoint);

/// <summary>렌더 중 선택적 편집기 오버레이를 제어한다.</summary>
public sealed record RenderOptions
{
    /// <summary>세이프 에어리어 오버레이가 보이는지 가져온다.</summary>
    public bool ShowSafeArea { get; init; }

    /// <summary>앵커 마커가 보이는지 가져온다.</summary>
    public bool ShowAnchorPoints { get; init; }

    /// <summary>플레이어 좌표 변환을 적용하는지 가져온다.</summary>
    public bool ApplyCoordinateTransform { get; init; } = true;

    /// <summary>편집기 폰트 배율 계수를 가져온다.</summary>
    public double FontScaleBase { get; init; } = 1.0;

    /// <summary>
    /// 흔들림처럼 시간에 따라 변하는 효과가 쓰는 결정적 프레임 인덱스를 가져온다.
    /// </summary>
    /// <remarks>스크럽하는 호출자는 같은 프레임에 같은 인덱스를 주어야 한다.</remarks>
    public long FrameIndex { get; init; }

    /// <summary>
    /// During inline editing, keep this cue in measurement and hit testing
    /// while omitting only its raster draw calls.
    /// </summary>
    public Guid? EditingCueId { get; init; }
}
