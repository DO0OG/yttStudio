namespace YttStudio.Video;

/// <summary>영상 렌더 경로가 지금까지 한 일의 누적 스냅샷이다.</summary>
/// <remarks>
/// 값은 소스가 만들어진 뒤로 계속 쌓인다. 특정 구간의 비용을 알고 싶으면 구간의 앞뒤에서
/// 한 번씩 읽어 빼면 된다. 스크럽처럼 짧고 격한 구간의 부하를 재려고 둔 것이다.
/// </remarks>
/// <param name="Seeks">탐색 명령을 mpv 에 보낸 횟수다.</param>
/// <param name="RenderedFrames">화소 버퍼에 실제로 그려 넣은 프레임 수다.</param>
/// <param name="SkippedFrames">버퍼가 모두 잠겨 있어 mpv 에 건너뛰라고 알린 횟수다.</param>
/// <param name="RenderMilliseconds">mpv 소프트웨어 렌더 호출에 쓴 시간이다.</param>
/// <param name="AlphaMilliseconds">알파 채우기에 쓴 시간이다.</param>
/// <param name="PixelBytes">화소 버퍼에 쓴 총 바이트 수다.</param>
public readonly record struct VideoRenderDiagnostics(
    long Seeks,
    long RenderedFrames,
    long SkippedFrames,
    double RenderMilliseconds,
    double AlphaMilliseconds,
    long PixelBytes)
{
    /// <summary>두 스냅샷 사이에 일어난 일만 남긴다.</summary>
    public VideoRenderDiagnostics Since(VideoRenderDiagnostics earlier)
        => new(
            Seeks - earlier.Seeks,
            RenderedFrames - earlier.RenderedFrames,
            SkippedFrames - earlier.SkippedFrames,
            RenderMilliseconds - earlier.RenderMilliseconds,
            AlphaMilliseconds - earlier.AlphaMilliseconds,
            PixelBytes - earlier.PixelBytes);
}
