using Avalonia;
using YttStudio.Core;

namespace YttStudio.App;

/// <summary>프리뷰에 표시할 한 모션 키프레임의 좌표다.</summary>
public sealed record MotionPathKeyframePresentation(
    int Index,
    TimeSpan RelativeTime,
    Point SubtitlePoint,
    Point ScreenPoint,
    bool Selected);

/// <summary>두 키프레임 사이의 시간 간격을 나타내는 프리뷰 점이다.</summary>
public sealed record MotionPathIntervalDot(TimeSpan RelativeTime, Point ScreenPoint);

/// <summary>한 선택 큐의 모션 경로 표시 데이터다.</summary>
public sealed record MotionPathPresentation(
    IReadOnlyList<MotionPathKeyframePresentation> Keyframes,
    IReadOnlyList<MotionPathIntervalDot> TimeIntervalDots);

/// <summary>
/// 모션 경로 오버레이의 좌표 변환과 적중 검사를 제공한다.
/// 모든 화면 좌표 변환은 <see cref="PreviewCanvasGeometry"/>를 거치므로
/// 레터박스와 기본값이 아닌 자막 공간에서도 큐 적중 검사와 같은 좌표계를 사용한다.
/// </summary>
public static class MotionPathGeometry
{
    /// <summary>인접 키프레임 사이에 표시할 시간 점의 목표 간격이다.</summary>
    public static readonly TimeSpan IntervalDotSpacing = TimeSpan.FromMilliseconds(250);

    public static MotionPathPresentation CreatePresentation(
        IReadOnlyList<MotionKeyframe> keyframes,
        Rect contentRect,
        Rect subtitleSpace,
        int? selectedIndex = null)
    {
        ArgumentNullException.ThrowIfNull(keyframes);
        MotionPathKeyframePresentation[] points = keyframes
            .Select((keyframe, index) =>
            {
                Point subtitlePoint = new(keyframe.X, keyframe.Y);
                return new MotionPathKeyframePresentation(
                    index,
                    keyframe.RelativeTime,
                    subtitlePoint,
                    PreviewCanvasGeometry.ToScreen(subtitlePoint, contentRect, subtitleSpace),
                    selectedIndex == index);
            })
            .ToArray();

        List<MotionPathIntervalDot> dots = [];
        for (int index = 0; index < keyframes.Count - 1; index++)
        {
            MotionKeyframe from = keyframes[index];
            MotionKeyframe to = keyframes[index + 1];
            double duration = (to.RelativeTime - from.RelativeTime).TotalMilliseconds;
            if (!double.IsFinite(duration) || duration <= 0)
            {
                continue;
            }

            double rawDivisions = Math.Ceiling(
                duration / IntervalDotSpacing.TotalMilliseconds);
            int divisions = rawDivisions >= 64
                ? 64
                : Math.Max(2, (int)rawDivisions);
            for (int division = 1; division < divisions; division++)
            {
                double progress = (double)division / divisions;
                TimeSpan time = from.RelativeTime + TimeSpan.FromMilliseconds(duration * progress);
                Point subtitlePoint = new(
                    Lerp(from.X, to.X, progress),
                    Lerp(from.Y, to.Y, progress));
                dots.Add(new MotionPathIntervalDot(
                    time,
                    PreviewCanvasGeometry.ToScreen(subtitlePoint, contentRect, subtitleSpace)));
            }
        }

        return new MotionPathPresentation(points, dots);
    }

    public static Point ToScreen(
        MotionKeyframe keyframe,
        Rect contentRect,
        Rect subtitleSpace)
    {
        ArgumentNullException.ThrowIfNull(keyframe);
        return PreviewCanvasGeometry.ToScreen(
            new Point(keyframe.X, keyframe.Y), contentRect, subtitleSpace);
    }

    public static bool TryHitMarker(
        MotionPathPresentation presentation,
        Point screenPoint,
        double hitRadius,
        out int keyframeIndex)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        double radius = double.IsFinite(hitRadius) ? Math.Max(0, hitRadius) : 0;
        double radiusSquared = radius * radius;
        double closest = double.PositiveInfinity;
        keyframeIndex = -1;
        foreach (MotionPathKeyframePresentation keyframe in presentation.Keyframes)
        {
            double dx = screenPoint.X - keyframe.ScreenPoint.X;
            double dy = screenPoint.Y - keyframe.ScreenPoint.Y;
            double distanceSquared = (dx * dx) + (dy * dy);
            if (double.IsFinite(distanceSquared) && distanceSquared <= radiusSquared &&
                distanceSquared < closest)
            {
                closest = distanceSquared;
                keyframeIndex = keyframe.Index;
            }
        }

        return keyframeIndex >= 0;
    }

    public static bool IsNearPath(
        MotionPathPresentation presentation,
        Point screenPoint,
        double hitRadius,
        out double distance)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        double closest = double.PositiveInfinity;
        IReadOnlyList<MotionPathKeyframePresentation> keyframes = presentation.Keyframes;
        for (int index = 0; index < keyframes.Count - 1; index++)
        {
            closest = Math.Min(closest, DistanceToSegment(
                screenPoint,
                keyframes[index].ScreenPoint,
                keyframes[index + 1].ScreenPoint));
        }

        distance = closest;
        return double.IsFinite(closest) && closest <= Math.Max(0, hitRadius);
    }

    private static double DistanceToSegment(Point point, Point first, Point second)
    {
        Vector axis = second - first;
        double lengthSquared = (axis.X * axis.X) + (axis.Y * axis.Y);
        if (!double.IsFinite(lengthSquared) || lengthSquared <= double.Epsilon)
        {
            double dx = point.X - first.X;
            double dy = point.Y - first.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        Vector offset = point - first;
        double progress = Math.Clamp(
            ((offset.X * axis.X) + (offset.Y * axis.Y)) / lengthSquared, 0, 1);
        Point closest = first + (axis * progress);
        double dxToClosest = point.X - closest.X;
        double dyToClosest = point.Y - closest.Y;
        return Math.Sqrt((dxToClosest * dxToClosest) + (dyToClosest * dyToClosest));
    }

    private static double Lerp(double first, double second, double progress)
        => first + ((second - first) * progress);
}

/// <summary>타임라인에 표시할 한 선택 큐의 키프레임이다.</summary>
public sealed record MotionTimelineMarker(
    Guid CueId,
    int KeyframeIndex,
    TimeSpan RelativeTime,
    double AbsoluteMilliseconds,
    int Track);

/// <summary>타임라인 키프레임 마커의 순수 도형 계산이다.</summary>
public static class TimelineMarkerGeometry
{
    public static Point GetCenter(
        double absoluteMilliseconds,
        double maximumMilliseconds,
        double viewportStartMilliseconds,
        double zoom,
        double headerWidth,
        double controlWidth,
        double trackTop,
        double trackHeight)
    {
        double safeMaximum = double.IsFinite(maximumMilliseconds)
            ? Math.Max(1, maximumMilliseconds)
            : 1;
        double safeZoom = double.IsFinite(zoom) ? Math.Clamp(zoom, 1, 16) : 1;
        double duration = safeMaximum / safeZoom;
        double safeControlWidth = double.IsFinite(controlWidth) ? controlWidth : headerWidth;
        double horizontalWidth = Math.Max(1, safeControlWidth - headerWidth);
        double safeTime = double.IsFinite(absoluteMilliseconds) ? absoluteMilliseconds : 0;
        double safeViewportStart = double.IsFinite(viewportStartMilliseconds)
            ? viewportStartMilliseconds
            : 0;
        double x = headerWidth +
            ((safeTime - safeViewportStart) / duration * horizontalWidth);
        return new Point(x, trackTop + (Math.Max(0, trackHeight) / 2));
    }

    public static IReadOnlyList<Point> GetDiamond(Point center, double radius = 6)
    {
        double size = double.IsFinite(radius) ? Math.Max(0, radius) : 0;
        return
        [
            new Point(center.X, center.Y - size),
            new Point(center.X + size, center.Y),
            new Point(center.X, center.Y + size),
            new Point(center.X - size, center.Y),
        ];
    }
}
