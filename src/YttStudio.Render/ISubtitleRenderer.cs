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

    /// <summary>유튜브 일반 플레이어 영역이다. 실측 대기 중이다.</summary>
    YouTubeDefault,

    /// <summary>유튜브 극장 플레이어 영역이다. 실측 대기 중이다.</summary>
    YouTubeTheater,

    /// <summary>유튜브 전체화면 플레이어 영역이다. 실측 대기 중이다.</summary>
    YouTubeFullscreen,

    /// <summary>유튜브 세로 모바일 플레이어 영역이다. 실측 대기 중이다.</summary>
    MobilePortrait,
}

/// <summary>
/// 플레이어와 자막 배치에 관여하는 두 좌표 공간을 기술한다.
/// </summary>
/// <remarks>
/// 브라우저 모드의 기하는 실측 전까지 의도적으로 추정하지 않는다.
/// 기준 픽스처를 측정하기 전까지는 기하를 추정하지 않는다. 확정된 모드는
/// 현재 항등 매핑뿐이다.
/// </remarks>
public sealed record PlayerViewport
{
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
}
