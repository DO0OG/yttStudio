using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render;

/// <summary>Contains deterministic layout data for one cue.</summary>
public sealed record CueLayout(
    Cue Cue,
    SKRect Bounds,
    SKPoint AnchorScreenPoint,
    IReadOnlyList<LineLayout> Lines,
    float ResolvedFontSize,
    bool ExceedsViewport);

/// <summary>Contains the bounds and baseline of one explicit subtitle line.</summary>
public sealed record LineLayout(SKRect Bounds, float Baseline, IReadOnlyList<RunLayout> Runs);

/// <summary>Contains the measured placement of one formatted text run.</summary>
public sealed record RunLayout(
    Section Section,
    ResolvedFormat Format,
    string Text,
    SKPoint Origin,
    SKRect Bounds,
    float Baseline,
    float FontSize);
