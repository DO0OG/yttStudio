namespace YttStudio.Core;

/// <summary>Identifies the point on a subtitle box fixed to its screen position.</summary>
public enum AnchorPoint
{
    TopLeft = 0,
    TopCenter = 1,
    TopRight = 2,
    MiddleLeft = 3,
    MiddleCenter = 4,
    MiddleRight = 5,
    BottomLeft = 6,
    BottomCenter = 7,
    BottomRight = 8,
}

/// <summary>Controls text alignment inside a subtitle box.</summary>
public enum Justification
{
    Left = 0,
    Right = 1,
    Center = 2,
}

/// <summary>Controls the print and progression direction of subtitle text.</summary>
public enum TextDirection
{
    Horizontal,
    HorizontalRtl,
    VerticalRightToLeft,
    VerticalLeftToRight,
    RotatedLeftToRight,
    RotatedRightToLeft,
}

/// <summary>Identifies one of the eight fonts supported by YTT.</summary>
public enum YtFont
{
    Default = 0,
    MonoSerif = 1,
    Serif = 2,
    MonoSans = 3,
    Sans = 4,
    Casual = 5,
    Cursive = 6,
    SmallCaps = 7,
}

/// <summary>Identifies the YTT edge or shadow treatment.</summary>
public enum EdgeType
{
    None = 0,
    HardShadow = 1,
    Bevel = 2,
    Glow = 3,
    SoftShadow = 4,
}

/// <summary>Identifies normal, subscript, or superscript placement.</summary>
public enum ScriptOffset
{
    Subscript = 0,
    Regular = 1,
    Superscript = 2,
}

/// <summary>Identifies a section's role in YTT ruby layout.</summary>
public enum RubyRole
{
    None,
    Base,
    Above,
    Below,
}

/// <summary>Stores a platform-neutral, non-premultiplied RGBA color.</summary>
public readonly record struct RgbaColor
{
    public RgbaColor(byte red, byte green, byte blue, byte alpha = YttConstants.MaximumOpacity)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    public byte Red { get; }
    public byte Green { get; }
    public byte Blue { get; }
    public byte Alpha { get; }

    /// <summary>Gets the safe YTT white used by default.</summary>
    public static RgbaColor White { get; } = new(254, 254, 254, YttConstants.MaximumOpacity);

    /// <summary>Gets transparent black.</summary>
    public static RgbaColor Transparent { get; } = new(0, 0, 0, 0);

    /// <summary>Gets the default secondary karaoke color.</summary>
    public static RgbaColor SecondaryDefault { get; } = new(180, 180, 180, YttConstants.MaximumOpacity);

    /// <summary>Gets the default YTT edge color.</summary>
    public static RgbaColor EdgeDefault { get; } = new(34, 34, 34, YttConstants.MaximumOpacity);
}

/// <summary>Contains a complete, inheritable subtitle section format.</summary>
public sealed class SectionFormat
{
    public YtFont Font { get; internal set; } = YtFont.Default;
    public int SizePercent { get; internal set; } = 100;
    public bool Bold { get; internal set; }
    public bool Italic { get; internal set; }
    public bool Underline { get; internal set; }
    public ScriptOffset Offset { get; internal set; } = ScriptOffset.Regular;
    public RgbaColor Foreground { get; internal set; } = RgbaColor.White;
    public RgbaColor Background { get; internal set; } = RgbaColor.Transparent;
    public RgbaColor SecondaryColor { get; internal set; } = RgbaColor.SecondaryDefault;
    public EdgeType Edge { get; internal set; } = EdgeType.Glow;
    public RgbaColor EdgeColor { get; internal set; } = RgbaColor.EdgeDefault;
    public bool Pack { get; internal set; }
}

/// <summary>Contains explicit section overrides; null means inherit from the selected style.</summary>
public sealed class SectionOverrides
{
    public YtFont? Font { get; internal set; }
    public int? SizePercent { get; internal set; }
    public bool? Bold { get; internal set; }
    public bool? Italic { get; internal set; }
    public bool? Underline { get; internal set; }
    public ScriptOffset? Offset { get; internal set; }
    public RgbaColor? Foreground { get; internal set; }
    public RgbaColor? Background { get; internal set; }
    public RgbaColor? SecondaryColor { get; internal set; }
    public EdgeType? Edge { get; internal set; }
    public RgbaColor? EdgeColor { get; internal set; }
    public bool? Pack { get; internal set; }

    /// <summary>Gets whether this object contains no explicit values.</summary>
    public bool IsEmpty => Font is null && SizePercent is null && Bold is null && Italic is null &&
        Underline is null && Offset is null && Foreground is null && Background is null &&
        SecondaryColor is null && Edge is null && EdgeColor is null && Pack is null;

    internal SectionOverrides Clone() => new()
    {
        Font = Font,
        SizePercent = SizePercent,
        Bold = Bold,
        Italic = Italic,
        Underline = Underline,
        Offset = Offset,
        Foreground = Foreground,
        Background = Background,
        SecondaryColor = SecondaryColor,
        Edge = Edge,
        EdgeColor = EdgeColor,
        Pack = Pack,
    };

    internal static SectionOverrides FromResolved(ResolvedFormat format) => new()
    {
        Font = format.Font,
        SizePercent = format.SizePercent,
        Bold = format.Bold,
        Italic = format.Italic,
        Underline = format.Underline,
        Offset = format.Offset,
        Foreground = format.Foreground,
        Background = format.Background,
        SecondaryColor = format.SecondaryColor,
        Edge = format.Edge,
        EdgeColor = format.EdgeColor,
        Pack = format.Pack,
    };
}

/// <summary>Describes explicit format values to apply without exposing domain setters.</summary>
public sealed record SectionFormatPatch
{
    public YtFont? Font { get; init; }
    public int? SizePercent { get; init; }
    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
    public bool? Underline { get; init; }
    public ScriptOffset? Offset { get; init; }
    public RgbaColor? Foreground { get; init; }
    public RgbaColor? Background { get; init; }
    public RgbaColor? SecondaryColor { get; init; }
    public EdgeType? Edge { get; init; }
    public RgbaColor? EdgeColor { get; init; }
    public bool? Pack { get; init; }
}

/// <summary>Contains the fully resolved format consumed by rendering and export.</summary>
public sealed record ResolvedFormat
{
    public ResolvedFormat(
        YtFont font,
        int sizePercent,
        bool bold,
        bool italic,
        bool underline,
        ScriptOffset offset,
        RgbaColor foreground,
        RgbaColor background,
        RgbaColor secondaryColor,
        EdgeType edge,
        RgbaColor edgeColor,
        bool pack)
    {
        Font = font;
        SizePercent = sizePercent;
        Bold = bold;
        Italic = italic;
        Underline = underline;
        Offset = offset;
        Foreground = foreground;
        Background = background;
        SecondaryColor = secondaryColor;
        Edge = edge;
        EdgeColor = edgeColor;
        Pack = pack;
    }

    public YtFont Font { get; }
    public int SizePercent { get; }
    public bool Bold { get; }
    public bool Italic { get; }
    public bool Underline { get; }
    public ScriptOffset Offset { get; }
    public RgbaColor Foreground { get; }
    public RgbaColor Background { get; }
    public RgbaColor SecondaryColor { get; }
    public EdgeType Edge { get; }
    public RgbaColor EdgeColor { get; }
    public bool Pack { get; }
}

/// <summary>Resolves nullable section overrides against a complete base format.</summary>
public static class FormatResolver
{
    public static ResolvedFormat Resolve(SectionFormat baseFormat, SectionOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(baseFormat);
        ArgumentNullException.ThrowIfNull(overrides);

        return new ResolvedFormat(
            overrides.Font ?? baseFormat.Font,
            overrides.SizePercent ?? baseFormat.SizePercent,
            overrides.Bold ?? baseFormat.Bold,
            overrides.Italic ?? baseFormat.Italic,
            overrides.Underline ?? baseFormat.Underline,
            overrides.Offset ?? baseFormat.Offset,
            overrides.Foreground ?? baseFormat.Foreground,
            overrides.Background ?? baseFormat.Background,
            overrides.SecondaryColor ?? baseFormat.SecondaryColor,
            overrides.Edge ?? baseFormat.Edge,
            overrides.EdgeColor ?? baseFormat.EdgeColor,
            overrides.Pack ?? baseFormat.Pack);
    }
}
