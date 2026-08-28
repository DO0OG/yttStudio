using System.Text;
using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render;

/// <summary>픽셀을 그리지 않고 결정적인 자막 기하를 계산한다.</summary>
public sealed class SubtitleLayoutEngine
{
    // [실측] 유튜브 자막 글자 크기는 플레이어 너비의 0.0244907 배다.
    // 기본 글자 크기도 같은 실측 비율을 따르도록 기존 32 px 기준 배율로 환산한다.
    private const double YouTubeSubtitleFontWidthFactor = 0.0244907;

    private readonly FontFallbackHelper fontFallback;

    public SubtitleLayoutEngine(IFontResolver fontResolver)
    {
        ArgumentNullException.ThrowIfNull(fontResolver);
        fontFallback = new FontFallbackHelper(fontResolver);
    }

    internal SubtitleLayoutEngine(IFontResolver fontResolver, FontFallbackHelper fontFallback)
    {
        ArgumentNullException.ThrowIfNull(fontResolver);
        this.fontFallback = fontFallback ?? throw new ArgumentNullException(nameof(fontFallback));
    }

    /// <summary>일곱 단계 레이아웃 알고리즘으로 큐 하나를 측정하고 배치한다.</summary>
    public CueLayout LayoutCue(PlayerViewport viewport, SubtitleProject project, Cue cue, RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(cue);
        options ??= new RenderOptions();
        if (viewport.SubtitleSpace.Width <= 0 || viewport.SubtitleSpace.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewport));
        }

        float viewportScale = GetViewportScale(viewport);
        List<MeasuredLine> measuredLines = MeasureLines(project, cue, viewportScale, options);
        float maximumFontSize = measuredLines.SelectMany(line => line.Runs).Select(run => run.FontSize).DefaultIfEmpty(
            (float)(YttConstants.DefaultFontSizePixels * viewportScale)).Max();
        float horizontalPadding = maximumFontSize * (float)YttConstants.HorizontalBoxPaddingFactor;
        float verticalPadding = maximumFontSize * (float)YttConstants.VerticalBoxPaddingFactor;

        bool vertical = cue.Direction is TextDirection.VerticalLeftToRight or TextDirection.VerticalRightToLeft;
        bool rotated = cue.Direction is TextDirection.RotatedLeftToRight or TextDirection.RotatedRightToLeft;
        float unrotatedWidth = vertical ? measuredLines.Sum(line => line.Height) : measuredLines.Max(line => line.Width);
        float unrotatedHeight = vertical ? measuredLines.Max(MeasureVerticalHeight) : measuredLines.Sum(line => line.Height);
        float contentWidth = rotated ? unrotatedHeight : unrotatedWidth;
        float contentHeight = rotated ? unrotatedWidth : unrotatedHeight;
        float boxWidth = contentWidth + ((rotated ? verticalPadding : horizontalPadding) * 2);
        float boxHeight = contentHeight + ((rotated ? horizontalPadding : verticalPadding) * 2);

        SKPoint anchor = GetAnchorPoint(viewport, cue, options.ApplyCoordinateTransform);
        int anchorColumn = (int)cue.Anchor % 3;
        int anchorRow = (int)cue.Anchor / 3;
        float originX = anchor.X - (boxWidth * anchorColumn / 2f);
        float originY = anchor.Y - (boxHeight * anchorRow / 2f);
        SKRect box = SKRect.Create(originX, originY, boxWidth, boxHeight);

        IReadOnlyList<LineLayout> lines = PlaceLines(cue, measuredLines, box,
            horizontalPadding, verticalPadding, vertical, rotated);
        return new CueLayout(cue, box, anchor, lines, maximumFontSize,
            box.Left < viewport.SubtitleSpace.Left || box.Top < viewport.SubtitleSpace.Top ||
            box.Right > viewport.SubtitleSpace.Right || box.Bottom > viewport.SubtitleSpace.Bottom);
    }

    private static IReadOnlyList<LineLayout> PlaceLines(
        Cue cue,
        IReadOnlyList<MeasuredLine> measuredLines,
        SKRect box,
        float horizontalPadding,
        float verticalPadding,
        bool vertical,
        bool rotated)
    {
        if (vertical)
        {
            return PlaceVerticalLines(cue, measuredLines, box, horizontalPadding, verticalPadding);
        }

        if (!rotated)
        {
            return PlaceHorizontalLines(cue, measuredLines, box, horizontalPadding, verticalPadding);
        }

        SKRect unrotatedBox = SKRect.Create(
            box.MidX - (box.Height / 2),
            box.MidY - (box.Width / 2),
            box.Height,
            box.Width);
        return PlaceHorizontalLines(cue, measuredLines, unrotatedBox, horizontalPadding, verticalPadding);
    }

    private List<MeasuredLine> MeasureLines(
        SubtitleProject project,
        Cue cue,
        float viewportScale,
        RenderOptions options)
    {
        List<List<MeasuredRun>> splitLines = [[]];
        foreach (Section section in cue.Sections)
        {
            StylePreset style = project.GetStyle(section.StyleIdOverride ?? cue.StyleId);
            ResolvedFormat format = FormatResolver.Resolve(style.BaseFormat, section.Overrides);
            string normalized = section.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            string[] parts = normalized.Split('\n');
            for (int index = 0; index < parts.Length; index++)
            {
                if (parts[index].Length > 0)
                {
                    splitLines[^1].Add(MeasureRun(section, format, parts[index], viewportScale, options));
                }

                if (index < parts.Length - 1)
                {
                    splitLines.Add([]);
                }
            }
        }

        if (splitLines.Count == 0)
        {
            splitLines.Add([]);
        }

        if (cue.Direction == TextDirection.HorizontalRtl)
        {
            foreach (List<MeasuredRun> line in splitLines)
            {
                line.Reverse();
            }
        }

        float fallbackHeight = (float)(YttConstants.DefaultFontSizePixels * viewportScale * options.FontScaleBase);
        return splitLines.Select(runs => new MeasuredLine(
            runs,
            runs.Sum(run => run.Width),
            runs.Count == 0 ? fallbackHeight : runs.Max(run => run.Height),
            runs.Count == 0 ? fallbackHeight * 0.8f : runs.Max(run => -run.Ascent),
            runs.Count == 0 ? fallbackHeight * 0.2f : runs.Max(run => run.Descent))).ToList();
    }

    private MeasuredRun MeasureRun(
        Section section,
        ResolvedFormat format,
        string text,
        float viewportScale,
        RenderOptions options)
    {
        float fontSize = (float)(YttConstants.DefaultFontSizePixels * viewportScale *
            (Math.Max(format.SizePercent, 75) / 100.0) * options.FontScaleBase);
        if (format.Offset != ScriptOffset.Regular)
        {
            fontSize *= (float)YttConstants.ScriptFontScale;
        }

        FontTextLayout textLayout = fontFallback.Layout(format, fontSize, text);
        return new MeasuredRun(section, format, text, textLayout.Width, textLayout.Height,
            textLayout.Ascent, textLayout.Descent, fontSize);
    }

    private static IReadOnlyList<LineLayout> PlaceHorizontalLines(
        Cue cue,
        IReadOnlyList<MeasuredLine> measured,
        SKRect box,
        float horizontalPadding,
        float verticalPadding)
    {
        List<LineLayout> lines = [];
        float y = box.Top + verticalPadding;
        foreach (MeasuredLine line in measured)
        {
            float x = cue.Justify switch
            {
                Justification.Right => box.Right - horizontalPadding - line.Width,
                Justification.Center => box.MidX - (line.Width / 2),
                _ => box.Left + horizontalPadding,
            };
            float baseline = y + line.AscentMagnitude;
            List<RunLayout> runs = [];
            foreach (MeasuredRun run in line.Runs)
            {
                float baselineOffset = run.Format.Offset switch
                {
                    ScriptOffset.Subscript => run.FontSize * (float)YttConstants.ScriptBaselineOffsetFactor,
                    ScriptOffset.Superscript => -run.FontSize * (float)YttConstants.ScriptBaselineOffsetFactor,
                    _ => 0,
                };
                float runBaseline = baseline + baselineOffset;
                SKRect bounds = SKRect.Create(x, runBaseline + run.Ascent, run.Width, run.Height);
                runs.Add(new RunLayout(run.Section, run.Format, run.Text, new SKPoint(x, runBaseline), bounds, runBaseline, run.FontSize));
                x += run.Width;
            }

            lines.Add(new LineLayout(SKRect.Create(
                runs.Count == 0 ? box.Left + horizontalPadding : runs.Min(run => run.Bounds.Left),
                y,
                line.Width,
                line.Height), baseline, runs));
            y += line.Height;
        }

        return lines;
    }

    private static IReadOnlyList<LineLayout> PlaceVerticalLines(
        Cue cue,
        IReadOnlyList<MeasuredLine> measured,
        SKRect box,
        float horizontalPadding,
        float verticalPadding)
    {
        List<LineLayout> lines = [];
        bool rightToLeft = cue.Direction is TextDirection.VerticalRightToLeft or TextDirection.RotatedRightToLeft;
        float x = rightToLeft ? box.Right - horizontalPadding : box.Left + horizontalPadding;
        foreach (MeasuredLine line in measured)
        {
            float columnWidth = line.Height;
            float columnLeft = rightToLeft ? x - columnWidth : x;
            float y = box.Top + verticalPadding;
            List<RunLayout> runs = [];
            foreach (MeasuredRun run in line.Runs)
            {
                foreach (Rune rune in run.Text.EnumerateRunes())
                {
                    string text = rune.ToString();
                    float baseline = y + run.FontSize;
                    SKRect bounds = SKRect.Create(columnLeft, y, columnWidth, run.FontSize);
                    runs.Add(new RunLayout(run.Section, run.Format, text,
                        new SKPoint(columnLeft + ((columnWidth - run.FontSize) / 2), baseline),
                        bounds, baseline, run.FontSize));
                    y += run.FontSize;
                }
            }

            lines.Add(new LineLayout(SKRect.Create(columnLeft, box.Top + verticalPadding, columnWidth,
                Math.Max(0, y - box.Top - verticalPadding)), 0, runs));
            x += rightToLeft ? -columnWidth : columnWidth;
        }

        return lines;
    }

    private static SKPoint GetAnchorPoint(PlayerViewport viewport, Cue cue, bool applyTransform)
    {
        int x = checked((int)Math.Round(cue.PositionX, MidpointRounding.ToEven));
        int y = checked((int)Math.Round(cue.PositionY, MidpointRounding.ToEven));
        SKRect space = viewport.SubtitleSpace;
        return applyTransform
            ? new SKPoint(space.Left + (float)YttMath.ToPixelCoordinate(x, space.Width),
                space.Top + (float)YttMath.ToPixelCoordinate(y, space.Height))
            : new SKPoint(space.Left + (float)(cue.PositionX / 100 * space.Width),
                space.Top + (float)(cue.PositionY / 100 * space.Height));
    }

    private static float MeasureVerticalHeight(MeasuredLine line)
        => line.Runs.Sum(run => run.Text.EnumerateRunes().Count() * run.FontSize);

    private static float GetViewportScale(PlayerViewport viewport)
        => (float)(viewport.SubtitleSpace.Width * YouTubeSubtitleFontWidthFactor /
            YttConstants.DefaultFontSizePixels);

    private sealed record MeasuredLine(
        IReadOnlyList<MeasuredRun> Runs,
        float Width,
        float Height,
        float AscentMagnitude,
        float Descent);

    private sealed record MeasuredRun(
        Section Section,
        ResolvedFormat Format,
        string Text,
        float Width,
        float Height,
        float Ascent,
        float Descent,
        float FontSize);
}
