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

/// <summary>선택 상태 관리와 화면 갱신 알림을 담당한다.</summary>
public sealed partial class MainWindowViewModel
{

    /// <summary>스타일 식별자를 표시용 이름으로 바꾼다. 알 수 없으면 기본 스타일 이름을 쓴다.</summary>
    internal string StyleNameOf(Guid? styleId)
    {
        if (project is null)
        {
            return "Default";
        }

        StylePreset style = project.GetStyle(styleId);
        return string.IsNullOrWhiteSpace(style.Name) ? "Default" : style.Name;
    }

    private Cue? SingleSelectedCue()
    {
        if (project is null || selectedCueIds.Count != 1)
        {
            return null;
        }

        Cue? cue = project.Cues[selectedCueIds.First()];
        return cue is null || cue.Sections.Count == 0 ? null : cue;
    }

    private Section? FirstSelectedSection() => SingleSelectedCue()?.Sections[0];

    public void SelectCue(Guid cueId, bool toggle)
    {
        if (project?.Cues[cueId] is null)
        {
            return;
        }

        if (!toggle)
        {
            selectedCueIds.Clear();
            selectedCueIds.Add(cueId);
        }
        else if (!selectedCueIds.Remove(cueId))
        {
            selectedCueIds.Add(cueId);
        }

        lastSelectedCueId = selectedCueIds.Contains(cueId) ? cueId : selectedCueIds.LastOrDefault();
        if (selectedCueIds.Count == 0)
        {
            lastSelectedCueId = null;
        }
        selectedCueRow = lastSelectedCueId is Guid selected
            ? CueRows.FirstOrDefault(row => row.Id == selected)
            : null;
        OnPropertyChanged(nameof(SelectedCueRow));
        RefreshCanvasSelection();
        NotifySelectionProperties();
    }

    public void SelectInRectangle(CanvasRect rectangle)
    {
        selectedCueIds.Clear();
        lastSelectedCueId = null;
        foreach (CanvasCueItem item in CanvasItems.Where(item => Intersects(item.Bounds, rectangle)))
        {
            selectedCueIds.Add(item.Id);
            lastSelectedCueId = item.Id;
        }

        RefreshCanvasSelection();
        NotifySelectionProperties();
    }

    private void AfterMutation(bool refreshRows = false)
    {
        // 자동 저장은 복구할 내용이 있을 때만 기록한다.
        unsavedChanges = true;
        if (refreshRows)
        {
            RefreshRowsAndStyles();
        }

        ReconcileSelection();

        UpdateMaximum();
        RenderSubtitlePreview();
        NotifySelectionProperties();
        NotifyCommandStates();
    }

    private void RefreshInlinePreview()
    {
        // Live typing is a visual/model notification only. The open editor
        // transaction owns the eventual dirty/history transition at commit.
        RefreshRowsAndStyles();
        ReconcileSelection();
        RenderSubtitlePreview();
        if (inlineEditorUsesReferencePlacement && inlineEditCueId is not null)
        {
            RefreshInlineEditorLayout(inlineEditorContentBounds, inlineEditorViewport);
        }
        NotifySelectionProperties();
    }

    private void RefreshRowsAndStyles()
    {
        CueRows.Clear();
        Styles.Clear();
        Styles.Add(new StyleOption(Guid.Empty, project?.Styles.Default.Name ?? "Default"));
        if (project is null)
        {
            selectedStyleId = Guid.Empty;
            selectedStyleName = string.Empty;
            return;
        }

        int number = 1;
        foreach (Cue cue in project.Cues.OrderBy(cue => cue.Start))
        {
            CueRows.Add(new CueRowViewModel(this, cue.Id, number++));
        }

        foreach (StylePreset style in project.Styles.Where(style => style.Id != Guid.Empty).OrderBy(style => style.Name))
        {
            Styles.Add(new StyleOption(style.Id, style.Name));
        }

        if (selectedStyleId is not Guid id || !Styles.Any(style => style.Id == id))
        {
            selectedStyleId = Guid.Empty;
        }

        selectedStyleName = Styles.FirstOrDefault(style => style.Id == selectedStyleId)?.Name ?? string.Empty;

        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(SelectedStyleId));
        OnPropertyChanged(nameof(SelectedStyleOption));
        OnPropertyChanged(nameof(SelectedStyleName));
    }

    private void ReconcileSelection()
    {
        lastSelectedCueId = ReconcileCueSelection(project, selectedCueIds, lastSelectedCueId);

        selectedCueRow = lastSelectedCueId is Guid selectedId
            ? CueRows.FirstOrDefault(row => row.Id == selectedId)
            : null;
        OnPropertyChanged(nameof(SelectedCueRow));
    }

    internal static Guid? ReconcileCueSelection(
        SubtitleProject? project,
        HashSet<Guid> selectedCueIds,
        Guid? lastSelectedCueId)
    {
        ArgumentNullException.ThrowIfNull(selectedCueIds);
        if (project is null)
        {
            selectedCueIds.Clear();
            return null;
        }

        selectedCueIds.RemoveWhere(id => project.Cues[id] is null);
        return lastSelectedCueId is Guid selected && selectedCueIds.Contains(selected)
            ? selected
            : selectedCueIds.Count == 0 ? null : selectedCueIds.Last();
    }

    private void RefreshCanvasSelection()
    {
        SetCanvasItems(CanvasItems
            .Select(item => item with { Selected = selectedCueIds.Contains(item.Id) })
            .ToArray());
    }

    private void NotifySelectionProperties()
    {
        RefreshKaraokePresentation();
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(HasMixedSelection));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(SelectedPositionX));
        OnPropertyChanged(nameof(SelectedPositionY));
        OnPropertyChanged(nameof(SelectedPositionXText));
        OnPropertyChanged(nameof(SelectedPositionYText));
        OnPropertyChanged(nameof(SelectedAnchor));
        OnPropertyChanged(nameof(SelectedAnchorDisplay));
        OnPropertyChanged(nameof(SelectedJustification));
        OnPropertyChanged(nameof(SelectedJustificationDisplay));
        OnPropertyChanged(nameof(SelectedDirection));
        OnPropertyChanged(nameof(SelectedCueStyleOption));
        OnPropertyChanged(nameof(SelectedFont));
        OnPropertyChanged(nameof(SelectedSizePercent));
        OnPropertyChanged(nameof(SelectedSizePercentValue));
        OnPropertyChanged(nameof(SelectedBold));
        OnPropertyChanged(nameof(SelectedItalic));
        OnPropertyChanged(nameof(SelectedUnderline));
        OnPropertyChanged(nameof(SelectedScriptOffset));
        OnPropertyChanged(nameof(SelectedPack));
        OnPropertyChanged(nameof(SelectedEdge));
        OnPropertyChanged(nameof(ForegroundHex));
        OnPropertyChanged(nameof(ForegroundOpacity));
        OnPropertyChanged(nameof(BackgroundHex));
        OnPropertyChanged(nameof(BackgroundOpacity));
        OnPropertyChanged(nameof(EdgeColorHex));
        OnPropertyChanged(nameof(EdgeOpacity));
        OnPropertyChanged(nameof(MoveEffectEnabled));
        OnPropertyChanged(nameof(FadeEffectEnabled));
        OnPropertyChanged(nameof(ShakeEffectEnabled));
        OnPropertyChanged(nameof(ChromaEffectEnabled));
        OnPropertyChanged(nameof(AnimateEffectEnabled));
        OnPropertyChanged(nameof(SelectedKaraokeCueId));
        OnPropertyChanged(nameof(HasKaraokeCue));
        OnPropertyChanged(nameof(SelectedKaraokeCueDurationMilliseconds));
        OnPropertyChanged(nameof(SelectedKaraokeTypeOption));

        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        SaveCommand.NotifyCanExecuteChanged();
        PlayPauseCommand.NotifyCanExecuteChanged();
        StepBackCommand.NotifyCanExecuteChanged();
        StepForwardCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        AddCueCommand.NotifyCanExecuteChanged();
        DeleteCueCommand.NotifyCanExecuteChanged();
        DuplicateCueCommand.NotifyCanExecuteChanged();
        AddStyleCommand.NotifyCanExecuteChanged();
        DeleteStyleCommand.NotifyCanExecuteChanged();
        RenameStyleCommand.NotifyCanExecuteChanged();
        SaveCueAsStyleCommand.NotifyCanExecuteChanged();
        ApplySelectedStyleCommand.NotifyCanExecuteChanged();
        AlignLeftCommand.NotifyCanExecuteChanged();
        AlignCenterCommand.NotifyCanExecuteChanged();
        AlignRightCommand.NotifyCanExecuteChanged();
        AlignTopCommand.NotifyCanExecuteChanged();
        AlignMiddleCommand.NotifyCanExecuteChanged();
        AlignBottomCommand.NotifyCanExecuteChanged();
        DistributeHorizontalCommand.NotifyCanExecuteChanged();
        DistributeVerticalCommand.NotifyCanExecuteChanged();
        BringToFrontCommand.NotifyCanExecuteChanged();
        SendToBackCommand.NotifyCanExecuteChanged();
        ValidateCommand.NotifyCanExecuteChanged();
        AutoSplitKaraokeCommand.NotifyCanExecuteChanged();
    }

    private void RefreshKaraokePresentation()
    {
        KaraokeSections.Clear();
        if (SelectedKaraokeCueId is not Guid cueId || project?.Cues[cueId] is not Cue cue)
        {
            return;
        }

        for (int index = 0; index < cue.Sections.Count; index++)
        {
            Section section = cue.Sections[index];
            KaraokeSections.Add(new KaraokeSectionViewModel(
                this,
                cue.Id,
                index,
                section.Text,
                section.KaraokeOffset));
        }
    }

    private void UpdateMaximum()
    {
        double cueMaximum = project?.Cues.Select(cue => cue.End.TotalMilliseconds).DefaultIfEmpty(1).Max() ?? 1;
        double videoMaximum = videoLoaded ? videoSource?.Info.Duration.TotalMilliseconds ?? 1 : 1;
        MaximumMilliseconds = Math.Max(1, Math.Max(cueMaximum, videoMaximum));
    }

    private static bool Intersects(CanvasRect first, CanvasRect second)
        => first.Left <= second.Right && first.Right >= second.Left &&
            first.Top <= second.Bottom && first.Bottom >= second.Top;
}
