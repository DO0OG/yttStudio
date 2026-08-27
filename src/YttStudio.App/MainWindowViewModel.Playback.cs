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

    private void InitializeVideoSource()
    {
        if (MpvVideoSource.TryCreate(out MpvVideoSource? source, out string diagnostic))
        {
            MpvVideoSource loadedSource = source!;
            videoSource = loadedSource;
            // 지난 실행에서 고른 재생 화질을 그대로 이어 쓴다.
            loadedSource.PlaybackScaleDivisor = playbackScaleDivisor;
            loadedSource.FrameReady += OnVideoFrameReady;
            VideoStatus = $"libmpv {loadedSource.LibraryVersion} · SW 콜백 렌더링";
            Serilog.Log.Information("libmpv initialized: {Version}; {Path}", loadedSource.LibraryVersion,
                loadedSource.LibraryPath);
        }
        else
        {
            VideoStatus = "libmpv 없음 · 배경 모드";
            Serilog.Log.Warning("libmpv unavailable: {Diagnostic}", diagnostic);
        }

        OpenVideoCommand.NotifyCanExecuteChanged();
    }

    private void DisposeVideoSource()
    {
        MpvVideoSource? source = videoSource;
        videoSource = null;
        videoLoaded = false;
        if (source is not null)
        {
            source.FrameReady -= OnVideoFrameReady;
            source.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        NotifyVideoState();
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

        if (videoToReload is not null && videoSource is not null && File.Exists(videoToReload))
        {
            await LoadVideoAsync(videoToReload);
            if (videoLoaded)
            {
                await SeekAsync(positionToRestore, exact: false);
            }
        }

        Status = videoSource is null ? Loc["MpvReloadFailed"] : Loc["MpvReloaded"];
        return Status;
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
    private async Task SeekAsync(double milliseconds, bool exact)
    {
        if (videoSource is null || !videoLoaded)
        {
            return;
        }

        if (seekInFlight)
        {
            pendingSeekMilliseconds = milliseconds;
            pendingSeekExact = exact;
            return;
        }

        seekInFlight = true;
        try
        {
            double target = milliseconds;
            bool targetExact = exact;
            while (true)
            {
                long startedAt = Environment.TickCount64;
                try
                {
                    await videoSource.SeekAsync(TimeSpan.FromMilliseconds(target), targetExact);
                }
                catch (Exception exception)
                {
                    if (videoLoaded)
                    {
                        Status = $"{Loc["SeekFailed"]}: {exception.Message}";
                    }
                }

                if (pendingSeekMilliseconds is null)
                {
                    break;
                }

                // 끌고 있는 동안에는 끝나는 즉시 다음 탐색이 나가서 디코딩과 화면 전송이
                // 쉴 틈 없이 돌아간다. 사람 눈에는 초당 열몇 장이면 충분히 이어져 보이므로
                // 최소 간격을 두어 그 위로는 올라가지 않게 한다.
                long elapsed = Environment.TickCount64 - startedAt;
                if (elapsed < MinimumScrubIntervalMilliseconds)
                {
                    await Task.Delay((int)(MinimumScrubIntervalMilliseconds - elapsed));
                }

                // 기다리는 사이에 더 새로운 목표가 들어왔을 수 있다. 지금 읽어야 마지막
                // 목표로 간다.
                if (pendingSeekMilliseconds is not double next)
                {
                    break;
                }

                pendingSeekMilliseconds = null;
                target = next;
                targetExact = pendingSeekExact;
            }
        }
        finally
        {
            seekInFlight = false;
        }
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
