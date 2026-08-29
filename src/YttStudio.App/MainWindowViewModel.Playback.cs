using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using YttStudio.Core;
using YttStudio.Core.Editing;
using YttStudio.Core.Format;
using YttStudio.Core.Project;
using YttStudio.Core.Validation;
using YttStudio.Render;
using YttStudio.Video;
using SubtitleRenderOptions = YttStudio.Render.RenderOptions;

namespace YttStudio.App;

/// <summary>영상 소스 · 재생 제어와 libmpv 설치를 담당한다.</summary>
public sealed partial class MainWindowViewModel
{

    private bool TryCreateVideoSource(
        out IVideoSource? source,
        out string diagnostic,
        string? ytdlpPath = null)
    {
        if (videoSourceFactory is null)
        {
            bool created = ytdlpPath is null
                ? MpvVideoSource.TryCreate(out MpvVideoSource? nativeSource, out diagnostic)
                : MpvVideoSource.TryCreate(out nativeSource, out diagnostic, ytdlpPath);
            source = nativeSource;
            return created;
        }

        try
        {
            source = videoSourceFactory();
            diagnostic = source is null ? "video source factory returned null" : "injected video source";
            return source is not null;
        }
        catch (Exception exception)
        {
            source = null;
            diagnostic = $"video source factory failed: {exception.Message}";
            return false;
        }
    }

    private void InitializeVideoSource(string? ytdlpPath = null)
    {
        if (TryCreateVideoSource(out IVideoSource? source, out string diagnostic, ytdlpPath))
        {
            IVideoSource loadedSource = source!;
            videoSource = loadedSource;
            // 지난 실행에서 고른 재생 화질을 그대로 이어 쓴다.
            loadedSource.PlaybackScaleDivisor = playbackScaleDivisor;
            loadedSource.FrameReady += OnVideoFrameReady;
            loadedSource.RenderFailed += OnVideoRenderFailed;
            if (loadedSource is MpvVideoSource nativeSource)
            {
                VideoStatus = $"libmpv {nativeSource.LibraryVersion} · SW 콜백 렌더링";
                Serilog.Log.Information("libmpv initialized: {Version}; {Path}", nativeSource.LibraryVersion,
                    nativeSource.LibraryPath);
            }
            else
            {
                VideoStatus = "video source ready";
            }
        }
        else
        {
            VideoStatus = "libmpv 없음 · 배경 모드";
            Serilog.Log.Warning("libmpv unavailable: {Diagnostic}", diagnostic);
        }

        OpenVideoCommand.NotifyCanExecuteChanged();
        OpenVideoUrlCommand.NotifyCanExecuteChanged();
    }

    private void DisposeVideoSource()
    {
        CancelActiveVideoLoad();
        IVideoSource? source = videoSource;
        videoSource = null;
        videoLoaded = false;
        if (source is not null)
        {
            source.FrameReady -= OnVideoFrameReady;
            source.RenderFailed -= OnVideoRenderFailed;
            source.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        NotifyVideoState();
    }

    private async Task<bool> RefreshMpvSourceForYtDlpAsync(
        string? ytdlpPath,
        long generation)
    {
        if (!IsCurrentVideoLoad(generation))
        {
            return false;
        }

        IVideoSource? previous = videoSource;
        videoSource = null;
        videoLoaded = false;
        if (previous is not null)
        {
            previous.FrameReady -= OnVideoFrameReady;
            previous.RenderFailed -= OnVideoRenderFailed;
            await previous.DisposeAsync();
        }

        NotifyVideoState();

        if (!IsCurrentVideoLoad(generation))
        {
            return false;
        }

        InitializeVideoSource(ytdlpPath);
        RenderFallbackFrame();
        return videoSource is not null;
    }

    private async Task SelectMpvPathAsync()
    {
        string? selectedPath = await dialogs.OpenMpvLibraryAsync();
        if (selectedPath is null)
        {
            return;
        }

        MpvPath = selectedPath;
        await ApplyMpvPathAsync();
    }

    private Task ApplyMpvPathAsync()
        => ApplyMpvPathFromSettingsAsync(MpvPath);

    internal async Task<string> ApplyMpvPathFromSettingsAsync(string path)
    {
        MpvPath = path;
        string selectedPath = NormalizeMpvPath(path);
        if (!string.IsNullOrWhiteSpace(selectedPath)
            && !File.Exists(selectedPath)
            && !Directory.Exists(selectedPath))
        {
            Status = Loc["MpvPathInvalid"];
            return Status;
        }

        MpvPath = selectedPath;
        preferences.MpvPath = selectedPath;
        SavePreferences();
        Environment.SetEnvironmentVariable(
            "YTTSTUDIO_MPV_PATH",
            string.IsNullOrWhiteSpace(selectedPath) ? null : selectedPath);

        string? videoToReload = loadedVideoPath;
        double positionToRestore = PositionMilliseconds;
        DisposeVideoSource();
        InitializeVideoSource();
        RenderFallbackFrame();

        bool reloadAttempted = await ReloadVideoAfterMpvChangeAsync(
            videoToReload,
            positionToRestore);

        if (videoSource is null)
        {
            Status = Loc["MpvReloadFailed"];
        }
        else if (!reloadAttempted || videoLoaded)
        {
            Status = Loc["MpvReloaded"];
        }

        return Status;
    }

    private async Task<bool> ReloadVideoAfterMpvChangeAsync(
        string? videoToReload,
        double positionToRestore)
    {
        if (videoToReload is null || videoSource is null)
        {
            return false;
        }

        string normalizedReloadUrl = string.Empty;
        bool reloadUrl = TryNormalizeYouTubeUrl(videoToReload, out normalizedReloadUrl, out _);
        if (!reloadUrl && !File.Exists(videoToReload))
        {
            return false;
        }

        await LoadVideoAsync(
            videoToReload,
            reloadUrl ? normalizedReloadUrl : null,
            originalUrl: reloadUrl ? loadedVideoOriginalUrl : null);
        if (videoLoaded)
        {
            await SeekAsync(positionToRestore, exact: false);
        }

        return true;
    }

    private void OpenMpvInstallationGuide()
    {
        if (!MpvInstallationGuide.TryOpen(out string? error))
        {
            Status = $"{Loc["MpvGuide"]}: {error}";
        }
    }

    private async Task<string?> InstallMpvAndApplyAsync(IProgress<MpvInstallProgress> progress)
    {
        if (mpvAutoInstaller is null)
        {
            return Loc["MpvAutoInstallUnavailable"];
        }

        try
        {
            Status = Loc["MpvAutoInstall"];
            string installedPath = await mpvAutoInstaller.InstallAsync(progress);
            return await ApplyMpvPathFromSettingsAsync(installedPath);
        }
        catch (MpvAutoInstallException exception)
        {
            Serilog.Log.Warning(exception, "libmpv 자동 설치 실패: {Kind}", exception.Kind);
            Status = Loc["MpvAutoInstallFailed"];
            return Status;
        }
        catch (OperationCanceledException)
        {
            Status = Loc["MpvAutoInstallCanceled"];
            return Status;
        }
        catch (Exception exception)
        {
            Serilog.Log.Warning(exception, "libmpv 자동 설치 중 처리되지 않은 오류");
            Status = Loc["MpvAutoInstallFailed"];
            return Status;
        }
    }

    private void TogglePlayback()
    {
        if (videoSource is null || !videoLoaded)
        {
            return;
        }

        if (videoSource.IsPlaying)
        {
            videoSource.Pause();
        }
        else
        {
            videoSource.Play();
        }

        NotifyVideoState();
    }

    private void StepFrame(int delta)
    {
        videoSource?.StepFrame(delta);
        NotifyVideoState();
    }

    /// <summary>탐색 요청을 보내되, 진행 중이면 마지막 목표만 남긴다.</summary>
    /// <remarks>
    /// 타임라인을 끄는 동안 위치는 영상 프레임보다 훨씬 자주 바뀐다. 요청을 그대로
    /// 흘려보내면 이미 지나간 지점으로 가는 탐색 명령이 쌓여 화면이 손보다 한참 뒤처진다.
    /// 중간 목표는 어차피 버려질 값이므로 진행 중인 탐색이 끝나면 가장 최근 목표로만
    /// 이어서 간다. 최종 도달 지점은 같다.
    /// </remarks>
    /// <summary>탐색 요청을 받아 최소 간격을 지키며 마지막 목표로만 이동한다.</summary>
    /// <remarks>
    /// 타임라인이나 슬라이더를 끄는 동안 위치는 포인터 이동마다 바뀐다. 그대로 흘려보내면
    /// 초당 아흔 번 가까운 탐색이 나가고, 탐색마다 디코더를 비웠다 다시 채우므로 GPU 가
    /// 포화된다. 중간 목표는 어차피 버려질 값이니 마지막 하나만 남기고, 발사 간격을
    /// 강제한다. 최종 도달 지점은 같다.
    ///
    /// 간격의 기준은 직전에 실제로 내보낸 시각이다. 진행 중인 탐색이 있는지로 판단하면
    /// 요청이 하나씩 띄엄띄엄 들어올 때 매번 곧바로 나가 버려 간격이 성립하지 않는다.
    /// </remarks>
    private async Task SeekAsync(double milliseconds, bool exact)
    {
        if (videoSource is null || !videoLoaded)
        {
            return;
        }

        pendingSeekMilliseconds = milliseconds;
        pendingSeekExact = exact;
        if (seekInFlight)
        {
            return;
        }

        seekInFlight = true;
        try
        {
            await DrainPendingSeeksAsync();
        }
        finally
        {
            seekInFlight = false;
        }
    }

    /// <summary>대기 중인 목표를 최소 간격을 지켜 하나씩 내보낸다.</summary>
    private async Task DrainPendingSeeksAsync()
    {
        while (true)
        {
            long waiting = MinimumScrubIntervalMilliseconds -
                (Environment.TickCount64 - lastSeekDispatchedAt);
            if (waiting > 0)
            {
                await Task.Delay((int)waiting);
            }

            // 기다리는 사이에 더 새로운 목표가 들어왔을 수 있다. 지금 읽어야 마지막
            // 목표로 간다.
            (bool hasNext, double next, bool nextExact) = ConsumePendingSeekTarget();
            if (!hasNext)
            {
                return;
            }

            lastSeekDispatchedAt = Environment.TickCount64;
            await SeekTargetAsync(next, nextExact);
        }
    }

    private async Task SeekTargetAsync(double target, bool targetExact)
    {
        try
        {
            await videoSource!.SeekAsync(TimeSpan.FromMilliseconds(target), targetExact);
        }
        catch (Exception exception)
        {
            if (videoLoaded)
            {
                Status = $"{Loc["SeekFailed"]}: {exception.Message}";
            }
        }
    }

    private (bool HasNext, double Target, bool Exact) ConsumePendingSeekTarget()
    {
        if (pendingSeekMilliseconds is not double next)
        {
            return (false, 0, false);
        }

        pendingSeekMilliseconds = null;
        return (true, next, pendingSeekExact);
    }

    /// <summary>렌더 스레드가 실패로 끝났음을 사용자에게 알린다.</summary>
    /// <remarks>
    /// 이 시점부터 프레임이 오지 않는다. 재생 상태만 살아 있으면 화면이 멎은 채로 남아
    /// 사용자는 무엇이 잘못됐는지 알 수 없다. 재생을 내리고 무슨 일이 있었는지 적는다.
    /// 자막 편집은 영상 없이도 계속할 수 있으므로 앱을 끝내지는 않는다.
    /// </remarks>
    private void OnVideoRenderFailed(Exception exception)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (disposed)
            {
                return;
            }

            videoLoaded = false;
            Status = $"{Loc["VideoRenderStopped"]}: {exception.Message}";
            VideoStatus = Loc["VideoRenderStopped"];
            Serilog.Log.Error(exception, "libmpv 렌더 스레드가 실패로 종료됨");
            RenderFallbackFrame();
            NotifyVideoState();
        });
    }

    private void OnVideoFrameReady()
    {
        if (Interlocked.Exchange(ref frameUpdatePending, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(BlitLatestFrame, DispatcherPriority.Render);
    }

    private unsafe void BlitLatestFrame()
    {
        Interlocked.Exchange(ref frameUpdatePending, 0);
        if (disposed || videoSource is null || !videoSource.TryLockLatestFrame(out VideoFrameLock frame))
        {
            return;
        }

        using (frame)
        {
            WriteableBitmap bitmap;
            if (VideoFrameImage is not WriteableBitmap existing || existing.PixelSize.Width != frame.Width ||
                existing.PixelSize.Height != frame.Height)
            {
                bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96),
                    PixelFormat.Bgra8888, AlphaFormat.Premul);
                VideoFrameImage = bitmap;
            }
            else
            {
                bitmap = existing;
            }

            using ILockedFramebuffer destination = bitmap.Lock();
            fixed (byte* source = frame.Pixels)
            {
                for (int row = 0; row < frame.Height; row++)
                {
                    Buffer.MemoryCopy(source + (row * frame.Stride),
                        (byte*)destination.Address + (row * destination.RowBytes),
                        destination.RowBytes,
                        Math.Min(frame.Width * 4, destination.RowBytes));
                }
            }
        }

        OnPropertyChanged(nameof(VideoFrameImage));
        updatingFromVideo = true;
        PositionMilliseconds = videoSource.Position.TotalMilliseconds;
        updatingFromVideo = false;
        NotifyPlaybackFrameState();
        SampleRenderDiagnostics();
    }

    /// <summary>프레임이 넘어갈 때만 달라질 수 있는 상태를 알린다.</summary>
    /// <remarks>
    /// 이 경로는 영상 프레임마다 돈다. <see cref="NotifyVideoState"/> 는 커맨드 스물여섯
    /// 개의 실행 가능 여부를 모두 다시 묻는데, 그중 재생 위치에 따라 달라지는 것은 없다.
    /// 편집 · 스타일 · 정렬 커맨드는 문서가 바뀔 때만 상태가 변하므로 프레임마다 물을
    /// 이유가 없다. 프레임 경로에서는 재생 관련만 알린다.
    /// </remarks>
    private void NotifyPlaybackFrameState()
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayPauseLabel));
        OnPropertyChanged(nameof(PlayPauseActionText));
    }

    private void NotifyVideoState()
    {
        OnPropertyChanged(nameof(HasVideo));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayPauseLabel));
        OnPropertyChanged(nameof(PlayPauseActionText));
        NotifyCommandStates();
    }
}
