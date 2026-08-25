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
}
