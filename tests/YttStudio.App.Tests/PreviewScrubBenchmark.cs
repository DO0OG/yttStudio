using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using YttStudio.App;

namespace YttStudio.App.Tests;

/// <summary>같은 수의 스크럽 입력을 즉시 처리할 때와 한 렌더 틱으로 묶을 때를 비교한다.</summary>
public sealed class PreviewScrubBenchmark(ITestOutputHelper output)
{
    private const int Inputs = 40;

    [AvaloniaFact]
    public async Task ComparesImmediateAndCoalescedScrubRendering()
    {
        using MainWindowViewModel viewModel = new(new StubFileDialogService());
        Guid cueId = viewModel.AddCueAtCanvasPoint(50, 50)!.Value;
        viewModel.BeginInlineEdit(cueId, 0, 0, 180);
        viewModel.InlineText = "스크럽 프리뷰 측정";
        viewModel.CommitInlineEdit();
        await FlushRenderQueueAsync();

        Measurement immediate = await MeasureAsync(viewModel, flushEachInput: true, 100);
        Measurement coalesced = await MeasureAsync(viewModel, flushEachInput: false, 200);
        string[] report =
        [
            $"스크럽 입력 수: {Inputs}",
            $"변경 전  렌더 {immediate.Renders,3}회  {immediate.Milliseconds,8:F1} ms",
            $"변경 후  렌더 {coalesced.Renders,3}회  {coalesced.Milliseconds,8:F1} ms",
        ];
        foreach (string line in report)
        {
            output.WriteLine(line);
        }

        string? reportPath = Environment.GetEnvironmentVariable("YTT_BENCHMARK_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            File.AppendAllLines(reportPath, [string.Empty, .. report]);
        }

        Assert.Equal(Inputs, immediate.Renders);
        Assert.Equal(1, coalesced.Renders);
    }

    private static async Task<Measurement> MeasureAsync(
        MainWindowViewModel viewModel,
        bool flushEachInput,
        int startMilliseconds)
    {
        long before = viewModel.PreviewRenderCount;
        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int input = 0; input < Inputs; input++)
        {
            viewModel.PositionMilliseconds = startMilliseconds + (input * 34);
            if (flushEachInput)
            {
                await FlushRenderQueueAsync();
            }
        }

        await FlushRenderQueueAsync();
        stopwatch.Stop();
        return new Measurement(stopwatch.Elapsed.TotalMilliseconds, viewModel.PreviewRenderCount - before);
    }

    private static async Task FlushRenderQueueAsync()
        => await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

    private sealed record Measurement(double Milliseconds, long Renders);

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
