using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace YttStudio.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        IInputElement? focused = TopLevel.GetTopLevel(this)?.FocusManager.GetFocusedElement();
        if (IsPlaybackShortcutBlocked(e.Source as Visual ?? focused as Visual))
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
