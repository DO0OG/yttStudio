using Avalonia;
using Avalonia.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using YttStudio.Core;
using YttStudio.Core.Editing;
using YttStudio.Render;

namespace YttStudio.App;

/// <summary>자막 공간과 화면 좌표 사이의 순수 변환을 제공한다.</summary>
public static class PreviewCanvasGeometry
{
    /// <summary>기존 편집기와 호환되는 기본 자막 공간이다.</summary>
    public static Rect DefaultSubtitleSpace =>
        new(0, 0, YttConstants.ReferenceWidth, YttConstants.ReferenceHeight);

    /// <summary>
    /// 컨트롤 안에 자막 공간 전체를 비율을 유지해 배치한다. 자막 공간의 오프셋은
    /// 원본 좌표에 남겨 두고, 반환된 사각형은 화면 좌표다.
    /// </summary>
    public static Rect GetContentRect(Size controlSize, Rect subtitleSpace)
    {
        Rect space = NormalizeSubtitleSpace(subtitleSpace);
        double width = Math.Max(0, Finite(controlSize.Width));
        double height = Math.Max(0, Finite(controlSize.Height));
        if (space.Width <= 0 || space.Height <= 0 || width <= 0 || height <= 0)
        {
            return new Rect(0, 0, width, height);
        }

        double scale = Math.Min(width / space.Width, height / space.Height);
        if (!double.IsFinite(scale) || scale < 0)
        {
            scale = 0;
        }

        double contentWidth = space.Width * scale;
        double contentHeight = space.Height * scale;
        return new Rect((width - contentWidth) / 2, (height - contentHeight) / 2,
            contentWidth, contentHeight);
    }

    /// <summary>자막 공간의 절대 픽셀 점을 화면 점으로 바꾼다.</summary>
    public static Point ToScreen(Point subtitlePoint, Rect contentRect, Rect subtitleSpace)
    {
        Rect space = NormalizeSubtitleSpace(subtitleSpace);
        double contentWidth = Math.Max(0, Finite(contentRect.Width));
        double contentHeight = Math.Max(0, Finite(contentRect.Height));
        return new Point(
            Finite(contentRect.X) + ((Finite(subtitlePoint.X) - space.X) / space.Width * contentWidth),
            Finite(contentRect.Y) + ((Finite(subtitlePoint.Y) - space.Y) / space.Height * contentHeight));
    }

    /// <summary>화면 점을 자막 공간의 절대 픽셀 점으로 바꾼다.</summary>
    public static Point ToSubtitle(Point screenPoint, Rect contentRect, Rect subtitleSpace)
    {
        Rect space = NormalizeSubtitleSpace(subtitleSpace);
        double contentWidth = Math.Max(0, Finite(contentRect.Width));
        double contentHeight = Math.Max(0, Finite(contentRect.Height));
        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return new Point(space.X, space.Y);
        }

        return new Point(
            space.X + ((Finite(screenPoint.X) - Finite(contentRect.X)) / contentWidth * space.Width),
            space.Y + ((Finite(screenPoint.Y) - Finite(contentRect.Y)) / contentHeight * space.Height));
    }

    /// <summary>자막 공간의 절대 픽셀 사각형을 화면 사각형으로 바꾼다.</summary>
    public static Rect ToScreen(CanvasRect subtitleBounds, Rect contentRect, Rect subtitleSpace)
    {
        Point topLeft = ToScreen(new Point(subtitleBounds.Left, subtitleBounds.Top),
            contentRect, subtitleSpace);
        Point bottomRight = ToScreen(new Point(subtitleBounds.Right, subtitleBounds.Bottom),
            contentRect, subtitleSpace);
        return new Rect(topLeft, bottomRight);
    }

    /// <summary>자막 공간의 이동량을 화면 이동량으로 바꾼다.</summary>
    public static Vector ToScreenDelta(double x, double y, Rect contentRect, Rect subtitleSpace)
    {
        Rect space = NormalizeSubtitleSpace(subtitleSpace);
        return new Vector(
            Finite(x) / space.Width * Math.Max(0, Finite(contentRect.Width)),
            Finite(y) / space.Height * Math.Max(0, Finite(contentRect.Height)));
    }

    /// <summary>자막 공간의 가이드 좌표를 화면 좌표로 바꾼다.</summary>
    public static double ToScreenCoordinate(double position, bool vertical,
        Rect contentRect, Rect subtitleSpace)
    {
        Rect space = NormalizeSubtitleSpace(subtitleSpace);
        double origin = vertical ? space.X : space.Y;
        double extent = vertical ? space.Width : space.Height;
        double contentOrigin = vertical ? contentRect.X : contentRect.Y;
        double contentExtent = vertical ? contentRect.Width : contentRect.Height;
        return Finite(contentOrigin) + ((Finite(position) - origin) / extent *
            Math.Max(0, Finite(contentExtent)));
    }

    /// <summary>잘못된 자막 공간은 기존 1280×720 공간으로 되돌린다.</summary>
    public static Rect NormalizeSubtitleSpace(Rect subtitleSpace)
    {
        if (!double.IsFinite(subtitleSpace.X) || !double.IsFinite(subtitleSpace.Y) ||
            !double.IsFinite(subtitleSpace.Width) || !double.IsFinite(subtitleSpace.Height) ||
            subtitleSpace.Width <= 0 || subtitleSpace.Height <= 0)
        {
            return DefaultSubtitleSpace;
        }

        return subtitleSpace;
    }

    private static double Finite(double value, double fallback = 0)
        => double.IsFinite(value) ? value : fallback;
}

public sealed record CanvasCueItem(
    Guid Id,
    CanvasRect Bounds,
    CanvasPoint Anchor,
    AnchorPoint AnchorKind,
    bool Selected);

public sealed record CanvasMovePreview(double DeltaX, double DeltaY, IReadOnlyList<SnapGuide> Guides);

/// <summary>
/// Pure geometry and value mapping for the preview's size handle.
/// Size changes are intentionally expressed as a multiplier over the
/// resolved baseline, rather than as a free-form box resize.
/// </summary>
/// <summary>
/// 크기 조절 중 맞은편 고정점을 화면 제자리에 두기 위한 기준값이다. 글자 크기가 배율만큼
/// 커지면 박스도 앵커를 기준으로 그만큼 커지므로, 앵커 좌표를 그 차이만큼 되밀어야
/// 고정점이 움직이지 않는다.
/// </summary>
internal sealed record CanvasResizeGeometry(
    CanvasPoint AnchorCanvas,
    double Width,
    double Height,
    double AnchorColumnFraction,
    double AnchorRowFraction,
    double PivotColumnFraction,
    double PivotRowFraction)
{
    /// <summary>3x3 격자 칸 번호를 박스 안의 비율로 바꾼다.</summary>
    public static double CellFraction(int cell) => Math.Clamp(cell, 0, 2) / 2.0;

    /// <summary>배율을 적용했을 때 고정점을 유지하는 앵커 좌표를 구한다.</summary>
    public CanvasPoint GetCompensatedAnchor(double achievedScale)
    {
        double scale = double.IsFinite(achievedScale) ? Math.Max(0, achievedScale) : 1.0;
        return new CanvasPoint(
            AnchorCanvas.X + (Width * (PivotColumnFraction - AnchorColumnFraction) * (1 - scale)),
            AnchorCanvas.Y + (Height * (PivotRowFraction - AnchorRowFraction) * (1 - scale)));
    }
}

public static class PreviewResizeGeometry
{
    /// <summary>선택 박스 위에 그리는 조절점의 반지름이다.</summary>
    public const double HandleRadius = 4.0;

    /// <summary>편집기가 허용하는 글자 크기 배율 상한이다.</summary>
    public const int MaximumSizePercent = 400;

    /// <summary>Shift를 누르면 배율 변화를 이 값으로 나눠 미세 조절한다.</summary>
    private const double ShiftFineDivisor = 4.0;

    /// <summary>축이 이보다 짧으면 배율이 발산하므로 크기를 바꾸지 않는다.</summary>
    private const double MinimumAxisLengthSquared = 1.0;

    /// <summary>3x3 격자에서 해당 칸의 중심점을 구한다.</summary>
    public static Point GetHandleCenter(Rect box, int row, int column)
        => new(
            Finite(box.X) + (Math.Max(0, Finite(box.Width)) * Math.Clamp(column, 0, 2) / 2),
            Finite(box.Y) + (Math.Max(0, Finite(box.Height)) * Math.Clamp(row, 0, 2) / 2));

    /// <summary>가운데 칸은 앵커 전용이고, 바깥 여덟 칸이 크기 조절점이다.</summary>
    public static bool IsResizeHandle(int row, int column) => !(row == 1 && column == 1);

    /// <summary>화면 좌표가 어떤 조절점 위에 있는지 찾는다.</summary>
    public static bool TryHitHandle(Rect box, Point point, double hitRadius, out int row, out int column)
    {
        double radius = Math.Max(0, Finite(hitRadius));
        for (int candidateRow = 0; candidateRow < 3; candidateRow++)
        {
            for (int candidateColumn = 0; candidateColumn < 3; candidateColumn++)
            {
                Point center = GetHandleCenter(box, candidateRow, candidateColumn);
                if (Math.Abs(Finite(point.X) - center.X) <= radius &&
                    Math.Abs(Finite(point.Y) - center.Y) <= radius)
                {
                    row = candidateRow;
                    column = candidateColumn;
                    return true;
                }
            }
        }

        row = -1;
        column = -1;
        return false;
    }

    /// <summary>잡은 칸의 맞은편 칸으로, 크기를 바꾸는 동안 제자리에 고정된다.</summary>
    public static (int Row, int Column) GetPivotCell(int row, int column)
        => (2 - Math.Clamp(row, 0, 2), 2 - Math.Clamp(column, 0, 2));

    /// <summary>
    /// 맞은편 고정점에서 잡은 조절점으로 향하는 축에 포인터 이동을 투영해 배율을 구한다.
    /// 고정점이 움직이지 않으므로 끄는 방향으로만 자란다.
    /// </summary>
    public static double ComputeMultiplier(
        Rect box,
        int row,
        int column,
        Vector pointerDelta,
        bool shiftPressed)
    {
        Point handle = GetHandleCenter(box, row, column);
        (int pivotRow, int pivotColumn) = GetPivotCell(row, column);
        Point pivot = GetHandleCenter(box, pivotRow, pivotColumn);
        double axisX = handle.X - pivot.X;
        double axisY = handle.Y - pivot.Y;
        double axisLengthSquared = (axisX * axisX) + (axisY * axisY);
        if (!double.IsFinite(axisLengthSquared) || axisLengthSquared <= MinimumAxisLengthSquared)
        {
            return 1.0;
        }

        double movedX = (handle.X + Finite(pointerDelta.X)) - pivot.X;
        double movedY = (handle.Y + Finite(pointerDelta.Y)) - pivot.Y;
        double multiplier = ((movedX * axisX) + (movedY * axisY)) / axisLengthSquared;
        if (!double.IsFinite(multiplier))
        {
            return 1.0;
        }

        if (shiftPressed)
        {
            multiplier = 1.0 + ((multiplier - 1.0) / ShiftFineDivisor);
        }

        return Math.Max(0, multiplier);
    }

    public static int ComputeSizePercent(int baselineSizePercent, double multiplier)
    {
        double normalizedMultiplier = double.IsFinite(multiplier) ? Math.Max(0, multiplier) : 1.0;
        return ClampSizePercent(baselineSizePercent * normalizedMultiplier);
    }

    public static int ClampSizePercent(double sizePercent)
    {
        if (!double.IsFinite(sizePercent))
        {
            return sizePercent is double.PositiveInfinity
                ? MaximumSizePercent
                : YttConstants.MinimumFontSizePercent;
        }

        return (int)Math.Clamp(
            Math.Round(sizePercent, MidpointRounding.AwayFromZero),
            YttConstants.MinimumFontSizePercent,
            MaximumSizePercent);
    }

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;
}

/// <summary>
/// The resolved style used by the inline editor before it is scaled into the
/// letterboxed preview.  The renderer and editor share the same reference
/// font/padding calculation so the text box follows the measured cue bounds.
/// </summary>
public sealed record InlineEditorStyle(
    string FontFamilyName,
    double ReferenceFontSize,
    Color ForegroundColor,
    TextAlignment TextAlignment,
    Thickness ReferencePadding)
{
    public FontFamily FontFamily => new(FontFamilyName);

    public IBrush ForegroundBrush => new SolidColorBrush(ForegroundColor);
}

/// <summary>Scaled inline-editor values for one preview control size.</summary>
public sealed record InlineEditorPresentation(
    Rect Bounds,
    FontFamily FontFamily,
    double FontSize,
    IBrush Foreground,
    TextAlignment TextAlignment,
    Thickness Padding);

/// <summary>Pure mapping and letterbox scaling for the WYSIWYG text box.</summary>
public static class InlineEditorPresentationMapper
{
    /// <summary>
    /// Keep the editor readable when a preview is smaller than the reference
    /// frame.  This is deliberately a screen-space floor and does not alter
    /// renderer measurement.
    /// </summary>
    public const double MinimumReadableFontSize = 8.0;

    public static InlineEditorStyle Map(
        ResolvedFormat format,
        FontResolution resolution,
        Justification justification)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(resolution);

        double sizePercent = Math.Max(format.SizePercent, YttConstants.MinimumFontSizePercent);
        double referenceFontSize = YttConstants.DefaultFontSizePixels * sizePercent / 100.0;
        if (format.Offset != ScriptOffset.Regular)
        {
            referenceFontSize *= YttConstants.ScriptFontScale;
        }

        double horizontalPadding = referenceFontSize * YttConstants.HorizontalBoxPaddingFactor;
        double verticalPadding = referenceFontSize * YttConstants.VerticalBoxPaddingFactor;
        byte yttAlpha = Math.Min(format.Foreground.Alpha, YttConstants.MaximumOpacity);
        byte foregroundAlpha = checked((byte)Math.Round(
            yttAlpha * 255.0 / YttConstants.MaximumOpacity));
        return new InlineEditorStyle(
            resolution.ActualFamilyName,
            referenceFontSize,
            Color.FromArgb(
                foregroundAlpha,
                format.Foreground.Red,
                format.Foreground.Green,
                format.Foreground.Blue),
            ToTextAlignment(justification),
            new Thickness(horizontalPadding, verticalPadding));
    }

    public static InlineEditorStyle Map(ResolvedFormat format, FontResolution resolution)
        => Map(format, resolution, Justification.Center);

    public static TextAlignment ToTextAlignment(Justification justification)
        => justification switch
        {
            Justification.Left => TextAlignment.Left,
            Justification.Right => TextAlignment.Right,
            _ => TextAlignment.Center,
        };

    public static InlineEditorPresentation Scale(
        InlineEditorStyle style,
        CanvasRect referenceBounds,
        Rect contentRect,
        Rect? subtitleSpace = null)
    {
        ArgumentNullException.ThrowIfNull(style);

        Rect space = PreviewCanvasGeometry.NormalizeSubtitleSpace(
            subtitleSpace ?? PreviewCanvasGeometry.DefaultSubtitleSpace);
        double contentWidth = Math.Max(0, Finite(contentRect.Width));
        double contentHeight = Math.Max(0, Finite(contentRect.Height));
        double scaleX = contentWidth / space.Width;
        double scaleY = contentHeight / space.Height;
        double scale = Math.Min(scaleX, scaleY);
        if (!double.IsFinite(scale) || scale < 0)
        {
            scale = 0;
        }

        Rect bounds = PreviewCanvasGeometry.ToScreen(referenceBounds, contentRect, space);
        Thickness padding = new(
            style.ReferencePadding.Left * scale,
            style.ReferencePadding.Top * scale,
            style.ReferencePadding.Right * scale,
            style.ReferencePadding.Bottom * scale);

        return new InlineEditorPresentation(
            bounds,
            style.FontFamily,
            Math.Max(MinimumReadableFontSize, style.ReferenceFontSize * scale),
            style.ForegroundBrush,
            style.TextAlignment,
            padding);
    }

    private static double Finite(double value, double fallback = 0)
        => double.IsFinite(value) ? value : fallback;
}

public sealed record StyleOption(Guid Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class CueRowViewModel : INotifyPropertyChanged
{
    private readonly MainWindowViewModel owner;

    public CueRowViewModel(MainWindowViewModel owner, Guid id, int number)
    {
        this.owner = owner;
        Id = id;
        Number = number;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid Id { get; }
    public int Number { get; }

    public double StartMilliseconds
    {
        get => Cue.Start.TotalMilliseconds;
        set => UpdateTiming(value, EndMilliseconds, Track);
    }

    public double EndMilliseconds
    {
        get => Cue.End.TotalMilliseconds;
        set => UpdateTiming(StartMilliseconds, value, Track);
    }

    public double DurationMilliseconds => EndMilliseconds - StartMilliseconds;

    public int Track
    {
        get => Cue.Track;
        set => UpdateTiming(StartMilliseconds, EndMilliseconds, value);
    }

    /// <summary>큐에 적용된 스타일 이름이다. 식별자 대신 사람이 읽는 이름을 보여준다.</summary>
    public string Style => owner.StyleNameOf(Cue.StyleId);

    public string Text
    {
        get => string.Concat(Cue.Sections.Select(section => section.Text));
        set
        {
            owner.UpdateCueText(Id, value ?? string.Empty);
            NotifyAll();
        }
    }

    private Cue Cue => owner.GetCue(Id) ?? throw new InvalidOperationException("Cue no longer exists.");

    private void UpdateTiming(double start, double end, int track)
    {
        owner.UpdateCueTiming(Id, start, end, track);
        NotifyAll();
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(StartMilliseconds));
        OnPropertyChanged(nameof(EndMilliseconds));
        OnPropertyChanged(nameof(DurationMilliseconds));
        OnPropertyChanged(nameof(Track));
        OnPropertyChanged(nameof(Style));
        OnPropertyChanged(nameof(Text));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
