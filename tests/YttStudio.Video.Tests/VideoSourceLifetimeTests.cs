using YttStudio.Video;

namespace YttStudio.Video.Tests;

public sealed class VideoSourceLifetimeTests
{
    [Fact]
    public void DoubleBufferAllowsNewFrontWhilePreviousFrameIsLocked()
    {
        FakeVideoSource source = new();
        Assert.True(source.Publish(1, 1));
        Assert.True(source.TryLockLatestFrame(out VideoFrameLock first));

        Assert.True(source.Publish(2, 2));
        Assert.True(source.TryLockLatestFrame(out VideoFrameLock second));
        Assert.Equal(2, second.SequenceNumber);
        second.Dispose();

        Assert.False(source.Publish(3, 3));
        first.Dispose();
        Assert.True(source.Publish(3, 3));
    }

    [Fact]
    public void SeekInvalidatesOldFrontAndRejectsOldEpoch()
    {
        FakeVideoSource source = new();
        long oldEpoch = source.Epoch;
        Assert.True(source.Publish(1, 1, oldEpoch));
        Assert.True(source.TryLockLatestFrame(out VideoFrameLock oldFrame));

        source.Seek();

        Assert.False(source.TryLockLatestFrame(out _));
        Assert.False(source.Publish(2, 2, oldEpoch));
        Assert.True(source.Publish(3, 3, source.Epoch));
        oldFrame.Dispose();
    }

    [Fact]
    public void RegressingSequenceIsIgnored()
    {
        FakeVideoSource source = new();
        Assert.True(source.Publish(10, 10));
        Assert.False(source.Publish(9, 9));

        Assert.True(source.TryLockLatestFrame(out VideoFrameLock frame));
        Assert.Equal(10, frame.SequenceNumber);
        Assert.Equal(TimeSpan.FromMilliseconds(10), frame.Timestamp);
        frame.Dispose();
    }

    [Fact]
    public async Task DisposeMakesFrameLockUnavailable()
    {
        FakeVideoSource source = new();
        Assert.True(source.Publish(1, 1));

        await source.DisposeAsync();

        Assert.False(source.TryLockLatestFrame(out _));
    }

    [Fact]
    public void FrameStorageIsReusedUntilDimensionsChange()
    {
        FakeVideoSource source = new();
        byte[] first = source.BeginWrite(2, 2, out int firstIndex);
        source.CancelWrite(firstIndex);
        byte[] second = source.BeginWrite(2, 2, out int secondIndex);
        source.CancelWrite(secondIndex);
        byte[] resized = source.BeginWrite(3, 3, out int resizedIndex);
        source.CancelWrite(resizedIndex);

        Assert.Same(first, second);
        Assert.NotSame(second, resized);
    }

    private sealed class FakeVideoSource : IVideoSource
    {
        private readonly LatestFrameBuffer buffer = new();
        private bool disposed;

        public VideoInfo Info { get; } = new(2, 2, TimeSpan.FromSeconds(1), 30);
        public TimeSpan Position { get; private set; }
        public bool IsPlaying { get; private set; }
        public int PlaybackScaleDivisor { get; set; } = 1;
        public long Epoch => buffer.SeekEpoch;
        public event Action? FrameReady;

        public Task LoadAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public Task SeekAsync(TimeSpan position, bool exact = true, CancellationToken cancellationToken = default)
        {
            Position = position;
            Seek();
            return Task.CompletedTask;
        }

        public void StepFrame(int delta) { }
        public void SetSpeed(double speed) { }
        public void SetVolume(double volume) { }
        public void SetMuted(bool muted) { }

        public bool TryLockLatestFrame(out VideoFrameLock frame)
        {
            if (disposed)
            {
                frame = default;
                return false;
            }

            return buffer.TryLockLatestFrame(out frame);
        }

        public ValueTask DisposeAsync()
        {
            disposed = true;
            buffer.Dispose();
            return ValueTask.CompletedTask;
        }

        public void Seek() => buffer.BeginSeek();

        public bool Publish(long sequence, byte value, long? epoch = null)
        {
            byte[] pixels = Enumerable.Repeat(value, 16).ToArray();
            bool published = buffer.WriteForTest(
                pixels,
                2,
                2,
                TimeSpan.FromMilliseconds(sequence),
                sequence,
                epoch ?? buffer.SeekEpoch);
            if (published)
            {
                FrameReady?.Invoke();
            }

            return published;
        }

        public byte[] BeginWrite(int width, int height, out int index)
        {
            Assert.True(buffer.TryBeginWrite(width, height, out index, out byte[] pixels, out _));
            return pixels;
        }

        public void CancelWrite(int index) => buffer.CancelWrite(index);
    }
}
