namespace YttStudio.Core.Editing;

/// <summary>Represents a point in editor canvas pixels.</summary>
public readonly record struct CanvasPoint(double X, double Y);

/// <summary>Represents a rectangle in editor canvas pixels.</summary>
public readonly record struct CanvasRect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>Identifies a highlighted snapping guide.</summary>
public sealed record SnapGuide(bool Vertical, double Position, string Label);

/// <summary>Contains a snapped point and the guides that caused the adjustment.</summary>
public sealed record SnapResult(CanvasPoint Point, IReadOnlyList<SnapGuide> Guides);

/// <summary>Implements canvas/YTT conversion, anchor preservation, and SPEC §9.3 snapping.</summary>
public static class CanvasGeometry
{
    public static CanvasPoint ToCanvasPoint(double positionX, double positionY, double width, double height)
    {
        ValidateExtent(width, height);
        return new CanvasPoint(ToPixel(positionX, width), ToPixel(positionY, height));
    }

    public static CanvasPoint ToYttPoint(double pixelX, double pixelY, double width, double height)
    {
        ValidateExtent(width, height);
        return new CanvasPoint(ToYtt(pixelX, width), ToYtt(pixelY, height));
    }

    public static CanvasPoint PreserveBoxForAnchor(
        CanvasRect box,
        AnchorPoint newAnchor,
        double canvasWidth,
        double canvasHeight)
    {
        int column = (int)newAnchor % 3;
        int row = (int)newAnchor / 3;
        double anchorX = box.Left + (box.Width * column / 2.0);
        double anchorY = box.Top + (box.Height * row / 2.0);
        return ToYttPoint(anchorX, anchorY, canvasWidth, canvasHeight);
    }

    public static SnapResult Snap(
        CanvasPoint point,
        double canvasWidth,
        double canvasHeight,
        bool altPressed,
        IReadOnlyList<SnapGuide>? additionalGuides = null,
        double threshold = YttConstants.DefaultSnapThresholdPixels)
    {
        ValidateExtent(canvasWidth, canvasHeight);
        if (altPressed)
        {
            return new SnapResult(point, []);
        }

        List<SnapGuide> candidates =
        [
            new(true, canvasWidth / 2, "가로 중앙"),
            new(true, canvasWidth / 3, "가로 1/3"),
            new(true, canvasWidth * 2 / 3, "가로 2/3"),
            new(false, canvasHeight / 2, "세로 중앙"),
            new(false, canvasHeight / 3, "세로 1/3"),
            new(false, canvasHeight * 2 / 3, "세로 2/3"),
            new(true, canvasWidth * YttConstants.DefaultSafeAreaPercent / 100, "세이프 좌측"),
            new(true, canvasWidth * (100 - YttConstants.DefaultSafeAreaPercent) / 100, "세이프 우측"),
            new(false, canvasHeight * YttConstants.DefaultSafeAreaPercent / 100, "세이프 상단"),
            new(false, canvasHeight * (100 - YttConstants.DefaultSafeAreaPercent) / 100, "세이프 하단"),
            new(false, ToPixel(90, canvasHeight), "하단 표준"),
        ];
        if (additionalGuides is not null)
        {
            candidates.AddRange(additionalGuides);
        }

        SnapGuide? vertical = candidates.Where(guide => guide.Vertical)
            .Where(guide => Math.Abs(guide.Position - point.X) <= threshold)
            .MinBy(guide => Math.Abs(guide.Position - point.X));
        SnapGuide? horizontal = candidates.Where(guide => !guide.Vertical)
            .Where(guide => Math.Abs(guide.Position - point.Y) <= threshold)
            .MinBy(guide => Math.Abs(guide.Position - point.Y));
        List<SnapGuide> applied = [];
        double x = point.X;
        double y = point.Y;
        if (vertical is not null)
        {
            x = vertical.Position;
            applied.Add(vertical);
        }

        if (horizontal is not null)
        {
            y = horizontal.Position;
            applied.Add(horizontal);
        }

        return new SnapResult(new CanvasPoint(x, y), applied);
    }

    private static double ToPixel(double ytt, double maximum)
        => (YttConstants.CoordinateOffset + (ytt * YttConstants.CoordinateScale)) / 100 * maximum;

    private static double ToYtt(double pixel, double maximum)
        => Math.Clamp(((pixel / maximum * 100) - YttConstants.CoordinateOffset) /
            YttConstants.CoordinateScale, 0, 100);

    private static void ValidateExtent(double width, double height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
    }
}
