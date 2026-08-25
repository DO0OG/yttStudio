using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render;

/// <summary>Renders and measures active subtitles for an editor viewport.</summary>
public interface ISubtitleRenderer
{
    /// <summary>Gets the font resolutions observed while measuring or rendering.</summary>
    IReadOnlyList<FontResolution> FontResolutions { get; }

    /// <summary>Renders the active cues at the requested time.</summary>
    void Render(SKCanvas canvas, PlayerViewport viewport, SubtitleProject project, TimeSpan time, RenderOptions options);

    /// <summary>Measures active cue bounds for editor hit testing.</summary>
    IReadOnlyList<CueHitBox> Measure(PlayerViewport viewport, SubtitleProject project, TimeSpan time);
}

/// <summary>Identifies the preview geometry that has been measured against the real player.</summary>
public enum PreviewViewportMode
{
    /// <summary>Uses the video frame itself as the player and subtitle coordinate space.</summary>
    VideoFrame,

    /// <summary>YouTube's regular player chrome (awaits empirical measurement).</summary>
    YouTubeDefault,

    /// <summary>YouTube's theater player chrome (awaits empirical measurement).</summary>
    YouTubeTheater,

    /// <summary>YouTube's fullscreen player chrome (awaits empirical measurement).</summary>
    YouTubeFullscreen,

    /// <summary>YouTube's portrait mobile player chrome (awaits empirical measurement).</summary>
    MobilePortrait,
}

/// <summary>
/// Describes the player and the two coordinate spaces involved in subtitle placement.
/// </summary>
/// <remarks>
/// SPEC §7.8 deliberately does not infer geometry for browser modes before the empirical
/// fixture is measured. Call <see cref="VideoFrame(SKSize)"/> for the only mode with an
/// identity mapping today.
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

    /// <summary>Creates the identity video-frame viewport used by M1–M3.</summary>
    public PlayerViewport(SKSize playerSize)
        : this(playerSize, IdentityRect(playerSize), IdentityRect(playerSize), PreviewViewportMode.VideoFrame)
    {
    }

    /// <summary>
    /// Compatibility constructor for callers that previously supplied a drawable width and height.
    /// It is intentionally an identity VideoFrame viewport.
    /// </summary>
    public PlayerViewport(float width, float height)
        : this(new SKSize(width, height))
    {
    }

    public SKSize PlayerSize { get; }
    public SKRect VideoContentRect { get; }
    public SKRect SubtitleSpace { get; }
    public PreviewViewportMode Mode { get; }

    /// <summary>Gets the player width retained for source compatibility with the M1 API.</summary>
    public float Width => PlayerSize.Width;

    /// <summary>Gets the player height retained for source compatibility with the M1 API.</summary>
    public float Height => PlayerSize.Height;

    /// <summary>Creates a VideoFrame viewport whose subtitle space is exactly the frame bounds.</summary>
    public static PlayerViewport VideoFrame(SKSize playerSize)
        => new(playerSize, IdentityRect(playerSize), IdentityRect(playerSize), PreviewViewportMode.VideoFrame);

    /// <summary>Creates a VideoFrame viewport from pixel dimensions.</summary>
    public static PlayerViewport VideoFrame(float width, float height)
        => VideoFrame(new SKSize(width, height));

    /// <summary>Alias for callers that prefer a factory-style name.</summary>
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

/// <summary>Describes a measured cue and its screen-space geometry.</summary>
public sealed record CueHitBox(Cue Cue, SKRect Bounds, SKPoint AnchorScreenPoint);

/// <summary>Controls optional editor overlays during rendering.</summary>
public sealed record RenderOptions
{
    /// <summary>Gets whether the safe-area overlay is visible.</summary>
    public bool ShowSafeArea { get; init; }

    /// <summary>Gets whether anchor markers are visible.</summary>
    public bool ShowAnchorPoints { get; init; }

    /// <summary>Gets whether the SPEC §5.2 coordinate transform is applied.</summary>
    public bool ApplyCoordinateTransform { get; init; } = true;

    /// <summary>Gets the editor font-scale multiplier.</summary>
    public double FontScaleBase { get; init; } = 1.0;

    /// <summary>
    /// Gets the deterministic frame index used by time-varying effects such as Shake.
    /// </summary>
    /// <remarks>Callers that scrub should provide the same index for the same frame.</remarks>
    public long FrameIndex { get; init; }
}
