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

/// <summary>큐 · 스타일 편집 명령과 제자리 편집 확정을 담당한다.</summary>
public sealed partial class MainWindowViewModel
{

    /// <summary>큐 타이밍을 일괄 이동한다. 편집기가 어떤 큐도 1 ms 이전에 시작하지 않도록 보정한다.</summary>
    private void ShiftTimes(bool selectedOnly)
    {
        if (editor is null || project is null)
        {
            return;
        }

        IEnumerable<Guid> targets = selectedOnly
            ? selectedCueIds.ToArray()
            : project.Cues.Select(cue => cue.Id).ToArray();

        TimeSpan applied = editor.ShiftCueTimes(targets, TimeSpan.FromMilliseconds(shiftMilliseconds));
        Status = $"{Loc["TimeShift"]}: {applied.TotalMilliseconds:0} ms";
        UpdateMaximum();
        AfterMutation(refreshRows: true);
    }

    private void Undo()
    {
        editor?.Undo();
        AfterMutation(refreshRows: true);
    }

    private void Redo()
    {
        editor?.Redo();
        AfterMutation(refreshRows: true);
    }

    private void AddCue()
    {
        if (editor is null)
        {
            project = new SubtitleProject();
            editor = new DocumentEditor(project);
        }

        Cue cue = editor.AddCue(TimeSpan.FromMilliseconds(PositionMilliseconds),
            TimeSpan.FromMilliseconds(PositionMilliseconds + 2000), "새 자막");
        RefreshRowsAndStyles();
        SelectCue(cue.Id, toggle: false);
        AfterMutation(refreshRows: false);
    }

    private void DeleteSelectedCues()
    {
        if (isInlineEditing || editor is null || project is null || selectedCueIds.Count == 0)
        {
            return;
        }

        Guid[] deletedIds = selectedCueIds.ToArray();
        Cue[] orderedCues = project.Cues
            .OrderBy(cue => cue.Start)
            .ToArray();
        int firstSelected = Array.FindIndex(orderedCues, cue => selectedCueIds.Contains(cue.Id));
        int lastSelected = Array.FindLastIndex(orderedCues, cue => selectedCueIds.Contains(cue.Id));
        Cue? neighbor = lastSelected >= 0
            ? orderedCues.Skip(lastSelected + 1).FirstOrDefault(cue => !selectedCueIds.Contains(cue.Id))
            : null;
        neighbor ??= firstSelected > 0
            ? orderedCues.Take(firstSelected).LastOrDefault(cue => !selectedCueIds.Contains(cue.Id))
            : null;

        editor.RemoveCues(deletedIds);
        selectedCueIds.Clear();
        lastSelectedCueId = neighbor?.Id;
        if (neighbor is not null)
        {
            selectedCueIds.Add(neighbor.Id);
        }

        AfterMutation(refreshRows: true);
    }

    private void DuplicateSelectedCues()
    {
        if (editor is null)
        {
            return;
        }

        IReadOnlyList<Cue> copies = editor.DuplicateCues(selectedCueIds);
        selectedCueIds.Clear();
        foreach (Cue cue in copies)
        {
            selectedCueIds.Add(cue.Id);
            lastSelectedCueId = cue.Id;
        }

        AfterMutation(refreshRows: true);
    }

    private void AddStyle()
    {
        StylePreset? style = editor?.AddStyle($"스타일 {Styles.Count}");
        RefreshRowsAndStyles();
        if (style is not null)
        {
            SelectedStyleId = style.Id;
        }
    }

    private void RenameSelectedStyle()
    {
        if (editor is null || selectedStyleId is not Guid id || id == Guid.Empty)
        {
            return;
        }

        editor.RenameStyle(id, selectedStyleName);
        RefreshRowsAndStyles();
        AfterMutation(refreshRows: false);
    }

    private void SaveSelectedCueAsStyle()
    {
        if (editor is null || selectedStyleId is not Guid id || id == Guid.Empty ||
            SelectedFormat is not ResolvedFormat format || SelectedCue is not Cue cue)
        {
            return;
        }

        editor.UpdateStyle(id, new SectionFormatPatch
        {
            Font = format.Font,
            SizePercent = format.SizePercent,
            Bold = format.Bold,
            Italic = format.Italic,
            Underline = format.Underline,
            Offset = format.Offset,
            Foreground = format.Foreground,
            Background = format.Background,
            SecondaryColor = format.SecondaryColor,
            Edge = format.Edge,
            EdgeColor = format.EdgeColor,
            Pack = format.Pack,
        }, cue.Anchor, cue.Justify);
        RefreshRowsAndStyles();
        AfterMutation();
    }

    private void ApplySelectedStyle()
    {
        if (editor is null || selectedStyleId is not Guid id || selectedCueIds.Count == 0)
        {
            return;
        }

        editor.ApplyStyle(selectedCueIds, id == Guid.Empty ? null : id);
        AfterMutation(refreshRows: true);
    }

    private InlineEditorStyle ResolveInlineEditorStyle(Cue cue)
    {
        ArgumentNullException.ThrowIfNull(project);
        Section section = cue.Sections[0];
        StylePreset style = project.GetStyle(section.StyleIdOverride ?? cue.StyleId);
        ResolvedFormat format = FormatResolver.Resolve(style.BaseFormat, section.Overrides);
        FontResolution resolution = renderer.ResolveFont(format.Font);
        return InlineEditorPresentationMapper.Map(format, resolution, cue.Justify);
    }

    private void ApplyInlineEditorStyle(InlineEditorStyle style)
    {
        inlineEditorStyle = style;
        InlineEditorFontFamily = style.FontFamily;
        InlineEditorFontSize = style.ReferenceFontSize;
        InlineEditorForeground = style.ForegroundBrush;
        InlineEditorTextAlignment = style.TextAlignment;
        InlineEditorPadding = style.ReferencePadding;
    }

    public void CommitInlineEdit()
    {
        if (!isInlineEditing)
        {
            return;
        }

        bool hasChanges = inlineEditIncludesNewCue ||
            (inlineEditCueId is Guid cueId &&
             project?.Cues[cueId] is Cue cue &&
             (uint)inlineEditSectionIndex < (uint)cue.Sections.Count &&
             cue.Sections[inlineEditSectionIndex].Text != inlineEditOriginalText);
        if (editor?.IsTransactionActive == true)
        {
            if (hasChanges)
            {
                editor.EndTransaction();
            }
            else
            {
                editor.CancelTransaction();
            }
        }

        inlineEditCueId = null;
        pendingInlineEditCueId = null;
        inlineEditIncludesNewCue = false;
        inlineEditOriginalText = string.Empty;
        inlineEditOriginalUnsavedChanges = false;
        inlineEditorUsesReferencePlacement = false;
        inlineEditReferenceBounds = null;
        IsInlineEditing = false;
        RefreshRowsAndStyles();
        if (hasChanges)
        {
            AfterMutation(refreshRows: false);
        }
        else
        {
            RefreshInlinePreview();
        }
    }

    public void CancelInlineEdit()
    {
        if (!isInlineEditing)
        {
            return;
        }

        if (editor?.IsTransactionActive == true)
        {
            editor.CancelTransaction();
        }

        inlineEditCueId = null;
        pendingInlineEditCueId = null;
        inlineEditIncludesNewCue = false;
        inlineEditorUsesReferencePlacement = false;
        inlineEditReferenceBounds = null;
        IsInlineEditing = false;
        SetField(ref inlineText, inlineEditOriginalText, nameof(InlineText));
        inlineEditOriginalText = string.Empty;
        RefreshRowsAndStyles();
        RefreshInlinePreview();
        unsavedChanges = inlineEditOriginalUnsavedChanges;
        inlineEditOriginalUnsavedChanges = false;
    }

    private async Task DeleteSelectedStyleAsync()
    {
        if (editor is null || selectedStyleId is not Guid id || id == Guid.Empty)
        {
            return;
        }

        bool confirmed = await dialogs.ConfirmAsync("스타일 삭제",
            "참조 중인 자막은 현재 해석된 값을 override로 굳혀 외형을 유지합니다. 삭제할까요?");
        if (!confirmed)
        {
            return;
        }

        editor.DeleteStyle(id);
        SelectedStyleId = Guid.Empty;
        RefreshRowsAndStyles();
        AfterMutation(refreshRows: false);
    }

    private void ApplyPosition(double? x, double? y)
    {
        if (editor is null || project is null || selectedCueIds.Count == 0)
        {
            return;
        }

        Dictionary<Guid, CanvasPoint> positions = selectedCueIds.ToDictionary(
            id => id,
            id => new CanvasPoint(x ?? project.Cues[id]!.PositionX, y ?? project.Cues[id]!.PositionY));
        editor.MoveCues(positions);
        AfterMutation();
    }

    private void ApplyAnchor(AnchorPoint anchor)
    {
        if (editor is null || selectedCueIds.Count == 0)
        {
            return;
        }

        editor.BeginTransaction("앵커 변경");
        foreach (Guid id in selectedCueIds)
        {
            CanvasCueItem? item = CanvasItems.FirstOrDefault(candidate => candidate.Id == id);
            if (item is null)
            {
                continue;
            }

            CanvasPoint ytt = PreserveBoxForAnchor(item.Bounds, anchor);
            editor.SetAnchor(id, anchor, ytt.X, ytt.Y);
        }

        editor.EndTransaction();
        AfterMutation();
    }

    private void ApplyFormat(SectionFormatPatch patch)
    {
        if (editor is null || selectedCueIds.Count == 0)
        {
            return;
        }

        editor.ApplyFormat(selectedCueIds, patch);
        AfterMutation();
    }
}
