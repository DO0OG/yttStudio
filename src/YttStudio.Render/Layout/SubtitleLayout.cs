using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render;

/// <summary>큐 하나의 결정적 레이아웃 데이터를 담는다.</summary>
public sealed record CueLayout(
    Cue Cue,
    SKRect Bounds,
    SKPoint AnchorScreenPoint,
    IReadOnlyList<LineLayout> Lines,
    float ResolvedFontSize,
    bool ExceedsViewport);

/// <summary>명시적 자막 줄 하나의 경계와 베이스라인을 담는다.</summary>
public sealed record LineLayout(SKRect Bounds, float Baseline, IReadOnlyList<RunLayout> Runs);

/// <summary>서식이 적용된 텍스트 런 하나의 측정된 배치를 담는다.</summary>
public sealed record RunLayout(
    Section Section,
    ResolvedFormat Format,
    string Text,
    SKPoint Origin,
    SKRect Bounds,
    float Baseline,
    float FontSize);
