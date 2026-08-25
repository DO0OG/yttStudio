using SkiaSharp;

namespace YttStudio.Render.Tests;

public sealed class RenderSmokeTests
{
    [Fact]
    public void RendererContractUsesSkiaCanvasWithoutUiDependency()
    {
        Type canvasParameter = typeof(ISubtitleRenderer)
            .GetMethod(nameof(ISubtitleRenderer.Render))!
            .GetParameters()[0]
            .ParameterType;

        Assert.Equal(typeof(SKCanvas), canvasParameter);
        Assert.DoesNotContain(
            typeof(ISubtitleRenderer).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name?.StartsWith("Avalonia", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void RendererDrawsVisiblePixelsAndReturnsReasonableBounds()
    {
        (YttStudio.Core.SubtitleProject project, _) = LayoutTests.CreateProject(
            YttStudio.Core.AnchorPoint.MiddleCenter,
            YttStudio.Core.Justification.Center,
            "Renderer smoke");
        using BundledFontResolver fonts = new();
        using SkiaSubtitleRenderer renderer = new(fonts);
        using SKBitmap bitmap = new(new SKImageInfo(1280, 720, SKColorType.Bgra8888, SKAlphaType.Premul));
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        renderer.Render(canvas, new PlayerViewport(1280, 720), project, TimeSpan.FromSeconds(1), new RenderOptions());
        CueHitBox hitBox = Assert.Single(renderer.Measure(
            new PlayerViewport(1280, 720), project, TimeSpan.FromSeconds(1)));

        Assert.True(hitBox.Bounds.Width > 0);
        Assert.True(hitBox.Bounds.Height > 0);
        Assert.InRange(hitBox.Bounds.Left, 0, 1280);
        Assert.InRange(hitBox.Bounds.Top, 0, 720);
        Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha > 0);
    }
}
