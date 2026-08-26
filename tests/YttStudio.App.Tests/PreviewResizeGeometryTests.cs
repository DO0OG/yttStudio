using Avalonia;
using Avalonia.Input;
using YttStudio.App;
using YttStudio.Core;
using YttStudio.Core.Editing;

namespace YttStudio.App.Tests;

public sealed class PreviewResizeGeometryTests
{
    private static readonly Rect Box = new(100, 80, 200, 100);

    [Fact]
    public void ClampSizePercentUsesDocumentMinimumAndEditorMaximum()
    {
        Assert.Equal(YttStudio.Core.YttConstants.MinimumFontSizePercent,
            PreviewResizeGeometry.ClampSizePercent(1));
        Assert.Equal(PreviewResizeGeometry.MaximumSizePercent,
            PreviewResizeGeometry.ClampSizePercent(999));
        Assert.Equal(YttStudio.Core.YttConstants.MinimumFontSizePercent,
            PreviewResizeGeometry.ClampSizePercent(double.NaN));
    }

    [Fact]
    public void HandleCentersSitOnTheSelectionBoxPerimeter()
    {
        Assert.Equal(new Point(100, 80), PreviewResizeGeometry.GetHandleCenter(Box, 0, 0));
        Assert.Equal(new Point(200, 80), PreviewResizeGeometry.GetHandleCenter(Box, 0, 1));
        Assert.Equal(new Point(300, 180), PreviewResizeGeometry.GetHandleCenter(Box, 2, 2));
        Assert.Equal(new Point(200, 130), PreviewResizeGeometry.GetHandleCenter(Box, 1, 1));
    }

    [Fact]
    public void OnlyTheCentreCellIsAnchorOnly()
    {
        int resizeHandles = 0;
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                if (PreviewResizeGeometry.IsResizeHandle(row, column))
                {
                    resizeHandles++;
                }
            }
        }

        Assert.Equal(8, resizeHandles);
        Assert.False(PreviewResizeGeometry.IsResizeHandle(1, 1));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public void EveryOuterHandleIsHitAtItsOwnCentre(int expectedRow, int expectedColumn)
    {
        Point centre = PreviewResizeGeometry.GetHandleCenter(Box, expectedRow, expectedColumn);

        Assert.True(PreviewResizeGeometry.TryHitHandle(Box, centre, 8, out int row, out int column));
        Assert.Equal(expectedRow, row);
        Assert.Equal(expectedColumn, column);
    }

    [Fact]
    public void PointAwayFromEveryHandleIsNotAHit()
    {
        Assert.False(PreviewResizeGeometry.TryHitHandle(
            Box, new Point(150, 100), 8, out _, out _));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public void TheOppositeCellIsTheFixedPivot(int row, int column)
    {
        (int pivotRow, int pivotColumn) = PreviewResizeGeometry.GetPivotCell(row, column);

        Assert.Equal(2 - row, pivotRow);
        Assert.Equal(2 - column, pivotColumn);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public void DraggingAHandleAwayFromItsPivotGrowsAndBackShrinks(int row, int column)
    {
        Point handle = PreviewResizeGeometry.GetHandleCenter(Box, row, column);
        (int pivotRow, int pivotColumn) = PreviewResizeGeometry.GetPivotCell(row, column);
        Point pivot = PreviewResizeGeometry.GetHandleCenter(Box, pivotRow, pivotColumn);
        Vector axis = handle - pivot;

        Assert.True(PreviewResizeGeometry.ComputeMultiplier(Box, row, column, axis * 0.5, false) > 1.0);
        Assert.True(PreviewResizeGeometry.ComputeMultiplier(Box, row, column, axis * -0.5, false) < 1.0);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public void MovementAlongTheFixedEdgeDoesNotChangeTheSize(int row, int column)
    {
        Point handle = PreviewResizeGeometry.GetHandleCenter(Box, row, column);
        (int pivotRow, int pivotColumn) = PreviewResizeGeometry.GetPivotCell(row, column);
        Point pivot = PreviewResizeGeometry.GetHandleCenter(Box, pivotRow, pivotColumn);
        Vector axis = handle - pivot;
        Vector perpendicular = new(-axis.Y, axis.X);

        Assert.Equal(1.0,
            PreviewResizeGeometry.ComputeMultiplier(Box, row, column, perpendicular, false), 10);
    }

    [Fact]
    public void GrabbingTheCentreCellDoesNotDivideByZero()
    {
        Assert.Equal(1.0,
            PreviewResizeGeometry.ComputeMultiplier(Box, 1, 1, new Vector(400, 400), false));
    }

    [Fact]
    public void ShiftReducesTheSizeChangeToAQuarter()
    {
        Vector delta = new(-40, -20);

        double normal = PreviewResizeGeometry.ComputeMultiplier(Box, 0, 0, delta, false);
        double fine = PreviewResizeGeometry.ComputeMultiplier(Box, 0, 0, delta, true);

        Assert.Equal(1 + ((normal - 1) / 4), fine, 10);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(2, 2)]
    public void CompensationKeepsThePivotWhereItWas(int grabbedRow, int grabbedColumn)
    {
        const double scale = 1.5;
        AnchorPoint anchor = AnchorPoint.BottomCenter;
        (int pivotRow, int pivotColumn) = PreviewResizeGeometry.GetPivotCell(grabbedRow, grabbedColumn);
        CanvasResizeGeometry geometry = new(
            new CanvasPoint(
                Box.X + (Box.Width * CanvasResizeGeometry.CellFraction((int)anchor % 3)),
                Box.Y + (Box.Height * CanvasResizeGeometry.CellFraction((int)anchor / 3))),
            Box.Width,
            Box.Height,
            CanvasResizeGeometry.CellFraction((int)anchor % 3),
            CanvasResizeGeometry.CellFraction((int)anchor / 3),
            CanvasResizeGeometry.CellFraction(pivotColumn),
            CanvasResizeGeometry.CellFraction(pivotRow));

        CanvasPoint movedAnchor = geometry.GetCompensatedAnchor(scale);

        // Rebuild the scaled box around the compensated anchor and confirm the
        // pivot corner landed back on its original screen position.
        double scaledWidth = Box.Width * scale;
        double scaledHeight = Box.Height * scale;
        double left = movedAnchor.X - (scaledWidth * geometry.AnchorColumnFraction);
        double top = movedAnchor.Y - (scaledHeight * geometry.AnchorRowFraction);
        double pivotX = left + (scaledWidth * geometry.PivotColumnFraction);
        double pivotY = top + (scaledHeight * geometry.PivotRowFraction);

        Assert.Equal(PreviewResizeGeometry.GetHandleCenter(Box, pivotRow, pivotColumn).X, pivotX, 9);
        Assert.Equal(PreviewResizeGeometry.GetHandleCenter(Box, pivotRow, pivotColumn).Y, pivotY, 9);
    }

    [Fact]
    public void PreviewDeleteRequiresASelectionAndNoInlineEdit()
    {
        Assert.True(PreviewCanvas.ShouldDeleteSelectedCues(
            Key.Delete, KeyModifiers.None, inlineEditing: false, selectedCueCount: 1));
        Assert.False(PreviewCanvas.ShouldDeleteSelectedCues(
            Key.Delete, KeyModifiers.None, inlineEditing: true, selectedCueCount: 1));
        Assert.False(PreviewCanvas.ShouldDeleteSelectedCues(
            Key.Delete, KeyModifiers.None, inlineEditing: false, selectedCueCount: 0));
        Assert.False(PreviewCanvas.ShouldDeleteSelectedCues(
            Key.Delete, KeyModifiers.Control, inlineEditing: false, selectedCueCount: 1));
        Assert.False(PreviewCanvas.ShouldDeleteSelectedCues(
            Key.Back, KeyModifiers.None, inlineEditing: false, selectedCueCount: 1));
    }
}
