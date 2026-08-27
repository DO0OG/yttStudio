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

/// <summary>제자리 편집기의 표시 상태와 배치 바인딩 속성을 담당한다.</summary>
public sealed partial class MainWindowViewModel
{

    public Guid? SelectedKaraokeCueId => selectedCueIds.Count == 1 ? lastSelectedCueId : null;

    public bool HasKaraokeCue => SelectedKaraokeCueId.HasValue && editor is not null;

    public double SelectedKaraokeCueDurationMilliseconds
        => SelectedCue is Cue cue ? Math.Max(1, (cue.End - cue.Start).TotalMilliseconds) : 1;

    public KaraokeTypeOption? SelectedKaraokeTypeOption
    {
        get
        {
            KaraokeType type = SelectedCue?.Effects.OfType<KaraokeSettings>().LastOrDefault()?.Type
                ?? KaraokeType.Simple;
            return KaraokeTypeOptions.FirstOrDefault(option => option.Value == type);
        }
        set
        {
            if (value is null || editor is null || SelectedKaraokeCueId is not Guid cueId)
            {
                return;
            }

            editor.SetKaraokeType(cueId, value.Value);
            AfterMutation();
        }
    }

    public ValidationIssue? SelectedValidationIssue
    {
        get => selectedValidationIssue;
        set => SetField(ref selectedValidationIssue, value);
    }

    public bool IsInlineEditing
    {
        get => isInlineEditing;
        private set
        {
            if (!SetField(ref isInlineEditing, value))
            {
                return;
            }

            CommitInlineEditCommand.NotifyCanExecuteChanged();
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
            DeleteCueCommand.NotifyCanExecuteChanged();
        }
    }

    public string InlineText
    {
        get => inlineText;
        set
        {
            string next = value ?? string.Empty;
            if (inlineText == next)
            {
                return;
            }

            bool appliedToModel = false;
            if (isInlineEditing && editor is not null && inlineEditCueId is Guid cueId &&
                project?.Cues[cueId] is Cue cue &&
                (uint)inlineEditSectionIndex < (uint)cue.Sections.Count)
            {
                Section section = cue.Sections[inlineEditSectionIndex];
                if (section.Text != next)
                {
                    editor.SetText(cueId, inlineEditSectionIndex, next);
                    appliedToModel = true;
                }
            }

            if (SetField(ref inlineText, next) && appliedToModel)
            {
                // SetText is part of the active transaction, while rendering is
                // intentionally immediate so typing is visible in the preview.
                // This path must not mark the document dirty: canceling the
                // session must preserve its pre-session autosave state.
                RefreshInlinePreview();
            }
        }
    }

    public double InlineEditorLeft
    {
        get => inlineEditorLeft;
        private set => SetField(ref inlineEditorLeft, value);
    }

    public double InlineEditorTop
    {
        get => inlineEditorTop;
        private set => SetField(ref inlineEditorTop, value);
    }

    public double InlineEditorWidth
    {
        get => inlineEditorWidth;
        private set => SetField(ref inlineEditorWidth, value);
    }

    public double InlineEditorHeight
    {
        get => inlineEditorHeight;
        private set => SetField(ref inlineEditorHeight, value);
    }

    public FontFamily InlineEditorFontFamily
    {
        get => inlineEditorFontFamily;
        private set => SetField(ref inlineEditorFontFamily, value);
    }

    public double InlineEditorFontSize
    {
        get => inlineEditorFontSize;
        private set => SetField(ref inlineEditorFontSize, value);
    }

    public IBrush InlineEditorForeground
    {
        get => inlineEditorForeground;
        private set => SetField(ref inlineEditorForeground, value);
    }

    public TextAlignment InlineEditorTextAlignment
    {
        get => inlineEditorTextAlignment;
        private set => SetField(ref inlineEditorTextAlignment, value);
    }

    public Thickness InlineEditorPadding
    {
        get => inlineEditorPadding;
        private set => SetField(ref inlineEditorPadding, value);
    }
}
