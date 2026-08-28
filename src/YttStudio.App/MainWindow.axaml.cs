using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.ComponentModel;

namespace YttStudio.App;

public partial class MainWindow : Window
{
    private MainWindowViewModel? observedViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        observedViewModel = DataContext as MainWindowViewModel;
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsInlineEditing) ||
            sender is not MainWindowViewModel viewModel ||
            !viewModel.IsInlineEditing)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!viewModel.IsInlineEditing || !InlineEditorTextBox.IsEffectivelyVisible)
            {
                return;
            }

            InlineEditorTextBox.Focus();
            InlineEditorTextBox.SelectAll();
        }, DispatcherPriority.Input);
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

    private void OnInlineEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && viewModel.IsInlineEditing)
        {
            viewModel.CommitInlineEdit();
        }
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

        if (TryHandleHistoryShortcut(e, source))
        {
            return;
        }

        if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (IsPlaybackShortcutBlocked(source))
        {
            return;
        }

        if (DataContext is MainWindowViewModel playbackViewModel &&
            playbackViewModel.PlayPauseCommand.CanExecute(null))
        {
            playbackViewModel.PlayPauseCommand.Execute(null);
            e.Handled = true;
        }
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

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = GetDropPaths(e.DataTransfer).Length > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.OpenDroppedPathsAsync(GetDropPaths(e.DataTransfer));
        }
    }

    private static string[] GetDropPaths(IDataTransfer dataTransfer)
    {
        IReadOnlyList<IStorageItem>? files = dataTransfer.TryGetFiles();
        if (files is null)
        {
            return [];
        }

        return files
            .Select(file => file.Path.LocalPath)
            .Where(File.Exists)
            .Where(OpenPathClassifier.IsDropSupported)
            .ToArray();
    }
}
