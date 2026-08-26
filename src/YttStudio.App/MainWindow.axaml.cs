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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        IInputElement? focused = TopLevel.GetTopLevel(this)?.FocusManager.GetFocusedElement();
        Visual? source = e.Source as Visual ?? focused as Visual;
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

        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.PlayPauseCommand.CanExecute(null))
        {
            viewModel.PlayPauseCommand.Execute(null);
            e.Handled = true;
        }
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
