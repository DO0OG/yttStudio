using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render.Tests;

public sealed class InlineEditingRenderTests
{
    [Fact]
    public void EditingCueIsExcludedFromRasterButRetainedInMeasure()
    {
        SubtitleProject project = new();
        Cue editing = CreateCue(25, "editing");
        Cue other = CreateCue(75, "other");
        project.Cues.Add(editing);
        project.Cues.Add(other);

        using BundledFontResolver fonts = new();
        using SkiaSubtitleRenderer renderer = new(fonts);
        using SKBitmap bitmap = new(new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul));
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        renderer.Render(canvas, new PlayerViewport(640, 360), project, TimeSpan.FromSeconds(1),
            new RenderOptions { EditingCueId = editing.Id });

        CueHitBox editingBounds = renderer.Measure(
            new PlayerViewport(640, 360), project, TimeSpan.FromSeconds(1))
            .Single(hit => hit.Cue.Id == editing.Id);
        Assert.DoesNotContain(
            bitmap.Pixels.Where((_, index) => IsInside(index, bitmap.Width, editingBounds.Bounds)),
            pixel => pixel.Alpha > 0);
        Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha > 0);
        Assert.Equal(2, renderer.Measure(
            new PlayerViewport(640, 360), project, TimeSpan.FromSeconds(1)).Count);
    }

    private static Cue CreateCue(double x, string text)
    {
        Cue cue = new(Guid.NewGuid())
        {
            Start = TimeSpan.Zero,
            End = TimeSpan.FromSeconds(3),
            PositionX = x,
            PositionY = 50,
            Anchor = AnchorPoint.MiddleCenter,
            Justify = Justification.Center,
        };
        cue.AddSection(new Section { Text = text });
        return cue;
    }

    private static bool IsInside(int index, int width, SKRect bounds)
    {
        int x = index % width;
        int y = index / width;
        return x >= Math.Floor(bounds.Left) && x <= Math.Ceiling(bounds.Right) &&
            y >= Math.Floor(bounds.Top) && y <= Math.Ceiling(bounds.Bottom);
    }
}
