using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Globalization;

namespace YttStudio.App;

/// <summary>스크럽과 확대와 이동과 끝 트림 제스처를 갖춘 트랙 타임라인을 제공한다.</summary>
public sealed class TimelineControl : Control
{
    private const double HeaderWidth = 64;
    private const double RulerHeight = 22;
    private const double TrackHeight = 28;
    private const double MaxZoom = 16;
    private const double WheelPanFraction = 0.1;
    private const double ScrollBarHeight = 12;
    private const double ScrollBarMargin = 4;
    private const double MinimumThumbWidth = 24;
    private double zoom = 1;
    private double viewportStartMilliseconds;
    private double verticalViewportStart;
    private Guid? dragCueId;
    private DragMode dragMode;
    private Point dragStart;
    private double originalStart;
    private double originalEnd;
    private double previewStart;
    private double previewEnd;
    private int originalTrack;
    private int previewTrack;
    private bool scrubbing;
    private bool panning;
    private bool scrollBarDragging;
    private bool spacePressed;
    private Point panStart;
    private double panViewportStart;
    private double panVerticalViewportStart;
    private double scrollBarGrabOffset;
    private MainWindowViewModel? observedViewModel;
    private readonly HashSet<CueRowViewModel> observedCueRows = [];

    public TimelineControl()
    {
        Focusable = true;
        IsTabStop = true;
        MinHeight = 110;
        ClipToBounds = true;
        DataContextChanged += (_, _) => ObserveViewModel();
    }

    private void ObserveViewModel()
    {
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            observedViewModel.CueRows.CollectionChanged -= OnCueRowsChanged;
        }

        foreach (CueRowViewModel row in observedCueRows)
        {
            row.PropertyChanged -= OnCueRowPropertyChanged;
        }

        observedCueRows.Clear();

        observedViewModel = DataContext as MainWindowViewModel;
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            observedViewModel.CueRows.CollectionChanged += OnCueRowsChanged;
            foreach (CueRowViewModel row in observedViewModel.CueRows)
            {
                ObserveCueRow(row);
            }
        }

        InvalidateVisual();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.PositionMilliseconds) or
            nameof(MainWindowViewModel.MaximumMilliseconds))
        {
            if (e.PropertyName == nameof(MainWindowViewModel.MaximumMilliseconds))
            {
                ClampViewport(observedViewModel?.MaximumMilliseconds ?? 1);
            }

            InvalidateVisual();
        }
    }

    private void OnCueRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (CueRowViewModel row in observedCueRows)
            {
                row.PropertyChanged -= OnCueRowPropertyChanged;
            }

            observedCueRows.Clear();
            if (observedViewModel is not null)
            {
                foreach (CueRowViewModel row in observedViewModel.CueRows)
                {
                    ObserveCueRow(row);
                }
            }
        }
        else
        {
            if (e.OldItems is not null)
            {
                foreach (object item in e.OldItems)
                {
                    if (item is CueRowViewModel row)
                    {
                        UnobserveCueRow(row);
                    }
                }
            }

            if (e.NewItems is not null)
            {
                foreach (object item in e.NewItems)
                {
                    if (item is CueRowViewModel row)
                    {
                        ObserveCueRow(row);
                    }
                }
            }
        }

        InvalidateVisual();
    }

    private void ObserveCueRow(CueRowViewModel row)
    {
        if (observedCueRows.Add(row))
        {
            row.PropertyChanged += OnCueRowPropertyChanged;
        }
    }

    private void UnobserveCueRow(CueRowViewModel row)
    {
        if (observedCueRows.Remove(row))
        {
            row.PropertyChanged -= OnCueRowPropertyChanged;
        }
    }

    private void OnCueRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CueRowViewModel.StartMilliseconds) or
            nameof(CueRowViewModel.EndMilliseconds) or
            nameof(CueRowViewModel.Track) or
            nameof(CueRowViewModel.DurationMilliseconds))
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#181818")), Bounds);
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        double maximum = Math.Max(1, viewModel.MaximumMilliseconds);
        ClampViewport(maximum);
        int trackCount = GetTrackCount(viewModel);
        ClampVerticalViewport(trackCount);

        using (context.PushClip(new Rect(0, RulerHeight, Bounds.Width,
                   Math.Max(0, Bounds.Height - RulerHeight))))
        {
            for (int track = 0; track < trackCount; track++)
            {
                double top = GetTrackTop(track);
                Rect band = new(HeaderWidth, top, Math.Max(0, Bounds.Width - HeaderWidth), TrackHeight);
                Rect label = new(0, top, Math.Min(HeaderWidth, Bounds.Width), TrackHeight);
                context.FillRectangle(track % 2 == 0
                    ? new SolidColorBrush(Color.Parse("#222222"))
                    : new SolidColorBrush(Color.Parse("#292929")), band);
                context.FillRectangle(new SolidColorBrush(Color.Parse("#303030")), label);
                context.DrawLine(new Pen(Brushes.DimGray, 1), new Point(0, band.Bottom),
                    new Point(Bounds.Width, band.Bottom));
                context.DrawText(CreateLabel($"Track {track}", 10, Brushes.LightGray),
                    new Point(7, top + 7));
            }
        }

        DrawTimeRuler(context, maximum);

        using (context.PushClip(new Rect(0, RulerHeight, Bounds.Width,
                   Math.Max(0, Bounds.Height - RulerHeight))))
        {
            foreach (CueRowViewModel row in viewModel.CueRows)
            {
                double start = dragCueId == row.Id ? previewStart : row.StartMilliseconds;
                double end = dragCueId == row.Id ? previewEnd : row.EndMilliseconds;
                int track = GetRenderedTrack(row);
                Rect block = GetCueRect(start, end, track, maximum);
                IBrush fill = viewModel.SelectedCueIds.Contains(row.Id) ? Brushes.DeepSkyBlue : Brushes.SlateBlue;
                context.DrawRectangle(fill, new Pen(Brushes.White, 1), block, 3, 3);
            }

            double playheadX = TimeToX(viewModel.PositionMilliseconds, maximum);
            context.DrawLine(new Pen(Brushes.OrangeRed, 2), new Point(playheadX, RulerHeight),
                new Point(playheadX, Bounds.Height));
        }

        DrawHorizontalScrollBar(context, maximum);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Point point = e.GetPosition(this);
        double maximum = Math.Max(1, viewModel.MaximumMilliseconds);
        ClampViewport(maximum);
        ClampVerticalViewport(GetTrackCount(viewModel));
        Focus();

        if (TryBeginScrollBarDrag(e, point, maximum))
        {
            return;
        }

        if (TryBeginPan(e, point))
        {
            return;
        }

        BeginTimelineInteraction(viewModel, e, point, maximum);
    }

    private bool TryBeginPan(PointerPressedEventArgs e, Point point)
    {
        if (!spacePressed && !IsMiddleButtonPressed(e))
        {
            return false;
        }

        CancelTimelineInteractionForNavigation();
        BeginPan(point);
        e.Pointer.Capture(this);
        e.Handled = true;
        return true;
    }

    private void BeginTimelineInteraction(
        MainWindowViewModel viewModel,
        PointerPressedEventArgs e,
        Point point,
        double maximum)
    {
        CueRowViewModel? hit = viewModel.CueRows.Reverse().FirstOrDefault(row =>
            GetCueRect(row.StartMilliseconds, row.EndMilliseconds, row.Track,
                maximum).Contains(point) && point.Y >= RulerHeight);
        if (hit is null)
        {
            scrubbing = true;
            viewModel.PositionMilliseconds = XToTime(point.X, maximum);
        }
        else
        {
            viewModel.SelectCue(hit.Id, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            Rect block = GetCueRect(hit.StartMilliseconds, hit.EndMilliseconds, hit.Track,
                maximum);
            dragCueId = hit.Id;
            originalStart = hit.StartMilliseconds;
            originalEnd = hit.EndMilliseconds;
            previewStart = originalStart;
            previewEnd = originalEnd;
            originalTrack = hit.Track;
            previewTrack = originalTrack;
            dragMode = Math.Abs(point.X - block.Left) <= 7
                ? DragMode.Start
                : Math.Abs(point.X - block.Right) <= 7 ? DragMode.End : DragMode.Body;
        }

        dragStart = point;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Point point = e.GetPosition(this);
        double maximum = Math.Max(1, viewModel.MaximumMilliseconds);
        ClampViewport(maximum);
        ClampVerticalViewport(GetTrackCount(viewModel));
        if (scrollBarDragging)
        {
            UpdateScrollBarDrag(point, maximum);
            e.Handled = true;
            return;
        }

        if (panning)
        {
            UpdatePan(point, maximum);
            e.Handled = true;
            return;
        }

        if (scrubbing)
        {
            viewModel.PositionMilliseconds = XToTime(point.X, maximum);
        }
        else if (dragCueId.HasValue)
        {
            double delta = XToTime(point.X, maximum) -
                XToTime(dragStart.X, maximum);
            switch (dragMode)
            {
                case DragMode.Start:
                    previewStart = Math.Clamp(originalStart + delta, 0, originalEnd - 1);
                    break;
                case DragMode.End:
                    previewEnd = Math.Max(originalStart + 1, originalEnd + delta);
                    break;
                case DragMode.Body:
                    double duration = originalEnd - originalStart;
                    previewStart = Math.Max(0, originalStart + delta);
                    previewEnd = previewStart + duration;
                    previewTrack = Math.Max(0, originalTrack +
                        (int)Math.Round((point.Y - dragStart.Y) / TrackHeight,
                            MidpointRounding.AwayFromZero));
                    break;
                default:
                    // DragMode.None 은 진행 중인 타임라인 드래그가 없다는 뜻이다.
                    break;
            }

            InvalidateVisual();
        }
    }

    protected override async void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (scrollBarDragging)
        {
            EndScrollBarDrag();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (panning)
        {
            EndPan();
            e.Pointer.Capture(null);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            if (scrubbing)
            {
                await viewModel.SeekExactAsync(viewModel.PositionMilliseconds);
            }
            else if (dragCueId is Guid cueId)
            {
                viewModel.UpdateCueTiming(cueId, previewStart, previewEnd, previewTrack);
            }
        }

        scrubbing = false;
        dragCueId = null;
        dragMode = DragMode.None;
        previewTrack = 0;
        e.Pointer.Capture(null);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        double maximum = Math.Max(1, viewModel.MaximumMilliseconds);
        ClampViewport(maximum);
        double wheelDelta = GetWheelDelta(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
            e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            ZoomAt(e.GetPosition(this), maximum, wheelDelta);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            PanHorizontally(wheelDelta, maximum);
        }
        else
        {
            PanVertically(wheelDelta, GetTrackCount(viewModel));
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Space)
        {
            spacePressed = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
        {
            spacePressed = false;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (scrollBarDragging)
        {
            EndScrollBarDrag();
        }

        if (panning)
        {
            EndPan();
            InvalidateVisual();
        }
    }

    private void BeginPan(Point point)
    {
        panning = true;
        panStart = point;
        panViewportStart = viewportStartMilliseconds;
        panVerticalViewportStart = verticalViewportStart;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    private void UpdatePan(Point point, double maximum)
    {
        double horizontalWidth = Math.Max(1, Bounds.Width - HeaderWidth);
        double duration = GetViewportDuration(maximum);
        viewportStartMilliseconds = panViewportStart -
            ((point.X - panStart.X) / horizontalWidth * duration);
        verticalViewportStart = panVerticalViewportStart - (point.Y - panStart.Y);
        ClampViewport(maximum);
        if (DataContext is MainWindowViewModel viewModel)
        {
            ClampVerticalViewport(GetTrackCount(viewModel));
        }

        InvalidateVisual();
    }

    private void EndPan()
    {
        panning = false;
        Cursor = null;
    }

    private bool TryBeginScrollBarDrag(
        PointerPressedEventArgs e,
        Point point,
        double maximum)
    {
        PointerPoint pointerPoint = e.GetCurrentPoint(this);
        Rect track = GetScrollBarTrack();
        if (!pointerPoint.Properties.IsLeftButtonPressed || !track.Contains(point))
        {
            return false;
        }

        Rect thumb = GetScrollBarThumb(maximum);
        CancelTimelineInteractionForNavigation();
        scrollBarGrabOffset = thumb.Contains(point) ? point.X - thumb.X : thumb.Width / 2;
        scrollBarDragging = zoom > 1;
        if (scrollBarDragging)
        {
            UpdateScrollBarDrag(point, maximum);
            Cursor = new Cursor(StandardCursorType.Hand);
            e.Pointer.Capture(this);
        }

        e.Handled = true;
        return true;
    }

    private void CancelTimelineInteractionForNavigation()
    {
        scrubbing = false;
        dragCueId = null;
        dragMode = DragMode.None;
    }

    private void UpdateScrollBarDrag(Point point, double maximum)
    {
        Rect track = GetScrollBarTrack();
        Rect thumb = GetScrollBarThumb(maximum);
        double travel = Math.Max(0, track.Width - thumb.Width);
        double thumbLeft = Math.Clamp(point.X - scrollBarGrabOffset - track.X, 0, travel);
        double maximumStart = Math.Max(0, maximum - GetViewportDuration(maximum));
        viewportStartMilliseconds = travel > 0 ? thumbLeft / travel * maximumStart : 0;
        ClampViewport(maximum);
        InvalidateVisual();
    }

    private void EndScrollBarDrag()
    {
        scrollBarDragging = false;
        Cursor = null;
        InvalidateVisual();
    }

    private void ZoomAt(Point point, double maximum, double wheelDelta)
    {
        double horizontalWidth = Math.Max(1, Bounds.Width - HeaderWidth);
        double anchorX = Math.Clamp(point.X, HeaderWidth, Bounds.Width);
        double anchorRatio = Math.Clamp((anchorX - HeaderWidth) / horizontalWidth, 0, 1);
        double anchorTime = XToTime(anchorX, maximum);
        zoom = Math.Clamp(zoom * (wheelDelta > 0 ? 1.2 : 1 / 1.2), 1, MaxZoom);
        viewportStartMilliseconds = anchorTime - anchorRatio * GetViewportDuration(maximum);
        ClampViewport(maximum);
    }

    private void PanHorizontally(double wheelDelta, double maximum)
    {
        viewportStartMilliseconds -= GetViewportDuration(maximum) * wheelDelta * WheelPanFraction;
        ClampViewport(maximum);
    }

    private void PanVertically(double wheelDelta, int trackCount)
    {
        verticalViewportStart -= wheelDelta * TrackHeight * 3;
        ClampVerticalViewport(trackCount);
    }

    private bool IsMiddleButtonPressed(PointerPressedEventArgs e)
        => e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed;

    private static double GetWheelDelta(PointerWheelEventArgs e)
        => Math.Abs(e.Delta.Y) > double.Epsilon ? e.Delta.Y : e.Delta.X;

    private Rect GetCueRect(double start, double end, int track, double maximum)
        => new(TimeToX(start, maximum), GetTrackTop(track) + 4,
            Math.Max(4, TimeToX(end, maximum) - TimeToX(start, maximum)), TrackHeight - 8);

    private double TimeToX(double milliseconds, double maximum)
        => HeaderWidth + ((milliseconds - viewportStartMilliseconds) / GetViewportDuration(maximum) *
            Math.Max(1, Bounds.Width - HeaderWidth));

    private double XToTime(double x, double maximum)
        => Math.Clamp(viewportStartMilliseconds +
            ((x - HeaderWidth) / Math.Max(1, Bounds.Width - HeaderWidth) * GetViewportDuration(maximum)),
            0, Math.Max(1, maximum));

    private int GetRenderedTrack(CueRowViewModel row)
        => dragCueId == row.Id ? previewTrack : row.Track;

    private int GetTrackCount(MainWindowViewModel viewModel)
        => Math.Max(1, viewModel.CueRows
            .Select(GetRenderedTrack)
            .DefaultIfEmpty(0)
            .Max() + 1);

    private double GetTrackTop(int track)
        => RulerHeight + (Math.Max(0, track) * TrackHeight) - verticalViewportStart;

    private double GetViewportDuration(double maximum)
        => Math.Max(1, maximum) / zoom;

    private void ClampViewport(double maximum)
    {
        double safeMaximum = Math.Max(1, maximum);
        double duration = GetViewportDuration(safeMaximum);
        viewportStartMilliseconds = Math.Clamp(viewportStartMilliseconds, 0,
            Math.Max(0, safeMaximum - duration));
    }

    private void ClampVerticalViewport(int trackCount)
    {
        double contentHeight = Math.Max(1, trackCount) * TrackHeight;
        double visibleHeight = Math.Max(0, Bounds.Height - RulerHeight);
        verticalViewportStart = Math.Clamp(verticalViewportStart, 0,
            Math.Max(0, contentHeight - visibleHeight));
    }

    private void DrawTimeRuler(DrawingContext context, double maximum)
    {
        context.FillRectangle(new SolidColorBrush(Color.Parse("#303030")),
            new Rect(0, 0, Bounds.Width, RulerHeight));
        context.DrawLine(new Pen(Brushes.DimGray, 1), new Point(HeaderWidth, 0),
            new Point(HeaderWidth, Bounds.Height));
        context.DrawText(CreateLabel("Track", 10, Brushes.LightGray), new Point(7, 4));

        double duration = GetViewportDuration(maximum);
        double step = GetTickStep(duration);
        double end = Math.Min(maximum, viewportStartMilliseconds + duration);
        double firstTick = Math.Ceiling(viewportStartMilliseconds / step) * step;
        int tickCount = 0;
        for (double tick = firstTick; tick <= end + (step * 0.5) && tickCount < 200;
             tick += step, tickCount++)
        {
            double x = TimeToX(tick, maximum);
            if (x < HeaderWidth - 0.5 || x > Bounds.Width + 0.5)
            {
                continue;
            }

            context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#555555")), 1),
                new Point(x, RulerHeight - 5), new Point(x, Bounds.Height));
            context.DrawText(CreateLabel(FormatTick(tick, step), 10, Brushes.LightGray),
                new Point(x + 3, 3));
        }
    }

    private void DrawHorizontalScrollBar(DrawingContext context, double maximum)
    {
        Rect track = GetScrollBarTrack();
        Rect thumb = GetScrollBarThumb(maximum);
        context.DrawRectangle(Brushes.Black, new Pen(Brushes.DimGray, 1), track, 3, 3);
        context.DrawRectangle(
            scrollBarDragging ? Brushes.LightGray : Brushes.Gray,
            null,
            thumb,
            3,
            3);
    }

    private Rect GetScrollBarTrack()
    {
        double width = Math.Max(1, Bounds.Width - HeaderWidth - (ScrollBarMargin * 2));
        double y = Math.Max(RulerHeight, Bounds.Height - ScrollBarHeight - ScrollBarMargin);
        return new Rect(HeaderWidth + ScrollBarMargin, y, width, ScrollBarHeight);
    }

    private Rect GetScrollBarThumb(double maximum)
    {
        Rect track = GetScrollBarTrack();
        double thumbWidth = Math.Clamp(track.Width / zoom, MinimumThumbWidth, track.Width);
        double maximumStart = Math.Max(0, maximum - GetViewportDuration(maximum));
        double ratio = maximumStart > 0 ? viewportStartMilliseconds / maximumStart : 0;
        double x = track.X + (Math.Clamp(ratio, 0, 1) * (track.Width - thumbWidth));
        return new Rect(x, track.Y, thumbWidth, track.Height);
    }

    private static double GetTickStep(double visibleDuration)
    {
        double rawStep = Math.Max(1, visibleDuration / 8);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        double normalized = rawStep / magnitude;
        double multiplier = normalized < 1.5 ? 1 : normalized < 3 ? 2 : normalized < 7 ? 5 : 10;
        return multiplier * magnitude;
    }

    private static string FormatTick(double milliseconds, double step)
    {
        TimeSpan time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        if (time.TotalHours >= 1)
        {
            return step < 1000
                ? time.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
                : time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
        }

        if (step >= 1000)
        {
            return time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }

        return step < 10
            ? time.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture)
            : step < 100
                ? time.ToString(@"m\:ss\.ff", CultureInfo.InvariantCulture)
                : time.ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);
    }

    private static FormattedText CreateLabel(string text, double fontSize, IBrush brush)
        => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), fontSize, brush);

    private enum DragMode
    {
        None,
        Start,
        End,
        Body,
    }
}
