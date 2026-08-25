using System.Drawing;
using System.Text.RegularExpressions;
using System.Xml;
using YTSubConverter.Shared;
using YTSubConverter.Shared.Formats;
using YTSubConverter.Shared.Formats.Ass;
using ExternalAnchorPoint = YTSubConverter.Shared.AnchorPoint;
using ExternalSection = YTSubConverter.Shared.Section;
using ModelAnchorPoint = YttStudio.Core.AnchorPoint;
using ModelSection = YttStudio.Core.Section;

namespace YttStudio.Core.Format;

/// <summary>Imports and exports the M1 subtitle formats through the pinned converter.</summary>
public sealed partial class SubtitleFileService
{
    private static readonly HashSet<string> SupportedAssTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "b", "i", "u", "fn", "fs", "c", "1c", "2c", "3c", "4c",
        "1a", "2a", "3a", "4a", "alpha", "pos", "an", "k", "r",
        "fad", "fade", "move", "t", "ytsub", "ytsup", "ytsur", "ytruby",
        "ytvert", "ytdir", "ytpack", "ytshake", "ytchroma", "ytkt",
    };

    /// <summary>Imports a .ytt, .srv3, or .ass document.</summary>
    public ImportResult Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".ytt" or ".srv3" => ImportYtt(path),
            ".ass" => ImportAss(path),
            _ => throw new NotSupportedException($"The '{extension}' format is outside the M1 import scope."),
        };
    }

    /// <summary>Exports a project to .ytt, .srv3, or .ass using upstream Save methods.</summary>
    public void Export(SubtitleProject project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        AdapterDocument adapterDocument = ToExternalDocument(project);
        AssDocument assDocument = new(adapterDocument);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        switch (extension)
        {
            case ".ytt":
            case ".srv3":
                // SPEC §5.1/§5.9 [UPSTREAM]: Save owns preprocessing, pool IDs, dummy items, and head order.
                // 근거: YttDocument.Save()/WriteHead(), docs/YTT-VERIFICATION.md.
                new YttDocument(assDocument).Save(path);
                break;
            case ".ass":
                assDocument.Save(path);
                break;
            default:
                throw new NotSupportedException($"The '{extension}' format is outside the M1 export scope.");
        }
    }

    private static ImportResult ImportYtt(string path)
    {
        YttDocument document = new(path);
        IReadOnlyList<Justification> justifications = ReadYttJustifications(path);
        SubtitleProject project = FromExternalDocument(document, justifications);
        ImportWarning[] warnings =
        [
            new("효과 정보 없이 가져옴"),
            new("YTT에는 ZOrder/Track 정보가 없어 원본 등장 순서로 가져옴"),
        ];
        return new ImportResult(project, warnings);
    }

    private static ImportResult ImportAss(string path)
    {
        IReadOnlyList<ImportWarning> warnings = FindUnsupportedAssTags(path);
        AssDocument document = new(path);
        SubtitleProject project = FromExternalDocument(document, null);
        return new ImportResult(project, warnings);
    }

    private static SubtitleProject FromExternalDocument(
        SubtitleDocument document,
        IReadOnlyList<Justification>? yttJustifications)
    {
        SubtitleProject project = new();
        project.Video = new VideoInfo(document.VideoDimensions.Width, document.VideoDimensions.Height, TimeSpan.Zero, 0);
        IReadOnlyList<StylePreset>? importedStyles = document is AssDocument assDocument
            ? ImportAssStyles(project, assDocument)
            : null;

        for (int lineIndex = 0; lineIndex < document.Lines.Count; lineIndex++)
        {
            Line line = document.Lines[lineIndex];
            Cue cue = new(Guid.NewGuid())
            {
                Start = line.Start - SubtitleDocument.TimeBase,
                End = line.End - SubtitleDocument.TimeBase,
                Anchor = ToModelAnchor(line.AnchorPoint),
                Justify = yttJustifications is not null && lineIndex < yttJustifications.Count
                    ? yttJustifications[lineIndex]
                    : AnchorToJustification(line.AnchorPoint),
                Direction = ToModelDirection(line.HorizontalTextDirection, line.VerticalTextType),
                Track = line is AssLine assLine ? assLine.Layer : 0,
                ZOrder = line is AssLine zOrderLine ? zOrderLine.Layer : lineIndex,
            };

            PointF position = line.Position ?? document.GetDefaultPosition(line.AnchorPoint);
            cue.PositionX = YttMath.ToYttCoordinate(position.X, document.VideoDimensions.Width);
            cue.PositionY = YttMath.ToYttCoordinate(position.Y, document.VideoDimensions.Height);

            for (int sectionIndex = 0; sectionIndex < line.Sections.Count; sectionIndex++)
            {
                ExternalSection externalSection = line.Sections[sectionIndex];
                ResolvedFormat fullFormat = ToResolvedFormat(externalSection);
                StylePreset? matchedStyle = importedStyles?.FirstOrDefault(style =>
                    FormatResolver.Resolve(style.BaseFormat, new SectionOverrides()) == fullFormat);
                if (sectionIndex == 0 && matchedStyle is not null)
                {
                    cue.StyleId = matchedStyle.Id == Guid.Empty ? null : matchedStyle.Id;
                }

                StylePreset cueStyle = project.GetStyle(cue.StyleId);
                SectionFormat inheritedFormat = matchedStyle?.BaseFormat ?? cueStyle.BaseFormat;
                ModelSection modelSection = ToModelSection(externalSection, inheritedFormat);
                if (matchedStyle is not null && matchedStyle.Id != cueStyle.Id)
                {
                    modelSection.StyleIdOverride = matchedStyle.Id;
                }
                if (externalSection.RubyPart == RubyPart.Base && sectionIndex + 3 < line.Sections.Count &&
                    line.Sections[sectionIndex + 1].RubyPart == RubyPart.Parenthesis &&
                    line.Sections[sectionIndex + 3].RubyPart == RubyPart.Parenthesis &&
                    line.Sections[sectionIndex + 2].RubyPart is RubyPart.TextBefore or RubyPart.TextAfter)
                {
                    ExternalSection rubySection = line.Sections[sectionIndex + 2];
                    modelSection.Ruby = rubySection.RubyPart == RubyPart.TextAfter ? RubyRole.Below : RubyRole.Above;
                    modelSection.RubyText = rubySection.Text;
                    sectionIndex += 3;
                }

                cue.AddSection(modelSection);
            }

            if (cue.Sections.Count == 0)
            {
                cue.AddSection(new ModelSection());
            }

            project.Cues.Add(cue);
        }

        return project;
    }

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

    private static IReadOnlyList<StylePreset> ImportAssStyles(SubtitleProject project, AssDocument document)
    {
        float defaultLineHeight = Math.Max(document.DefaultStyle.LineHeight, float.Epsilon);
        List<StylePreset> result = [];
        foreach (AssStyle externalStyle in document.Styles)
        {
            bool isDefault = ReferenceEquals(externalStyle, document.DefaultStyle);
            StylePreset style = isDefault ? project.Styles.Default : new StylePreset(Guid.NewGuid());
            style.Name = externalStyle.Name;
            style.BaseFormat = FromAssStyle(externalStyle, defaultLineHeight);
            style.DefaultAnchor = ToModelAnchor(externalStyle.AnchorPoint);
            style.DefaultJustify = AnchorToJustification(externalStyle.AnchorPoint);
            if (!isDefault)
            {
                project.Styles.Add(style);
            }

            result.Add(style);
        }

        return result;
    }

    private static SectionFormat FromAssStyle(AssStyle style, float defaultLineHeight)
    {
        EdgeType edge = style.HasOutline && !style.OutlineIsBox
            ? EdgeType.Glow
            : style.HasShadow ? EdgeType.SoftShadow : EdgeType.None;
        Color edgeColor = style.HasOutline && !style.OutlineIsBox ? style.OutlineColor : style.ShadowColor;
        return new SectionFormat
        {
            Font = ToModelFont(style.Font),
            // SPEC §6.5 [UPSTREAM]: Default is 100%; all ASS styles are relative to its line height.
            // 근거: AssDocument.cs style.Scale calculation, docs/YTT-VERIFICATION.md.
            SizePercent = Math.Max(75, checked((int)Math.Round(style.LineHeight / defaultLineHeight * 100))),
            Bold = style.Bold,
            Italic = style.Italic,
            Underline = style.Underline,
            Foreground = ToModelColor(style.PrimaryColor, RgbaColor.White),
            Background = style.HasOutlineBox
                ? ToModelColor(style.OutlineColor, RgbaColor.Transparent)
                : RgbaColor.Transparent,
            SecondaryColor = ToModelColor(style.SecondaryColor, RgbaColor.SecondaryDefault),
            Edge = edge,
            EdgeColor = ToModelColor(edgeColor, RgbaColor.EdgeDefault),
        };
    }

    private static AdapterDocument ToExternalDocument(SubtitleProject project)
    {
        int width = project.Video?.Width is > 0 ? project.Video.Width : YttConstants.ReferenceWidth;
        int height = project.Video?.Height is > 0 ? project.Video.Height : YttConstants.ReferenceHeight;
        AdapterDocument document = new(width, height);

        foreach (Cue cue in project.Cues.OrderBy(cue => cue.ZOrder).ThenBy(cue => cue.Start))
        {
            Line line = new(
                SubtitleDocument.TimeBase + cue.Start,
                SubtitleDocument.TimeBase + cue.End)
            {
                AnchorPoint = ToExternalAnchor(cue.Anchor),
                Position = new PointF(
                    (float)YttMath.ToPixelCoordinate(checked((int)Math.Round(cue.PositionX)), width),
                    (float)YttMath.ToPixelCoordinate(checked((int)Math.Round(cue.PositionY)), height)),
            };
            SetExternalDirection(line, cue.Direction);

            foreach (ModelSection section in cue.Sections)
            {
                StylePreset style = project.GetStyle(section.StyleIdOverride ?? cue.StyleId);
                ResolvedFormat format = FormatResolver.Resolve(style.BaseFormat, section.Overrides);
                AssSection externalSection = ToExternalSection(section, format);
                if (!string.IsNullOrEmpty(section.RubyText) && section.Ruby is RubyRole.Above or RubyRole.Below)
                {
                    externalSection.RubyPart = RubyPart.Base;
                    line.Sections.Add(externalSection);
                    line.Sections.Add(CloneRubyPart(externalSection, "(", RubyPart.Parenthesis));
                    line.Sections.Add(CloneRubyPart(externalSection, section.RubyText,
                        section.Ruby == RubyRole.Below ? RubyPart.TextAfter : RubyPart.TextBefore));
                    line.Sections.Add(CloneRubyPart(externalSection, ")", RubyPart.Parenthesis));
                }
                else
                {
                    line.Sections.Add(externalSection);
                }
            }

            document.Lines.Add(new AssLine(line) { Layer = cue.Track });
        }

        return document;
    }

    private static AssSection ToExternalSection(ModelSection section, ResolvedFormat format)
    {
        AssSection externalSection = new(section.Text)
        {
            Font = ToExternalFont(format.Font),
            Scale = Math.Max(format.SizePercent, 75) / 100f,
            Bold = format.Bold,
            Italic = format.Italic,
            Underline = format.Underline,
            Offset = format.Offset switch
            {
                ScriptOffset.Subscript => OffsetType.Subscript,
                ScriptOffset.Superscript => OffsetType.Superscript,
                _ => OffsetType.Regular,
            },
            ForeColor = ToExternalColor(format.Foreground),
            BackColor = ToExternalColor(format.Background),
            SecondaryColor = ToExternalColor(format.SecondaryColor),
            Packed = format.Pack,
            StartOffset = section.KaraokeOffset ?? TimeSpan.Zero,
            RubyPart = section.Ruby switch
            {
                RubyRole.Base => RubyPart.Base,
                RubyRole.Above => RubyPart.TextBefore,
                RubyRole.Below => RubyPart.TextAfter,
                _ => RubyPart.None,
            },
        };

        if (format.Edge != EdgeType.None)
        {
            externalSection.ShadowColors.Add(ToExternalEdge(format.Edge), ToExternalColor(format.EdgeColor));
        }

        return externalSection;
    }

    private static AssSection CloneRubyPart(AssSection source, string text, RubyPart rubyPart)
    {
        AssSection clone = (AssSection)source.Clone();
        clone.Text = text;
        clone.RubyPart = rubyPart;
        return clone;
    }

    private static IReadOnlyList<ImportWarning> FindUnsupportedAssTags(string path)
    {
        List<ImportWarning> warnings = [];
        string[] lines = File.ReadAllLines(path);
        for (int index = 0; index < lines.Length; index++)
        {
            if (!lines[index].StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (Match block in OverrideBlockRegex().Matches(lines[index]))
            {
                foreach (Match match in AssTagRegex().Matches(block.Value))
                {
                    string tagName = match.Groups[1].Value;
                    if (!SupportedAssTags.Contains(tagName))
                    {
                        warnings.Add(new ImportWarning(
                            $"지원하지 않는 ASS 태그 \\{tagName} (줄 {index + 1})",
                            $"\\{tagName}",
                            index + 1));
                    }
                }
            }
        }

        return warnings;
    }

    private static IReadOnlyList<Justification> ReadYttJustifications(string path)
    {
        XmlDocument xml = new();
        xml.Load(path);
        Dictionary<int, Justification> styles = [];
        foreach (XmlElement element in xml.SelectNodes("/timedtext/head/ws")!.OfType<XmlElement>())
        {
            if (int.TryParse(element.GetAttribute("id"), out int id) &&
                int.TryParse(element.GetAttribute("ju"), out int justification) &&
                Enum.IsDefined((Justification)justification))
            {
                styles[id] = (Justification)justification;
            }
        }

        List<Justification> result = [];
        foreach (XmlElement element in xml.SelectNodes("/timedtext/body/p")!.OfType<XmlElement>())
        {
            result.Add(int.TryParse(element.GetAttribute("ws"), out int styleId) && styles.TryGetValue(styleId, out Justification value)
                ? value
                : Justification.Center);
        }

        return result;
    }

    private static RgbaColor ToModelColor(Color color, RgbaColor fallback)
        => color.IsEmpty ? fallback : new RgbaColor(color.R, color.G, color.B, color.A);

    private static Color ToExternalColor(RgbaColor color)
    {
        // SPEC §5.7 [UPSTREAM]: 255 alpha is stripped on upload; YttDocument also normalizes it.
        // 근거: YttDocument.LimitColors(), docs/YTT-VERIFICATION.md.
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

    [GeneratedRegex(@"\{[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex OverrideBlockRegex();

    [GeneratedRegex(@"\\([1-4]?[A-Za-z]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AssTagRegex();

    private sealed class AdapterDocument : SubtitleDocument
    {
        public AdapterDocument(int width, int height)
        {
            VideoDimensions = new Size(width, height);
        }
    }
}
