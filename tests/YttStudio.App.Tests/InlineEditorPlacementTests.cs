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
}
