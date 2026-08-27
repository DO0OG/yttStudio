using Avalonia;
using YttStudio.App;
using YttStudio.Core.Editing;

namespace YttStudio.App.Tests;

public sealed class PreviewCanvasGeometryTests
{
    private static readonly Size ControlSize = new(1600, 900);
    private static readonly Rect OffsetSubtitleSpace = new(100, 50, 1000, 800);

    [Fact]
    public void GetContentRectLetterboxesAnOffsetNonSixteenByNineSpace()
    {
        Rect result = PreviewCanvasGeometry.GetContentRect(ControlSize, OffsetSubtitleSpace);

        AssertRect(result, 237.5, 0, 1125, 900);
    }

    [Fact]
    public void SourceAndScreenPointRoundTripPreservesTheSubtitleOffset()
    {
        Rect content = PreviewCanvasGeometry.GetContentRect(ControlSize, OffsetSubtitleSpace);
        Point source = new(350, 250);

        Point screen = PreviewCanvasGeometry.ToScreen(source, content, OffsetSubtitleSpace);
        Point roundTrip = PreviewCanvasGeometry.ToSubtitle(screen, content, OffsetSubtitleSpace);

        AssertPoint(screen, 518.75, 225);
        AssertPoint(roundTrip, source.X, source.Y);
    }

    [Fact]
    public void SourceBoundsMapIntoTheLetterboxedContentRect()
    {
        Rect content = PreviewCanvasGeometry.GetContentRect(ControlSize, OffsetSubtitleSpace);
        CanvasRect source = new(250, 150, 400, 300);

        Rect result = PreviewCanvasGeometry.ToScreen(source, content, OffsetSubtitleSpace);

        AssertRect(result, 406.25, 112.5, 450, 337.5);
    }

    [Fact]
    public void SourceDeltaUsesTheSubtitleSpaceExtents()
    {
        Rect content = PreviewCanvasGeometry.GetContentRect(ControlSize, OffsetSubtitleSpace);

        Vector result = PreviewCanvasGeometry.ToScreenDelta(120, -80, content, OffsetSubtitleSpace);

        Assert.Equal(135, result.X, 10);
        Assert.Equal(-90, result.Y, 10);
    }

    [Fact]
    public void GuideCoordinatesIncludeTheContentOriginAndSubtitleOffset()
    {
        Rect content = PreviewCanvasGeometry.GetContentRect(ControlSize, OffsetSubtitleSpace);

        double vertical = PreviewCanvasGeometry.ToScreenCoordinate(
            600, vertical: true, contentRect: content, subtitleSpace: OffsetSubtitleSpace);
        double horizontal = PreviewCanvasGeometry.ToScreenCoordinate(
            450, vertical: false, contentRect: content, subtitleSpace: OffsetSubtitleSpace);

        Assert.Equal(800, vertical, 10);
        Assert.Equal(450, horizontal, 10);
    }

    [Fact]
    public void DefaultSubtitleSpaceKeepsThe1280By720CoordinateMapping()
    {
        Rect subtitleSpace = PreviewCanvasGeometry.DefaultSubtitleSpace;
        Rect content = PreviewCanvasGeometry.GetContentRect(
            new Size(1280, 720), subtitleSpace);
        Point source = new(320, 180);

        AssertRect(content, 0, 0, 1280, 720);
        AssertPoint(PreviewCanvasGeometry.ToScreen(source, content, subtitleSpace), 320, 180);
        AssertPoint(PreviewCanvasGeometry.ToSubtitle(source, content, subtitleSpace), 320, 180);
        AssertRect(
            PreviewCanvasGeometry.ToScreen(new CanvasRect(320, 180, 640, 360), content, subtitleSpace),
            320, 180, 640, 360);
        Assert.Equal(640, PreviewCanvasGeometry.ToScreenCoordinate(
            640, vertical: true, contentRect: content, subtitleSpace: subtitleSpace), 10);
        Assert.Equal(360, PreviewCanvasGeometry.ToScreenCoordinate(
            360, vertical: false, contentRect: content, subtitleSpace: subtitleSpace), 10);
    }

    private static void AssertPoint(Point result, double expectedX, double expectedY)
    {
        Assert.Equal(expectedX, result.X, 10);
        Assert.Equal(expectedY, result.Y, 10);
    }

    private static void AssertRect(
        Rect result,
        double expectedX,
        double expectedY,
        double expectedWidth,
        double expectedHeight)
    {
        Assert.Equal(expectedX, result.X, 10);
        Assert.Equal(expectedY, result.Y, 10);
        Assert.Equal(expectedWidth, result.Width, 10);
        Assert.Equal(expectedHeight, result.Height, 10);
    }
}
