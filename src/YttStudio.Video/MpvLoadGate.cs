namespace YttStudio.Video;

/// <summary>loadfile 요청과 libmpv 파일 이벤트의 순서를 연결한다.</summary>
internal sealed class MpvLoadGate
{
    private readonly object sync = new();
    private MpvLoadPhase phase;
    private long generation;

    public long BeginLoad()
    {
        lock (sync)
        {
            generation++;
            phase = MpvLoadPhase.AwaitingStart;
            return generation;
        }
    }

    public long Generation
    {
        get
        {
            lock (sync)
            {
                return generation;
            }
        }
    }

    public bool IsLoaded(long requestedGeneration)
    {
        lock (sync)
        {
            return requestedGeneration == generation && phase == MpvLoadPhase.Loaded;
        }
    }

    public bool Observe(MpvEventId eventId)
    {
        lock (sync)
        {
            switch (phase)
            {
                case MpvLoadPhase.AwaitingStart when eventId == MpvEventId.StartFile:
                    phase = MpvLoadPhase.AwaitingFileLoaded;
                    break;
                case MpvLoadPhase.AwaitingFileLoaded when eventId == MpvEventId.FileLoaded:
                    phase = MpvLoadPhase.Loaded;
                    return true;
                case MpvLoadPhase.AwaitingFileLoaded when eventId == MpvEventId.EndFile:
                    phase = MpvLoadPhase.Ended;
                    break;
                case MpvLoadPhase.Ended when eventId == MpvEventId.StartFile:
                    phase = MpvLoadPhase.AwaitingFileLoaded;
                    break;
            }

            return false;
        }
    }

    private enum MpvLoadPhase
    {
        Inactive,
        AwaitingStart,
        AwaitingFileLoaded,
        Loaded,
        Ended,
    }
}
