using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using YttStudio.Core;

namespace YttStudio.App;

public sealed partial class PreviewCanvas
{
    private const double MotionMarkerHitRadius = 10.0;
    private const double MotionPathHitRadius = 9.0;
    private const double MotionMarkerRadius = 6.5;
    private const double MotionIntervalDotRadius = 2.5;
    private static readonly Pen MotionPathPen = new(Brushes.DeepSkyBlue, 2);
    private static readonly Pen MotionMarkerPen = new(Brushes.White, 1);
    private static readonly IBrush MotionMarkerBrush =
        new SolidColorBrush(Color.FromArgb(235, 0, 145, 220));
    private static readonly IBrush SelectedMotionMarkerBrush =
        new SolidColorBrush(Color.FromArgb(245, 255, 170, 0));
    private static readonly IBrush MotionIntervalDotBrush =
        new SolidColorBrush(Color.FromArgb(215, 100, 205, 255));
    private static readonly IBrush CurrentMotionIntervalDotBrush =
        new SolidColorBrush(Color.FromArgb(235, 255, 180, 80));
    private bool motionKeyframeDragging;
    private bool motionKeyframeChanged;
    private Guid motionCueId;
    private int motionKeyframeIndex = -1;
    private Point motionPreviewSubtitlePoint;

    private void DrawMotionPath(
        DrawingContext context,
        MainWindowViewModel viewModel,
        Rect content)
    {
        if (!viewModel.IsMotionPathEditing || viewModel.SelectedCueKeyframes.Count == 0)
        {
            return;
        }

        IReadOnlyList<MotionKeyframe> keyframes = GetMotionPathForRender(viewModel);
        MotionPathPresentation presentation = MotionPathGeometry.CreatePresentation(
            keyframes,
            content,
            SubtitleSpace,
            viewModel.SelectedMotionKeyframeIndex);

        for (int index = 0; index < presentation.Keyframes.Count - 1; index++)
        {
            context.DrawLine(
                MotionPathPen,
                presentation.Keyframes[index].ScreenPoint,
                presentation.Keyframes[index + 1].ScreenPoint);
        }

        TimeSpan elapsed = viewModel.SelectedCueRow is CueRowViewModel row
            ? TimeSpan.FromMilliseconds(viewModel.PositionMilliseconds - row.StartMilliseconds)
            : TimeSpan.Zero;
        foreach (MotionPathIntervalDot dot in presentation.TimeIntervalDots)
        {
            IBrush brush = Math.Abs((dot.RelativeTime - elapsed).TotalMilliseconds) < 125
                ? CurrentMotionIntervalDotBrush
                : MotionIntervalDotBrush;
            context.DrawEllipse(
                brush,
                null,
                dot.ScreenPoint,
                MotionIntervalDotRadius,
                MotionIntervalDotRadius);
        }

        foreach (MotionPathKeyframePresentation keyframe in presentation.Keyframes)
        {
            double radius = keyframe.Selected ? MotionMarkerRadius + 1 : MotionMarkerRadius;
            DrawDiamond(
                context,
                keyframe.ScreenPoint,
                radius,
                keyframe.Selected ? SelectedMotionMarkerBrush : MotionMarkerBrush);
        }
    }

    private IReadOnlyList<MotionKeyframe> GetMotionPathForRender(MainWindowViewModel viewModel)
    {
        IReadOnlyList<MotionKeyframe> keyframes = viewModel.SelectedCueKeyframes;
        if (!motionKeyframeDragging || motionCueId == Guid.Empty ||
            motionKeyframeIndex < 0 || motionKeyframeIndex >= keyframes.Count ||
            !viewModel.SelectedCueIds.Contains(motionCueId))
        {
            return keyframes;
        }

        MotionKeyframe[] preview = keyframes.ToArray();
        MotionKeyframe original = preview[motionKeyframeIndex];
        preview[motionKeyframeIndex] = original with
        {
            X = motionPreviewSubtitlePoint.X,
            Y = motionPreviewSubtitlePoint.Y,
        };
        return preview;
    }

    private bool TryBeginMotionInteraction(
        MainWindowViewModel viewModel,
        PointerPressedEventArgs e,
        Point screen,
        Rect content)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            !viewModel.IsMotionPathEditing)
        {
            return false;
        }

        // 캔버스는 실제 자막 공간을 사용한다. ViewModel 도우미는 headless 호출을 위해
        // 기본 프리뷰 공간을 쓰지만 캔버스는 배치와 배율 적용 후 다른 참조 사각형을 쓸 수 있다.
        MotionPathPresentation presentation = MotionPathGeometry.CreatePresentation(
            viewModel.SelectedCueKeyframes,
            content,
            SubtitleSpace,
            viewModel.SelectedMotionKeyframeIndex);
        if (!MotionPathGeometry.TryHitMarker(
                presentation, screen, MotionMarkerHitRadius, out int keyframeIndex))
        {
            if (e.ClickCount == 2 && MotionPathGeometry.IsNearPath(
                    presentation, screen, MotionPathHitRadius, out _))
            {
                _ = AddMotionKeyframeAtScreenPoint(viewModel, screen, content);
                return true;
            }

            return false;
        }

        viewModel.SelectMotionKeyframe(keyframeIndex);
        if (e.ClickCount == 2)
        {
            _ = AddMotionKeyframeAtScreenPoint(viewModel, screen, content);
            return true;
        }

        motionCueId = viewModel.SelectedCueIds.FirstOrDefault();
        motionKeyframeIndex = keyframeIndex;
        motionKeyframeDragging = true;
        motionKeyframeChanged = false;
        motionPreviewSubtitlePoint = presentation.Keyframes[keyframeIndex].SubtitlePoint;
        e.Pointer.Capture(this);
        return true;
    }

    private bool AddMotionKeyframeAtScreenPoint(
        MainWindowViewModel viewModel,
        Point screen,
        Rect content)
    {
        Point subtitle = PreviewCanvasGeometry.ToSubtitle(screen, content, SubtitleSpace);
        return viewModel.AddMotionKeyframeAtCurrentTime(subtitle.X, subtitle.Y);
    }

    private void UpdateMotionKeyframePreview(MainWindowViewModel viewModel, Point screen)
    {
        if (!motionKeyframeDragging || viewModel.SelectedCueKeyframes.Count == 0)
        {
            return;
        }

        Rect content = GetContentRect();
        Point subtitle = PreviewCanvasGeometry.ToSubtitle(screen, content, SubtitleSpace);
        Rect space = PreviewCanvasGeometry.NormalizeSubtitleSpace(SubtitleSpace);
        motionPreviewSubtitlePoint = new Point(
            Math.Clamp(subtitle.X, space.Left, space.Right),
            Math.Clamp(subtitle.Y, space.Top, space.Bottom));
        MotionKeyframe original = viewModel.SelectedCueKeyframes[motionKeyframeIndex];
        motionKeyframeChanged = Math.Abs(original.X - motionPreviewSubtitlePoint.X) >= double.Epsilon ||
            Math.Abs(original.Y - motionPreviewSubtitlePoint.Y) >= double.Epsilon;
        InvalidateVisual();
    }

    private bool CommitMotionPointerRelease(MainWindowViewModel viewModel)
    {
        if (!motionKeyframeDragging)
        {
            return false;
        }

        if (motionKeyframeChanged)
        {
            _ = viewModel.CommitMotionKeyframeDrag(
                motionKeyframeIndex,
                motionPreviewSubtitlePoint.X,
                motionPreviewSubtitlePoint.Y);
        }

        return true;
    }

    private void ResetMotionPointerState()
    {
        motionKeyframeDragging = false;
        motionKeyframeChanged = false;
        motionCueId = Guid.Empty;
        motionKeyframeIndex = -1;
        motionPreviewSubtitlePoint = default;
    }

    private void CancelMotionPointerInteraction()
    {
        if (!motionKeyframeDragging)
        {
            return;
        }

        ResetMotionPointerState();
        InvalidateVisual();
    }

    private static bool TryHandleMotionDelete(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || e.KeyModifiers != KeyModifiers.None ||
            viewModel.IsInlineEditing || !viewModel.HasSelectedMotionKeyframe)
        {
            return false;
        }

        // 경로가 최소 두 점뿐이어도 키 입력을 소비한다.
        // 선택 마커 삭제가 큐 삭제로 넘어가면 안 된다.
        _ = viewModel.DeleteSelectedMotionKeyframe();
        e.Handled = true;
        return true;
    }

    private static void DrawDiamond(
        DrawingContext context,
        Point center,
        double radius,
        IBrush brush)
    {
        IReadOnlyList<Point> points = TimelineMarkerGeometry.GetDiamond(center, radius);
        StreamGeometry geometry = new();
        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(points[0], true);
            for (int index = 1; index < points.Count; index++)
            {
                geometryContext.LineTo(points[index]);
            }

            geometryContext.EndFigure(true);
        }

        context.DrawGeometry(brush, MotionMarkerPen, geometry);
    }
}
