using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render;

/// <summary>Resolves a YTT font identifier to a concrete Skia typeface.</summary>
public interface IFontResolver
{
    FontResolution Resolve(YtFont requested);
}

/// <summary>Describes how a requested YTT font was resolved.</summary>
public sealed record FontResolution(
    YtFont Requested,
    SKTypeface Typeface,
    string ActualFamilyName,
    FontResolutionStatus Status)
{
    public bool IsApproximation => Status == FontResolutionStatus.ApproximateFallback;
}

/// <summary>Identifies the fidelity of a resolved font.</summary>
public enum FontResolutionStatus
{
    BundledExact,
    BundledMetricCompatible,
    SystemExact,
    ApproximateFallback,
}
