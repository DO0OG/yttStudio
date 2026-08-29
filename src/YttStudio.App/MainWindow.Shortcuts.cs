using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace YttStudio.App;

/// <summary>창 전역 단축키 라우팅을 담는다.</summary>
public partial class MainWindow
{
    /// <summary>
    /// 재생 · 정지 단축키를 터널 단계에서 가로챈다.
    /// </summary>
    /// <remarks>
    /// 버블 단계에서 처리하면 포커스를 가진 단추나 목록이 <c>Space</c> 를 먼저 삼킨다.
    /// 그래서 재생 줄의 단추를 한 번 누른 뒤에는 스페이스바가 재생 · 정지 대신 방금 누른
    /// 단추를 다시 눌렀다. 터널 단계는 포커스를 가진 컨트롤보다 먼저 도착하므로 창 어디에
    /// 포커스가 있든 같은 키가 같은 일을 한다.
    /// </remarks>
    private void OnPlaybackShortcutPreview(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (!ShouldTogglePlayback(
            e.Key,
            e.KeyModifiers,
            IsPlaybackShortcutBlocked(e.Source as Visual),
            viewModel.PlayPauseCommand.CanExecute(null)))
        {
            return;
        }

        viewModel.PlayPauseCommand.Execute(null);
        e.Handled = true;
    }

    private void OnInlineEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsInlineEditing)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            viewModel.CancelInlineEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            viewModel.CommitInlineEdit();
            e.Handled = true;
        }
        // Shift+Enter is intentionally left to the TextBox so it inserts a
        // line break.
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        IInputElement? focused = TopLevel.GetTopLevel(this)?.FocusManager.GetFocusedElement();
        Visual? source = e.Source as Visual ?? focused as Visual;
        if (TryHandleInlineEditorKeyDown(e, source))
        {
            return;
        }

        if (TryHandleDeleteFromList(e, source))
        {
            return;
        }

        TryHandleHistoryShortcut(e, source);
    }

    private bool TryHandleInlineEditorKeyDown(KeyEventArgs e, Visual? source)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsInlineEditing ||
            !IsTextBoxFocused(source))
        {
            return false;
        }

        if (e.Key == Key.Escape)
        {
            viewModel.CancelInlineEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            viewModel.CommitInlineEdit();
            e.Handled = true;
        }

        return e.Handled || e.Key == Key.Enter;
    }

    private bool TryHandleDeleteFromList(KeyEventArgs e, Visual? source)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            !ShouldDeleteCueFromList(
                e.Key,
                e.KeyModifiers,
                IsCueListFocused(source),
                IsTextBoxFocused(source),
                viewModel.IsInlineEditing))
        {
            return false;
        }

        if (viewModel.DeleteCueCommand.CanExecute(null))
        {
            viewModel.DeleteCueCommand.Execute(null);
        }

        e.Handled = true;
        return true;
    }

    private bool TryHandleHistoryShortcut(KeyEventArgs e, Visual? source)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            !e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
            e.KeyModifiers.HasFlag(KeyModifiers.Alt) ||
            IsTextBoxFocused(source))
        {
            return false;
        }

        DelegateCommand command;
        if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            command = viewModel.RedoCommand;
        }
        else if (e.Key == Key.Y && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            command = viewModel.RedoCommand;
        }
        else if (e.Key == Key.Z && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            command = viewModel.UndoCommand;
        }
        else
        {
            return false;
        }

        if (!command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        e.Handled = true;
        return true;
    }

    /// <summary>스페이스바를 재생 · 정지로 돌릴지 판단한다.</summary>
    /// <remarks>
    /// 재생할 것이 없으면 가로채지 않는다. 그래야 포커스를 가진 단추가 평소대로
    /// 스페이스바로 눌린다.
    /// </remarks>
    internal static bool ShouldTogglePlayback(
        Key key,
        KeyModifiers modifiers,
        bool shortcutBlocked,
        bool canTogglePlayback)
        => key == Key.Space && modifiers == KeyModifiers.None && !shortcutBlocked &&
            canTogglePlayback;

    internal static bool ShouldDeleteCueFromList(
        Key key,
        KeyModifiers modifiers,
        bool cueListFocused,
        bool textBoxFocused,
        bool inlineEditing)
        => key == Key.Delete && modifiers == KeyModifiers.None && cueListFocused &&
            !textBoxFocused && !inlineEditing;

    private bool IsCueListFocused(Visual? element)
    {
        if (CueList is null)
        {
            return false;
        }

        for (Visual? current = element; current is not null; current = current.GetVisualParent())
        {
            if (ReferenceEquals(current, CueList))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTextBoxFocused(Visual? element)
    {
        for (Visual? current = element; current is not null; current = current.GetVisualParent())
        {
            if (current is TextBox)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 스페이스바를 재생 · 정지로 쓸 수 없는 자리인지 판단한다.
    /// </summary>
    /// <remarks>
    /// 세 자리는 <c>Space</c> 에 이미 제 역할이 있다. 입력 상자는 공백을 넣고,
    /// 가라오케 타임라인은 음절 구간을 찍고, 타임라인은 누른 채 끌어 화면을 민다.
    /// </remarks>
    private static bool IsPlaybackShortcutBlocked(Visual? element)
    {
        for (Visual? current = element; current is not null; current = current.GetVisualParent())
        {
            if (current is TextBox or TimelineControl or KaraokeTimelineControl)
            {
                return true;
            }
        }

        return false;
    }
}
