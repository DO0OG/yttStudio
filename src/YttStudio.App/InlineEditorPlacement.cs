using Avalonia;

namespace YttStudio.App;

/// <summary>프리뷰 안쪽에 인라인 편집기를 배치하기 위한 순수 좌표 계산이다.</summary>
public static class InlineEditorPlacement
{
    /// <summary>편집기 행의 기본 높이다.</summary>
    public const double DefaultHeight = 36;

    /// <summary>요청한 사각형을 뷰포트 안쪽으로 이동하고 크기를 줄인다.</summary>
    public static Rect Clamp(Rect requested, Rect viewport)
    {
        double viewportWidth = Math.Max(0, Finite(viewport.Width));
        double viewportHeight = Math.Max(0, Finite(viewport.Height));
        double width = Math.Clamp(Math.Max(0, Finite(requested.Width)), 0, viewportWidth);
        double height = Math.Clamp(Math.Max(0, Finite(requested.Height)), 0, viewportHeight);

        double viewportRight = viewport.X + viewportWidth;
        double viewportBottom = viewport.Y + viewportHeight;
        double left = Math.Clamp(Finite(requested.X, viewport.X), viewport.X, viewportRight - width);
        double top = Math.Clamp(Finite(requested.Y, viewport.Y), viewport.Y, viewportBottom - height);
        return new Rect(left, top, width, height);
    }

    /// <summary>원점이 0인 뷰포트에 대해 사각형을 클램프한다.</summary>
    public static Rect Clamp(Rect requested, double viewportWidth, double viewportHeight)
        => Clamp(requested, new Rect(0, 0, Math.Max(0, viewportWidth), Math.Max(0, viewportHeight)));

    /// <summary>원점이 0인 뷰포트에 대해 인라인 편집기 위치를 계산한다.</summary>
    public static Rect Clamp(
        double left,
        double top,
        double width,
        double height,
        double viewportWidth,
        double viewportHeight)
        => Clamp(new Rect(left, top, width, height), viewportWidth, viewportHeight);

    private static double Finite(double value, double fallback = 0)
        => double.IsFinite(value) ? value : fallback;
}
