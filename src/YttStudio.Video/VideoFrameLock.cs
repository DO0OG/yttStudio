namespace YttStudio.Video;

/// <summary>최신 프레임 버퍼에 범위가 한정된 읽기 접근을 제공한다.</summary>
/// <remarks>스택 전용 값이며 TryLockLatestFrame 에서만 쓴다.</remarks>
public readonly ref struct VideoFrameLock : IDisposable
{
    private readonly Action? release;

    internal VideoFrameLock(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int stride,
        TimeSpan timestamp,
        long sequenceNumber,
        Action? release)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        Stride = stride;
        Timestamp = timestamp;
        SequenceNumber = sequenceNumber;
        this.release = release;
    }

    /// <summary>BGRA8888 프리멀티플라이드 픽셀 바이트를 가져온다.</summary>
    public ReadOnlySpan<byte> Pixels { get; }

    /// <summary>프레임 너비를 픽셀로 가져온다.</summary>
    public int Width { get; }

    /// <summary>프레임 높이를 픽셀로 가져온다.</summary>
    public int Height { get; }

    /// <summary>행 스트라이드를 바이트로 가져온다.</summary>
    public int Stride { get; }

    /// <summary>원본 타임스탬프를 가져온다.</summary>
    public TimeSpan Timestamp { get; }

    /// <summary>단조 증가하는 프레임 일련번호를 가져온다.</summary>
    public long SequenceNumber { get; }

    /// <summary>읽기 잠금을 해제한다.</summary>
    public void Dispose()
    {
        // 더블 버퍼 해제 콜백은 영상 파이프라인이 제공한다.
        release?.Invoke();
    }
}
