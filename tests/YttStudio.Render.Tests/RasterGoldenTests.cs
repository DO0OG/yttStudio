using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render.Tests;

public sealed class RasterGoldenTests
{
    [Fact]
    public void BundledRobotoGlowMatchesGoldenPixels()
    {
        (SubtitleProject project, _) = LayoutTests.CreateProject(
            AnchorPoint.MiddleCenter,
            Justification.Center,
            "YttStudio M1\nRenderer");
        using BundledFontResolver fonts = new();
        using SkiaSubtitleRenderer renderer = new(fonts);
        using SKBitmap actual = Render(renderer, project);
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "default-glow.png");

        using SKBitmap expected = SKBitmap.Decode(fixturePath)
            ?? throw new InvalidDataException("Raster golden could not be decoded.");
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    private static SKBitmap Render(SkiaSubtitleRenderer renderer, SubtitleProject project)
    {
        SKBitmap bitmap = new(new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul));
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(new SKColor(32, 32, 32));
        renderer.Render(canvas, new PlayerViewport(640, 360), project, TimeSpan.FromSeconds(1), new RenderOptions());
        return bitmap;
    }

}
