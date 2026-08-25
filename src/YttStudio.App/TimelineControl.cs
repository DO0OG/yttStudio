using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.ComponentModel;
using System.Collections.Specialized;

namespace YttStudio.App;

/// <summary>Provides the M2 track timeline with scrub, zoom, move, and edge trim gestures.</summary>
public sealed class TimelineControl : Control
{
    private const double HeaderWidth = 44;
    private const double TrackHeight = 28;
    private double zoom = 1;
    private Guid? dragCueId;
    private DragMode dragMode;
    private Point dragStart;
    private double originalStart;
    private double originalEnd;
    private double previewStart;
    private double previewEnd;
    private int originalTrack;
    private bool scrubbing;
    private MainWindowViewModel? observedViewModel;

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

        observedViewModel = DataContext as MainWindowViewModel;
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            observedViewModel.CueRows.CollectionChanged += OnCueRowsChanged;
        }

        InvalidateVisual();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.PositionMilliseconds) or
            nameof(MainWindowViewModel.MaximumMilliseconds))
        {
            InvalidateVisual();
        }
    }

    private void OnCueRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#181818")), Bounds);
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        int trackCount = Math.Max(1, viewModel.CueRows.Select(row => row.Track).DefaultIfEmpty(0).Max() + 1);
        for (int track = 0; track < trackCount; track++)
        {
            Rect band = new(0, track * TrackHeight, Bounds.Width, TrackHeight);
            context.FillRectangle(track % 2 == 0
                ? new SolidColorBrush(Color.Parse("#222222"))
                : new SolidColorBrush(Color.Parse("#292929")), band);
            context.DrawLine(new Pen(Brushes.DimGray, 1), band.BottomLeft, band.BottomRight);
        }

        foreach (CueRowViewModel row in viewModel.CueRows)
        {
            double start = dragCueId == row.Id ? previewStart : row.StartMilliseconds;
            double end = dragCueId == row.Id ? previewEnd : row.EndMilliseconds;
            Rect block = GetCueRect(start, end, row.Track, viewModel.MaximumMilliseconds);
            IBrush fill = viewModel.SelectedCueIds.Contains(row.Id) ? Brushes.DeepSkyBlue : Brushes.SlateBlue;
            context.DrawRectangle(fill, new Pen(Brushes.White, 1), block, 3, 3);
        }

        double playheadX = TimeToX(viewModel.PositionMilliseconds, viewModel.MaximumMilliseconds);
        context.DrawLine(new Pen(Brushes.OrangeRed, 2), new Point(playheadX, 0),
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
        CueRowViewModel? hit = viewModel.CueRows.Reverse().FirstOrDefault(row =>
            GetCueRect(row.StartMilliseconds, row.EndMilliseconds, row.Track,
                viewModel.MaximumMilliseconds).Contains(point));
        if (hit is null)
        {
            scrubbing = true;
            viewModel.PositionMilliseconds = XToTime(point.X, viewModel.MaximumMilliseconds);
        }
        else
        {
            viewModel.SelectCue(hit.Id, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            Rect block = GetCueRect(hit.StartMilliseconds, hit.EndMilliseconds, hit.Track,
                viewModel.MaximumMilliseconds);
            dragCueId = hit.Id;
            originalStart = hit.StartMilliseconds;
            originalEnd = hit.EndMilliseconds;
            previewStart = originalStart;
            previewEnd = originalEnd;
            originalTrack = hit.Track;
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
        if (scrubbing)
        {
            viewModel.PositionMilliseconds = XToTime(point.X, viewModel.MaximumMilliseconds);
        }
        else if (dragCueId.HasValue)
        {
            double delta = XToTime(point.X, viewModel.MaximumMilliseconds) -
                XToTime(dragStart.X, viewModel.MaximumMilliseconds);
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
                viewModel.UpdateCueTiming(cueId, previewStart, previewEnd, originalTrack);
            }
        }

        scrubbing = false;
        dragCueId = null;
        dragMode = DragMode.None;
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

        zoom = Math.Clamp(zoom * (e.Delta.Y > 0 ? 1.2 : 1 / 1.2), 1, 16);
        InvalidateVisual();
        e.Handled = true;
    }

    private Rect GetCueRect(double start, double end, int track, double maximum)
        => new(TimeToX(start, maximum), (track * TrackHeight) + 4,
            Math.Max(4, TimeToX(end, maximum) - TimeToX(start, maximum)), TrackHeight - 8);

    private double TimeToX(double milliseconds, double maximum)
        => HeaderWidth + (milliseconds / Math.Max(1, maximum) * (Bounds.Width - HeaderWidth) * zoom);

    private double XToTime(double x, double maximum)
        => Math.Clamp((x - HeaderWidth) / Math.Max(1, (Bounds.Width - HeaderWidth) * zoom) * maximum, 0, maximum);

    private enum DragMode
    {
        None,
        Start,
        End,
        Body,
    }
}
