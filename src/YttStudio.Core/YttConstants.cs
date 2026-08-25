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

    // SPEC §7.5 [PRODUCT]: horizontal box padding is a quarter of the resolved font size.
    public const double HorizontalBoxPaddingFactor = 0.25;

    // SPEC §7.5 [PRODUCT]: vertical box padding is 15 percent of the resolved font size.
    public const double VerticalBoxPaddingFactor = 0.15;

    // SPEC §7.5 [PRODUCT]: subscript and superscript use 65 percent glyphs.
    public const double ScriptFontScale = 0.65;

    // SPEC §7.5 [PRODUCT]: script glyphs move by 30 percent of the base font size.
    public const double ScriptBaselineOffsetFactor = 0.30;

    // SPEC §7.5 [PRODUCT]: hard shadow offset is six percent of the font size.
    public const double HardShadowOffsetFactor = 0.06;

    // SPEC §7.5 [PRODUCT]: glow stroke width is eight percent of the font size.
    public const double GlowStrokeWidthFactor = 0.08;

    // SPEC §7.5 [PRODUCT]: soft-shadow blur is ten percent of the font size.
    public const double SoftShadowBlurFactor = 0.10;

    // SPEC §7.5 [PRODUCT]: underline thickness is one sixteenth of the font size.
    public const double UnderlineThicknessFactor = 1.0 / 16.0;

    // SPEC §9.3 [PRODUCT]: canvas snapping uses an eight-pixel default threshold.
    public const double DefaultSnapThresholdPixels = 8.0;

    // SPEC §9.3 [PRODUCT]: the default editor safe area is five percent on each edge.
    public const double DefaultSafeAreaPercent = 5.0;

    // SPEC §13 [PRODUCT]: undo and redo history retains at most 200 operations.
    public const int MaximumUndoDepth = 200;

    // SPEC §5.5 [UPSTREAM]: t=0 is unreliable on Android and is clamped to 1 ms.
    public const long MinimumCueStartMilliseconds = 1;

    // SPEC §5.6 [UPSTREAM]: the serialized YTT size cannot represent a value below 75%.
    public const int MinimumFontSizePercent = 75;

    // SPEC §9.4 [PRODUCT]: 200% is the recommended UX ceiling, not a format ceiling.
    public const int RecommendedFontSizePercent = 200;

    // SPEC §11.1 [UPSTREAM]: the upstream browser gate is 10240 compressed bits/sec.
    public const double UpstreamCompressedBitsPerSecondLimit = 10240.0;

    // SPEC §11.1 [PRODUCT]: warn at 70% of the upstream gate because the estimator is approximate.
    public const double SizeRiskSafetyMargin = 0.70;

    // SPEC §11.1 [PRODUCT]: 0.70 × 10240 = 7168 bit/s.
    public const double SizeRiskBitsPerSecondThreshold =
        UpstreamCompressedBitsPerSecondLimit * SizeRiskSafetyMargin;

    // SPEC §11.2 [PRODUCT]: a luminance below this value is treated as dark text when no
    // measured renderer metric is supplied. This is a conservative accessibility heuristic.
    public const double DarkTextLuminanceThreshold = 0.25;
}
