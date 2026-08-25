using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using YttStudio.Core;

namespace YttStudio.App;

/// <summary>Draws karaoke chips and the cue-relative timing bar for the selected cue.</summary>
public sealed class KaraokeTimelineControl : Control
{
    private const double ChipRowTop = 8;
    private const double ChipRowHeight = 40;
    private const double TimelineTop = 62;
    private const double TimelineHeight = 28;
    private const double HorizontalPadding = 8;

    private MainWindowViewModel? observedViewModel;
    private int? dragSectionIndex;
    private int? mergeTargetIndex;
    private Point? chipPressPoint;
    private bool chipDragged;
    private int? timelineSectionIndex;
    private double? pendingTimelineOffset;

    public KaraokeTimelineControl()
    {
        Focusable = true;
        IsTabStop = true;
        MinHeight = 116;
        ClipToBounds = true;
        DataContextChanged += (_, _) => ObserveViewModel();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#181818")), Bounds);

        if (DataContext is not MainWindowViewModel viewModel || !viewModel.HasKaraokeCue ||
            viewModel.KaraokeSections.Count == 0)
        {
            context.DrawText(CreateLabel("단일 큐를 선택하면 가라오케 칩이 표시됩니다.", 11, Brushes.Gray),
                new Point(HorizontalPadding, ChipRowTop + 8));
            return;
        }

        IReadOnlyList<KaraokeSectionViewModel> sections = viewModel.KaraokeSections;
        Rect[] chips = GetChipRects(sections);
        for (int index = 0; index < chips.Length; index++)
        {
            Rect chip = chips[index];
            bool isMergeTarget = mergeTargetIndex == index;
            IBrush fill = isMergeTarget ? Brushes.DarkOrange :
                index % 2 == 0 ? Brushes.SlateBlue : Brushes.DarkSlateBlue;
            context.DrawRectangle(fill, new Pen(Brushes.LightGray, 1), chip, 4, 4);
            context.DrawText(CreateLabel(sections[index].Text, 13, Brushes.White),
                new Point(chip.Left + 7, chip.Top + 11));
        }

        double duration = GetCueDuration(viewModel);
        Rect timeline = new(
            HorizontalPadding,
            TimelineTop,
            Math.Max(1, Bounds.Width - (HorizontalPadding * 2)),
            TimelineHeight);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#292929")),
            new Pen(Brushes.DimGray, 1), timeline, 3, 3);

        for (int index = 0; index < sections.Count; index++)
        {
            double offset = pendingTimelineOffset.HasValue && timelineSectionIndex == index
                ? pendingTimelineOffset.Value
                : GetSectionOffsetMilliseconds(sections, index, duration);
            double x = OffsetToX(offset, duration, timeline);
            context.DrawLine(new Pen(index == timelineSectionIndex
                    ? Brushes.Orange
                    : Brushes.LightSkyBlue, index == timelineSectionIndex ? 3 : 2),
                new Point(x, timeline.Top), new Point(x, timeline.Bottom));
            context.DrawText(CreateLabel($"{offset:0} ms", 9, Brushes.LightGray),
                new Point(Math.Min(x + 3, Math.Max(timeline.Left, timeline.Right - 48)), timeline.Bottom + 2));
        }

        context.DrawText(CreateLabel(
                "칩을 이웃 칩 위로 드래그하면 병합 · 아래 경계를 드래그하면 ms 미세 조정",
                10,
                Brushes.Gray),
            new Point(HorizontalPadding, TimelineTop + TimelineHeight + 18));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.HasKaraokeCue ||
            viewModel.KaraokeSections.Count == 0)
        {
            return;
        }

        Focus();
        Point point = e.GetPosition(this);
        if (point.Y >= ChipRowTop && point.Y <= ChipRowTop + ChipRowHeight)
        {
            int? boundary = HitChipBoundary(viewModel.KaraokeSections, point);
            if (boundary is int leftIndex)
            {
                viewModel.MergeKaraokeSections(viewModel.SelectedKaraokeCueId!.Value, leftIndex);
                e.Handled = true;
                return;
            }

            int? sectionIndex = HitChip(viewModel.KaraokeSections, point);
            if (sectionIndex is int index)
            {
                dragSectionIndex = index;
                mergeTargetIndex = index;
                chipPressPoint = point;
                chipDragged = false;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        Rect timeline = GetTimelineRect();
        if (timeline.Contains(point))
        {
            viewModel.BeginKaraokeTimelineAdjustment();
            double duration = GetCueDuration(viewModel);
            timelineSectionIndex = FindNearestSectionBoundary(viewModel.KaraokeSections,
                XToOffset(point.X, duration, timeline), duration);
            pendingTimelineOffset = timelineSectionIndex is int index
                ? XToOffset(point.X, duration, timeline)
                : null;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.HasKaraokeCue)
        {
            return;
        }

        Point point = e.GetPosition(this);
        if (dragSectionIndex is int)
        {
            if (chipPressPoint is Point pressed &&
                Math.Abs(point.X - pressed.X) + Math.Abs(point.Y - pressed.Y) > 4)
            {
                chipDragged = true;
            }
            mergeTargetIndex = HitChip(viewModel.KaraokeSections, point);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (timelineSectionIndex is int)
        {
            Rect timeline = GetTimelineRect();
            pendingTimelineOffset = XToOffset(point.X, GetCueDuration(viewModel), timeline);
            viewModel.PreviewKaraokeTimelineOffset(
                viewModel.SelectedKaraokeCueId!.Value,
                timelineSectionIndex.Value,
                pendingTimelineOffset.Value);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (DataContext is MainWindowViewModel viewModel && viewModel.HasKaraokeCue)
        {
            if (dragSectionIndex is int source && mergeTargetIndex is int target &&
                source != target && Math.Abs(source - target) == 1)
            {
                viewModel.MergeKaraokeSections(viewModel.SelectedKaraokeCueId!.Value,
                    Math.Min(source, target));
            }
            else if (dragSectionIndex is int clicked && !chipDragged && chipPressPoint is Point pressed &&
                TryGetSplitOffset(viewModel.KaraokeSections, clicked, pressed.X, out int textOffset))
            {
                viewModel.SplitKaraokeSection(
                    viewModel.SelectedKaraokeCueId!.Value, clicked, textOffset);
            }
            else if (timelineSectionIndex is int sectionIndex && pendingTimelineOffset is double offset)
            {
                viewModel.PreviewKaraokeTimelineOffset(
                    viewModel.SelectedKaraokeCueId!.Value, sectionIndex, offset);
                viewModel.EndKaraokeTimelineAdjustment();
            }
        }

        dragSectionIndex = null;
        mergeTargetIndex = null;
        chipPressPoint = null;
        chipDragged = false;
        timelineSectionIndex = null;
        pendingTimelineOffset = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.HasKaraokeCue ||
            e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            viewModel.RecordKaraokeTabForSelectedCue();
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            viewModel.CancelLastKaraokeTabForSelectedCue();
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (timelineSectionIndex is not null && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.EndKaraokeTimelineAdjustment();
        }
        dragSectionIndex = null;
        mergeTargetIndex = null;
        chipPressPoint = null;
        chipDragged = false;
        timelineSectionIndex = null;
        pendingTimelineOffset = null;
        InvalidateVisual();
    }

    private void ObserveViewModel()
    {
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            observedViewModel.KaraokeSections.CollectionChanged -= OnKaraokeSectionsChanged;
        }

        observedViewModel = DataContext as MainWindowViewModel;
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            observedViewModel.KaraokeSections.CollectionChanged += OnKaraokeSectionsChanged;
        }

        InvalidateVisual();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.KaraokeSections) or
            nameof(MainWindowViewModel.HasKaraokeCue) or
            nameof(MainWindowViewModel.SelectedKaraokeCueId))
        {
            InvalidateVisual();
        }
    }

    private void OnKaraokeSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => InvalidateVisual();

    private Rect[] GetChipRects(IReadOnlyList<KaraokeSectionViewModel> sections)
    {
        double availableWidth = Math.Max(1, Bounds.Width - (HorizontalPadding * 2));
        double totalWeight = sections.Sum(section => Math.Max(1, section.Text.Length));
        double x = HorizontalPadding;
        Rect[] result = new Rect[sections.Count];
        for (int index = 0; index < sections.Count; index++)
        {
            double width = availableWidth * Math.Max(1, sections[index].Text.Length) / totalWeight;
            result[index] = new Rect(x, ChipRowTop, Math.Max(22, width - 3), ChipRowHeight);
            x += width;
        }

        return result;
    }

    private int? HitChip(IReadOnlyList<KaraokeSectionViewModel> sections, Point point)
    {
        Rect[] chips = GetChipRects(sections);
        for (int index = 0; index < chips.Length; index++)
        {
            if (chips[index].Contains(point))
            {
                return index;
            }
        }

        return null;
    }

    private int? HitChipBoundary(IReadOnlyList<KaraokeSectionViewModel> sections, Point point)
    {
        Rect[] chips = GetChipRects(sections);
        for (int index = 0; index < chips.Length - 1; index++)
        {
            if (Math.Abs(point.X - chips[index].Right) <= 5)
            {
                return index;
            }
        }

        return null;
    }

    private bool TryGetSplitOffset(
        IReadOnlyList<KaraokeSectionViewModel> sections,
        int index,
        double x,
        out int textOffset)
    {
        string text = sections[index].Text;
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        if (starts.Length < 2)
        {
            textOffset = 0;
            return false;
        }

        Rect chip = GetChipRects(sections)[index];
        double ratio = Math.Clamp((x - chip.Left) / Math.Max(1, chip.Width), 0, 1);
        int boundary = Math.Clamp((int)Math.Round(ratio * starts.Length), 1, starts.Length - 1);
        textOffset = starts[boundary];
        return true;
    }

    private Rect GetTimelineRect()
        => new(HorizontalPadding, TimelineTop,
            Math.Max(1, Bounds.Width - (HorizontalPadding * 2)), TimelineHeight);

    private static double GetCueDuration(MainWindowViewModel viewModel)
        => Math.Max(1, viewModel.SelectedKaraokeCueDurationMilliseconds);

    private static double GetSectionOffsetMilliseconds(
        IReadOnlyList<KaraokeSectionViewModel> sections,
        int index,
        double duration)
    {
        if (double.TryParse(sections[index].OffsetMillisecondsText, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double parsed))
        {
            return Math.Clamp(parsed, 0, duration);
        }

        return duration * index / Math.Max(1, sections.Count);
    }

    private static double OffsetToX(double offset, double duration, Rect timeline)
        => timeline.Left + Math.Clamp(offset / Math.Max(1, duration), 0, 1) * timeline.Width;

    private static double XToOffset(double x, double duration, Rect timeline)
        => Math.Clamp((x - timeline.Left) / Math.Max(1, timeline.Width), 0, 1) * duration;

    private static int? FindNearestSectionBoundary(
        IReadOnlyList<KaraokeSectionViewModel> sections,
        double offset,
        double duration)
    {
        if (sections.Count == 0)
        {
            return null;
        }

        int nearest = 0;
        double distance = double.MaxValue;
        for (int index = 0; index < sections.Count; index++)
        {
            double candidate = GetSectionOffsetMilliseconds(sections, index, duration);
            double candidateDistance = Math.Abs(candidate - offset);
            if (candidateDistance < distance)
            {
                nearest = index;
                distance = candidateDistance;
            }
        }

        return nearest;
    }

    private static FormattedText CreateLabel(string text, double fontSize, IBrush brush)
        => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), fontSize, brush);
}
