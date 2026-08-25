namespace YttStudio.Core;

/// <summary>Shared YTT format and editor reference constants.</summary>
public static class YttConstants
{
    // SPEC §5.2 [UPSTREAM]: YouTube applies specifiedCoord × 0.96 + 2.
    public const double CoordinateScale = 0.96;

    // SPEC §5.2 [UPSTREAM]: YouTube offsets converted coordinates by 2 percent.
    public const double CoordinateOffset = 2.0;

    // SPEC §5.6 [UPSTREAM]: YTT font scale changes at one quarter of the stored delta.
    public const double FontScaleDivisor = 4.0;

    // SPEC §5.7 [UPSTREAM]: opacity 255 is stripped on upload, so 254 is the safe maximum.
    public const byte MaximumOpacity = 254;

    // SPEC §5.5 [UPSTREAM]: t=0 is unreliable on Android and must be clamped to 1 ms.
    public const long MinimumStartTimeMilliseconds = 1;

    // SPEC §7.3 [UPSTREAM]: renderer calculations use the upstream 1280×720 reference frame.
    public const int ReferenceWidth = 1280;

    // SPEC §7.3 [UPSTREAM]: renderer calculations use the upstream 1280×720 reference frame.
    public const int ReferenceHeight = 720;

    // SPEC §7.3 [PRODUCT]: the editor uses 32 px at 720p as its 100 percent font baseline.
    public const double DefaultFontSizePixels = 32.0;
}
