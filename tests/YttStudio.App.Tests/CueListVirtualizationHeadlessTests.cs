using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using YttStudio.App;
using YttStudio.Core;

namespace YttStudio.App.Tests;

public sealed class CueListVirtualizationHeadlessTests
{
    private readonly ITestOutputHelper testOutput;

    public CueListVirtualizationHeadlessTests(ITestOutputHelper testOutput)
    {
        this.testOutput = testOutput;
    }

    [AvaloniaFact]
    public async Task CueListWith3000ItemsRealizesOnlyViewportContainers()
    {
        using CueListTestDocument document = new();
        using MainWindowViewModel viewModel = new(
            new StubFileDialogService(),
            new PreferencesStore(Path.Combine(document.Root, "preferences.json")));

        Assert.True(await viewModel.OpenPathAsync(document.SubtitlePath));
        SubtitleProject project = Assert.IsType<SubtitleProject>(viewModel.CurrentProject);
        Assert.Equal(CueListTestDocument.CueCount, project.Cues.Count);
        Assert.Equal(CueListTestDocument.CueCount, viewModel.CueRows.Count);

        MainWindow window = new()
        {
            Width = 1280,
            Height = 900,
            DataContext = viewModel,
        };
        try
        {
            ListBox cueList = GetCueList(window);
            Assert.Same(viewModel.CueRows, cueList.ItemsSource);
            Assert.NotNull(cueList.ItemTemplate);

            window.Show();
            UpdateLayout(window);

            Assert.Equal(CueListTestDocument.CueCount, cueList.ItemCount);
            Assert.True(cueList.Bounds.Height > 0);
            VirtualizingStackPanel panel = Assert.IsType<VirtualizingStackPanel>(cueList.ItemsPanelRoot);
            Assert.NotNull(panel);

            ScrollViewer scrollViewer = Assert.IsType<ScrollViewer>(
                cueList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault());
            Assert.True(double.IsFinite(scrollViewer.Viewport.Width));
            Assert.True(double.IsFinite(scrollViewer.Viewport.Height));
            Assert.True(double.IsFinite(scrollViewer.Extent.Width));
            Assert.True(double.IsFinite(scrollViewer.Extent.Height));
            Assert.True(scrollViewer.Viewport.Width > 0);
            Assert.True(scrollViewer.Viewport.Height > 0);
            Assert.True(scrollViewer.Extent.Width >= scrollViewer.Viewport.Width);
            Assert.True(scrollViewer.Extent.Height > scrollViewer.Viewport.Height);

            int realized = CountRealized(cueList);
            Assert.InRange(realized, 1, 200);

            ListBoxItem firstContainer = Assert.IsType<ListBoxItem>(cueList.ContainerFromIndex(0));
            TextBox multilineEditor = Assert.Single(
                firstContainer.GetVisualDescendants().OfType<TextBox>(),
                textBox => textBox.AcceptsReturn);
            Assert.Equal(1, multilineEditor.MinLines);
            Assert.Equal(viewModel.MaxSubtitleLines, multilineEditor.MaxLines);

            long before = GC.GetAllocatedBytesForCurrentThread();
            Stopwatch timer = Stopwatch.StartNew();
            foreach (int target in new[] { 0, 1500, 2999 })
            {
                cueList.ScrollIntoView(target);
                UpdateLayout(window);

                ListBoxItem container = Assert.IsType<ListBoxItem>(cueList.ContainerFromIndex(target));
                Assert.Same(viewModel.CueRows[target], container.Content);
                int realizedAfterScroll = CountRealized(cueList);
                Assert.InRange(realizedAfterScroll, 1, 200);
            }

            timer.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            string metrics =
                $"CueList 3000 items: realized={realized}, top/mid/bottom elapsedMs={timer.Elapsed.TotalMilliseconds:F2}, allocatedBytes={allocated}";
            Console.WriteLine(metrics);
            testOutput.WriteLine(metrics);
        }
        finally
        {
            window.Close();
        }
    }

    private static ListBox GetCueList(MainWindow window)
        => Assert.IsType<ListBox>(typeof(MainWindow)
            .GetField("CueList", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window));

    private static void UpdateLayout(MainWindow window)
    {
        window.ApplyTemplate();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static int CountRealized(ListBox cueList)
        => Enumerable.Range(0, CueListTestDocument.CueCount)
            .Count(index => cueList.ContainerFromIndex(index) is not null);

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

    private sealed class CueListTestDocument : IDisposable
    {
        public const int CueCount = 3000;

        public CueListTestDocument()
        {
            Root = Path.Combine(Path.GetTempPath(), $"YttStudio-cue-list-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            SubtitlePath = Path.Combine(Root, "cues.ass");
            File.WriteAllText(SubtitlePath, CreateSubtitle(), new UTF8Encoding(false));
        }

        public string Root { get; }

        public string SubtitlePath { get; }

        private static string CreateSubtitle()
        {
            StringBuilder content = new();
            content.AppendLine("[Script Info]");
            content.AppendLine("PlayResX: 1280");
            content.AppendLine("PlayResY: 720");
            content.AppendLine();
            content.AppendLine("[V4+ Styles]");
            content.AppendLine(
                "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
            content.AppendLine(
                "Style: Default,Arial,40,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1");
            content.AppendLine();
            content.AppendLine("[Events]");
            content.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
            for (int index = 0; index < CueCount; index++)
            {
                TimeSpan start = TimeSpan.FromSeconds(1 + (index * 2));
                TimeSpan end = start + TimeSpan.FromSeconds(1);
                string text = index % 7 == 0
                    ? $"cue {index}\\Nline {index}\\Nthird line {index}"
                    : $"cue {index}";
                content.Append("Dialogue: 0,")
                    .Append(FormatAssTime(start)).Append(',')
                    .Append(FormatAssTime(end))
                    .Append(",Default,,0,0,0,,")
                    .AppendLine(text);
            }

            return content.ToString();
        }

        private static string FormatAssTime(TimeSpan time)
            => $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds / 10:00}";

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
