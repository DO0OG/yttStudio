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
    private double zoom = 1;
    private double viewportStartMilliseconds;
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
    private MainWindowViewModel? observedViewModel;
    private readonly HashSet<CueRowViewModel> observedCueRows = [];

    public TimelineControl()
    {
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
        int trackCount = Math.Max(1, viewModel.CueRows
            .Select(GetRenderedTrack)
            .DefaultIfEmpty(0)
            .Max() + 1);

        for (int track = 0; track < trackCount; track++)
        {
            double top = RulerHeight + track * TrackHeight;
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

        DrawTimeRuler(context, maximum);

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
        CueRowViewModel? hit = viewModel.CueRows.Reverse().FirstOrDefault(row =>
            GetCueRect(row.StartMilliseconds, row.EndMilliseconds, row.Track,
                maximum).Contains(point));
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
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        double maximum = Math.Max(1, viewModel.MaximumMilliseconds);
        ClampViewport(maximum);
        double oldViewportDuration = GetViewportDuration(maximum);
        double playhead = Math.Clamp(viewModel.PositionMilliseconds, 0, maximum);
        double playheadRatio = Math.Clamp(
            (playhead - viewportStartMilliseconds) / oldViewportDuration, 0, 1);
        zoom = Math.Clamp(zoom * (e.Delta.Y > 0 ? 1.2 : 1 / 1.2), 1, 16);
        double newViewportDuration = GetViewportDuration(maximum);
        viewportStartMilliseconds = playhead - playheadRatio * newViewportDuration;
        ClampViewport(maximum);
        InvalidateVisual();
        e.Handled = true;
    }

    private Rect GetCueRect(double start, double end, int track, double maximum)
        => new(TimeToX(start, maximum), RulerHeight + (Math.Max(0, track) * TrackHeight) + 4,
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

    private double GetViewportDuration(double maximum)
        => Math.Max(1, maximum) / zoom;

    private void ClampViewport(double maximum)
    {
        double safeMaximum = Math.Max(1, maximum);
        double duration = GetViewportDuration(safeMaximum);
        viewportStartMilliseconds = Math.Clamp(viewportStartMilliseconds, 0,
            Math.Max(0, safeMaximum - duration));
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
