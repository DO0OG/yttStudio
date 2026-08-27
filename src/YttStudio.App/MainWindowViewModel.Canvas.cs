using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using YttStudio.Core;
using YttStudio.Core.Editing;
using YttStudio.Core.Format;
using YttStudio.Core.Project;
using YttStudio.Core.Validation;
using YttStudio.Render;
using YttStudio.Video;
using SubtitleRenderOptions = YttStudio.Render.RenderOptions;

namespace YttStudio.App;

/// <summary>프리뷰 캔버스의 크기 변경 · 이동 · 앵커 조작을 담당한다.</summary>
public sealed partial class MainWindowViewModel
{

    public bool BeginCanvasResize(Guid primaryCueId, int grabbedRow, int grabbedColumn)
    {
        if (canvasResizeActive || isInlineEditing || editor is null || editor.IsTransactionActive || project is null ||
            selectedCueIds.Count == 0 || !selectedCueIds.Contains(primaryCueId) ||
            CanvasItems.FirstOrDefault(item => item.Id == primaryCueId) is not CanvasCueItem primary)
        {
            return false;
        }

        canvasResizeBaselines.Clear();
        canvasResizeGeometry.Clear();
        (int pivotRow, int pivotColumn) = PreviewResizeGeometry.GetPivotCell(grabbedRow, grabbedColumn);
        foreach (Guid cueId in selectedCueIds)
        {
            if (project.Cues[cueId] is not Cue cue)
            {
                continue;
            }

            if (CanvasItems.FirstOrDefault(item => item.Id == cueId) is CanvasCueItem canvasItem &&
                double.IsFinite(canvasItem.Bounds.Width) && double.IsFinite(canvasItem.Bounds.Height))
            {
                canvasResizeGeometry[cueId] = new CanvasResizeGeometry(
                    ToCanvasPoint(cue.PositionX, cue.PositionY),
                    canvasItem.Bounds.Width,
                    canvasItem.Bounds.Height,
                    CanvasResizeGeometry.CellFraction((int)canvasItem.AnchorKind % 3),
                    CanvasResizeGeometry.CellFraction((int)canvasItem.AnchorKind / 3),
                    CanvasResizeGeometry.CellFraction(pivotColumn),
                    CanvasResizeGeometry.CellFraction(pivotRow));
            }

            for (int sectionIndex = 0; sectionIndex < cue.Sections.Count; sectionIndex++)
            {
                Section section = cue.Sections[sectionIndex];
                canvasResizeBaselines[(cue.Id, sectionIndex)] =
                    ResolveSectionFormat(cue, section).SizePercent;
            }
        }

        if (canvasResizeBaselines.Count == 0 ||
            !double.IsFinite(primary.Bounds.X) || !double.IsFinite(primary.Bounds.Y) ||
            !double.IsFinite(primary.Bounds.Width) || !double.IsFinite(primary.Bounds.Height) ||
            primary.Bounds.Width <= 0 || primary.Bounds.Height <= 0)
        {
            canvasResizeBaselines.Clear();
            canvasResizeGeometry.Clear();
            return false;
        }

        try
        {
            canvasResizeOriginalUnsavedChanges = unsavedChanges;
            editor.BeginTransaction("자막 크기 변경");
        }
        catch
        {
            canvasResizeBaselines.Clear();
            canvasResizeGeometry.Clear();
            throw;
        }

        canvasResizeActive = true;
        canvasResizeChanged = false;
        return true;
    }

    public void PreviewCanvasResize(double multiplier)
    {
        if (!canvasResizeActive || editor is null || project is null)
        {
            return;
        }

        double normalizedMultiplier = double.IsFinite(multiplier) ? Math.Max(0, multiplier) : 1.0;
        bool applied = false;
        foreach (KeyValuePair<(Guid CueId, int SectionIndex), int> baseline in canvasResizeBaselines)
        {
            Guid cueId = baseline.Key.CueId;
            int sectionIndex = baseline.Key.SectionIndex;
            int baselineSizePercent = baseline.Value;
            if (project.Cues[cueId] is not Cue cue ||
                (uint)sectionIndex >= (uint)cue.Sections.Count)
            {
                continue;
            }

            Section section = cue.Sections[sectionIndex];
            int targetSizePercent = PreviewResizeGeometry.ComputeSizePercent(
                baselineSizePercent, normalizedMultiplier);
            if (ResolveSectionFormat(cue, section).SizePercent == targetSizePercent)
            {
                continue;
            }

            // The copy keeps every existing override (including style, color,
            // edge, and pack values) and changes only the explicit size.
            editor.SetFormatOverrides(
                cueId,
                sectionIndex,
                section.Overrides.WithSizePercent(targetSizePercent));
            applied = true;
        }

        canvasResizeChanged = canvasResizeBaselines.Any(item =>
            PreviewResizeGeometry.ComputeSizePercent(item.Value, normalizedMultiplier) != item.Value);
        applied |= ApplyCanvasResizeCompensation(normalizedMultiplier);
        if (applied)
        {
            AfterMutation();
        }
    }

    /// <summary>
    /// 글자만 키우면 박스가 앵커를 중심으로 양쪽으로 자란다. 맞은편 고정점이 제자리에
    /// 머무르도록 앵커 좌표를 되밀어, 잡은 조절점 방향으로만 자라는 것처럼 보이게 한다.
    /// </summary>
    private bool ApplyCanvasResizeCompensation(double multiplier)
    {
        if (editor is null || project is null || canvasResizeGeometry.Count == 0)
        {
            return false;
        }

        Dictionary<Guid, CanvasPoint> positions = [];
        foreach (KeyValuePair<Guid, CanvasResizeGeometry> entry in canvasResizeGeometry)
        {
            if (project.Cues[entry.Key] is not Cue cue ||
                !canvasResizeBaselines.TryGetValue((entry.Key, 0), out int baselineSizePercent) ||
                baselineSizePercent <= 0)
            {
                continue;
            }

            // The clamp can refuse part of the requested multiplier, so pin the
            // box against the size that was actually applied, not the request.
            double achievedScale =
                (double)PreviewResizeGeometry.ComputeSizePercent(baselineSizePercent, multiplier) /
                baselineSizePercent;
            CanvasPoint compensated = entry.Value.GetCompensatedAnchor(achievedScale);
            CanvasPoint target = ToYttPoint(compensated.X, compensated.Y);
            if (Math.Abs(cue.PositionX - target.X) > double.Epsilon ||
                Math.Abs(cue.PositionY - target.Y) > double.Epsilon)
            {
                positions[entry.Key] = target;
            }
        }

        if (positions.Count == 0)
        {
            return false;
        }

        editor.MoveCues(positions);
        return true;
    }

    public void EndCanvasResize(double multiplier)
    {
        if (!canvasResizeActive || editor is null)
        {
            return;
        }

        PreviewCanvasResize(multiplier);
        bool changed = canvasResizeChanged;
        bool originalUnsavedChanges = canvasResizeOriginalUnsavedChanges;
        if (editor.IsTransactionActive)
        {
            if (changed)
            {
                editor.EndTransaction();
            }
            else
            {
                editor.CancelTransaction();
            }
        }

        ClearCanvasResizeState();
        if (!changed)
        {
            RefreshAfterCanvasResizeCancel(originalUnsavedChanges);
        }
        else
        {
            NotifyCommandStates();
        }
    }

    public void CancelCanvasResize()
    {
        if (!canvasResizeActive || editor is null)
        {
            return;
        }

        bool originalUnsavedChanges = canvasResizeOriginalUnsavedChanges;
        if (editor.IsTransactionActive)
        {
            editor.CancelTransaction();
        }

        ClearCanvasResizeState();
        RefreshAfterCanvasResizeCancel(originalUnsavedChanges);
    }

    private void RefreshAfterCanvasResizeCancel(bool originalUnsavedChanges)
    {
        RefreshRowsAndStyles();
        ReconcileSelection();
        UpdateMaximum();
        RenderSubtitlePreview();
        NotifySelectionProperties();
        unsavedChanges = originalUnsavedChanges;
        NotifyCommandStates();
    }

    private void ClearCanvasResizeState()
    {
        canvasResizeBaselines.Clear();
        canvasResizeGeometry.Clear();
        canvasResizeActive = false;
        canvasResizeChanged = false;
        canvasResizeOriginalUnsavedChanges = false;
    }

    private ResolvedFormat ResolveSectionFormat(Cue cue, Section section)
    {
        ArgumentNullException.ThrowIfNull(project);
        StylePreset style = project.GetStyle(section.StyleIdOverride ?? cue.StyleId);
        return FormatResolver.Resolve(style.BaseFormat, section.Overrides);
    }

    private CanvasPoint ToCanvasPoint(double positionX, double positionY)
    {
        SKRect space = CurrentSubtitleSpace;
        CanvasPoint point = CanvasGeometry.ToCanvasPoint(
            positionX, positionY, space.Width, space.Height);
        return new CanvasPoint(point.X + space.Left, point.Y + space.Top);
    }

    private CanvasPoint ToYttPoint(double pixelX, double pixelY)
    {
        SKRect space = CurrentSubtitleSpace;
        return CanvasGeometry.ToYttPoint(
            pixelX - space.Left, pixelY - space.Top, space.Width, space.Height);
    }

    private CanvasPoint PreserveBoxForAnchor(CanvasRect box, AnchorPoint anchor)
    {
        SKRect space = CurrentSubtitleSpace;
        CanvasRect relative = new(
            box.X - space.Left,
            box.Y - space.Top,
            box.Width,
            box.Height);
        return CanvasGeometry.PreserveBoxForAnchor(
            relative, anchor, space.Width, space.Height);
    }

    public CanvasMovePreview PreviewCanvasMove(double deltaX, double deltaY, bool altPressed)
    {
        CanvasCueItem? primary = CanvasItems.FirstOrDefault(item => item.Id == lastSelectedCueId);
        if (primary is null)
        {
            return new CanvasMovePreview(deltaX, deltaY, []);
        }

        List<SnapGuide> guides = [];
        foreach (CanvasCueItem item in CanvasItems.Where(item => !selectedCueIds.Contains(item.Id)))
        {
            guides.Add(new SnapGuide(true, item.Anchor.X, "다른 자막 앵커"));
            guides.Add(new SnapGuide(false, item.Anchor.Y, "다른 자막 앵커"));
            guides.Add(new SnapGuide(true, item.Bounds.Left, "다른 자막 경계"));
            guides.Add(new SnapGuide(true, item.Bounds.Right, "다른 자막 경계"));
            guides.Add(new SnapGuide(false, item.Bounds.Top, "다른 자막 경계"));
            guides.Add(new SnapGuide(false, item.Bounds.Bottom, "다른 자막 경계"));
        }

        CanvasPoint requested = new(primary.Anchor.X + deltaX, primary.Anchor.Y + deltaY);
        SKRect subtitleSpace = CurrentSubtitleSpace;
        List<SnapGuide> relativeGuides = guides
            .Select(guide => guide with
            {
                Position = guide.Position - (guide.Vertical ? subtitleSpace.Left : subtitleSpace.Top),
            })
            .ToList();
        CanvasPoint relativeRequested = new(
            requested.X - subtitleSpace.Left,
            requested.Y - subtitleSpace.Top);
        SnapResult snapped = CanvasGeometry.Snap(relativeRequested, subtitleSpace.Width,
            subtitleSpace.Height, altPressed, relativeGuides);
        SnapGuide[] absoluteGuides = snapped.Guides
            .Select(guide => guide with
            {
                Position = guide.Position + (guide.Vertical ? subtitleSpace.Left : subtitleSpace.Top),
            })
            .ToArray();
        return new CanvasMovePreview(
            snapped.Point.X + subtitleSpace.Left - primary.Anchor.X,
            snapped.Point.Y + subtitleSpace.Top - primary.Anchor.Y,
            absoluteGuides);
    }

    public void CommitCanvasMove(double deltaX, double deltaY, bool altPressed)
    {
        if (editor is null || project is null || selectedCueIds.Count == 0)
        {
            return;
        }

        CanvasMovePreview preview = PreviewCanvasMove(deltaX, deltaY, altPressed);
        Dictionary<Guid, CanvasPoint> positions = [];
        foreach (Guid id in selectedCueIds)
        {
            Cue cue = project.Cues[id]!;
            CanvasPoint current = ToCanvasPoint(cue.PositionX, cue.PositionY);
            positions[id] = ToYttPoint(current.X + preview.DeltaX, current.Y + preview.DeltaY);
        }

        editor.MoveCues(positions);
        AfterMutation();
    }

    public void ChangeAnchor(Guid cueId, AnchorPoint anchor)
    {
        if (editor is null)
        {
            return;
        }

        CanvasCueItem? item = CanvasItems.FirstOrDefault(candidate => candidate.Id == cueId);
        if (item is null)
        {
            return;
        }

        CanvasPoint ytt = PreserveBoxForAnchor(item.Bounds, anchor);
        editor.SetAnchor(cueId, anchor, ytt.X, ytt.Y);
        AfterMutation();
    }

    public Guid? AddCueAtCanvasPoint(double canvasX, double canvasY)
    {
        if (isInlineEditing || editor?.IsTransactionActive == true)
        {
            return null;
        }

        if (editor is null)
        {
            project ??= new SubtitleProject();
            editor = new DocumentEditor(project);
        }

        bool wasUnsaved = unsavedChanges;
        CanvasPoint position = ToYttPoint(canvasX, canvasY);
        TimeSpan start = TimeSpan.FromMilliseconds(PositionMilliseconds);
        Cue cue;
        editor.BeginTransaction("자막 추가 및 위치 지정");
        try
        {
            cue = editor.AddCue(start, start + TimeSpan.FromSeconds(2), "새 자막");
            editor.MoveCue(cue.Id, position.X, position.Y);
        }
        catch
        {
            editor.CancelTransaction();
            throw;
        }

        // The transaction remains open until BeginInlineEdit commits or
        // cancels it, so a new cue and its initial typing are one session.
        pendingInlineEditCueId = cue.Id;
        inlineEditOriginalUnsavedChanges = wasUnsaved;
        RefreshRowsAndStyles();
        SelectCue(cue.Id, toggle: false);
        RefreshInlinePreview();
        return cue.Id;
    }

    public void NudgeSelected(double deltaX, double deltaY)
    {
        if (editor is not null && selectedCueIds.Count > 0)
        {
            editor.Nudge(selectedCueIds, deltaX, deltaY);
            AfterMutation();
        }
    }
}
