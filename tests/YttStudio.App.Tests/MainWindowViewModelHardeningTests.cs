using System.Text;
using Avalonia.Headless.XUnit;
using YttStudio.App;
using YttStudio.Core;
using YttStudio.Core.Editing;
using YttStudio.Video;
using CoreVideoInfo = YttStudio.Core.VideoInfo;
using SourceVideoInfo = YttStudio.Video.VideoInfo;

namespace YttStudio.App.Tests;

public sealed class MainWindowViewModelHardeningTests
{
    [AvaloniaFact]
    public async Task LoadingVideoPersistsPathAndMetadataAndMarksProjectDirty()
    {
        using TestFiles files = new();
        TestFileDialogService dialogs = new();
        FakeVideoSource source = new(new(1920, 1080, TimeSpan.FromSeconds(12), 29.97));
        string preferencesPath = Path.Combine(files.Root, "preferences.json");

        using MainWindowViewModel viewModel = CreateViewModel(dialogs, source, preferencesPath);
        await viewModel.OpenPathAsync(files.SubtitlePath);
        dialogs.SaveProjectPaths.Enqueue(files.ProjectPath);
        await viewModel.SaveProjectCommand.ExecuteAsync();
        Assert.False(viewModel.IsDirty);

        await viewModel.OpenPathAsync(files.VideoPath);

        SubtitleProject project = Assert.IsType<SubtitleProject>(viewModel.CurrentProject);
        Assert.Equal(Path.GetFullPath(files.VideoPath), project.VideoPath);
        CoreVideoInfo video = Assert.IsType<CoreVideoInfo>(project.Video);
        Assert.Equal(source.Info.Width, video.Width);
        Assert.Equal(source.Info.Height, video.Height);
        Assert.Equal(source.Info.Duration, video.Duration);
        Assert.Equal(source.Info.NominalFps, video.NominalFps);
        Assert.True(viewModel.IsDirty);
        Assert.Single(source.LoadedPaths);
    }

    [AvaloniaFact]
    public async Task RelinkingMissingVideoPersistsReplacementAndDoesNotCreateUndoStep()
    {
        using TestFiles files = new();
        TestFileDialogService dialogs = new();
        FakeVideoSource source = new(new(1920, 1080, TimeSpan.FromSeconds(12), 29.97));
        string preferencesPath = Path.Combine(files.Root, "preferences.json");

        using MainWindowViewModel viewModel = CreateViewModel(dialogs, source, preferencesPath);
        await viewModel.OpenPathAsync(files.SubtitlePath);
        // Replacing a dirty subtitle document is deliberately discarded so the
        // video load itself is the change under test.
        dialogs.UnsavedChoices.Enqueue(UnsavedChangesChoice.Discard);
        await viewModel.OpenPathAsync(files.VideoPath);
        dialogs.SaveProjectPaths.Enqueue(files.ProjectPath);
        await viewModel.SaveProjectCommand.ExecuteAsync();
        Assert.False(viewModel.IsDirty);

        File.Delete(files.VideoPath);
        dialogs.ConfirmResults.Enqueue(true);
        dialogs.RelinkVideoPath = files.ReplacementVideoPath;

        await viewModel.OpenPathAsync(files.ProjectPath);

        SubtitleProject project = Assert.IsType<SubtitleProject>(viewModel.CurrentProject);
        Assert.Equal(Path.GetFullPath(files.ReplacementVideoPath), project.VideoPath);
        CoreVideoInfo video = Assert.IsType<CoreVideoInfo>(project.Video);
        Assert.Equal(source.Info.Width, video.Width);
        Assert.Equal(source.Info.Height, video.Height);
        Assert.Equal(source.Info.Duration, video.Duration);
        Assert.Equal(source.Info.NominalFps, video.NominalFps);
        Assert.True(viewModel.IsDirty);
        DocumentEditor editor = Assert.IsType<DocumentEditor>(viewModel.CurrentEditor);
        Assert.False(editor.CanUndo);
        Assert.Contains(Path.GetFullPath(files.ReplacementVideoPath), source.LoadedPaths);
    }

    [AvaloniaTheory]
    [InlineData(UnsavedChangesChoice.Save)]
    [InlineData(UnsavedChangesChoice.Discard)]
    [InlineData(UnsavedChangesChoice.Cancel)]
    public async Task OpenPathHonorsSaveDiscardAndCancelChoices(UnsavedChangesChoice choice)
    {
        using TestFiles files = new();
        TestFileDialogService dialogs = new();
        FakeVideoSource source = new(new(1280, 720, TimeSpan.FromSeconds(5), 30));
        string preferencesPath = Path.Combine(files.Root, "preferences.json");

        using MainWindowViewModel viewModel = CreateViewModel(dialogs, source, preferencesPath);
        await viewModel.OpenPathAsync(files.SubtitlePath);
        SubtitleProject original = Assert.IsType<SubtitleProject>(viewModel.CurrentProject);
        dialogs.UnsavedChoices.Enqueue(choice);
        if (choice == UnsavedChangesChoice.Save)
        {
            dialogs.SaveProjectPaths.Enqueue(files.ProjectPath);
        }

        bool opened = await viewModel.OpenPathAsync(files.ReplacementSubtitlePath);

        Assert.Equal(choice != UnsavedChangesChoice.Cancel, opened);
        if (choice == UnsavedChangesChoice.Cancel)
        {
            Assert.Same(original, viewModel.CurrentProject);
            Assert.True(viewModel.IsDirty);
        }
        else
        {
            Assert.NotSame(original, viewModel.CurrentProject);
            Assert.True(viewModel.IsDirty);
        }

        Assert.Equal(1, dialogs.UnsavedChoiceCalls);
        if (choice == UnsavedChangesChoice.Save)
        {
            Assert.True(File.Exists(files.ProjectPath));
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenPathStopsWhenSavingBeforeReplacementFailsOrIsCanceled(bool savePickerReturnsPath)
    {
        using TestFiles files = new();
        TestFileDialogService dialogs = new();
        FakeVideoSource source = new(new(1280, 720, TimeSpan.FromSeconds(5), 30));
        string preferencesPath = Path.Combine(files.Root, "preferences.json");

        using MainWindowViewModel viewModel = CreateViewModel(dialogs, source, preferencesPath);
        await viewModel.OpenPathAsync(files.SubtitlePath);
        SubtitleProject original = Assert.IsType<SubtitleProject>(viewModel.CurrentProject);
        dialogs.UnsavedChoices.Enqueue(UnsavedChangesChoice.Save);
        dialogs.SaveProjectPaths.Enqueue(savePickerReturnsPath
            ? Path.Combine(files.Root, "missing", "project.yttproj")
            : null);

        bool opened = await viewModel.OpenPathAsync(files.ReplacementSubtitlePath);

        Assert.False(opened);
        Assert.Same(original, viewModel.CurrentProject);
        Assert.True(viewModel.IsDirty);
        Assert.Equal(1, dialogs.UnsavedChoiceCalls);
    }

    [AvaloniaFact]
    public async Task OpenCommandsUseCommonPathConfirmationAndDropStopsAfterCancel()
    {
        using TestFiles files = new();
        TestFileDialogService dialogs = new() { OpenSubtitlePath = files.SubtitlePath };
        FakeVideoSource source = new(new(1280, 720, TimeSpan.FromSeconds(5), 30));
        string preferencesPath = Path.Combine(files.Root, "preferences.json");

        using MainWindowViewModel viewModel = CreateViewModel(dialogs, source, preferencesPath);
        await viewModel.OpenSubtitleCommand.ExecuteAsync();
        SubtitleProject original = Assert.IsType<SubtitleProject>(viewModel.CurrentProject);

        dialogs.OpenVideoPath = files.VideoPath;
        dialogs.UnsavedChoices.Enqueue(UnsavedChangesChoice.Cancel);
        await viewModel.OpenVideoCommand.ExecuteAsync();
        Assert.Same(original, viewModel.CurrentProject);
        Assert.Empty(source.LoadedPaths);

        dialogs.UnsavedChoices.Enqueue(UnsavedChangesChoice.Discard);
        await viewModel.OpenDroppedPathsAsync([files.VideoPath, files.ReplacementVideoPath]);

        Assert.Single(source.LoadedPaths);
        Assert.Equal(Path.GetFullPath(files.VideoPath), source.LoadedPaths[0]);
    }

    private static MainWindowViewModel CreateViewModel(
        TestFileDialogService dialogs,
        FakeVideoSource source,
        string preferencesPath)
        => new(dialogs, new PreferencesStore(preferencesPath), () => source);

    private sealed class FakeVideoSource(SourceVideoInfo info) : IVideoSource
    {
        public SourceVideoInfo Info { get; private set; } = info;
        public TimeSpan Position { get; private set; }
        public bool IsPlaying { get; private set; }
        public int PlaybackScaleDivisor { get; set; } = 1;
        public event Action? FrameReady;
        public event Action<Exception>? RenderFailed
        {
            add { }
            remove { }
        }

        public List<string> LoadedPaths { get; } = [];

        public Task LoadAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadedPaths.Add(Path.GetFullPath(path));
            Position = TimeSpan.Zero;
            IsPlaying = false;
            FrameReady?.Invoke();
            return Task.CompletedTask;
        }

        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public Task SeekAsync(TimeSpan position, bool exact = true, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Position = position;
            return Task.CompletedTask;
        }

        public void StepFrame(int delta)
        {
        }

        public void SetSpeed(double speed)
        {
        }

        public void SetVolume(double volume)
        {
        }

        public void SetMuted(bool muted)
        {
        }

        public bool TryLockLatestFrame(out VideoFrameLock frame)
        {
            frame = default;
            return false;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestFileDialogService : IFileDialogService
    {
        public string? OpenSubtitlePath { get; set; }
        public string? OpenVideoPath { get; set; }
        public string? OpenProjectPath { get; set; }
        public string? RelinkVideoPath { get; set; }
        public Queue<UnsavedChangesChoice> UnsavedChoices { get; } = [];
        public Queue<bool> ConfirmResults { get; } = [];
        public Queue<string?> SaveProjectPaths { get; } = [];
        public int UnsavedChoiceCalls { get; private set; }

        public Task<string?> OpenSubtitleAsync() => Task.FromResult(OpenSubtitlePath);
        public Task<string?> OpenVideoAsync() => Task.FromResult(OpenVideoPath);
        public Task<string?> SaveYttAsync(string? suggestedName) => Task.FromResult<string?>(null);

        public Task<UnsavedChangesChoice> ConfirmUnsavedChangesAsync(
            string title,
            string message,
            string saveLabel = "저장",
            string discardLabel = "버리기",
            string cancelLabel = "취소")
        {
            UnsavedChoiceCalls++;
            return Task.FromResult(UnsavedChoices.Count > 0
                ? UnsavedChoices.Dequeue()
                : UnsavedChangesChoice.Cancel);
        }

        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "삭제")
            => Task.FromResult(ConfirmResults.Count > 0 && ConfirmResults.Dequeue());

        public Task<string?> OpenProjectAsync() => Task.FromResult(OpenProjectPath);

        public Task<string?> SaveProjectAsync(string? suggestedName)
            => Task.FromResult(SaveProjectPaths.Count > 0 ? SaveProjectPaths.Dequeue() : null);

        public Task<string?> OpenMpvLibraryAsync() => Task.FromResult<string?>(null);
        public Task<string?> RelinkVideoAsync(string missingPath) => Task.FromResult(RelinkVideoPath);
    }

    private sealed class TestFiles : IDisposable
    {
        public TestFiles()
        {
            Root = Path.Combine(Path.GetTempPath(), $"YttStudio-hardening-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            SubtitlePath = CreateSubtitle("initial.ass", "initial");
            ReplacementSubtitlePath = CreateSubtitle("replacement.ass", "replacement");
            VideoPath = CreateVideo("initial.mp4");
            ReplacementVideoPath = CreateVideo("replacement.mp4");
            ProjectPath = Path.Combine(Root, "saved.yttproj");
        }

        public string Root { get; }
        public string SubtitlePath { get; }
        public string ReplacementSubtitlePath { get; }
        public string VideoPath { get; }
        public string ReplacementVideoPath { get; }
        public string ProjectPath { get; }

        private string CreateSubtitle(string name, string text)
        {
            string path = Path.Combine(Root, name);
            string content = string.Join("\n", [
                "[Script Info]",
                "PlayResX: 1280",
                "PlayResY: 720",
                "",
                "[V4+ Styles]",
                "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding",
                "Style: Default,Arial,40,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1",
                "",
                "[Events]",
                "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text",
                $"Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,{text}",
            ]);
            File.WriteAllText(path, content,
                new UTF8Encoding(false));
            return path;
        }

        private string CreateVideo(string name)
        {
            string path = Path.Combine(Root, name);
            File.WriteAllBytes(path, []);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
