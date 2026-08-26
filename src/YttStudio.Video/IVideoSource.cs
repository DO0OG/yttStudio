namespace YttStudio.Video;

/// <summary>재생 상태와 최신 프레임 접근을 제공한다.</summary>
public interface IVideoSource : IAsyncDisposable
{
    /// <summary>불러온 영상의 메타데이터를 가져온다.</summary>
    VideoInfo Info { get; }

    /// <summary>현재 재생 위치를 가져온다.</summary>
    TimeSpan Position { get; }

    /// <summary>재생 중인지 가져온다.</summary>
    bool IsPlaying { get; }

    /// <summary>영상 소스를 불러온다.</summary>
    Task LoadAsync(string path, CancellationToken cancellationToken);

    /// <summary>재생을 시작한다.</summary>
    void Play();

    /// <summary>재생을 일시정지한다.</summary>
    void Pause();

    /// <summary>재생 위치로 이동한다.</summary>
    Task SeekAsync(TimeSpan position, bool exact = true, CancellationToken cancellationToken = default);

    /// <summary>부호 있는 프레임 수만큼 이동한다.</summary>
    void StepFrame(int delta);

    /// <summary>재생 속도를 설정한다.</summary>
    void SetSpeed(double speed);

    /// <summary>재생 볼륨을 0부터 100 사이로 설정한다.</summary>
    void SetVolume(double volume);

    /// <summary>음소거 상태를 설정한다.</summary>
    void SetMuted(bool muted);

    /// <summary>더 새로운 프레임을 잠글 수 있음을 알린다.</summary>
    event Action FrameReady;

    /// <summary>호출자의 현재 범위 동안 최신 프레임을 잠근다.</summary>
    bool TryLockLatestFrame(out VideoFrameLock frame);
}

/// <summary>불러온 영상의 불변 메타데이터를 담는다.</summary>
public sealed record VideoInfo(int Width, int Height, TimeSpan Duration, double NominalFps);
