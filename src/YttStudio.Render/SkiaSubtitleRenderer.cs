using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render;

/// <summary>Measures and renders YTT subtitle cues on a headless Skia canvas.</summary>
public sealed class SkiaSubtitleRenderer : ISubtitleRenderer, IDisposable
{
    private readonly IFontResolver fontResolver;
    private readonly SubtitleLayoutEngine layoutEngine;
    private readonly Dictionary<PaintKey, FormatResources> formatCache = [];
    private readonly Dictionary<BlobKey, SKTextBlob> blobCache = [];
    private readonly Dictionary<YtFont, FontResolution> fontResolutions = [];
    private bool disposed;

    public SkiaSubtitleRenderer(IFontResolver fontResolver)
    {
        this.fontResolver = fontResolver ?? throw new ArgumentNullException(nameof(fontResolver));
        layoutEngine = new SubtitleLayoutEngine(fontResolver);
    }

    public IReadOnlyList<FontResolution> FontResolutions => fontResolutions.Values.OrderBy(item => item.Requested).ToArray();

    public void Render(SKCanvas canvas, PlayerViewport viewport, SubtitleProject project, TimeSpan time, RenderOptions options)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<CueLayout> layouts = GetLayouts(viewport, project, time, options);
        foreach (CueLayout layout in layouts)
        {
            CueEffectState effect = CueEffectEvaluator.Evaluate(layout.Cue, time, options.FrameIndex,
                layout.AnchorScreenPoint);
            int saveCount = canvas.Save();
            canvas.Translate(effect.Translation);
            if (effect.Scale != 1)
            {
                canvas.Scale(effect.Scale, effect.Scale, layout.Bounds.MidX, layout.Bounds.MidY);
            }
            if (effect.Alpha < 1)
            {
                using SKPaint alphaPaint = new() { Color = SKColors.White.WithAlpha((byte)Math.Round(effect.Alpha * 255)) };
                canvas.SaveLayer(alphaPaint);
            }
            DrawChroma(canvas, layout, time, options, effect);
            DrawBackground(canvas, layout);
            DrawEdges(canvas, layout, time, options);
            DrawBody(canvas, layout, time, options, effect);
            DrawUnderlines(canvas, layout);
            DrawRuby(canvas, layout);
            canvas.RestoreToCount(saveCount);
        }

        if (options.ShowSafeArea)
        {
            using SKPaint safeAreaPaint = new()
            {
                Color = new SKColor(255, 255, 255, 96),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                IsAntialias = true,
            };
            SKRect space = viewport.SubtitleSpace;
            canvas.DrawRect(SKRect.Create(space.Left + (space.Width * 0.05f), space.Top + (space.Height * 0.05f),
                space.Width * 0.9f, space.Height * 0.9f), safeAreaPaint);
        }

        if (options.ShowAnchorPoints)
        {
            using SKPaint anchorPaint = new() { Color = SKColors.Magenta, IsAntialias = true };
            foreach (CueLayout layout in layouts)
            {
                canvas.DrawCircle(layout.AnchorScreenPoint, 3, anchorPaint);
            }
        }
    }

    public IReadOnlyList<CueHitBox> Measure(PlayerViewport viewport, SubtitleProject project, TimeSpan time)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return GetLayouts(viewport, project, time, new RenderOptions())
            .Select(layout => new CueHitBox(layout.Cue, layout.Bounds, layout.AnchorScreenPoint))
            .ToArray();
    }

    /// <summary>Returns complete numeric layout data for diagnostics and deterministic tests.</summary>
    public IReadOnlyList<CueLayout> MeasureLayout(
        PlayerViewport viewport,
        SubtitleProject project,
        TimeSpan time,
        RenderOptions? options = null)
        => GetLayouts(viewport, project, time, options ?? new RenderOptions());

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        foreach (FormatResources resources in formatCache.Values)
        {
            resources.Dispose();
        }

        foreach (SKTextBlob blob in blobCache.Values)
        {
            blob.Dispose();
        }

        if (fontResolver is IDisposable disposableResolver)
        {
            disposableResolver.Dispose();
        }

        disposed = true;
    }

    private IReadOnlyList<CueLayout> GetLayouts(
        PlayerViewport viewport,
        SubtitleProject project,
        TimeSpan time,
        RenderOptions options)
        => project.Cues.GetActiveAt(time)
            .OrderBy(cue => cue.ZOrder)
            .Select(cue => layoutEngine.LayoutCue(viewport, project, cue, options))
            .ToArray();

    private void DrawBackground(SKCanvas canvas, CueLayout layout)
    {
        RunLayout? firstVisible = layout.Lines.SelectMany(line => line.Runs)
            .FirstOrDefault(run => run.Format.Background.Alpha > 0);
        if (firstVisible is null)
        {
            return;
        }

        FormatResources resources = GetResources(firstVisible.Format, firstVisible.FontSize);
        canvas.DrawRect(layout.Bounds, resources.Background);
    }

    private void DrawEdges(SKCanvas canvas, CueLayout layout, TimeSpan time, RenderOptions options)
    {
        int saveCount = ApplyTextTransform(canvas, layout);
        foreach (RunLayout run in layout.Lines.SelectMany(line => line.Runs))
        {
            FormatResources resources = GetResources(run.Format, run.FontSize);
            string text = GetPreviewText(layout.Cue, run, time, options.FrameIndex);
            float offset = run.FontSize * (float)YttConstants.HardShadowOffsetFactor;
            DrawWithBlob(run, text, resources.Font, blob =>
            {
                switch (run.Format.Edge)
                {
                    case EdgeType.HardShadow:
                        canvas.DrawText(blob, run.Origin.X + offset, run.Origin.Y + offset, resources.Edge);
                        break;
                    case EdgeType.Bevel:
                        canvas.DrawText(blob, run.Origin.X - 1, run.Origin.Y - 1, resources.BevelLight);
                        canvas.DrawText(blob, run.Origin.X + 1, run.Origin.Y + 1, resources.BevelDark);
                        break;
                    case EdgeType.Glow:
                        canvas.DrawText(blob, run.Origin.X, run.Origin.Y, resources.Edge);
                        break;
                    case EdgeType.SoftShadow:
                        canvas.DrawText(blob, run.Origin.X + offset, run.Origin.Y + offset, resources.Edge);
                        break;
                    default:
                        // EdgeType.None draws no edge pass. SPEC §5.4: one pen carries one et.
                        break;
                }
            });
        }

        canvas.RestoreToCount(saveCount);
    }

    private void DrawBody(
        SKCanvas canvas,
        CueLayout layout,
        TimeSpan time,
        RenderOptions options,
        CueEffectState effect)
    {
        int saveCount = ApplyTextTransform(canvas, layout);
        KaraokeType karaokeType = KaraokePreview.GetType(layout.Cue);
        foreach (RunLayout run in layout.Lines.SelectMany(line => line.Runs))
        {
            FormatResources resources = GetResources(run.Format, run.FontSize);
            bool sung = KaraokePreview.IsSung(layout.Cue, run.Section, time);
            string text = GetPreviewText(layout.Cue, run, time, options.FrameIndex);
            RgbaColor color = KaraokePreview.ResolveColor(layout.Cue, run.Section, run.Format, time);
            if (sung && effect.Foreground is RgbaColor animated)
            {
                color = animated;
            }

            DrawWithBlob(run, text, resources.Font, blob =>
            {
                if (color == run.Format.Foreground)
                {
                    canvas.DrawText(blob, run.Origin.X, run.Origin.Y, resources.Foreground);
                }
                else if (color == run.Format.SecondaryColor)
                {
                    canvas.DrawText(blob, run.Origin.X, run.Origin.Y, resources.Secondary);
                }
                else
                {
                    using SKPaint paint = new() { Color = ToSkColor(color), IsAntialias = true };
                    canvas.DrawText(blob, run.Origin.X, run.Origin.Y, paint);
                }
            });
        }

        DrawCursor(canvas, layout, time, karaokeType);

        canvas.RestoreToCount(saveCount);
    }

    private void DrawCursor(SKCanvas canvas, CueLayout layout, TimeSpan time, KaraokeType karaokeType)
    {
        if (karaokeType is not (KaraokeType.Cursor or KaraokeType.LeftCursor) ||
            KaraokePreview.GetSettings(layout.Cue) is not KaraokeSettings settings)
        {
            return;
        }

        string cursorText = KaraokePreview.GetCursorText(settings);
        if (cursorText.Length == 0)
        {
            return;
        }

        // The model currently stores one cursor string. The interval still determines the
        // upstream-compatible frame cadence, even though there are no alternate strings yet.
        _ = KaraokePreview.GetCursorFrameIndex(time - layout.Cue.Start, settings.CursorInterval);
        foreach (LineLayout line in layout.Lines)
        {
            IReadOnlyList<RunLayout> runs = line.Runs;
            if (runs.Count == 0)
            {
                continue;
            }

            int activeCount = 0;
            while (activeCount < runs.Count && KaraokePreview.IsSung(layout.Cue, runs[activeCount].Section, time))
            {
                activeCount++;
            }

            int insertion = karaokeType == KaraokeType.LeftCursor
                ? Math.Max(0, activeCount - 1)
                : activeCount;
            RunLayout styleRun = insertion > 0 ? runs[insertion - 1] : runs[0];
            FormatResources resources = GetResources(styleRun.Format, styleRun.FontSize);
            SKTextBlob blob = GetBlob(styleRun.Format, styleRun.FontSize, cursorText, resources.Font);
            using SKPaint measurePaint = new() { IsAntialias = true };
            float cursorWidth = resources.Font.MeasureText(cursorText, measurePaint);
            float boundary = insertion < runs.Count ? runs[insertion].Bounds.Left : runs[^1].Bounds.Right;
            float x = boundary - (insertion == 0 || karaokeType == KaraokeType.LeftCursor ? cursorWidth : 0);
            float baseline = insertion < runs.Count ? runs[insertion].Baseline : runs[^1].Baseline;
            canvas.DrawText(blob, x, baseline, resources.Foreground);
        }
    }

    private void DrawChroma(
        SKCanvas canvas,
        CueLayout layout,
        TimeSpan time,
        RenderOptions options,
        CueEffectState effect)
    {
        if (effect.Chroma is not ChromaEffect chroma || effect.ChromaAmount <= 0)
        {
            return;
        }
        IReadOnlyList<RgbaColor> colors = chroma.CustomColors is { Count: > 0 } custom
            ? custom
            : [new RgbaColor(254, 0, 0, 128), new RgbaColor(0, 254, 0, 128), new RgbaColor(0, 0, 254, 128)];
        float center = (colors.Count - 1) / 2f;
        foreach (RunLayout run in layout.Lines.SelectMany(line => line.Runs))
        {
            FormatResources resources = GetResources(run.Format, run.FontSize);
            string text = GetPreviewText(layout.Cue, run, time, options.FrameIndex);
            DrawWithBlob(run, text, resources.Font, blob =>
            {
                for (int index = 0; index < colors.Count; index++)
                {
                    float direction = colors.Count == 1 ? 1 : (index - center) / Math.Max(center, 1);
                    using SKPaint paint = new() { Color = ToSkColor(colors[index]), IsAntialias = true };
                    canvas.DrawText(blob,
                        run.Origin.X + ((float)chroma.OffsetX * effect.ChromaAmount * direction),
                        run.Origin.Y + ((float)chroma.OffsetY * effect.ChromaAmount * direction), paint);
                }
            });
        }
    }

    private void DrawUnderlines(SKCanvas canvas, CueLayout layout)
    {
        int saveCount = ApplyTextTransform(canvas, layout);
        foreach (RunLayout run in layout.Lines.SelectMany(line => line.Runs).Where(run => run.Format.Underline))
        {
            FormatResources resources = GetResources(run.Format, run.FontSize);
            float y = run.Baseline + (resources.Font.Metrics.Descent / 2);
            canvas.DrawLine(run.Bounds.Left, y, run.Bounds.Right, y, resources.Underline);
        }

        canvas.RestoreToCount(saveCount);
    }

    private void DrawRuby(SKCanvas canvas, CueLayout layout)
    {
        int saveCount = ApplyTextTransform(canvas, layout);
        foreach (RunLayout run in layout.Lines.SelectMany(line => line.Runs).Where(run => !string.IsNullOrEmpty(run.Section.RubyText)))
        {
            float rubySize = run.FontSize * 0.5f;
            FormatResources rubyResources = GetResources(run.Format, rubySize);
            using SKTextBlob ruby = SKTextBlob.Create(run.Section.RubyText!, rubyResources.Font, SKPoint.Empty)
                ?? throw new InvalidOperationException("Skia could not shape ruby text.");
            using SKPaint measurePaint = new();
            float width = rubyResources.Font.MeasureText(run.Section.RubyText!, measurePaint);
            float x = run.Bounds.MidX - (width / 2);
            float y = run.Section.Ruby == RubyRole.Below
                ? run.Bounds.Bottom + rubySize
                : run.Bounds.Top - (rubyResources.Font.Metrics.Descent);
            canvas.DrawText(ruby, x, y, rubyResources.Foreground);
        }

        canvas.RestoreToCount(saveCount);
    }

    private static int ApplyTextTransform(SKCanvas canvas, CueLayout layout)
    {
        int saveCount = canvas.Save();
        if (layout.Cue.Direction is TextDirection.RotatedLeftToRight or TextDirection.RotatedRightToLeft)
        {
            canvas.RotateDegrees(-90, layout.Bounds.MidX, layout.Bounds.MidY);
        }

        return saveCount;
    }

    private FormatResources GetResources(ResolvedFormat format, float fontSize)
    {
        PaintKey key = new(format, fontSize);
        if (formatCache.TryGetValue(key, out FormatResources? resources))
        {
            return resources;
        }

        FontResolution resolution = fontResolver.Resolve(format.Font);
        fontResolutions[format.Font] = resolution;
        resources = new FormatResources(format, fontSize, resolution.Typeface);
        formatCache.Add(key, resources);
        return resources;
    }

    private SKTextBlob GetBlob(ResolvedFormat format, float fontSize, string text, SKFont font)
    {
        BlobKey key = new(format, fontSize, text);
        if (blobCache.TryGetValue(key, out SKTextBlob? blob))
        {
            return blob;
        }

        blob = CreateBlob(format, fontSize, text, font);
        blobCache.Add(key, blob);
        return blob;
    }

    private static SKTextBlob CreateBlob(ResolvedFormat format, float fontSize, string text, SKFont font)
    {
        if (format.Pack && text.Length > 0)
        {
            SKPoint[] positions = Enumerable.Range(0, text.Length)
                .Select(index => new SKPoint(index * fontSize, 0))
                .ToArray();
            return SKTextBlob.CreatePositioned(text, font, positions)
                ?? throw new InvalidOperationException("Skia could not shape packed text.");
        }

        return SKTextBlob.Create(text, font, SKPoint.Empty)
            ?? throw new InvalidOperationException("Skia could not shape subtitle text.");
    }

    private void DrawWithBlob(RunLayout run, string text, SKFont font, Action<SKTextBlob> draw)
    {
        if (text == run.Text)
        {
            draw(GetBlob(run.Format, run.FontSize, text, font));
            return;
        }

        using SKTextBlob transient = CreateBlob(run.Format, run.FontSize, text, font);
        draw(transient);
    }

    private static string GetPreviewText(Cue cue, RunLayout run, TimeSpan time, long frameIndex)
        => KaraokePreview.GetType(cue) == KaraokeType.Glitch && !KaraokePreview.IsSung(cue, run.Section, time)
            ? KaraokePreview.GetGlitchedText(cue, run.Text, frameIndex)
            : run.Text;

    private static SKColor ToSkColor(RgbaColor color)
    {
        // SPEC §7.5 [UPSTREAM]: model alpha 254 represents fully opaque YTT output.
        byte yttAlpha = Math.Min(color.Alpha, YttConstants.MaximumOpacity);
        byte alpha = checked((byte)Math.Round(yttAlpha * 255.0 / YttConstants.MaximumOpacity));
        return new SKColor(color.Red, color.Green, color.Blue, alpha);
    }

    private static SKColor Adjust(SKColor color, int delta)
        => new(
            checked((byte)Math.Clamp(color.Red + delta, 0, 255)),
            checked((byte)Math.Clamp(color.Green + delta, 0, 255)),
            checked((byte)Math.Clamp(color.Blue + delta, 0, 255)),
            color.Alpha);

    private readonly record struct PaintKey(ResolvedFormat Format, float FontSize);
    private readonly record struct BlobKey(ResolvedFormat Format, float FontSize, string Text);

    private sealed class FormatResources : IDisposable
    {
        public FormatResources(ResolvedFormat format, float fontSize, SKTypeface typeface)
        {
            Font = new SKFont(typeface, fontSize)
            {
                Embolden = format.Bold,
                SkewX = format.Italic ? -0.25f : 0,
                Subpixel = true,
            };
            Foreground = CreateFill(ToSkColor(format.Foreground));
            Secondary = CreateFill(ToSkColor(format.SecondaryColor));
            Background = CreateFill(ToSkColor(format.Background));
            SKColor edgeColor = ToSkColor(format.EdgeColor);
            Edge = CreateFill(edgeColor);
            if (format.Edge == EdgeType.Glow)
            {
                Edge.Style = SKPaintStyle.Stroke;
                Edge.StrokeWidth = fontSize * (float)YttConstants.GlowStrokeWidthFactor;
                Edge.StrokeJoin = SKStrokeJoin.Round;
                Edge.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal,
                    fontSize * (float)YttConstants.HardShadowOffsetFactor);
            }
            else if (format.Edge == EdgeType.SoftShadow)
            {
                Edge.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal,
                    fontSize * (float)YttConstants.SoftShadowBlurFactor);
            }

            BevelLight = CreateFill(Adjust(edgeColor, 72));
            BevelDark = CreateFill(Adjust(edgeColor, -72));
            Underline = CreateFill(ToSkColor(format.Foreground));
            Underline.StrokeWidth = Math.Max(1, fontSize * (float)YttConstants.UnderlineThicknessFactor);
        }

        public SKFont Font { get; }
        public SKPaint Foreground { get; }
        public SKPaint Secondary { get; }
        public SKPaint Background { get; }
        public SKPaint Edge { get; }
        public SKPaint BevelLight { get; }
        public SKPaint BevelDark { get; }
        public SKPaint Underline { get; }

        public void Dispose()
        {
            Font.Dispose();
            Foreground.Dispose();
            Secondary.Dispose();
            Background.Dispose();
            Edge.Dispose();
            BevelLight.Dispose();
            BevelDark.Dispose();
            Underline.Dispose();
        }

        private static SKPaint CreateFill(SKColor color) => new()
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
    }
}
