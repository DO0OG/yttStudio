using System.Diagnostics;
using SkiaSharp;

namespace YttStudio.Render.Tests;

/// <summary>
/// 프리뷰 렌더 경로의 변경 전후를 같은 조건에서 잰다.
/// </summary>
/// <remarks>
/// 재생 중에는 이 경로가 영상 프레임마다 돈다. 기존 방식은 프레임마다 비트맵을 새로
/// 만들고 PNG 로 압축했다가 되읽었으며, 렌더와 측정을 따로 불러 레이아웃을 두 번
/// 계산했다. 바꾼 방식은 화소 버퍼를 재사용하고 레이아웃을 한 번만 계산한다.
/// 수치는 기기와 부하에 따라 달라지므로 단정값을 검사하지 않고 출력만 남긴다.
/// </remarks>
public sealed class PreviewRenderBenchmark(ITestOutputHelper output)
{
    private const int Width = 1280;
    private const int Height = 720;
    private const int Frames = 120;

    [Fact]
    public void ComparesPreviewRenderPaths()
    {
        (YttStudio.Core.SubtitleProject project, _) = LayoutTests.CreateProject(
            YttStudio.Core.AnchorPoint.MiddleCenter,
            YttStudio.Core.Justification.Center,
            "재생 중 프리뷰 렌더 비용 측정");
        using BundledFontResolver fonts = new();
        using SkiaSubtitleRenderer renderer = new(fonts);
        PlayerViewport viewport = PlayerViewport.VideoFrame(Width, Height);

        // 첫 호출의 폰트 해석과 JIT 를 측정에서 뺀다.
        RunLegacyPath(renderer, viewport, project, 2);
        RunCurrentPath(renderer, viewport, project, 2, cacheLayouts: false);
        RunCurrentPath(renderer, viewport, project, 2, cacheLayouts: true);

        Measurement legacy = Measure(() => RunLegacyPath(renderer, viewport, project, Frames));
        Measurement uncached = Measure(() => RunCurrentPath(
            renderer, viewport, project, Frames, cacheLayouts: false));
        Measurement current = Measure(() => RunCurrentPath(
            renderer, viewport, project, Frames, cacheLayouts: true));

        string[] report =
        [
            $"프레임 수: {Frames} ({Width}x{Height})",
            $"변경 전  {legacy}",
            $"레이아웃 캐시 전  {uncached}",
            $"레이아웃 캐시 후  {current}",
            $"시간 {legacy.Milliseconds / Math.Max(current.Milliseconds, 0.001):F1} 배, " +
                $"할당 {legacy.AllocatedBytes / (double)Math.Max(current.AllocatedBytes, 1):F1} 배 줄었다.",
            $"레이아웃 캐시로 시간 {uncached.Milliseconds / Math.Max(current.Milliseconds, 0.001):F1} 배, " +
                $"할당 {uncached.AllocatedBytes / (double)Math.Max(current.AllocatedBytes, 1):F1} 배 줄었다.",
        ];
        foreach (string line in report)
        {
            output.WriteLine(line);
        }

        // 테스트 러너가 출력을 숨기는 환경이 있어 파일로도 남긴다.
        string? reportPath = Environment.GetEnvironmentVariable("YTT_BENCHMARK_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            File.WriteAllLines(reportPath, report);
        }

        Assert.True(current.Milliseconds > 0);
    }

    /// <summary>비트맵을 새로 만들고 PNG 로 왕복하며 레이아웃을 두 번 계산하던 경로다.</summary>
    private static void RunLegacyPath(SkiaSubtitleRenderer renderer, PlayerViewport viewport,
        YttStudio.Core.SubtitleProject project, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            TimeSpan time = GetFrameTime(frame);
            using SKBitmap bitmap = new(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using SKCanvas canvas = new(bitmap);
            canvas.Clear(SKColors.Transparent);
            renderer.Render(canvas, viewport, project, time, new RenderOptions { FrameIndex = frame });

            // 화면에 올리기 전 PNG 로 압축했다가 곧바로 되읽던 부분이다.
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            byte[] encoded = data.ToArray();
            using SKBitmap decoded = SKBitmap.Decode(encoded);

            _ = renderer.Measure(viewport, project, time);
        }
    }

    /// <summary>화소 버퍼를 재사용하고 레이아웃을 한 번만 계산하는 경로다.</summary>
    private static void RunCurrentPath(SkiaSubtitleRenderer renderer, PlayerViewport viewport,
        YttStudio.Core.SubtitleProject project, int frames, bool cacheLayouts)
    {
        SKImageInfo info = new(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKBitmap reused = new(info);
        for (int frame = 0; frame < frames; frame++)
        {
            TimeSpan time = GetFrameTime(frame);
            using SKSurface surface = SKSurface.Create(info, reused.GetPixels(), reused.RowBytes);
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            _ = renderer.RenderAndMeasure(canvas, viewport, project, time,
                new RenderOptions
                {
                    DocumentRevision = cacheLayouts ? 0 : null,
                    FrameIndex = frame,
                });
        }
    }

    private static TimeSpan GetFrameTime(int frame) => TimeSpan.FromSeconds(1 + (frame / 30.0));

    private static Measurement Measure(Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        int gen0Before = GC.CollectionCount(0);
        Stopwatch stopwatch = Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        return new Measurement(
            stopwatch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            GC.CollectionCount(0) - gen0Before);
    }

    private sealed record Measurement(double Milliseconds, long AllocatedBytes, int Gen0Collections)
    {
        public override string ToString()
            => $"{Milliseconds,8:F1} ms   할당 {AllocatedBytes / 1024.0 / 1024.0,7:F1} MB   " +
                $"gen0 수거 {Gen0Collections,3}";
    }
}
