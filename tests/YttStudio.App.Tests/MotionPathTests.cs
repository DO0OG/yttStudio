using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using YttStudio.App;
using YttStudio.Core;
using YttStudio.Video;

namespace YttStudio.App.Tests;

public sealed class MotionPathTests
{
    [Fact]
    public void PresentationUsesPreviewGeometryAndAddsTimeIntervalDots()
    {
        Rect subtitleSpace = new(100, 50, 1000, 800);
        Rect content = PreviewCanvasGeometry.GetContentRect(new Size(1600, 900), subtitleSpace);
        MotionKeyframe[] path =
        [
            new(TimeSpan.Zero, 100, 50),
            new(TimeSpan.FromSeconds(1), 600, 450),
        ];

        MotionPathPresentation presentation = MotionPathGeometry.CreatePresentation(
            path, content, subtitleSpace);

        Assert.Equal(
            PreviewCanvasGeometry.ToScreen(new Point(100, 50), content, subtitleSpace),
            presentation.Keyframes[0].ScreenPoint);
        Assert.Equal(
            PreviewCanvasGeometry.ToScreen(new Point(600, 450), content, subtitleSpace),
            presentation.Keyframes[1].ScreenPoint);
        Assert.NotEmpty(presentation.TimeIntervalDots);
        Assert.True(MotionPathGeometry.TryHitMarker(
            presentation, presentation.Keyframes[1].ScreenPoint, 8, out int index));
        Assert.Equal(1, index);
        Assert.True(MotionPathGeometry.IsNearPath(
            presentation,
            new Point(
                (presentation.Keyframes[0].ScreenPoint.X + presentation.Keyframes[1].ScreenPoint.X) / 2,
                (presentation.Keyframes[0].ScreenPoint.Y + presentation.Keyframes[1].ScreenPoint.Y) / 2),
            2,
            out _));
    }

    [AvaloniaFact]
    public void AddMoveAndDeleteUseSingleUndoableEditorCommands()
    {
        using MainWindowViewModel viewModel = CreateViewModel(out string preferencePath);
        try
        {
            Guid cueId = AddCue(viewModel);
            viewModel.MoveEffectEnabled = true;
            Assert.Equal(2, viewModel.SelectedCueKeyframes.Count);

            viewModel.PositionMilliseconds = 500;
            Assert.True(viewModel.AddMotionKeyframeAtCurrentTime(640, 360));
            Assert.Equal(3, viewModel.SelectedCueKeyframes.Count);
            Assert.Contains(viewModel.SelectedCueKeyframeMarkers,
                marker => marker.CueId == cueId &&
                    marker.RelativeTime == TimeSpan.FromMilliseconds(500));

            viewModel.UndoCommand.Execute(null);
            Assert.Equal(2, viewModel.SelectedCueKeyframes.Count);
            viewModel.RedoCommand.Execute(null);
            Assert.Equal(3, viewModel.SelectedCueKeyframes.Count);

            MotionKeyframe before = viewModel.SelectedCueKeyframes[0];
            Assert.True(viewModel.CommitMotionKeyframeDrag(
                0, before.X + 40, before.Y + 20));
            Assert.Equal(before.X + 40, viewModel.SelectedCueKeyframes[0].X);
            viewModel.UndoCommand.Execute(null);
            Assert.Equal(before.X, viewModel.SelectedCueKeyframes[0].X);

            Assert.True(viewModel.SelectMotionKeyframe(1));
            Assert.True(viewModel.DeleteSelectedMotionKeyframe());
            Assert.Equal(2, viewModel.SelectedCueKeyframes.Count);
        }
        finally
        {
            DeletePreferencePath(preferencePath);
        }
    }

    [AvaloniaFact]
    public void MinimumTwoKeyframesAreProtectedFromCueDeleteRouting()
    {
        using MainWindowViewModel viewModel = CreateViewModel(out string preferencePath);
        try
        {
            _ = AddCue(viewModel);
            viewModel.MoveEffectEnabled = true;

            Assert.True(viewModel.SelectMotionKeyframe(0));
            Assert.True(viewModel.HasSelectedMotionKeyframe);
            Assert.False(viewModel.DeleteSelectedMotionKeyframe());
            Assert.Single(viewModel.SelectedCueKeyframeMarkers.Take(1));
            Assert.Single(viewModel.SelectedCueIds);
        }
        finally
        {
            DeletePreferencePath(preferencePath);
        }
    }

    [Fact]
    public void TimelineMarkerPresentationUsesCueStartPlusRelativeTime()
    {
        Guid cueId = Guid.NewGuid();
        MotionTimelineMarker marker = new(
            cueId,
            2,
            TimeSpan.FromMilliseconds(275),
            1_275,
            1);

        Assert.Equal(cueId, marker.CueId);
        Assert.Equal(1_275, marker.AbsoluteMilliseconds);
        IReadOnlyList<Point> diamond = TimelineMarkerGeometry.GetDiamond(new Point(20, 30));
        Assert.Equal(new Point(20, 24), diamond[0]);
        Assert.Equal(new Point(26, 30), diamond[1]);
    }

    [AvaloniaFact]
    public void MaxSubtitleLinesFlowsThroughMainViewModelAndPersists()
    {
        using MainWindowViewModel viewModel = CreateViewModel(out string preferencePath);
        try
        {
            Assert.Equal(AppPreferences.DefaultSubtitleLines, viewModel.MaxSubtitleLines);
            viewModel.MaxSubtitleLines = AppPreferences.MaximumSubtitleLines;
            Assert.Equal(AppPreferences.MaximumSubtitleLines, viewModel.MaxSubtitleLines);

            using MainWindowViewModel restored = new(
                new StubFileDialogService(), new PreferencesStore(preferencePath));
            Assert.Equal(AppPreferences.MaximumSubtitleLines, restored.MaxSubtitleLines);
        }
        finally
        {
            DeletePreferencePath(preferencePath);
        }
    }

    [AvaloniaFact]
    public void PreviewCanvasCanRenderSelectedMotionPathHeadlessly()
    {
        using MainWindowViewModel viewModel = CreateViewModel(out string preferencePath);
        Window? window = null;
        try
        {
            _ = AddCue(viewModel);
            viewModel.MoveEffectEnabled = true;
            PreviewCanvas canvas = new()
            {
                Width = 640,
                Height = 360,
                DataContext = viewModel,
                SubtitleSpace = viewModel.PreviewSubtitleSpace,
            };
            window = new Window
            {
                Width = 640,
                Height = 360,
                Content = canvas,
            };
            window.Show();
            window.UpdateLayout();

            Assert.NotNull(window.CaptureRenderedFrame());
        }
        finally
        {
            window?.Close();
            DeletePreferencePath(preferencePath);
        }
    }

    private static MainWindowViewModel CreateViewModel(out string preferencePath)
    {
        preferencePath = Path.Combine(
            Path.GetTempPath(), $"YttStudio-motion-{Guid.NewGuid():N}.json");
        return new MainWindowViewModel(
            new StubFileDialogService(), new PreferencesStore(preferencePath));
    }

    private static Guid AddCue(MainWindowViewModel viewModel)
    {
        Guid cueId = viewModel.AddCueAtCanvasPoint(320, 180)!.Value;
        viewModel.BeginInlineEdit(cueId, 0, 0, 180);
        viewModel.InlineText = "motion";
        viewModel.CommitInlineEdit();
        return cueId;
    }

    private static void DeletePreferencePath(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class StubFileDialogService : IFileDialogService
    {
        public Task<string?> OpenSubtitleAsync() => Task.FromResult<string?>(null);
        public Task<string?> OpenVideoAsync() => Task.FromResult<string?>(null);
        public Task<string?> OpenVideoUrlAsync(VideoUrlDialogOptions? options = null)
            => Task.FromResult<string?>(null);
        public Task<string?> SaveYttAsync(string? suggestedName) => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Confirm")
            => Task.FromResult(false);
        public Task<string?> OpenProjectAsync() => Task.FromResult<string?>(null);
        public Task<string?> SaveProjectAsync(string? suggestedName) => Task.FromResult<string?>(null);
        public Task<string?> OpenMpvLibraryAsync() => Task.FromResult<string?>(null);
        public Task<string?> RelinkVideoAsync(string missingPath) => Task.FromResult<string?>(null);
    }
}
