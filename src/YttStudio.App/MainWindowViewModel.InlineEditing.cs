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

/// <summary>프리뷰 제자리 편집기와 선택 큐 정렬 · 분배를 담당한다.</summary>
public sealed partial class MainWindowViewModel
{

    public void BeginInlineEdit(Guid cueId, double left, double top, double width)
    {
        if (isInlineEditing || project?.Cues[cueId] is not Cue cue || cue.Sections.Count == 0)
        {
            return;
        }

        if (editor is null)
        {
            editor = new DocumentEditor(project);
        }

        bool pendingNewCue = pendingInlineEditCueId == cueId && editor.IsTransactionActive;
        if (editor.IsTransactionActive && !pendingNewCue)
        {
            // Do not absorb an unrelated transaction into this edit session.
            return;
        }

        if (!pendingNewCue)
        {
            editor.BeginTransaction("인라인 텍스트 편집");
        }

        pendingInlineEditCueId = null;
        SelectCue(cueId, toggle: false);
        inlineEditCueId = cueId;
        inlineEditSectionIndex = 0;
        inlineEditOriginalText = cue.Sections[inlineEditSectionIndex].Text;
        inlineEditOriginalUnsavedChanges = pendingNewCue
            ? inlineEditOriginalUnsavedChanges
            : unsavedChanges;
        inlineEditIncludesNewCue = pendingNewCue;
        ApplyInlineEditorStyle(ResolveInlineEditorStyle(cue));
        inlineEditorUsesReferencePlacement = false;
        inlineEditReferenceBounds = null;
        SetField(ref inlineText, inlineEditOriginalText, nameof(InlineText));
        InlineEditorLeft = double.IsFinite(left) ? left : 0;
        InlineEditorTop = double.IsFinite(top) ? top : 0;
        InlineEditorWidth = Math.Max(0, double.IsFinite(width) ? width : 180);
        InlineEditorHeight = InlineEditorPlacement.DefaultHeight;
        IsInlineEditing = true;
        RefreshInlinePreview();
    }

    public void BeginInlineEdit(Guid cueId, Rect placement, Rect viewport)
    {
        Rect clamped = InlineEditorPlacement.Clamp(placement, viewport);
        BeginInlineEdit(cueId, clamped.Left, clamped.Top, clamped.Width);
    }

    public void RefreshInlineEditorLayout(Rect contentRect, Rect viewport)
    {
        if (!isInlineEditing || !inlineEditorUsesReferencePlacement ||
            inlineEditorStyle is not InlineEditorStyle style || inlineEditCueId is not Guid cueId)
        {
            return;
        }

        inlineEditorContentBounds = contentRect;
        inlineEditorViewport = viewport;
        if (CanvasItems.FirstOrDefault(item => item.Id == cueId) is CanvasCueItem item)
        {
            inlineEditReferenceBounds = item.Bounds;
        }

        if (inlineEditReferenceBounds is not CanvasRect referenceBounds)
        {
            return;
        }

        InlineEditorPresentation presentation = InlineEditorPresentationMapper.Scale(
            style, referenceBounds, contentRect, PreviewSubtitleSpace);
        Rect requested = new(
            presentation.Bounds.X,
            presentation.Bounds.Y,
            Math.Max(140, presentation.Bounds.Width),
            Math.Max(InlineEditorPlacement.DefaultHeight, presentation.Bounds.Height));
        Rect clamped = InlineEditorPlacement.Clamp(requested, viewport);
        InlineEditorLeft = clamped.Left;
        InlineEditorTop = clamped.Top;
        InlineEditorWidth = clamped.Width;
        InlineEditorHeight = clamped.Height;
        InlineEditorFontSize = presentation.FontSize;
        InlineEditorPadding = presentation.Padding;
    }

    public void AlignSelected(char command)
    {
        if (editor is null || project is null || selectedCueIds.Count == 0)
        {
            return;
        }

        editor.BeginTransaction("화면 기준 정렬");
        foreach (Guid id in selectedCueIds)
        {
            Cue cue = project.Cues[id]!;
            switch (command)
            {
                case 'H':
                    editor.MoveCue(id, 50, cue.PositionY);
                    break;
                case 'V':
                    editor.MoveCue(id, cue.PositionX, 50);
                    break;
                case 'C':
                    editor.SetAnchor(id, AnchorPoint.MiddleCenter, 50, 50);
                    break;
                case 'B':
                    editor.SetAnchor(id, AnchorPoint.BottomCenter, 50, 90);
                    break;
                default:
                    // 화면 기준 정렬 단축키 네 개만 이 메서드에 도달한다.
                    break;
            }
        }

        editor.EndTransaction();
        AfterMutation();
    }

    public void AlignSelected(string command)
    {
        if (command is not ("L" or "C" or "R" or "T" or "M" or "B"))
        {
            return;
        }

        CanvasCueItem[] items = CanvasItems.Where(item => selectedCueIds.Contains(item.Id)).ToArray();
        CanvasCueItem? reference = items.FirstOrDefault(item => item.Id == lastSelectedCueId);
        if (editor is null || items.Length < 2 || reference is null)
        {
            return;
        }

        bool horizontal = command is "L" or "C" or "R";
        double target = command switch
        {
            "L" => reference.Bounds.Left,
            "C" => reference.Bounds.Left + (reference.Bounds.Width / 2),
            "R" => reference.Bounds.Right,
            "T" => reference.Bounds.Top,
            "M" => reference.Bounds.Top + (reference.Bounds.Height / 2),
            _ => reference.Bounds.Bottom,
        };

        ApplyMeasuredMove(items, item =>
        {
            double current = horizontal
                ? command switch
                {
                    "L" => item.Bounds.Left,
                    "C" => item.Bounds.Left + (item.Bounds.Width / 2),
                    _ => item.Bounds.Right,
                }
                : command switch
                {
                    "T" => item.Bounds.Top,
                    "M" => item.Bounds.Top + (item.Bounds.Height / 2),
                    _ => item.Bounds.Bottom,
                };
            return horizontal ? new CanvasPoint(target - current, 0) : new CanvasPoint(0, target - current);
        });
    }

    public void DistributeSelected(bool horizontal)
    {
        CanvasCueItem[] items = CanvasItems.Where(item => selectedCueIds.Contains(item.Id))
            .OrderBy(item => horizontal ? item.Bounds.Left : item.Bounds.Top)
            .ToArray();
        if (editor is null || items.Length < 3)
        {
            return;
        }

        double first = horizontal
            ? items[0].Bounds.Left + (items[0].Bounds.Width / 2)
            : items[0].Bounds.Top + (items[0].Bounds.Height / 2);
        double last = horizontal
            ? items[^1].Bounds.Left + (items[^1].Bounds.Width / 2)
            : items[^1].Bounds.Top + (items[^1].Bounds.Height / 2);
        double step = (last - first) / (items.Length - 1);
        ApplyMeasuredMove(items, item =>
        {
            int index = Array.IndexOf(items, item);
            double current = horizontal
                ? item.Bounds.Left + (item.Bounds.Width / 2)
                : item.Bounds.Top + (item.Bounds.Height / 2);
            double target = first + (step * index);
            return horizontal ? new CanvasPoint(target - current, 0) : new CanvasPoint(0, target - current);
        });
    }

    private void MoveSelectionToZOrder(bool front)
    {
        if (editor is null || project is null || selectedCueIds.Count == 0)
        {
            return;
        }

        int boundary = front
            ? project.Cues.Max(cue => cue.ZOrder)
            : project.Cues.Min(cue => cue.ZOrder);
        editor.SetZOrder(selectedCueIds, front ? boundary + 1 : boundary - 1);
        AfterMutation();
    }

    private void ApplyMeasuredMove(IEnumerable<CanvasCueItem> items, Func<CanvasCueItem, CanvasPoint> deltaFactory)
    {
        if (editor is null)
        {
            return;
        }

        Dictionary<Guid, CanvasPoint> positions = [];
        foreach (CanvasCueItem item in items)
        {
            CanvasPoint delta = deltaFactory(item);
            positions[item.Id] = ToYttPoint(item.Anchor.X + delta.X, item.Anchor.Y + delta.Y);
        }

        if (positions.Count > 0)
        {
            editor.MoveCues(positions);
            AfterMutation();
        }
    }
}
