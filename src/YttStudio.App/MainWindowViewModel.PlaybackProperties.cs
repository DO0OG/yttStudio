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

/// <summary>재생 상태 · 프리뷰 이미지 · 음량 바인딩 속성을 담당한다.</summary>
public sealed partial class MainWindowViewModel
{

    /// <summary>재생 화질로 고를 수 있는 축소 배수다.</summary>
    /// <remarks>
    /// 편집 중에는 원본 해상도가 필요 없을 때가 많다. 배수를 올리면 디코딩 뒤의 변환 ·
    /// 전송 · 합성이 함께 줄어 재생 부하가 크게 내려간다. 화면에 보이는 크기는 그대로이고
    /// 선명도만 낮아지며, 내보내는 자막에는 아무 영향이 없다.
    /// </remarks>
    public IReadOnlyList<PlaybackQualityOption> PlaybackQualityOptions { get; } =
    [
        new(1, "원본"),
        new(2, "1/2"),
        new(4, "1/4"),
        new(8, "1/8"),
    ];

    /// <summary>현재 선택한 재생 화질을 가져오거나 설정한다.</summary>
    public PlaybackQualityOption? SelectedPlaybackQuality
    {
        get => PlaybackQualityOptions.FirstOrDefault(option => option.Divisor == playbackScaleDivisor)
            ?? PlaybackQualityOptions[0];
        set
        {
            int divisor = value?.Divisor ?? 1;
            if (!SetField(ref playbackScaleDivisor, divisor))
            {
                return;
            }

            if (videoSource is not null)
            {
                videoSource.PlaybackScaleDivisor = divisor;
            }

            preferences.PlaybackScaleDivisor = divisor;
            SavePreferences();
        }
    }

    public bool HasVideo => videoLoaded;

    public bool IsPlaying => videoSource?.IsPlaying == true;

    public string PlayPauseLabel => IsPlaying ? PauseIcon : PlayIcon;

    public string PlayPauseActionText => IsPlaying ? Loc["Pause"] : Loc["Play"];

    public Bitmap? VideoFrameImage
    {
        get => videoFrameImage;
        private set => SetImage(ref videoFrameImage, value);
    }

    public Bitmap? SubtitleImage
    {
        get => subtitleImage;
        private set => SetImage(ref subtitleImage, value);
    }

    public string VideoStatus
    {
        get => videoStatus;
        private set => SetField(ref videoStatus, value);
    }

    public double MaximumMilliseconds
    {
        get => maximumMilliseconds;
        private set => SetField(ref maximumMilliseconds, value);
    }

    public double PositionMilliseconds
    {
        get => positionMilliseconds;
        set
        {
            double clamped = Math.Clamp(value, 0, MaximumMilliseconds);
            if (!SetField(ref positionMilliseconds, clamped))
            {
                return;
            }

            OnPropertyChanged(nameof(PositionDisplay));
            RequestSubtitlePreviewRender();
            if (!updatingFromVideo && videoLoaded && videoSource is not null)
            {
                _ = SeekAsync(clamped, exact: false);
            }
        }
    }

    public string PositionDisplay => TimeSpan.FromMilliseconds(PositionMilliseconds).ToString(@"mm\:ss\.fff");

    public double PlaybackSpeed
    {
        get => playbackSpeed;
        set
        {
            if (SetField(ref playbackSpeed, value) && videoLoaded)
            {
                videoSource?.SetSpeed(value);
            }
        }
    }

    public bool UseCheckerboard
    {
        get => useCheckerboard;
        set
        {
            if (SetField(ref useCheckerboard, value) && !videoLoaded)
            {
                RenderFallbackFrame();
            }
        }
    }

    public Task SeekExactAsync(double milliseconds) => SeekAsync(milliseconds, exact: true);

    public double Volume
    {
        get => volume;
        set
        {
            double clamped = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 100;
            if (!SetField(ref volume, clamped))
            {
                return;
            }

            preferences.Volume = clamped;
            SavePreferences();
            if (videoLoaded)
            {
                videoSource?.SetVolume(clamped);
            }
        }
    }

    public bool IsMuted
    {
        get => isMuted;
        set
        {
            if (!SetField(ref isMuted, value))
            {
                return;
            }

            OnPropertyChanged(nameof(MuteLabel));
            preferences.IsMuted = value;
            SavePreferences();
            if (videoLoaded)
            {
                videoSource?.SetMuted(value);
            }
        }
    }

    public string MuteLabel => IsMuted ? Loc["Unmute"] : Loc["Mute"];
}
