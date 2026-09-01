using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using YTSubConverter.Shared;
using YTSubConverter.Shared.Formats;
using YTSubConverter.Shared.Formats.Ass;
using ExternalAnchorPoint = YTSubConverter.Shared.AnchorPoint;
using ExternalSection = YTSubConverter.Shared.Section;
using ModelSection = YttStudio.Core.Section;

namespace YttStudio.Core.Format;

/// <summary>고정된 변환기를 통해 자막 형식을 가져오고 내보낸다.</summary>
public sealed partial class SubtitleFileService
{
    private static readonly HashSet<string> SupportedAssTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "b", "i", "u", "fn", "fs", "c", "1c", "2c", "3c", "4c",
        "1a", "2a", "3a", "4a", "alpha", "pos", "an", "k", "r",
        "fad", "fade", "move", "t", "ytsub", "ytsup", "ytsur", "ytruby",
        "ytvert", "ytdir", "ytpack", "ytshake", "ytchroma", "ytkt", "ytmotion",
    };

    private static void EnsureImportSizeWithinLimit(string path)
    {
        FileInfo info = new(path);
        if (info.Exists && info.Length > MaximumImportBytes)
        {
            throw new InvalidDataException(
                $"자막 파일이 너무 큽니다. {info.Length:N0} 바이트이며 한도는 {MaximumImportBytes:N0} 바이트입니다.");
        }
    }

    private static ImportResult ImportYtt(string path)
    {
        YttDocument document = new(path);
        SubtitleProject project = FromExternalDocument(document);
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
        string sanitized = AssEffectCodec.SanitizeAndRead(path, out List<IReadOnlyList<CueEffect>> effectsByLine);
        string temporaryPath = Path.Combine(Path.GetTempPath(), $"YttStudio-{Guid.NewGuid():N}.ass");
        try
        {
            File.WriteAllText(temporaryPath, sanitized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            AssDocument document = new(temporaryPath);
            SubtitleProject project = FromExternalDocument(document);
            Cue[] cues = project.Cues.ToArray();
            for (int index = 0; index < Math.Min(cues.Length, effectsByLine.Count); index++)
            {
                foreach (CueEffect effect in effectsByLine[index])
                {
                    cues[index].AddEffect(effect);
                }
            }
            return new ImportResult(project, warnings);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static SubtitleProject FromExternalDocument(SubtitleDocument document)
    {
        SubtitleProject project = new();
        project.Video = new VideoInfo(document.VideoDimensions.Width, document.VideoDimensions.Height, TimeSpan.Zero, 0);
        IReadOnlyList<StylePreset>? importedStyles = document is AssDocument assDocument
            ? ImportAssStyles(project, assDocument)
            : null;

        for (int lineIndex = 0; lineIndex < document.Lines.Count; lineIndex++)
        {
            Line line = document.Lines[lineIndex];
            Cue cue = CreateCue(document, line, lineIndex);
            AddSections(project, cue, line, importedStyles);
            project.Cues.Add(cue);
        }

        return project;
    }

    private static Cue CreateCue(SubtitleDocument document, Line line, int lineIndex)
    {
        Cue cue = new(Guid.NewGuid())
        {
            Start = line.Start - SubtitleDocument.TimeBase,
            End = line.End - SubtitleDocument.TimeBase,
            Anchor = ToModelAnchor(line.AnchorPoint),
            Justify = ToModelJustification(line.Justification, line.AnchorPoint),
            Direction = ToModelDirection(line.HorizontalTextDirection, line.VerticalTextType),
            Track = line is AssLine assLine ? assLine.Layer : 0,
            ZOrder = line is AssLine zOrderLine ? zOrderLine.Layer : lineIndex,
        };

        PointF position = line.Position ?? document.GetDefaultPosition(line.AnchorPoint);
        cue.PositionX = YttMath.ToYttCoordinate(position.X, document.VideoDimensions.Width);
        cue.PositionY = YttMath.ToYttCoordinate(position.Y, document.VideoDimensions.Height);
        return cue;
    }

    private static void AddSections(
        SubtitleProject project,
        Cue cue,
        Line line,
        IReadOnlyList<StylePreset>? importedStyles)
    {
        // 루비 그룹은 네 섹션에 걸쳐 있어 인덱스가 한 칸 넘게 전진한다.
        // while 루프로 두어야 for 카운터를 변경하지 않고 전진량이 드러난다.
        int sectionIndex = 0;
        while (sectionIndex < line.Sections.Count)
        {
            sectionIndex += AddSection(project, cue, line, sectionIndex, importedStyles);
        }

        if (cue.Sections.Count == 0)
        {
            cue.AddSection(new ModelSection());
        }
    }

    private static int AddSection(
        SubtitleProject project,
        Cue cue,
        Line line,
        int sectionIndex,
        IReadOnlyList<StylePreset>? importedStyles)
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

        int consumed = 1;
        if (externalSection.RubyPart == RubyPart.Base && sectionIndex + 3 < line.Sections.Count &&
            line.Sections[sectionIndex + 1].RubyPart == RubyPart.Parenthesis &&
            line.Sections[sectionIndex + 3].RubyPart == RubyPart.Parenthesis &&
            line.Sections[sectionIndex + 2].RubyPart is RubyPart.TextBefore or RubyPart.TextAfter)
        {
            ExternalSection rubySection = line.Sections[sectionIndex + 2];
            modelSection.Ruby = rubySection.RubyPart == RubyPart.TextAfter ? RubyRole.Below : RubyRole.Above;
            modelSection.RubyText = rubySection.Text;
            consumed = 4;
        }

        cue.AddSection(modelSection);
        return consumed;
    }

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
            // [UPSTREAM] Default 스타일이 100% 기준이며 모든 ASS 스타일은 그 줄 높이에 상대적이다.
            // 근거: AssDocument.cs 의 style.Scale 계산, docs/YTT-VERIFICATION.md
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

    [GeneratedRegex(@"\{[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex OverrideBlockRegex();

    [GeneratedRegex(@"\\([1-4]?[A-Za-z]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AssTagRegex();
}
