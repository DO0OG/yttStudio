using System.Drawing;
using System.Text;
using YTSubConverter.Shared;
using YTSubConverter.Shared.Formats;
using YTSubConverter.Shared.Formats.Ass;
using ModelSection = YttStudio.Core.Section;

namespace YttStudio.Core.Format;

/// <summary>고정된 변환기를 통해 자막 형식을 가져오고 내보낸다.</summary>
public sealed partial class SubtitleFileService
{
    private static void ExportWithEffects(AssDocument assDocument, IReadOnlyList<ExportCue> exportCues,
        string path, string extension)
    {
        if (extension is not ".ytt" and not ".srv3" and not ".ass")
        {
            throw new NotSupportedException($"The '{extension}' format is outside the M3 export scope.");
        }

        string basePath = Path.Combine(Path.GetTempPath(), $"YttStudio-{Guid.NewGuid():N}-base.ass");
        string effectsPath = Path.Combine(Path.GetTempPath(), $"YttStudio-{Guid.NewGuid():N}-effects.ass");
        try
        {
            assDocument.Save(basePath);
            string source = File.ReadAllText(basePath);
            IReadOnlyList<IReadOnlyList<CueEffect>> effects = exportCues
                .Select(cue => cue.Effects)
                .ToArray();
            string encoded = AssEffectCodec.Inject(source, effects);
            File.WriteAllText(effectsPath, encoded, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            if (extension == ".ass")
            {
                File.Copy(effectsPath, path, overwrite: true);
            }
            else
            {
                // [UPSTREAM] 효과는 ASS 를 거쳐 들어가고 XML 조립은 YttDocument.Save 가 맡는다.
                new YttDocument(new AssDocument(effectsPath)).Save(path);
            }
        }
        finally
        {
            File.Delete(basePath);
            File.Delete(effectsPath);
        }
    }

    private static AdapterDocument ToExternalDocument(
        SubtitleProject project,
        IReadOnlyList<ExportCue> exportCues)
    {
        int width = project.Video?.Width is > 0 ? project.Video.Width : YttConstants.ReferenceWidth;
        int height = project.Video?.Height is > 0 ? project.Video.Height : YttConstants.ReferenceHeight;
        AdapterDocument document = new(width, height);

        foreach (ExportCue exportCue in exportCues)
        {
            Cue cue = exportCue.Cue;
            Line line = new(
                SubtitleDocument.TimeBase + exportCue.Start,
                SubtitleDocument.TimeBase + exportCue.End)
            {
                AnchorPoint = ToExternalAnchor(cue.Anchor),
                Justification = ToExternalJustification(cue.Justify),
                Position = exportCue.PixelPosition is MotionPoint pixel
                    ? new PointF((float)pixel.X, (float)pixel.Y)
                    : new PointF(
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

    private sealed class AdapterDocument : SubtitleDocument
    {
        public AdapterDocument(int width, int height)
        {
            VideoDimensions = new Size(width, height);
        }
    }
}
