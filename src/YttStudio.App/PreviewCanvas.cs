using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using System.ComponentModel;
using YttStudio.Core;
using YttStudio.Core.Editing;

namespace YttStudio.App;

/// <summary>합성된 영상 위에 선택과 앵커와 세이프 에어리어와 스냅 오버레이를 그린다.</summary>
public sealed class PreviewCanvas : Control, ICustomHitTest
{
    private const double AnchorHitRadius = 8.0;
    // 편집 가이드용 여백이다. 유튜브에서 측정한 값이 아니다. 판정과 같은 값을 쓴다.
    private const double EditorSafeAreaInsetRatio = 0.05;

    /// <summary>자막 경계가 놓인 원본 픽셀 좌표 공간이다.</summary>
    public static readonly StyledProperty<Rect> SubtitleSpaceProperty =
        AvaloniaProperty.Register<PreviewCanvas, Rect>(nameof(SubtitleSpace),
            PreviewCanvasGeometry.DefaultSubtitleSpace);

    /// <summary>이 거리를 넘게 끌어야 조절점 누름이 크기 조절로 바뀐다.</summary>
    private const double DragThresholdPixels = 4.0;
    private static readonly Pen SelectionPen = new(Brushes.DeepSkyBlue, 2,
        new DashStyle([5, 3], 0));
    private static readonly Pen SafeAreaPen = new(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1,
        new DashStyle([4, 4], 0));
    private static readonly Pen SnapPen = new(Brushes.Magenta, 1.5);
    private static readonly Pen ResizeHandlePen = new(Brushes.White, 1);
    private static readonly IBrush ResizeHandleBrush =
        new SolidColorBrush(Color.FromArgb(230, 20, 120, 220));
    private Point pointerStart;
    private bool draggingCue;
    private bool resizingCue;
    private Rect resizePrimaryBounds;
    private double resizeMultiplier = 1.0;
    private bool pendingHandlePress;
    private Guid pendingHandleCueId;
    private int pendingHandleRow;
    private int pendingHandleColumn;
    private bool selectingRange;
    private Rect selectionRectangle;
    private CanvasMovePreview? movePreview;
    private bool shiftPressed;
    private bool altPressed;
    private INotifyPropertyChanged? observedViewModel;

    public PreviewCanvas()
    {
        Focusable = true;
        DataContextChanged += (_, _) => ObserveViewModel();
    }

    /// <summary>
    /// 렌더러의 자막 공간이다. 기본값은 기존 1280×720 캔버스와 같다. 경계와 앵커는
    /// 이 사각형과 같은 절대 좌표 공간에 있어야 하며, 오프셋도 보존된다.
    /// </summary>
    public Rect SubtitleSpace
    {
        get => GetValue(SubtitleSpaceProperty);
        set => SetValue(SubtitleSpaceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            NotifyPreviewPlayerSize();
            return;
        }

        if (change.Property != SubtitleSpaceProperty)
        {
            return;
        }

        InvalidateVisual();
        NotifyPreviewPlayerSize();
        if (DataContext is MainWindowViewModel viewModel && viewModel.IsInlineEditing)
        {
            viewModel.RefreshInlineEditorLayout(GetContentRect(), GetViewport());
        }
    }

    private void ObserveViewModel()
    {
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        observedViewModel = DataContext as INotifyPropertyChanged;
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        InvalidateVisual();
        NotifyPreviewPlayerSize();
    }

    private void NotifyPreviewPlayerSize()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Rect content = GetContentRect();
        viewModel.UpdatePreviewPlayerSize(content.Width, content.Height);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.CanvasItems))
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // Avalonia hit-tests against drawn geometry, so a control that only
        // strokes outlines receives no pointer events over its empty areas.
        // This transparent fill makes the whole canvas hit-testable, which
        // double-click-to-add and click-to-select both depend on.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Rect content = GetContentRect();
        DrawSafeArea(context, content);
        DrawSelectedCues(context, viewModel, content);
        DrawSnapGuides(context, content);
        DrawSelectionRectangle(context);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Focus();
        Point screen = e.GetPosition(this);
        Rect content = GetContentRect();

        if (TryBeginHandlePress(viewModel, e, screen, content))
        {
            e.Handled = true;
            return;
        }

        if (!content.Contains(screen))
        {
            return;
        }

        Point reference = ToReference(screen, content);

        if (TryBeginCueInteraction(viewModel, e, screen, reference, content))
        {
            e.Handled = true;
            return;
        }

        BeginEmptyCanvasInteraction(viewModel, e, screen, reference, content);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point current = e.GetPosition(this);
        ActivatePendingResize(current);

        if (resizingCue && DataContext is MainWindowViewModel resizeViewModel)
        {
            UpdateResizePreview(resizeViewModel, e, current);
            return;
        }

        if (draggingCue && DataContext is MainWindowViewModel viewModel)
        {
            UpdateMovePreview(viewModel, e, current);
            return;
        }

        if (selectingRange)
        {
            selectionRectangle = Normalize(pointerStart, current);
            InvalidateVisual();
        }
    }
    private static void DrawSafeArea(DrawingContext context, Rect content)
        => context.DrawRectangle(null, SafeAreaPen, new Rect(
            content.X + (content.Width * EditorSafeAreaInsetRatio),
            content.Y + (content.Height * EditorSafeAreaInsetRatio),
            content.Width * (1 - (EditorSafeAreaInsetRatio * 2)),
            content.Height * (1 - (EditorSafeAreaInsetRatio * 2))));

    private void DrawSelectedCues(
        DrawingContext context,
        MainWindowViewModel viewModel,
        Rect content)
    {
        foreach (CanvasCueItem item in viewModel.CanvasItems.Where(item => item.Selected))
        {
            Rect bounds = ToScreen(item.Bounds, content);
            if (movePreview is not null)
            {
                Vector delta = ToScreenDelta(movePreview.DeltaX, movePreview.DeltaY, content);
                bounds = bounds.Translate(delta);
            }

            context.DrawRectangle(null, SelectionPen, bounds);
            DrawHandles(context, bounds, item.AnchorKind);
        }
    }

    private void DrawSnapGuides(DrawingContext context, Rect content)
    {
        if (movePreview is null)
        {
            return;
        }

        foreach (SnapGuide guide in movePreview.Guides)
        {
            if (guide.Vertical)
            {
                double x = PreviewCanvasGeometry.ToScreenCoordinate(
                    guide.Position, true, content, SubtitleSpace);
                context.DrawLine(SnapPen, new Point(x, content.Top), new Point(x, content.Bottom));
            }
            else
            {
                double y = PreviewCanvasGeometry.ToScreenCoordinate(
                    guide.Position, false, content, SubtitleSpace);
                context.DrawLine(SnapPen, new Point(content.Left, y), new Point(content.Right, y));
            }
        }
    }

    private void DrawSelectionRectangle(DrawingContext context)
    {
        if (selectingRange)
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(35, 0, 180, 255)), SelectionPen,
                selectionRectangle);
        }
    }

    private bool TryBeginHandlePress(
        MainWindowViewModel viewModel,
        PointerPressedEventArgs e,
        Point screen,
        Rect content)
    {
        if (!TryHitHandle(viewModel, screen, content,
                out Guid handleCueId, out int row, out int column))
        {
            return false;
        }

        if (PreviewResizeGeometry.IsResizeHandle(row, column))
        {
            pendingHandlePress = true;
            pendingHandleCueId = handleCueId;
            pendingHandleRow = row;
            pendingHandleColumn = column;
            resizePrimaryBounds = ToScreen(
                viewModel.CanvasItems.First(item => item.Id == handleCueId).Bounds, content);
            resizeMultiplier = 1.0;
            pointerStart = screen;
            e.Pointer.Capture(this);
        }
        else
        {
            viewModel.ChangeAnchor(handleCueId, ToAnchor(row, column));
        }

        return true;
    }

    private bool TryBeginCueInteraction(
        MainWindowViewModel viewModel,
        PointerPressedEventArgs e,
        Point screen,
        Point reference,
        Rect content)
    {
        CanvasCueItem? hit = viewModel.CanvasItems.Reverse()
            .FirstOrDefault(item => Contains(item.Bounds, reference));
        if (hit is null)
        {
            return false;
        }

        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (!hit.Selected || control)
        {
            viewModel.SelectCue(hit.Id, control);
        }

        if (e.ClickCount == 2)
        {
            viewModel.BeginInlineEdit(hit.Id, hit.Bounds, content, GetViewport());
            return true;
        }

        draggingCue = true;
        pointerStart = screen;
        e.Pointer.Capture(this);
        return true;
    }

    private void BeginEmptyCanvasInteraction(
        MainWindowViewModel viewModel,
        PointerPressedEventArgs e,
        Point screen,
        Point reference,
        Rect content)
    {
        if (e.ClickCount == 2)
        {
            Guid? addedCueId = viewModel.AddCueAtCanvasPoint(reference.X, reference.Y);
            if (addedCueId is Guid id)
            {
                BeginInlineEditForAddedCue(viewModel, id, screen, content);
            }

            return;
        }

        selectingRange = true;
        pointerStart = screen;
        selectionRectangle = new Rect(screen, screen);
        e.Pointer.Capture(this);
    }

    private void BeginInlineEditForAddedCue(
        MainWindowViewModel viewModel,
        Guid cueId,
        Point screen,
        Rect content)
    {
        if (viewModel.CanvasItems.FirstOrDefault(item => item.Id == cueId) is CanvasCueItem added)
        {
            viewModel.BeginInlineEdit(cueId, added.Bounds, content, GetViewport());
            return;
        }

        Rect inlineBounds = ClampInlinePlacement(new Rect(screen.X, screen.Y, 180,
            InlineEditorPlacement.DefaultHeight));
        viewModel.BeginInlineEdit(cueId, inlineBounds.Left, inlineBounds.Top,
            inlineBounds.Width);
    }

    private void ActivatePendingResize(Point current)
    {
        if (!pendingHandlePress || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Vector moved = current - pointerStart;
        if (Math.Sqrt((moved.X * moved.X) + (moved.Y * moved.Y)) <= DragThresholdPixels)
        {
            return;
        }

        pendingHandlePress = false;
        if (viewModel.BeginCanvasResize(pendingHandleCueId, pendingHandleRow, pendingHandleColumn))
        {
            resizingCue = true;
        }
    }

    private void UpdateResizePreview(
        MainWindowViewModel viewModel,
        PointerEventArgs e,
        Point current)
    {
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        resizeMultiplier = PreviewResizeGeometry.ComputeMultiplier(
            resizePrimaryBounds, pendingHandleRow, pendingHandleColumn,
            current - pointerStart, shift);
        viewModel.PreviewCanvasResize(resizeMultiplier);
        InvalidateVisual();
    }

    private void UpdateMovePreview(
        MainWindowViewModel viewModel,
        PointerEventArgs e,
        Point current)
    {
        Rect content = GetContentRect();
        Vector delta = current - pointerStart;
        Rect subtitleSpace = PreviewCanvasGeometry.NormalizeSubtitleSpace(SubtitleSpace);
        double deltaX = delta.X / content.Width * subtitleSpace.Width;
        double deltaY = delta.Y / content.Height * subtitleSpace.Height;
        shiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        altPressed = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        ConstrainMoveToAxis(ref deltaX, ref deltaY);

        movePreview = viewModel.PreviewCanvasMove(deltaX, deltaY, altPressed);
        CanvasCueItem? coordinateCue = viewModel.CanvasItems.LastOrDefault(item => item.Selected);
        if (coordinateCue is not null)
        {
            ToolTip.SetTip(this,
                $"ah {coordinateCue.Anchor.X + movePreview.DeltaX:F1} \\u00B7 av {coordinateCue.Anchor.Y + movePreview.DeltaY:F1}");
            ToolTip.SetIsOpen(this, true);
        }

        InvalidateVisual();
    }

    private void ConstrainMoveToAxis(ref double deltaX, ref double deltaY)
    {
        if (!shiftPressed)
        {
            return;
        }

        if (Math.Abs(deltaX) >= Math.Abs(deltaY))
        {
            deltaY = 0;
        }
        else
        {
            deltaX = 0;
        }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            CommitPointerRelease(viewModel);
        }

        pendingHandlePress = false;
        resizingCue = false;
        resizePrimaryBounds = default;
        resizeMultiplier = 1.0;
        draggingCue = false;
        selectingRange = false;
        movePreview = null;
        ToolTip.SetIsOpen(this, false);
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    private void CommitPointerRelease(MainWindowViewModel viewModel)
    {
        if (pendingHandlePress)
        {
            // Never crossed the drag threshold, so treat it as an anchor pick.
            viewModel.ChangeAnchor(pendingHandleCueId,
                ToAnchor(pendingHandleRow, pendingHandleColumn));
        }
        else if (resizingCue)
        {
            viewModel.EndCanvasResize(resizeMultiplier);
        }
        else if (draggingCue && movePreview is not null)
        {
            viewModel.CommitCanvasMove(movePreview.DeltaX, movePreview.DeltaY, altPressed);
        }
        else if (selectingRange)
        {
            CommitSelection(viewModel);
        }
    }

    private void CommitSelection(MainWindowViewModel viewModel)
    {
        Rect content = GetContentRect();
        Point first = ToReference(selectionRectangle.TopLeft, content);
        Point second = ToReference(selectionRectangle.BottomRight, content);
        viewModel.SelectInRectangle(new CanvasRect(first.X, first.Y,
            Math.Max(0, second.X - first.X), Math.Max(0, second.Y - first.Y)));
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (resizingCue && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CancelCanvasResize();
        }

        pendingHandlePress = false;
        resizingCue = false;
        resizePrimaryBounds = default;
        resizeMultiplier = 1.0;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (TryHandleDelete(viewModel, e) ||
            TryHandleInlineEdit(viewModel, e) ||
            TryHandleAlignment(viewModel, e))
        {
            return;
        }

        if (TryNudge(viewModel, e))
        {
            e.Handled = true;
        }
    }
    private static bool TryHandleDelete(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (!ShouldDeleteSelectedCues(e.Key, e.KeyModifiers, viewModel.IsInlineEditing,
                viewModel.SelectedCueIds.Count) ||
            !viewModel.DeleteCueCommand.CanExecute(null))
        {
            return false;
        }

        viewModel.DeleteCueCommand.Execute(null);
        e.Handled = true;
        return true;
    }

    private bool TryHandleInlineEdit(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if ((e.Key is not (Key.F2 or Key.Enter)) || e.KeyModifiers != KeyModifiers.None ||
            viewModel.SelectedCueIds.Count != 1)
        {
            return false;
        }

        if (viewModel.CanvasItems.FirstOrDefault(item => item.Selected) is not CanvasCueItem selected)
        {
            return false;
        }

        viewModel.BeginInlineEdit(selected.Id, selected.Bounds, GetContentRect(), GetViewport());
        e.Handled = true;
        return true;
    }

    private static bool TryHandleAlignment(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        const KeyModifiers alignmentModifiers = KeyModifiers.Control | KeyModifiers.Shift;
        if ((e.KeyModifiers & alignmentModifiers) != alignmentModifiers)
        {
            return false;
        }

        char? alignment = e.Key switch
        {
            Key.H => 'H',
            Key.V => 'V',
            Key.C => 'C',
            Key.B => 'B',
            _ => null,
        };
        if (!alignment.HasValue)
        {
            return false;
        }

        viewModel.AlignSelected(alignment.Value);
        e.Handled = true;
        return true;
    }

    private static bool TryNudge(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        double amount = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.1 : 1.0;
        switch (e.Key)
        {
            case Key.Left:
                viewModel.NudgeSelected(-amount, 0);
                break;
            case Key.Right:
                viewModel.NudgeSelected(amount, 0);
                break;
            case Key.Up:
                viewModel.NudgeSelected(0, -amount);
                break;
            case Key.Down:
                viewModel.NudgeSelected(0, amount);
                break;
            default:
                return false;
        }

        return true;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arranged = base.ArrangeOverride(finalSize);
        if (DataContext is MainWindowViewModel viewModel && viewModel.IsInlineEditing)
        {
            viewModel.RefreshInlineEditorLayout(
                GetContentRect(arranged),
                new Rect(0, 0, Math.Max(0, arranged.Width), Math.Max(0, arranged.Height)));
        }

        return arranged;
    }

    private Rect GetContentRect()
        => GetContentRect(Bounds.Size);

    private Rect GetContentRect(Size size)
        => PreviewCanvasGeometry.GetContentRect(size, SubtitleSpace);

    private Rect GetViewport()
        => GetContentRect();

    private Point ToReference(Point point, Rect content)
        => PreviewCanvasGeometry.ToSubtitle(point, content, SubtitleSpace);

    private Rect ToScreen(CanvasRect rectangle, Rect content)
        => PreviewCanvasGeometry.ToScreen(rectangle, content, SubtitleSpace);

    private Vector ToScreenDelta(double x, double y, Rect content)
        => PreviewCanvasGeometry.ToScreenDelta(x, y, content, SubtitleSpace);

    private Rect ClampInlinePlacement(Rect requested)
        => InlineEditorPlacement.Clamp(
            new Rect(requested.X, requested.Y, Math.Max(140, requested.Width),
                Math.Max(InlineEditorPlacement.DefaultHeight, requested.Height)),
            new Rect(0, 0, Math.Max(0, Bounds.Width), Math.Max(0, Bounds.Height)));

    private static bool Contains(CanvasRect rectangle, Point point)
        => point.X >= rectangle.Left && point.X <= rectangle.Right &&
            point.Y >= rectangle.Top && point.Y <= rectangle.Bottom;

    private static Rect Normalize(Point first, Point second)
        => new(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
            Math.Abs(second.X - first.X), Math.Abs(second.Y - first.Y));

    /// <summary>프리뷰에 포커스가 있을 때 Delete가 선택 큐 삭제여야 하는지 판정한다.</summary>
    internal static bool ShouldDeleteSelectedCues(
        Key key,
        KeyModifiers modifiers,
        bool inlineEditing,
        int selectedCueCount)
        => key == Key.Delete && modifiers == KeyModifiers.None && !inlineEditing &&
            selectedCueCount > 0;

    /// <summary>3x3 격자 칸을 앵커 값으로 바꾼다.</summary>
    private static AnchorPoint ToAnchor(int row, int column)
        => (AnchorPoint)((row * 3) + column);

    /// <summary>
    /// 바깥 여덟 칸은 크기 조절점이라 사각형으로, 가운데 앵커 전용 칸은 원으로 그려
    /// 잡았을 때 무슨 일이 일어나는지 구분되게 한다.
    /// </summary>
    private static void DrawHandles(DrawingContext context, Rect bounds, AnchorPoint selected)
    {
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                Point point = PreviewResizeGeometry.GetHandleCenter(bounds, row, column);
                bool isAnchor = ToAnchor(row, column) == selected;
                IBrush fill = isAnchor ? Brushes.Magenta : ResizeHandleBrush;
                if (PreviewResizeGeometry.IsResizeHandle(row, column))
                {
                    context.DrawRectangle(fill, ResizeHandlePen, new Rect(
                        point.X - PreviewResizeGeometry.HandleRadius,
                        point.Y - PreviewResizeGeometry.HandleRadius,
                        PreviewResizeGeometry.HandleRadius * 2,
                        PreviewResizeGeometry.HandleRadius * 2));
                }
                else
                {
                    context.DrawEllipse(isAnchor ? Brushes.Magenta : Brushes.White,
                        new Pen(Brushes.Magenta, 1), point,
                        PreviewResizeGeometry.HandleRadius, PreviewResizeGeometry.HandleRadius);
                }
            }
        }
    }

    private bool TryHitHandle(
        MainWindowViewModel viewModel,
        Point screen,
        Rect content,
        out Guid cueId,
        out int row,
        out int column)
    {
        foreach (CanvasCueItem item in viewModel.CanvasItems.Where(item => item.Selected).Reverse())
        {
            Rect bounds = ToScreen(item.Bounds, content);
            if (PreviewResizeGeometry.TryHitHandle(bounds, screen, AnchorHitRadius, out row, out column))
            {
                cueId = item.Id;
                return true;
            }
        }

        cueId = default;
        row = -1;
        column = -1;
        return false;
    }

    bool ICustomHitTest.HitTest(Point point)
        => new Rect(Bounds.Size).Contains(point);
}
