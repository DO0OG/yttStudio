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
        context.DrawRectangle(null, SafeAreaPen, new Rect(
            content.X + (content.Width * 0.05),
            content.Y + (content.Height * 0.05),
            content.Width * 0.9,
            content.Height * 0.9));

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

        if (movePreview is not null)
        {
            foreach (SnapGuide guide in movePreview.Guides)
            {
                if (guide.Vertical)
                {
                    double x = content.X + (guide.Position / YttConstants.ReferenceWidth * content.Width);
                    context.DrawLine(SnapPen, new Point(x, content.Top), new Point(x, content.Bottom));
                }
                else
                {
                    double y = content.Y + (guide.Position / YttConstants.ReferenceHeight * content.Height);
                    context.DrawLine(SnapPen, new Point(content.Left, y), new Point(content.Right, y));
                }
            }
        }

        if (selectingRange)
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(35, 0, 180, 255)), SelectionPen,
                selectionRectangle);
        }
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

        if (TryHitHandle(viewModel, screen, content, out Guid handleCueId, out int row, out int column))
        {
            if (PreviewResizeGeometry.IsResizeHandle(row, column))
            {
                // A press on an outer handle is still ambiguous: a drag resizes
                // the cue, while a click without movement picks that anchor.
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

            e.Handled = true;
            return;
        }

        if (!content.Contains(screen))
        {
            return;
        }

        Point reference = ToReference(screen, content);

        CanvasCueItem? hit = viewModel.CanvasItems.Reverse()
            .FirstOrDefault(item => Contains(item.Bounds, reference));
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (hit is not null)
        {
            if (!hit.Selected || control)
            {
                viewModel.SelectCue(hit.Id, control);
            }

            if (e.ClickCount == 2)
            {
                viewModel.BeginInlineEdit(
                    hit.Id,
                    hit.Bounds,
                    content,
                    GetViewport());
                e.Handled = true;
                return;
            }

            draggingCue = true;
            pointerStart = screen;
            e.Pointer.Capture(this);
        }
        else
        {
            if (e.ClickCount == 2)
            {
                Guid? addedCueId = viewModel.AddCueAtCanvasPoint(reference.X, reference.Y);
                if (addedCueId is Guid id)
                {
                    if (viewModel.CanvasItems.FirstOrDefault(item => item.Id == id) is CanvasCueItem added)
                    {
                        viewModel.BeginInlineEdit(id, added.Bounds, content, GetViewport());
                    }
                    else
                    {
                        Rect inlineBounds = ClampInlinePlacement(new Rect(screen.X, screen.Y, 180,
                            InlineEditorPlacement.DefaultHeight));
                        viewModel.BeginInlineEdit(id, inlineBounds.Left, inlineBounds.Top,
                            inlineBounds.Width);
                    }
                }

                e.Handled = true;
                return;
            }

            selectingRange = true;
            pointerStart = screen;
            selectionRectangle = new Rect(screen, screen);
            e.Pointer.Capture(this);
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point current = e.GetPosition(this);
        if (pendingHandlePress && DataContext is MainWindowViewModel pendingViewModel)
        {
            Vector moved = current - pointerStart;
            if (Math.Sqrt((moved.X * moved.X) + (moved.Y * moved.Y)) > DragThresholdPixels)
            {
                pendingHandlePress = false;
                if (pendingViewModel.BeginCanvasResize(
                        pendingHandleCueId, pendingHandleRow, pendingHandleColumn))
                {
                    resizingCue = true;
                }
            }
        }

        if (resizingCue && DataContext is MainWindowViewModel resizeViewModel)
        {
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            resizeMultiplier = PreviewResizeGeometry.ComputeMultiplier(
                resizePrimaryBounds, pendingHandleRow, pendingHandleColumn,
                current - pointerStart, shift);
            // Alt is intentionally ignored for size changes; it belongs to
            // the move-snap gesture and must not affect this path.
            resizeViewModel.PreviewCanvasResize(resizeMultiplier);
            InvalidateVisual();
        }
        else if (draggingCue && DataContext is MainWindowViewModel viewModel)
        {
            Rect content = GetContentRect();
            Vector delta = current - pointerStart;
            double deltaX = delta.X / content.Width * YttConstants.ReferenceWidth;
            double deltaY = delta.Y / content.Height * YttConstants.ReferenceHeight;
            shiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            altPressed = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            if (shiftPressed)
            {
                if (Math.Abs(deltaX) >= Math.Abs(deltaY))
                {
                    deltaY = 0;
                }
                else
                {
                    deltaX = 0;
                }
            }

            movePreview = viewModel.PreviewCanvasMove(deltaX, deltaY, altPressed);
            CanvasCueItem? coordinateCue = viewModel.CanvasItems.LastOrDefault(item => item.Selected);
            if (coordinateCue is not null)
            {
                ToolTip.SetTip(this,
                    $"ah {coordinateCue.Anchor.X + movePreview.DeltaX:F1} · av {coordinateCue.Anchor.Y + movePreview.DeltaY:F1}");
                ToolTip.SetIsOpen(this, true);
            }

            InvalidateVisual();
        }
        else if (selectingRange)
        {
            selectionRectangle = Normalize(pointerStart, current);
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (DataContext is MainWindowViewModel viewModel)
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
                Rect content = GetContentRect();
                Point first = ToReference(selectionRectangle.TopLeft, content);
                Point second = ToReference(selectionRectangle.BottomRight, content);
                viewModel.SelectInRectangle(new CanvasRect(first.X, first.Y,
                    Math.Max(0, second.X - first.X), Math.Max(0, second.Y - first.Y)));
            }
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

        if (ShouldDeleteSelectedCues(e.Key, e.KeyModifiers, viewModel.IsInlineEditing,
                viewModel.SelectedCueIds.Count) &&
            viewModel.DeleteCueCommand.CanExecute(null))
        {
            viewModel.DeleteCueCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if ((e.Key is Key.F2 or Key.Enter) && e.KeyModifiers == KeyModifiers.None &&
            viewModel.SelectedCueIds.Count == 1)
        {
            CanvasCueItem? selected = viewModel.CanvasItems.FirstOrDefault(item => item.Selected);
            if (selected is not null)
            {
                viewModel.BeginInlineEdit(
                    selected.Id,
                    selected.Bounds,
                    GetContentRect(),
                    GetViewport());
                e.Handled = true;
                return;
            }
        }

        double amount = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.1 : 1.0;
        const KeyModifiers alignmentModifiers = KeyModifiers.Control | KeyModifiers.Shift;
        if ((e.KeyModifiers & alignmentModifiers) == alignmentModifiers)
        {
            char? alignment = e.Key switch
            {
                Key.H => 'H',
                Key.V => 'V',
                Key.C => 'C',
                Key.B => 'B',
                _ => null,
            };
            if (alignment.HasValue)
            {
                viewModel.AlignSelected(alignment.Value);
                e.Handled = true;
                return;
            }
        }

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
                return;
        }

        e.Handled = true;
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

    private static Rect GetContentRect(Size size)
    {
        double scale = Math.Min(size.Width / YttConstants.ReferenceWidth,
            size.Height / YttConstants.ReferenceHeight);
        double width = YttConstants.ReferenceWidth * scale;
        double height = YttConstants.ReferenceHeight * scale;
        return new Rect((size.Width - width) / 2, (size.Height - height) / 2, width, height);
    }

    private Rect GetViewport()
        => new(0, 0, Math.Max(0, Bounds.Width), Math.Max(0, Bounds.Height));

    private static Point ToReference(Point point, Rect content)
        => new(
            (point.X - content.X) / content.Width * YttConstants.ReferenceWidth,
            (point.Y - content.Y) / content.Height * YttConstants.ReferenceHeight);

    private static Rect ToScreen(CanvasRect rectangle, Rect content)
        => new(
            content.X + (rectangle.X / YttConstants.ReferenceWidth * content.Width),
            content.Y + (rectangle.Y / YttConstants.ReferenceHeight * content.Height),
            rectangle.Width / YttConstants.ReferenceWidth * content.Width,
            rectangle.Height / YttConstants.ReferenceHeight * content.Height);

    private static Vector ToScreenDelta(double x, double y, Rect content)
        => new(x / YttConstants.ReferenceWidth * content.Width,
            y / YttConstants.ReferenceHeight * content.Height);

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

    private static bool TryHitHandle(
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
