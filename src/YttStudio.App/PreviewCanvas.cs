using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.ComponentModel;
using YttStudio.Core;
using YttStudio.Core.Editing;

namespace YttStudio.App;

/// <summary>합성된 영상 위에 선택과 앵커와 세이프 에어리어와 스냅 오버레이를 그린다.</summary>
public sealed class PreviewCanvas : Control
{
    private static readonly Pen SelectionPen = new(Brushes.DeepSkyBlue, 2,
        new DashStyle([5, 3], 0));
    private static readonly Pen SafeAreaPen = new(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1,
        new DashStyle([4, 4], 0));
    private static readonly Pen SnapPen = new(Brushes.Magenta, 1.5);
    private Point pointerStart;
    private bool draggingCue;
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
            DrawAnchorMarkers(context, bounds, item.AnchorKind);
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
        if (!content.Contains(screen))
        {
            return;
        }

        Point reference = ToReference(screen, content);
        if (TryHitAnchor(viewModel, reference, content, out Guid cueId, out AnchorPoint anchor))
        {
            viewModel.ChangeAnchor(cueId, anchor);
            e.Handled = true;
            return;
        }

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
                Rect inlineBounds = ToScreen(hit.Bounds, content);
                viewModel.BeginInlineEdit(hit.Id, inlineBounds.Left, inlineBounds.Top,
                    inlineBounds.Width);
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
                    viewModel.BeginInlineEdit(id, screen.X, screen.Y, 180);
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
        if (draggingCue && DataContext is MainWindowViewModel viewModel)
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
            if (draggingCue && movePreview is not null)
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

        draggingCue = false;
        selectingRange = false;
        movePreview = null;
        ToolTip.SetIsOpen(this, false);
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
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

    private Rect GetContentRect()
    {
        double scale = Math.Min(Bounds.Width / YttConstants.ReferenceWidth,
            Bounds.Height / YttConstants.ReferenceHeight);
        double width = YttConstants.ReferenceWidth * scale;
        double height = YttConstants.ReferenceHeight * scale;
        return new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
    }

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

    private static bool Contains(CanvasRect rectangle, Point point)
        => point.X >= rectangle.Left && point.X <= rectangle.Right &&
            point.Y >= rectangle.Top && point.Y <= rectangle.Bottom;

    private static Rect Normalize(Point first, Point second)
        => new(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
            Math.Abs(second.X - first.X), Math.Abs(second.Y - first.Y));

    private static void DrawAnchorMarkers(DrawingContext context, Rect bounds, AnchorPoint selected)
    {
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                AnchorPoint anchor = (AnchorPoint)((row * 3) + column);
                Point point = new(bounds.Left + (bounds.Width * column / 2),
                    bounds.Top + (bounds.Height * row / 2));
                IBrush fill = anchor == selected ? Brushes.Magenta : Brushes.White;
                context.DrawEllipse(fill, new Pen(Brushes.Magenta, 1), point, 4, 4);
            }
        }
    }

    private static bool TryHitAnchor(
        MainWindowViewModel viewModel,
        Point reference,
        Rect content,
        out Guid cueId,
        out AnchorPoint anchor)
    {
        double radius = 8 / Math.Max(0.001, content.Width / YttConstants.ReferenceWidth);
        foreach (CanvasCueItem item in viewModel.CanvasItems.Where(item => item.Selected).Reverse())
        {
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    double x = item.Bounds.Left + (item.Bounds.Width * column / 2);
                    double y = item.Bounds.Top + (item.Bounds.Height * row / 2);
                    if (Math.Abs(reference.X - x) <= radius && Math.Abs(reference.Y - y) <= radius)
                    {
                        cueId = item.Id;
                        anchor = (AnchorPoint)((row * 3) + column);
                        return true;
                    }
                }
            }
        }

        cueId = default;
        anchor = default;
        return false;
    }
}
