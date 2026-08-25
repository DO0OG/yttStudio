using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render;

/// <summary>YTT 폰트 식별자를 실제 Skia 타입페이스로 해석한다.</summary>
public interface IFontResolver
{
    FontResolution Resolve(YtFont requested);
}

/// <summary>요청한 YTT 폰트가 어떻게 해석되었는지 기술한다.</summary>
public sealed record FontResolution(
    YtFont Requested,
    SKTypeface Typeface,
    string ActualFamilyName,
    FontResolutionStatus Status)
{
    public bool IsApproximation => Status == FontResolutionStatus.ApproximateFallback;
}

/// <summary>해석된 폰트의 정확도를 식별한다.</summary>
public enum FontResolutionStatus
{
    BundledExact,
    BundledMetricCompatible,
    SystemExact,
    ApproximateFallback,
}
