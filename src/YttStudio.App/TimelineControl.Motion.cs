using Avalonia;
using Avalonia.Media;

namespace YttStudio.App;

public sealed partial class TimelineControl
{
    /// <summary>키프레임의 시간 드래그 UI는 아직 제공하지 않는다.</summary>
    public const bool SupportsMotionKeyframeTimeDrag = false;

    private static readonly IBrush MotionTimelineMarkerBrush =
        new SolidColorBrush(Color.FromArgb(245, 255, 190, 70));
    private static readonly IBrush SelectedMotionTimelineMarkerBrush =
        new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
    private static readonly Pen MotionTimelineMarkerPen = new(Brushes.Black, 1);

    private void DrawMotionKeyframeMarkers(
        DrawingContext context,
        MainWindowViewModel viewModel,
        CueRowViewModel row,
        double renderedStart,
        int renderedTrack,
        double maximum)
    {
        if (viewModel.SelectedCueIds.Count != 1 ||
            !viewModel.SelectedCueIds.Contains(row.Id) ||
            !viewModel.IsMotionPathEditing)
        {
            return;
        }

        foreach (MotionTimelineMarker marker in viewModel.SelectedCueKeyframeMarkers)
        {
            if (marker.CueId != row.Id)
            {
                continue;
            }

            double absoluteMilliseconds = renderedStart + marker.RelativeTime.TotalMilliseconds;
            Point center = new(
                TimeToX(absoluteMilliseconds, maximum),
                GetTrackTop(renderedTrack) + (TrackHeight / 2));
            IReadOnlyList<Point> points = TimelineMarkerGeometry.GetDiamond(center, 5.5);
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

            bool selected = viewModel.SelectedMotionKeyframeIndex == marker.KeyframeIndex;
            context.DrawGeometry(
                selected ? SelectedMotionTimelineMarkerBrush : MotionTimelineMarkerBrush,
                MotionTimelineMarkerPen,
                geometry);
        }
    }
}
