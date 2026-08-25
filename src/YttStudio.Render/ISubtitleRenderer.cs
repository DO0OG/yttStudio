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

/// <summary>Describes the drawable player area in pixels.</summary>
public readonly record struct PlayerViewport(float Width, float Height);

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
}
