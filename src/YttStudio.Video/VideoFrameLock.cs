namespace YttStudio.Video;

/// <summary>Provides scoped read access to a latest-frame buffer.</summary>
/// <remarks>SPEC §8.3: this stack-only value is used only by TryLockLatestFrame.</remarks>
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

    /// <summary>Gets BGRA8888 premultiplied pixel bytes.</summary>
    public ReadOnlySpan<byte> Pixels { get; }

    /// <summary>Gets the frame width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the frame height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the row stride in bytes.</summary>
    public int Stride { get; }

    /// <summary>Gets the source timestamp.</summary>
    public TimeSpan Timestamp { get; }

    /// <summary>Gets the monotonically increasing frame sequence.</summary>
    public long SequenceNumber { get; }

    /// <summary>Releases the read lock.</summary>
    public void Dispose()
    {
        // M2 (SPEC §8.3-8.4) supplies the double-buffer release callback.
        release?.Invoke();
    }
}
