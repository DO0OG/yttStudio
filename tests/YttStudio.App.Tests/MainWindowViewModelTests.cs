using Avalonia.Headless.XUnit;
using YttStudio.App;
using YttStudio.Core;
using YttStudio.Core.Editing;
using YttStudio.Core.Validation;
using YttStudio.Render;

namespace YttStudio.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void ReconcileSelectionDropsCueRemovedByUndo()
    {
        SubtitleProject project = new();
        DocumentEditor editor = new(project);
        Cue cue = editor.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(2), "text");
        HashSet<Guid> selectedCueIds = [cue.Id];

        editor.RemoveCue(cue.Id);
        Guid? lastSelectedCueId = MainWindowViewModel.ReconcileCueSelection(
            project, selectedCueIds, cue.Id);

        Assert.Empty(selectedCueIds);
        Assert.Null(lastSelectedCueId);
    }

    [AvaloniaFact]
    public void InlineCommitIsOneUndoStep()
    {
        using MainWindowViewModel viewModel = CreateViewModel();
        Guid cueId = AddInlineCue(viewModel, "before");

        viewModel.CommitInlineEdit();

        Assert.Equal("before", viewModel.GetCue(cueId)?.Sections[0].Text);
        Assert.True(viewModel.UndoCommand.CanExecute(null));
        viewModel.UndoCommand.Execute(null);

        Assert.Null(viewModel.GetCue(cueId));
    }

    [AvaloniaFact]
    public void ExistingInlineCancelDoesNotAddHistoryEntry()
    {
        using MainWindowViewModel viewModel = CreateViewModel();
        Guid cueId = AddInlineCue(viewModel, "before");
        viewModel.CommitInlineEdit();

        viewModel.BeginInlineEdit(cueId, 0, 0, 180);
        viewModel.InlineText = "changed";
        viewModel.CancelInlineEdit();

        Assert.Equal("before", viewModel.GetCue(cueId)?.Sections[0].Text);
        // The only history item is the original add. If cancel had pushed a
        // text command, this undo would leave the cue in the document.
        viewModel.UndoCommand.Execute(null);
        Assert.Null(viewModel.GetCue(cueId));
    }

    [AvaloniaFact]
    public void ExistingInlineCommitUndoesToOriginalTextInOneStep()
    {
        using MainWindowViewModel viewModel = CreateViewModel();
        Guid cueId = AddInlineCue(viewModel, "before");
        viewModel.CommitInlineEdit();

        viewModel.BeginInlineEdit(cueId, 0, 0, 180);
        viewModel.InlineText = "after";
        viewModel.CommitInlineEdit();

        Assert.Equal("after", viewModel.GetCue(cueId)?.Sections[0].Text);
        viewModel.UndoCommand.Execute(null);
        Assert.Equal("before", viewModel.GetCue(cueId)?.Sections[0].Text);
    }

    [AvaloniaFact]
    public void NetNoOpInlineCommitPreservesRedoHistory()
    {
        using MainWindowViewModel viewModel = CreateViewModel();
        Guid firstCueId = AddInlineCue(viewModel, "first");
        viewModel.CommitInlineEdit();
        Guid secondCueId = AddInlineCue(viewModel, "second");
        viewModel.CommitInlineEdit();
        viewModel.UndoCommand.Execute(null);

        viewModel.BeginInlineEdit(firstCueId, 0, 0, 180);
        viewModel.InlineText = "temporary";
        viewModel.InlineText = "first";
        viewModel.CommitInlineEdit();

        Assert.True(viewModel.RedoCommand.CanExecute(null));
        viewModel.RedoCommand.Execute(null);
        Assert.NotNull(viewModel.GetCue(secondCueId));
    }

    [AvaloniaFact]
    public void NewInlineCancelPreservesRedoHistory()
    {
        using MainWindowViewModel viewModel = CreateViewModel();
        Guid existingCueId = AddInlineCue(viewModel, "existing");
        viewModel.CommitInlineEdit();
        viewModel.UndoCommand.Execute(null);
        Guid newCueId = viewModel.AddCueAtCanvasPoint(50, 50)!.Value;
        viewModel.BeginInlineEdit(newCueId, 0, 0, 180);

        viewModel.CancelInlineEdit();

        Assert.Null(viewModel.GetCue(newCueId));
        Assert.True(viewModel.RedoCommand.CanExecute(null));
        viewModel.RedoCommand.Execute(null);
        Assert.NotNull(viewModel.GetCue(existingCueId));
    }

    [AvaloniaFact]
    public void NewInlineCancelRemovesCueAndLeavesHistoryEmpty()
    {
        using MainWindowViewModel viewModel = CreateViewModel();
        Guid cueId = viewModel.AddCueAtCanvasPoint(50, 50)!.Value;
        viewModel.BeginInlineEdit(cueId, 0, 0, 180);
        viewModel.InlineText = "typed";

        viewModel.CancelInlineEdit();

        Assert.Null(viewModel.GetCue(cueId));
        Assert.False(viewModel.UndoCommand.CanExecute(null));
        Assert.False(viewModel.RedoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void InlineTypingNotifiesPreviewPropertiesImmediately()
    {
        using MainWindowViewModel viewModel = CreateViewModel();
        Guid cueId = AddInlineCue(viewModel, "before");
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.InlineText = "live";

        Assert.Equal("live", viewModel.GetCue(cueId)?.Sections[0].Text);
        Assert.Contains(nameof(MainWindowViewModel.CanvasItems), changed);
        Assert.Contains(nameof(MainWindowViewModel.SubtitleImage), changed);
        viewModel.CancelInlineEdit();
    }

    [AvaloniaFact]
    public void CanvasResizeAppliesOneMultiplierToEverySelectedCueAndUndoesOnce()
    {
        using MainWindowViewModel viewModel = CreateViewModel();
        viewModel.PositionMilliseconds = 0;
        Guid firstCueId = AddInlineCue(viewModel, "first");
        viewModel.CommitInlineEdit();
        viewModel.PositionMilliseconds = 1000;
        Guid secondCueId = AddInlineCue(viewModel, "second");
        viewModel.CommitInlineEdit();
        viewModel.SelectCue(firstCueId, toggle: false);
        viewModel.SelectCue(secondCueId, toggle: true);

        Cue first = viewModel.GetCue(firstCueId)!;
        Cue second = viewModel.GetCue(secondCueId)!;
        AnchorPoint firstAnchor = first.Anchor;
        double firstX = first.PositionX;
        double firstY = first.PositionY;
        // Grabbing the handle opposite the anchor pins the anchor itself, so the
        // cue keeps its position while only the font size grows.
        Assert.True(viewModel.BeginCanvasResize(
            firstCueId, 2 - ((int)firstAnchor / 3), 2 - ((int)firstAnchor % 3)));
        viewModel.PreviewCanvasResize(1.5);

        Assert.All(first.Sections.Concat(second.Sections), section =>
            Assert.Equal(150, section.Overrides.SizePercent));
        Assert.Equal(firstAnchor, first.Anchor);
        Assert.Equal(firstX, first.PositionX);
        Assert.Equal(firstY, first.PositionY);
        viewModel.EndCanvasResize(1.5);

        viewModel.UndoCommand.Execute(null);

        Assert.All(first.Sections.Concat(second.Sections), section =>
            Assert.Null(section.Overrides.SizePercent));
    }

    [AvaloniaFact]
    public void DeleteCueCommandIsDisabledDuringInlineEditing()
    {
        using MainWindowViewModel viewModel = CreateViewModel();
        Guid cueId = AddInlineCue(viewModel, "text");
        viewModel.CommitInlineEdit();
        viewModel.BeginInlineEdit(cueId, 0, 0, 180);

        Assert.False(viewModel.DeleteCueCommand.CanExecute(null));
        viewModel.CancelInlineEdit();
        Assert.True(viewModel.DeleteCueCommand.CanExecute(null));
        Assert.NotNull(viewModel.GetCue(cueId));
    }

    [AvaloniaFact]
    public void DeleteSelectsNextCueOrPreviousWhenDeletingLast()
    {
        using MainWindowViewModel viewModel = CreateViewModel();
        viewModel.PositionMilliseconds = 0;
        Guid firstCueId = AddInlineCue(viewModel, "first");
        viewModel.CommitInlineEdit();
        viewModel.PositionMilliseconds = 1000;
        Guid secondCueId = AddInlineCue(viewModel, "second");
        viewModel.CommitInlineEdit();
        viewModel.PositionMilliseconds = 2000;
        Guid thirdCueId = AddInlineCue(viewModel, "third");
        viewModel.CommitInlineEdit();

        viewModel.SelectCue(secondCueId, toggle: false);
        viewModel.DeleteCueCommand.Execute(null);

        Assert.Null(viewModel.GetCue(secondCueId));
        Assert.Contains(thirdCueId, viewModel.SelectedCueIds);

        viewModel.DeleteCueCommand.Execute(null);

        Assert.Null(viewModel.GetCue(thirdCueId));
        Assert.Contains(firstCueId, viewModel.SelectedCueIds);
    }

    [AvaloniaFact]
    public void ViewportModeSelectionPersistsAndExcludesMobilePortrait()
    {
        string path = Path.Combine(Path.GetTempPath(),
            $"YttStudio-viewport-tests-{Guid.NewGuid():N}.json");
        try
        {
            using (MainWindowViewModel viewModel = CreateViewModel(new PreferencesStore(path)))
            {
                Assert.DoesNotContain(PreviewViewportMode.MobilePortrait, viewModel.ViewportModes);

                viewModel.SelectYouTubeTheaterViewportCommand.Execute(null);

                Assert.Equal(PreviewViewportMode.YouTubeTheater, viewModel.SelectedViewportMode);
                Assert.True(viewModel.IsYouTubeTheaterViewport);
                Assert.Equal(1162, viewModel.PreviewSubtitleSpace.Width, precision: 2);
            }

            using MainWindowViewModel restored = CreateViewModel(new PreferencesStore(path));

            Assert.Equal(PreviewViewportMode.YouTubeTheater, restored.SelectedViewportMode);
            Assert.Equal(PreviewViewportMode.YouTubeTheater, restored.PreviewViewport.Mode);
            Assert.Equal(1162, restored.PreviewSubtitleSpace.Width, precision: 2);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void KeepsOriginalSingleArgumentConstructor()
    {
        Assert.NotNull(typeof(MainWindowViewModel).GetConstructor([typeof(IFileDialogService)]));
    }

    [AvaloniaFact]
    public void FullscreenUsesMeasuredPreviewPlayerSize()
    {
        using MainWindowViewModel viewModel = CreateViewModel();
        viewModel.SelectYouTubeFullscreenViewportCommand.Execute(null);

        viewModel.UpdatePreviewPlayerSize(900, 500);

        Assert.Equal(new SkiaSharp.SKSize(900, 500), viewModel.PreviewViewport.PlayerSize);
        Assert.Equal(900, viewModel.PreviewPlayerWidth);
        Assert.Equal(500, viewModel.PreviewPlayerHeight);
    }

    [AvaloniaFact]
    public void ViewportModeChangeRefreshesExistingW103Message()
    {
        using MainWindowViewModel viewModel = CreateViewModel();
        Guid cueId = viewModel.AddCueAtCanvasPoint(640, 360)!.Value;
        viewModel.BeginInlineEdit(cueId, 0, 0, 180);
        viewModel.InlineText = "하단 경고";
        viewModel.CommitInlineEdit();
        viewModel.PositionMilliseconds = 1;
        viewModel.SelectYouTubeTheaterViewportCommand.Execute(null);
        Assert.NotEmpty(viewModel.CanvasItems);
        viewModel.CommitCanvasMove(0, viewModel.PreviewSubtitleSpace.Height, altPressed: true);
        CanvasCueItem moved = Assert.Single(viewModel.CanvasItems);
        Assert.True(moved.Bounds.Bottom > viewModel.PreviewSubtitleSpace.Bottom * 0.98,
            $"bounds={moved.Bounds}, space={viewModel.PreviewSubtitleSpace}");
        viewModel.ValidateCommand.Execute(null);
        Assert.Contains(viewModel.ValidationIssues,
            issue => issue.Code == ValidationCodes.W103 && issue.Message.Contains("극장"));

        viewModel.SelectYouTubeDefaultViewportCommand.Execute(null);

        Assert.DoesNotContain(viewModel.ValidationIssues,
            issue => issue.Code == ValidationCodes.W103);
    }

    private static MainWindowViewModel CreateViewModel(PreferencesStore? preferencesStore = null)
    {
        return new MainWindowViewModel(new StubFileDialogService(), preferencesStore);
    }

    private static Guid AddInlineCue(MainWindowViewModel viewModel, string text)
    {
        Guid cueId = viewModel.AddCueAtCanvasPoint(50, 50)!.Value;
        viewModel.BeginInlineEdit(cueId, 0, 0, 180);
        viewModel.InlineText = text;
        return cueId;
    }

    private sealed class StubFileDialogService : IFileDialogService
    {
        public Task<string?> OpenSubtitleAsync() => Task.FromResult<string?>(null);
        public Task<string?> OpenVideoAsync() => Task.FromResult<string?>(null);
        public Task<string?> SaveYttAsync(string? suggestedName) => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "삭제")
            => Task.FromResult(false);
        public Task<string?> OpenProjectAsync() => Task.FromResult<string?>(null);
        public Task<string?> SaveProjectAsync(string? suggestedName) => Task.FromResult<string?>(null);
        public Task<string?> OpenMpvLibraryAsync() => Task.FromResult<string?>(null);
        public Task<string?> RelinkVideoAsync(string missingPath) => Task.FromResult<string?>(null);
    }
}
