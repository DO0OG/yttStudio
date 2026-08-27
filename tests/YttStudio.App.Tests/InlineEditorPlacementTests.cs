using Avalonia;
using YttStudio.App;

namespace YttStudio.App.Tests;

public sealed class InlineEditorPlacementTests
{
    [Fact]
    public void ClampMovesEditorInsideViewport()
    {
        Rect result = InlineEditorPlacement.Clamp(new Rect(95, 85, 30, 25), 100, 100);

        Assert.Equal(70, result.Left);
        Assert.Equal(75, result.Top);
        Assert.Equal(30, result.Width);
        Assert.Equal(25, result.Height);
    }

    [Fact]
    public void ClampReducesEditorWhenViewportIsSmaller()
    {
        Rect result = InlineEditorPlacement.Clamp(new Rect(-10, -5, 200, 200), 100, 80);

        Assert.Equal(0, result.Left);
        Assert.Equal(0, result.Top);
        Assert.Equal(100, result.Width);
        Assert.Equal(80, result.Height);
    }

    [Fact]
    public void ClampUsesTheLetterboxedContentRectForScreenPlacement()
    {
        Rect subtitleSpace = new(100, 50, 1000, 800);
        Rect content = PreviewCanvasGeometry.GetContentRect(new Size(1600, 900), subtitleSpace);
        Rect requested = new(1250, 787.5, 180, 150);

        Rect result = InlineEditorPlacement.Clamp(requested, content);

        Assert.Equal(1182.5, result.Left, 10);
        Assert.Equal(750, result.Top, 10);
        Assert.Equal(180, result.Width, 10);
        Assert.Equal(150, result.Height, 10);
    }
}
