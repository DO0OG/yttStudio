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

    /// <summary>
    /// 뷰포트와 화면에 표시된 자막 공간의 교집합 안쪽으로 편집기를 클램프한다.
    /// 자막 공간에 오프셋이 있으면 그 오프셋도 화면 좌표로 취급한다.
    /// </summary>
    public static Rect Clamp(Rect requested, Rect viewport, Rect subtitleSpace)
    {
        Rect effectiveViewport = Intersect(viewport, subtitleSpace);
        return effectiveViewport.Width > 0 && effectiveViewport.Height > 0
            ? Clamp(requested, effectiveViewport)
            : Clamp(requested, viewport);
    }

    /// <summary>자막 공간 자체를 편집기 뷰포트로 사용해 클램프한다.</summary>
    public static Rect ClampToSubtitleSpace(Rect requested, Rect subtitleSpace)
        => Clamp(requested, subtitleSpace);

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

    private static Rect Intersect(Rect first, Rect second)
    {
        double firstLeft = Finite(first.X);
        double firstTop = Finite(first.Y);
        double firstRight = firstLeft + Math.Max(0, Finite(first.Width));
        double firstBottom = firstTop + Math.Max(0, Finite(first.Height));
        double secondLeft = Finite(second.X);
        double secondTop = Finite(second.Y);
        double secondRight = secondLeft + Math.Max(0, Finite(second.Width));
        double secondBottom = secondTop + Math.Max(0, Finite(second.Height));
        double left = Math.Max(firstLeft, secondLeft);
        double top = Math.Max(firstTop, secondTop);
        double right = Math.Min(firstRight, secondRight);
        double bottom = Math.Min(firstBottom, secondBottom);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
