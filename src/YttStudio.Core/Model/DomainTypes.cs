namespace YttStudio.Core;

/// <summary>자막 박스에서 화면 좌표에 고정되는 지점을 식별한다.</summary>
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

/// <summary>자막 박스 내부의 텍스트 정렬을 제어한다.</summary>
public enum Justification
{
    Left = 0,
    Right = 1,
    Center = 2,
}

/// <summary>자막 텍스트의 인쇄 방향과 진행 방향을 제어한다.</summary>
public enum TextDirection
{
    Horizontal,
    HorizontalRtl,
    VerticalRightToLeft,
    VerticalLeftToRight,
    RotatedLeftToRight,
    RotatedRightToLeft,
}

/// <summary>YTT 가 지원하는 여덟 폰트 중 하나를 식별한다.</summary>
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

/// <summary>YTT 의 엣지 또는 그림자 처리를 식별한다.</summary>
public enum EdgeType
{
    None = 0,
    HardShadow = 1,
    Bevel = 2,
    Glow = 3,
    SoftShadow = 4,
}

/// <summary>보통과 아래첨자와 위첨자 배치를 식별한다.</summary>
public enum ScriptOffset
{
    Subscript = 0,
    Regular = 1,
    Superscript = 2,
}

/// <summary>YTT 루비 배치에서 섹션의 역할을 식별한다.</summary>
public enum RubyRole
{
    None,
    Base,
    Above,
    Below,
}

/// <summary>플랫폼 중립적이고 프리멀티플라이하지 않은 RGBA 색을 담는다.</summary>
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

    /// <summary>기본으로 쓰는 안전한 YTT 흰색을 가져온다.</summary>
    public static RgbaColor White { get; } = new(254, 254, 254, YttConstants.MaximumOpacity);

    /// <summary>투명한 검정을 가져온다.</summary>
    public static RgbaColor Transparent { get; } = new(0, 0, 0, 0);

    /// <summary>가라오케 보조 색 기본값을 가져온다.</summary>
    public static RgbaColor SecondaryDefault { get; } = new(180, 180, 180, YttConstants.MaximumOpacity);

    /// <summary>YTT 엣지 색 기본값을 가져온다.</summary>
    public static RgbaColor EdgeDefault { get; } = new(34, 34, 34, YttConstants.MaximumOpacity);
}

/// <summary>상속 가능한 완전한 자막 섹션 서식을 담는다.</summary>
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

/// <summary>명시적 섹션 재정의를 담는다. null 은 선택한 스타일에서 상속한다는 뜻이다.</summary>
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

    /// <summary>이 객체에 명시적 값이 하나도 없는지 가져온다.</summary>
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

    /// <summary>
    /// Creates a copy that changes only the explicit size override. All other
    /// inherited or explicitly overridden format values are preserved.
    /// </summary>
    public SectionOverrides WithSizePercent(int sizePercent)
    {
        SectionOverrides copy = Clone();
        copy.SizePercent = sizePercent;
        return copy;
    }

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

/// <summary>도메인 setter 를 노출하지 않고 적용할 명시적 서식 값을 기술한다.</summary>
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

/// <summary>렌더와 내보내기가 사용하는 완전히 해석된 서식을 담는다.</summary>
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

/// <summary>nullable 섹션 재정의를 완전한 기준 서식에 대해 해석한다.</summary>
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
