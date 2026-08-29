using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.ComponentModel;

namespace YttStudio.App;

public partial class MainWindow : Window
{
    private MainWindowViewModel? observedViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // 터널 단계로 붙인다. 포커스를 가진 단추나 목록이 Space 를 삼키기 전에 받아야
        // 창 어디에서든 재생 · 정지가 같은 키로 걸린다.
        AddHandler(KeyDownEvent, OnPlaybackShortcutPreview, RoutingStrategies.Tunnel);
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

    private void OnInlineEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && viewModel.IsInlineEditing)
        {
            viewModel.CommitInlineEdit();
        }
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
