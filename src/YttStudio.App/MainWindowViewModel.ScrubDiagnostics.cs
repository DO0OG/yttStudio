using System.Diagnostics;
using YttStudio.Video;

namespace YttStudio.App;

/// <summary>영상 렌더 경로가 실제로 무엇을 얼마나 하는지 주기적으로 기록한다.</summary>
/// <remarks>
/// 스크럽이 무겁다는 보고는 반복해서 나왔지만 어디서 타는지는 재본 적이 없다. 어느 경로로
/// 위치를 옮기든 프레임은 결국 이리로 오므로, 프레임이 도착하는 동안 일정 간격으로 누적값의
/// 차이를 남긴다. 놀고 있을 때는 프레임이 없으니 로그도 없다.
/// </remarks>
public sealed partial class MainWindowViewModel
{
    private const double DiagnosticsIntervalSeconds = 2.0;

    private VideoRenderDiagnostics lastDiagnostics;
    private long lastDiagnosticsTimestamp;

    private void SampleRenderDiagnostics()
    {
        if (videoSource is null)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (lastDiagnosticsTimestamp == 0)
        {
            lastDiagnostics = videoSource.ReadDiagnostics();
            lastDiagnosticsTimestamp = now;
            return;
        }

        double seconds = (now - lastDiagnosticsTimestamp) / (double)Stopwatch.Frequency;
        if (seconds < DiagnosticsIntervalSeconds)
        {
            return;
        }

        VideoRenderDiagnostics current = videoSource.ReadDiagnostics();
        VideoRenderDiagnostics delta = current.Since(lastDiagnostics);
        lastDiagnostics = current;
        lastDiagnosticsTimestamp = now;
        // 재생만 하는 구간은 남길 것이 없다. 탐색이 있었던 구간, 곧 사용자가 위치를
        // 옮긴 구간만 기록한다. 재생 중 mpv 렌더 시간은 계산이 아니라 다음 프레임
        // 표시 시각까지의 대기라서 그대로 읽으면 부하로 오해하기 쉽다.
        if (delta.Seeks == 0)
        {
            return;
        }

        Serilog.Log.Information(
            "렌더경로 {Seconds:F1}초 · 탐색 {Seeks}({SeekRate:F1}/s) · 렌더 {Frames}({FrameRate:F1}/s) · " +
            "건너뜀 {Skipped} · 탐색당렌더 {PerSeek:F1} · mpv렌더 {RenderMs:F0}ms({RenderShare:F0}%) · " +
            "알파 {AlphaMs:F0}ms({AlphaShare:F0}%) · 전송 {Megabytes:F0}MB({Throughput:F0}MB/s)",
            seconds,
            delta.Seeks,
            delta.Seeks / seconds,
            delta.RenderedFrames,
            delta.RenderedFrames / seconds,
            delta.SkippedFrames,
            delta.Seeks > 0 ? delta.RenderedFrames / (double)delta.Seeks : 0,
            delta.RenderMilliseconds,
            delta.RenderMilliseconds / (seconds * 1000) * 100,
            delta.AlphaMilliseconds,
            delta.AlphaMilliseconds / (seconds * 1000) * 100,
            delta.PixelBytes / 1024.0 / 1024.0,
            delta.PixelBytes / 1024.0 / 1024.0 / seconds);
    }
}
