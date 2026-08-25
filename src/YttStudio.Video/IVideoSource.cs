namespace YttStudio.Video;

/// <summary>Provides playback state and latest-frame access.</summary>
public interface IVideoSource : IAsyncDisposable
{
    /// <summary>Gets metadata for the loaded video.</summary>
    VideoInfo Info { get; }

    /// <summary>Gets the current playback position.</summary>
    TimeSpan Position { get; }

    /// <summary>Gets whether playback is active.</summary>
    bool IsPlaying { get; }

    /// <summary>Loads a video source.</summary>
    Task LoadAsync(string path, CancellationToken cancellationToken);

    /// <summary>Starts playback.</summary>
    void Play();

    /// <summary>Pauses playback.</summary>
    void Pause();

    /// <summary>Seeks to a playback position.</summary>
    Task SeekAsync(TimeSpan position, bool exact = true, CancellationToken cancellationToken = default);

    /// <summary>Steps by a signed number of frames.</summary>
    void StepFrame(int delta);

    /// <summary>Sets playback speed.</summary>
    void SetSpeed(double speed);

    /// <summary>Signals that a newer frame may be locked.</summary>
    event Action FrameReady;

    /// <summary>Locks the latest frame for the caller's current scope.</summary>
    bool TryLockLatestFrame(out VideoFrameLock frame);
}

/// <summary>Contains immutable metadata for a loaded video.</summary>
public sealed record VideoInfo(int Width, int Height, TimeSpan Duration);
