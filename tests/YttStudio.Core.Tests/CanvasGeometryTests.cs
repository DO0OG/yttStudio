using YttStudio.Core.Editing;

namespace YttStudio.Core.Tests;

public sealed class CanvasGeometryTests
{
    [Fact]
    public void TopLeftCoordinateRoundTrips()
    {
        CanvasPoint pixel = CanvasGeometry.ToCanvasPoint(0, 0, 1280, 720);
        CanvasPoint ytt = CanvasGeometry.ToYttPoint(pixel.X, pixel.Y, 1280, 720);
        Assert.Equal(0, ytt.X, 6);
        Assert.Equal(0, ytt.Y, 6);
    }

    [Fact]
    public void CenterCoordinateRoundTrips()
    {
        CanvasPoint pixel = CanvasGeometry.ToCanvasPoint(50, 50, 1920, 1080);
        CanvasPoint ytt = CanvasGeometry.ToYttPoint(pixel.X, pixel.Y, 1920, 1080);
        Assert.Equal(50, ytt.X, 6);
        Assert.Equal(50, ytt.Y, 6);
    }

    [Fact]
    public void BottomRightCoordinateRoundTrips()
    {
        CanvasPoint pixel = CanvasGeometry.ToCanvasPoint(100, 100, 3840, 2160);
        CanvasPoint ytt = CanvasGeometry.ToYttPoint(pixel.X, pixel.Y, 3840, 2160);
        Assert.Equal(100, ytt.X, 6);
        Assert.Equal(100, ytt.Y, 6);
    }

    [Fact]
    public void AnchorChangePreservesBoxScreenPosition()
    {
        CanvasRect box = new(200, 100, 300, 120);
        CanvasPoint ytt = CanvasGeometry.PreserveBoxForAnchor(box, AnchorPoint.BottomRight, 1280, 720);
        CanvasPoint pixel = CanvasGeometry.ToCanvasPoint(ytt.X, ytt.Y, 1280, 720);
        Assert.Equal(box.Right, pixel.X, 6);
        Assert.Equal(box.Bottom, pixel.Y, 6);
    }

    [Fact]
    public void SnapInsideThresholdUsesNearestGuide()
    {
        SnapResult result = CanvasGeometry.Snap(new CanvasPoint(645, 365), 1280, 720, false);
        Assert.Equal(new CanvasPoint(640, 360), result.Point);
        Assert.Equal(2, result.Guides.Count);
    }

    [Fact]
    public void SnapOutsideThresholdLeavesPointUnchanged()
    {
        CanvasPoint point = new(650, 370);
        SnapResult result = CanvasGeometry.Snap(point, 1280, 720, false);
        Assert.Equal(point, result.Point);
        Assert.Empty(result.Guides);
    }

    [Fact]
    public void AltDisablesSnapping()
    {
        CanvasPoint point = new(641, 361);
        SnapResult result = CanvasGeometry.Snap(point, 1280, 720, true);
        Assert.Equal(point, result.Point);
        Assert.Empty(result.Guides);
    }
}
