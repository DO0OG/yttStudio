namespace YttStudio.Video;

/// <summary>Owns the two reusable latest-frame buffers required by SPEC §8.4.</summary>
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

    public bool TryBeginWrite(int width, int height, out int index, out byte[] pixels, out int stride)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        lock (gate)
        {
            if (disposed || writingIndex >= 0)
            {
                pixels = [];
                stride = 0;
                index = -1;
                return false;
            }

            index = -1;
            for (int candidate = 0; candidate < slots.Length; candidate++)
            {
                if (candidate != frontIndex && slots[candidate].Readers == 0)
                {
                    index = candidate;
                    break;
                }
            }

            if (index < 0)
            {
                pixels = [];
                stride = 0;
                return false;
            }

            Slot slot = slots[index];

            stride = Align(width * 4, 64);
            int requiredLength = checked(stride * height);
            if (slot.Pixels.Length != requiredLength)
            {
                // SPEC §8.4 [API]: frame storage is reused and reallocates only on a size change.
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

    private static int Align(int value, int alignment)
        => checked(((value + alignment - 1) / alignment) * alignment);

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
