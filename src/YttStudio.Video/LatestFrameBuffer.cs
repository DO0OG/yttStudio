namespace YttStudio.Video;

/// <summary>재사용하는 최신 프레임 버퍼 두 개를 소유한다.</summary>
internal sealed class LatestFrameBuffer
{
    private readonly object gate = new();
    private readonly Slot[] slots;
    private int frontIndex = -1;
    private int writingIndex = -1;
    private long seekEpoch;
    private long lastSequence = -1;
    private bool disposed;

    public LatestFrameBuffer()
    {
        slots = [new Slot(this, 0), new Slot(this, 1)];
    }

    public long SeekEpoch => Interlocked.Read(ref seekEpoch);

    public long BeginSeek()
    {
        lock (gate)
        {
            long next = ++seekEpoch;
            frontIndex = -1;
            foreach (Slot slot in slots)
            {
                slot.Valid = false;
            }

            return next;
        }
    }

    /// <summary>한 변의 최대 화소 수다. 8K 를 크게 웃도는 값이라 실사용을 막지 않는다.</summary>
    private const int MaximumDimension = 16384;

    /// <summary>프레임 하나가 쓸 수 있는 최대 바이트다. 16384 x 8192 BGRA 를 담는다.</summary>
    private const long MaximumFrameBytes = 512L * 1024 * 1024;

    /// <summary>네이티브에 넘기기 전에 프레임 크기가 현실적인지 본다.</summary>
    /// <remarks>
    /// 폭과 높이는 libmpv 가 읽은 미디어 메타데이터에서 온다. 손상된 파일이나 오작동하는
    /// 디코더가 터무니없는 값을 주면 stride 계산이 int 를 넘겨 음수나 작은 값이 되고, 그
    /// 크기로 잡은 버퍼의 포인터를 원래 크기로 알고 있는 네이티브 렌더에 넘기게 된다.
    /// 곱하기 전에 막는다.
    /// </remarks>
    private static void ValidateFrameSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (width > MaximumDimension || height > MaximumDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(width),
                $"Frame size {width}x{height} exceeds the supported maximum of {MaximumDimension}.");
        }

        long requiredBytes = Align(width * 4L, 64) * height;
        if (requiredBytes > MaximumFrameBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(width),
                $"Frame size {width}x{height} needs {requiredBytes} bytes, over the {MaximumFrameBytes} limit.");
        }
    }

    public bool TryBeginWrite(int width, int height, out int index, out byte[] pixels, out int stride)
    {
        ValidateFrameSize(width, height);

        lock (gate)
        {
            if (disposed || writingIndex >= 0)
            {
                pixels = [];
                stride = 0;
                index = -1;
                return false;
            }

            index = FindWritableSlot();

            if (index < 0)
            {
                pixels = [];
                stride = 0;
                return false;
            }

            Slot slot = slots[index];

            stride = (int)Align(width * 4L, 64);
            int requiredLength = checked(stride * height);
            if (slot.Pixels.Length != requiredLength)
            {
                // [API] 프레임 저장소는 재사용하고 크기가 바뀔 때만 다시 할당한다.
                slot.Pixels = GC.AllocateUninitializedArray<byte>(requiredLength, pinned: true);
            }

            slot.Width = width;
            slot.Height = height;
            slot.Stride = stride;
            writingIndex = index;
            pixels = slot.Pixels;
            return true;
        }
    }

    public bool Publish(int index, TimeSpan timestamp, long sequenceNumber, long epoch)
    {
        lock (gate)
        {
            if (index != writingIndex)
            {
                return false;
            }

            writingIndex = -1;
            if (disposed || epoch != seekEpoch || sequenceNumber <= lastSequence)
            {
                return false;
            }

            Slot slot = slots[index];
            slot.Timestamp = timestamp;
            slot.SequenceNumber = sequenceNumber;
            slot.Valid = true;
            lastSequence = sequenceNumber;
            frontIndex = index;
            return true;
        }
    }

    public void CancelWrite(int index)
    {
        lock (gate)
        {
            if (writingIndex == index)
            {
                writingIndex = -1;
            }
        }
    }

    public bool TryLockLatestFrame(out VideoFrameLock frame)
    {
        lock (gate)
        {
            if (disposed || frontIndex < 0 || !slots[frontIndex].Valid)
            {
                frame = default;
                return false;
            }

            Slot slot = slots[frontIndex];
            slot.Readers++;
            frame = new VideoFrameLock(
                slot.Pixels.AsSpan(0, checked(slot.Stride * slot.Height)),
                slot.Width,
                slot.Height,
                slot.Stride,
                slot.Timestamp,
                slot.SequenceNumber,
                slot.ReleaseAction);
            return true;
        }
    }

    public bool WriteForTest(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        TimeSpan timestamp,
        long sequenceNumber,
        long epoch)
    {
        if (!TryBeginWrite(width, height, out int index, out byte[] destination, out int stride))
        {
            return false;
        }

        try
        {
            int sourceStride = checked(width * 4);
            for (int row = 0; row < height; row++)
            {
                pixels.Slice(row * sourceStride, sourceStride)
                    .CopyTo(destination.AsSpan(row * stride, sourceStride));
            }

            return Publish(index, timestamp, sequenceNumber, epoch);
        }
        catch
        {
            CancelWrite(index);
            throw;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            frontIndex = -1;
            writingIndex = -1;
            foreach (Slot slot in slots)
            {
                slot.Valid = false;
            }
        }
    }

    private int FindWritableSlot()
    {
        for (int candidate = 0; candidate < slots.Length; candidate++)
        {
            if (candidate != frontIndex && slots[candidate].Readers == 0)
            {
                return candidate;
            }
        }

        return -1;
    }

    private void Release(int index)
    {
        lock (gate)
        {
            if (slots[index].Readers > 0)
            {
                slots[index].Readers--;
            }
        }
    }

    private static long Align(long value, long alignment)
        => ((value + alignment - 1) / alignment) * alignment;

    private sealed class Slot
    {
        public Slot(LatestFrameBuffer owner, int index)
        {
            ReleaseAction = () => owner.Release(index);
        }

        public byte[] Pixels { get; set; } = [];
        public int Width { get; set; }
        public int Height { get; set; }
        public int Stride { get; set; }
        public TimeSpan Timestamp { get; set; }
        public long SequenceNumber { get; set; }
        public int Readers { get; set; }
        public bool Valid { get; set; }
        public Action ReleaseAction { get; }
    }
}
