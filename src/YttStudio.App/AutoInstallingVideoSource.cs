using YttStudio.Video;

namespace YttStudio.App;

/// <summary>
/// libmpv가 아직 없는 첫 실행에서 설치를 지연하고, 실제 영상을 열 때 기존 mpv 소스로 전환한다.
/// </summary>
internal sealed class AutoInstallingVideoSource : IVideoSource
{
    private readonly MpvAutoInstaller installer;
    private readonly SemaphoreSlim sourceGate = new(1, 1);
    private MpvVideoSource? inner;
    private int playbackScaleDivisor = 1;
    private double speed = 1;
    private double volume = 100;
    private bool muted;
    private bool disposed;

    public AutoInstallingVideoSource(MpvAutoInstaller installer)
    {
        this.installer = installer;
    }

    public event Action? FrameReady;
    public event Action<Exception>? RenderFailed;

    public VideoInfo Info => inner?.Info ?? new(0, 0, TimeSpan.Zero, 0);
    public TimeSpan Position => inner?.Position ?? TimeSpan.Zero;
    public bool IsPlaying => inner?.IsPlaying == true;

    public int PlaybackScaleDivisor
    {
        get => inner?.PlaybackScaleDivisor ?? playbackScaleDivisor;
        set
        {
            playbackScaleDivisor = Math.Clamp(value, 1, 8);
            if (inner is not null)
            {
                inner.PlaybackScaleDivisor = playbackScaleDivisor;
            }
        }
    }

    public async Task LoadAsync(string path, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        bool isYouTube = YouTubeUrlValidator.TryValidate(path, out _, out _);
        await EnsureInnerAsync(isYouTube, cancellationToken).ConfigureAwait(false);
        await inner!.LoadAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public void Play() => inner?.Play();
    public void Pause() => inner?.Pause();

    public Task SeekAsync(
        TimeSpan position,
        bool exact = true,
        CancellationToken cancellationToken = default)
        => inner?.SeekAsync(position, exact, cancellationToken) ?? Task.CompletedTask;

    public void StepFrame(int delta) => inner?.StepFrame(delta);

    public void SetSpeed(double value)
    {
        speed = value;
        inner?.SetSpeed(value);
    }

    public void SetVolume(double value)
    {
        volume = value;
        inner?.SetVolume(value);
    }

    public void SetMuted(bool value)
    {
        muted = value;
        inner?.SetMuted(value);
    }

    public bool TryLockLatestFrame(out VideoFrameLock frame)
    {
        if (inner is not null)
        {
            return inner.TryLockLatestFrame(out frame);
        }

        frame = default;
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await sourceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeInnerAsync().ConfigureAwait(false);
        }
        finally
        {
            sourceGate.Release();
            sourceGate.Dispose();
        }
    }

    private async Task EnsureInnerAsync(bool isYouTube, CancellationToken cancellationToken)
    {
        string? desiredYtDlpPath = isYouTube ? YtDlpLocator.Find() : inner?.YtDlpPath;
        if (inner is not null && (!isYouTube || PathsEqual(inner.YtDlpPath, desiredYtDlpPath)))
        {
            return;
        }

        await sourceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            desiredYtDlpPath = isYouTube ? YtDlpLocator.Find() : inner?.YtDlpPath;
            if (inner is not null && (!isYouTube || PathsEqual(inner.YtDlpPath, desiredYtDlpPath)))
            {
                return;
            }

            if (inner is not null)
            {
                await DisposeInnerAsync().ConfigureAwait(false);
            }

            string installedPath = MpvAutoInstaller.TryFindInstalledLibrary(out string? existingPath)
                ? existingPath!
                : await installer.InstallAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            Environment.SetEnvironmentVariable("YTTSTUDIO_MPV_PATH", installedPath);

            desiredYtDlpPath = isYouTube ? YtDlpLocator.Find() : null;
            if (!MpvVideoSource.TryCreate(
                    out MpvVideoSource? source,
                    out string diagnostic,
                    desiredYtDlpPath))
            {
                throw new MpvAutoInstallException(
                    MpvAutoInstallErrorKind.InstallationFailed,
                    $"설치한 libmpv를 초기화하지 못했습니다: {diagnostic}");
            }

            inner = source!;
            inner.PlaybackScaleDivisor = playbackScaleDivisor;
            inner.SetSpeed(speed);
            inner.SetVolume(volume);
            inner.SetMuted(muted);
            inner.FrameReady += OnFrameReady;
            inner.RenderFailed += OnRenderFailed;
        }
        finally
        {
            sourceGate.Release();
        }
    }

    private async ValueTask DisposeInnerAsync()
    {
        MpvVideoSource? source = inner;
        inner = null;
        if (source is null)
        {
            return;
        }

        source.FrameReady -= OnFrameReady;
        source.RenderFailed -= OnRenderFailed;
        await source.DisposeAsync().ConfigureAwait(false);
    }

    private void OnFrameReady() => FrameReady?.Invoke();
    private void OnRenderFailed(Exception exception) => RenderFailed?.Invoke(exception);

    private static bool PathsEqual(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
