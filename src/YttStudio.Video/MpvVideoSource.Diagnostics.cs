using System.Diagnostics;

namespace YttStudio.Video;

/// <summary>렌더 경로가 한 일을 세어 둔다.</summary>
/// <remarks>
/// 스크럽이 무겁다는 보고를 재려고 넣었다. 세는 비용은 프레임당 Interlocked 몇 번이라
/// 재생에 영향을 주지 않는다. 값은 소스가 만들어진 뒤로 계속 쌓이므로 구간의 비용은
/// 앞뒤에서 한 번씩 읽어 빼서 구한다.
/// </remarks>
public sealed partial class MpvVideoSource
{
    private long seekCount;
    private long renderedFrameCount;
    private long skippedFrameCount;
    private long renderTicks;
    private long alphaTicks;
    private long pixelBytes;

    /// <summary>렌더 경로가 지금까지 한 일을 읽는다.</summary>
    /// <remarks>
    /// 구간의 비용을 알고 싶으면 앞뒤에서 한 번씩 읽어 <see cref="VideoRenderDiagnostics.Since"/>
    /// 로 빼라. 각 값은 원자적으로 읽지만 서로 같은 순간의 값은 아니다. 부하의 크기를 가늠하는
    /// 용도이지 회계 장부가 아니다.
    /// </remarks>
    public VideoRenderDiagnostics ReadDiagnostics()
        => new(
            Interlocked.Read(ref seekCount),
            Interlocked.Read(ref renderedFrameCount),
            Interlocked.Read(ref skippedFrameCount),
            TicksToMilliseconds(Interlocked.Read(ref renderTicks)),
            TicksToMilliseconds(Interlocked.Read(ref alphaTicks)),
            Interlocked.Read(ref pixelBytes));

    private static double TicksToMilliseconds(long ticks)
        => ticks * 1000.0 / Stopwatch.Frequency;

    private void RaiseRenderFailed(Exception exception)
    {
        Action<Exception>? handlers = RenderFailed;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<Exception> handler in handlers.GetInvocationList().Cast<Action<Exception>>())
        {
            try
            {
                handler(exception);
            }
            catch
            {
                // 구독자가 전용 네이티브 렌더 스레드를 끝내면 안 된다.
            }
        }
    }
}
