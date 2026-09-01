using System.Drawing;
using YTSubConverter.Shared;
using YTSubConverter.Shared.Formats;
using YTSubConverter.Shared.Formats.Ass;
using ExternalAnchorPoint = YTSubConverter.Shared.AnchorPoint;
using ExternalSection = YTSubConverter.Shared.Section;
using ModelAnchorPoint = YttStudio.Core.AnchorPoint;
using ModelSection = YttStudio.Core.Section;

namespace YttStudio.Core.Format;

/// <summary>고정된 변환기를 통해 자막 형식을 가져오고 내보낸다.</summary>
public sealed partial class SubtitleFileService
{
    private static ModelSection ToModelSection(ExternalSection section, SectionFormat inheritedFormat)
    {
        ResolvedFormat fullFormat = ToResolvedFormat(section);
        SectionOverrides overrides = CreateOverrides(fullFormat, inheritedFormat);

        return new ModelSection
        {
            Text = section.Text,
            KaraokeOffset = section.StartOffset > TimeSpan.Zero ? section.StartOffset : null,
            Overrides = overrides,
            Ruby = section.RubyPart switch
            {
                RubyPart.Base => RubyRole.Base,
                RubyPart.TextBefore => RubyRole.Above,
                RubyPart.TextAfter => RubyRole.Below,
                _ => RubyRole.None,
            },
        };
    }

    private static ResolvedFormat ToResolvedFormat(ExternalSection section)
        => new(
            ToModelFont(section.Font),
            Math.Max(75, checked((int)Math.Round(section.Scale * 100, MidpointRounding.ToEven))),
            section.Bold,
            section.Italic,
            section.Underline,
            section.Offset switch
            {
                OffsetType.Subscript => ScriptOffset.Subscript,
                OffsetType.Superscript => ScriptOffset.Superscript,
                _ => ScriptOffset.Regular,
            },
            ToModelColor(section.ForeColor, RgbaColor.White),
            ToModelColor(section.BackColor, RgbaColor.Transparent),
            section is AssSection assSection && !assSection.SecondaryColor.IsEmpty
                ? ToModelColor(assSection.SecondaryColor, RgbaColor.SecondaryDefault)
                : RgbaColor.SecondaryDefault,
            section.ShadowColors.Count == 0 ? EdgeType.None : ToModelEdge(section.ShadowColors.First().Key),
            section.ShadowColors.Count == 0
                ? RgbaColor.EdgeDefault
                : ToModelColor(section.ShadowColors.First().Value, RgbaColor.EdgeDefault),
            section.Packed);

    private static SectionOverrides CreateOverrides(ResolvedFormat full, SectionFormat inherited)
        => new()
        {
            Font = full.Font == inherited.Font ? null : full.Font,
            SizePercent = full.SizePercent == inherited.SizePercent ? null : full.SizePercent,
            Bold = full.Bold == inherited.Bold ? null : full.Bold,
            Italic = full.Italic == inherited.Italic ? null : full.Italic,
            Underline = full.Underline == inherited.Underline ? null : full.Underline,
            Offset = full.Offset == inherited.Offset ? null : full.Offset,
            Foreground = full.Foreground == inherited.Foreground ? null : full.Foreground,
            Background = full.Background == inherited.Background ? null : full.Background,
            SecondaryColor = full.SecondaryColor == inherited.SecondaryColor ? null : full.SecondaryColor,
            Edge = full.Edge == inherited.Edge ? null : full.Edge,
            EdgeColor = full.EdgeColor == inherited.EdgeColor ? null : full.EdgeColor,
            Pack = full.Pack == inherited.Pack ? null : full.Pack,
        };

    private static RgbaColor ToModelColor(Color color, RgbaColor fallback)
        => color.IsEmpty ? fallback : new RgbaColor(color.R, color.G, color.B, color.A);

    private static Color ToExternalColor(RgbaColor color)
    {
        // [UPSTREAM] 알파 255 는 업로드 시 제거된다. YttDocument 도 이를 정규화한다.
        // 근거: YttDocument.LimitColors(), docs/YTT-VERIFICATION.md
        byte alpha = Math.Min(color.Alpha, YttConstants.MaximumOpacity);
        bool pureWhite = color.Red == byte.MaxValue && color.Green == byte.MaxValue && color.Blue == byte.MaxValue;
        byte red = pureWhite ? YttConstants.MaximumOpacity : color.Red;
        byte green = pureWhite ? YttConstants.MaximumOpacity : color.Green;
        byte blue = pureWhite ? YttConstants.MaximumOpacity : color.Blue;
        return Color.FromArgb(alpha, red, green, blue);
    }

    private static YtFont ToModelFont(string? font) => font?.ToLowerInvariant() switch
    {
        "courier new" or "courier" or "liberation mono" => YtFont.MonoSerif,
        "times new roman" or "times" or "liberation serif" => YtFont.Serif,
        "lucida console" or "consolas" or "dejavu sans mono" => YtFont.MonoSans,
        "comic sans ms" => YtFont.Casual,
        "monotype corsiva" => YtFont.Cursive,
        "carrois gothic sc" or "arial" or "liberation sans" => YtFont.SmallCaps,
        "roboto" => YtFont.Default,
        _ => YtFont.Default,
    };

    private static string ToExternalFont(YtFont font) => font switch
    {
        YtFont.MonoSerif => "Courier New",
        YtFont.Serif => "Times New Roman",
        YtFont.MonoSans => "Lucida Console",
        YtFont.Casual => "Comic Sans MS",
        YtFont.Cursive => "Monotype Corsiva",
        YtFont.SmallCaps => "Carrois Gothic SC",
        _ => "Roboto",
    };

    private static ModelAnchorPoint ToModelAnchor(ExternalAnchorPoint anchor) => anchor switch
    {
        ExternalAnchorPoint.TopLeft => ModelAnchorPoint.TopLeft,
        ExternalAnchorPoint.TopCenter => ModelAnchorPoint.TopCenter,
        ExternalAnchorPoint.TopRight => ModelAnchorPoint.TopRight,
        ExternalAnchorPoint.MiddleLeft => ModelAnchorPoint.MiddleLeft,
        ExternalAnchorPoint.Center => ModelAnchorPoint.MiddleCenter,
        ExternalAnchorPoint.MiddleRight => ModelAnchorPoint.MiddleRight,
        ExternalAnchorPoint.BottomLeft => ModelAnchorPoint.BottomLeft,
        ExternalAnchorPoint.BottomCenter => ModelAnchorPoint.BottomCenter,
        ExternalAnchorPoint.BottomRight => ModelAnchorPoint.BottomRight,
        _ => ModelAnchorPoint.BottomCenter,
    };

    private static ExternalAnchorPoint ToExternalAnchor(ModelAnchorPoint anchor) => anchor switch
    {
        ModelAnchorPoint.TopLeft => ExternalAnchorPoint.TopLeft,
        ModelAnchorPoint.TopCenter => ExternalAnchorPoint.TopCenter,
        ModelAnchorPoint.TopRight => ExternalAnchorPoint.TopRight,
        ModelAnchorPoint.MiddleLeft => ExternalAnchorPoint.MiddleLeft,
        ModelAnchorPoint.MiddleCenter => ExternalAnchorPoint.Center,
        ModelAnchorPoint.MiddleRight => ExternalAnchorPoint.MiddleRight,
        ModelAnchorPoint.BottomLeft => ExternalAnchorPoint.BottomLeft,
        ModelAnchorPoint.BottomCenter => ExternalAnchorPoint.BottomCenter,
        ModelAnchorPoint.BottomRight => ExternalAnchorPoint.BottomRight,
        _ => ExternalAnchorPoint.BottomCenter,
    };

    private static Justification AnchorToJustification(ExternalAnchorPoint anchor) => anchor switch
    {
        ExternalAnchorPoint.TopLeft or ExternalAnchorPoint.MiddleLeft or ExternalAnchorPoint.BottomLeft => Justification.Left,
        ExternalAnchorPoint.TopRight or ExternalAnchorPoint.MiddleRight or ExternalAnchorPoint.BottomRight => Justification.Right,
        _ => Justification.Center,
    };

    private static Justification ToModelJustification(int? justification, ExternalAnchorPoint anchor) => justification switch
    {
        0 => Justification.Left,
        1 => Justification.Right,
        2 => Justification.Center,
        _ => AnchorToJustification(anchor),
    };

    private static int ToExternalJustification(Justification justification) => justification switch
    {
        Justification.Left => 0,
        Justification.Right => 1,
        Justification.Center => 2,
        _ => 2,
    };

    private static TextDirection ToModelDirection(HorizontalTextDirection horizontal, VerticalTextType vertical)
        => (horizontal, vertical) switch
        {
            (HorizontalTextDirection.RightToLeft, VerticalTextType.None) => TextDirection.HorizontalRtl,
            (HorizontalTextDirection.RightToLeft, VerticalTextType.Positioned) => TextDirection.VerticalRightToLeft,
            (HorizontalTextDirection.LeftToRight, VerticalTextType.Positioned) => TextDirection.VerticalLeftToRight,
            (HorizontalTextDirection.LeftToRight, VerticalTextType.Rotated) => TextDirection.RotatedLeftToRight,
            (HorizontalTextDirection.RightToLeft, VerticalTextType.Rotated) => TextDirection.RotatedRightToLeft,
            _ => TextDirection.Horizontal,
        };

    private static void SetExternalDirection(Line line, TextDirection direction)
    {
        (line.HorizontalTextDirection, line.VerticalTextType) = direction switch
        {
            TextDirection.HorizontalRtl => (HorizontalTextDirection.RightToLeft, VerticalTextType.None),
            TextDirection.VerticalRightToLeft => (HorizontalTextDirection.RightToLeft, VerticalTextType.Positioned),
            TextDirection.VerticalLeftToRight => (HorizontalTextDirection.LeftToRight, VerticalTextType.Positioned),
            TextDirection.RotatedLeftToRight => (HorizontalTextDirection.LeftToRight, VerticalTextType.Rotated),
            TextDirection.RotatedRightToLeft => (HorizontalTextDirection.RightToLeft, VerticalTextType.Rotated),
            _ => (HorizontalTextDirection.LeftToRight, VerticalTextType.None),
        };
    }

    private static EdgeType ToModelEdge(ShadowType edge) => edge switch
    {
        ShadowType.HardShadow => EdgeType.HardShadow,
        ShadowType.Bevel => EdgeType.Bevel,
        ShadowType.Glow => EdgeType.Glow,
        ShadowType.SoftShadow => EdgeType.SoftShadow,
        _ => EdgeType.None,
    };

    private static ShadowType ToExternalEdge(EdgeType edge) => edge switch
    {
        EdgeType.HardShadow => ShadowType.HardShadow,
        EdgeType.Bevel => ShadowType.Bevel,
        EdgeType.SoftShadow => ShadowType.SoftShadow,
        _ => ShadowType.Glow,
    };

}
