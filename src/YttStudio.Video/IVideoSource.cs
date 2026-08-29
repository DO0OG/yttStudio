namespace YttStudio.Video;

/// <summary>재생 상태와 최신 프레임 접근을 제공한다.</summary>
public interface IVideoSource : IAsyncDisposable
{
    /// <summary>초기화 이후 렌더가 실패로 끝났음을 알린다.</summary>
    /// <remarks>이 신호 뒤로는 프레임이 오지 않는다.</remarks>
    event Action<Exception>? RenderFailed;

    /// <summary>불러온 영상의 메타데이터를 가져온다.</summary>
    VideoInfo Info { get; }

    /// <summary>현재 재생 위치를 가져온다.</summary>
    TimeSpan Position { get; }

    /// <summary>재생 중인지 가져온다.</summary>
    bool IsPlaying { get; }

    /// <summary>
    /// 프레임을 받아올 해상도의 축소 배수를 가져오거나 설정한다. 1 이면 원본이고
    /// 2 면 가로 세로 절반이다.
    /// </summary>
    /// <remarks>
    /// 편집 중에는 원본 해상도가 필요 없을 때가 많다. 배수를 올리면 디코딩 뒤의 변환 ·
    /// 전송 · 합성이 모두 그 제곱만큼 줄어 재생 부하가 크게 내려간다. 화면에 보이는
    /// 크기는 그대로이고 선명도만 낮아진다.
    /// </remarks>
    int PlaybackScaleDivisor { get; set; }

    /// <summary>로컬 영상 또는 검증된 YouTube 영상 소스를 불러온다.</summary>
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
