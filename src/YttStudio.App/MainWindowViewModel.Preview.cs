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

/// <summary>프리뷰 뷰포트 산출과 자막 · 영상 프레임 렌더링을 담당한다.</summary>
public sealed partial class MainWindowViewModel
{

    /// <summary>화면에 실제 표시되는 전체화면 플레이어 크기를 갱신한다.</summary>
    public void UpdatePreviewPlayerSize(double width, double height)
    {
        if (previewViewportMode != PreviewViewportMode.YouTubeFullscreen ||
            !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 ||
            width > float.MaxValue || height > float.MaxValue)
        {
            return;
        }

        SKSize value = new((float)width, (float)height);
        if (Math.Abs(fullscreenPlayerSize.Width - value.Width) < 0.5f &&
            Math.Abs(fullscreenPlayerSize.Height - value.Height) < 0.5f)
        {
            return;
        }

        fullscreenPlayerSize = value;
        SetPreviewViewport(CreatePlayerViewport(value));
        if (!videoLoaded)
        {
            RenderFallbackFrame();
        }

        RenderSubtitlePreview();
        if (validationHasRun)
        {
            RunValidation();
        }
    }

    private SKSize GetPreviewPlayerSize()
        => previewViewportMode == PreviewViewportMode.YouTubeFullscreen
            ? fullscreenPlayerSize
            : ReferencePlayerSize;

    private PlayerViewport CreatePlayerViewport(SKSize playerSize)
    {
        SKSize? videoSize = GetVideoSize();
        PlayerViewport viewport = previewViewportMode switch
        {
            PreviewViewportMode.YouTubeDefault => videoSize is SKSize defaultVideoSize
                ? PlayerViewport.YouTubeDefault(defaultVideoSize)
                : PlayerViewport.YouTubeDefault(),
            PreviewViewportMode.YouTubeTheater => videoSize is SKSize theaterVideoSize
                ? PlayerViewport.YouTubeTheater(theaterVideoSize)
                : PlayerViewport.YouTubeTheater(),
            PreviewViewportMode.YouTubeFullscreen => videoSize is SKSize fullscreenVideoSize
                ? PlayerViewport.YouTubeFullscreen(playerSize, fullscreenVideoSize)
                : PlayerViewport.YouTubeFullscreen(playerSize),
            _ => PlayerViewport.VideoFrame(playerSize),
        };

        // 일반과 극장 팩터리가 주는 크기는 측정 당시의 창 크기라 기준 너비보다 작다.
        // 그대로 그리면 프리뷰 비트맵 해상도가 모드에 따라 낮아져 흐릿해진다. 두 모드는
        // 서로 닮음이라 배치가 달라지지 않으므로 기준 너비로 맞춰 선명도를 일정하게 둔다.
        // 전체화면과 VideoFrame 은 호출자가 실제 크기를 정하므로 건드리지 않는다.
        return viewport.Mode is PreviewViewportMode.YouTubeDefault or PreviewViewportMode.YouTubeTheater
            ? viewport.ScaleToWidth(ReferencePlayerSize.Width)
            : viewport;
    }

    private SKSize? GetVideoSize()
    {
        if (videoLoaded && videoSource is not null)
        {
            var info = videoSource.Info;
            if (info.Width > 0 && info.Height > 0)
            {
                return new SKSize(info.Width, info.Height);
            }
        }

        if (project?.Video is { Width: > 0, Height: > 0 } video)
        {
            return new SKSize(video.Width, video.Height);
        }

        return null;
    }

    private void SetPreviewViewport(PlayerViewport value)
    {
        if (previewViewport == value)
        {
            return;
        }

        previewViewport = value;
        OnPropertyChanged(nameof(PreviewViewport));
        OnPropertyChanged(nameof(PreviewSubtitleSpace));
        OnPropertyChanged(nameof(PreviewPlayerWidth));
        OnPropertyChanged(nameof(PreviewPlayerHeight));
        OnPropertyChanged(nameof(PreviewVideoContentLeft));
        OnPropertyChanged(nameof(PreviewVideoContentTop));
        OnPropertyChanged(nameof(PreviewVideoContentWidth));
        OnPropertyChanged(nameof(PreviewVideoContentHeight));
    }

    private static int ToBitmapDimension(float value)
    {
        if (!float.IsFinite(value) || value <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    private static Rect ToAvaloniaRect(SKRect value)
        => new(value.Left, value.Top, value.Width, value.Height);

    private void RenderFallbackFrame()
    {
        if (videoLoaded || disposed)
        {
            return;
        }

        SKSize playerSize = previewViewport.PlayerSize;
        using SKBitmap bitmap = new(new SKImageInfo(
            ToBitmapDimension(playerSize.Width),
            ToBitmapDimension(playerSize.Height),
            SKColorType.Bgra8888,
            SKAlphaType.Premul));
        using SKCanvas canvas = new(bitmap);
        if (UseCheckerboard)
        {
            DrawCheckerboard(canvas, bitmap.Width, bitmap.Height);
        }
        else
        {
            canvas.Clear(new SKColor(32, 32, 32));
        }

        VideoFrameImage = EncodeBitmap(bitmap);
    }

    private void RenderSubtitlePreview()
    {
        if (project is null || disposed)
        {
            SubtitleImage = null;
            CanvasItems = [];
            OnPropertyChanged(nameof(CanvasItems));
            return;
        }

        PlayerViewport viewport = CreatePlayerViewport(GetPreviewPlayerSize());
        SetPreviewViewport(viewport);
        int width = ToBitmapDimension(viewport.PlayerSize.Width);
        int height = ToBitmapDimension(viewport.PlayerSize.Height);

        // 이 경로는 재생 중 프레임마다 돈다. 매번 비트맵을 새로 만들고 PNG 로 압축했다가
        // 곧바로 되읽으면 프레임당 수 MB 할당과 무손실 압축 한 번이 통째로 낭비된다.
        // 영상 프레임과 같은 방식으로 비트맵을 재사용하고 Skia 가 그 화소 버퍼에 직접
        // 그리게 한다. 같은 인스턴스를 고쳐 쓰므로 변경 알림은 아래에서 직접 올린다.
        WriteableBitmap target;
        if (SubtitleImage is WriteableBitmap existing &&
            existing.PixelSize.Width == width && existing.PixelSize.Height == height)
        {
            target = existing;
        }
        else
        {
            target = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
            SubtitleImage = target;
        }

        TimeSpan time = TimeSpan.FromMilliseconds(PositionMilliseconds);
        double framesPerSecond = project.Video?.NominalFps is > 0 ? project.Video.NominalFps : 30;
        long frameIndex = checked((long)Math.Floor(time.TotalSeconds * framesPerSecond));
        IReadOnlyList<CueHitBox> hitBoxes;
        using (ILockedFramebuffer framebuffer = target.Lock())
        {
            SKImageInfo info = new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using SKSurface surface = SKSurface.Create(info, framebuffer.Address, framebuffer.RowBytes);
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            hitBoxes = renderer.RenderAndMeasure(canvas, viewport, project, time, new SubtitleRenderOptions
            {
                FrameIndex = frameIndex,
                ShowSafeArea = showSafeArea,
                ShowAnchorPoints = showAnchors,
                EditingCueId = isInlineEditing ? inlineEditCueId : null,
            });
        }

        OnPropertyChanged(nameof(SubtitleImage));
        CanvasItems = hitBoxes
            .Select(hit => new CanvasCueItem(
                hit.Cue.Id,
                new CanvasRect(hit.Bounds.Left, hit.Bounds.Top, hit.Bounds.Width, hit.Bounds.Height),
                new CanvasPoint(hit.AnchorScreenPoint.X, hit.AnchorScreenPoint.Y),
                hit.Cue.Anchor,
                selectedCueIds.Contains(hit.Cue.Id)))
            .ToArray();
        OnPropertyChanged(nameof(CanvasItems));
    }

    private static Bitmap EncodeBitmap(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using MemoryStream stream = new(data.ToArray());
        return new Bitmap(stream);
    }

    private static void DrawCheckerboard(SKCanvas canvas, int width, int height)
    {
        const int cellSize = 32;
        using SKPaint light = new() { Color = new SKColor(64, 64, 64) };
        using SKPaint dark = new() { Color = new SKColor(40, 40, 40) };
        for (int y = 0; y < height; y += cellSize)
        {
            for (int x = 0; x < width; x += cellSize)
            {
                canvas.DrawRect(x, y, cellSize, cellSize,
                    ((x / cellSize) + (y / cellSize)) % 2 == 0 ? light : dark);
            }
        }
    }
}
