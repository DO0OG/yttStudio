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
            ClearSubtitlePreview();
            return;
        }

        PlayerViewport viewport = CreatePlayerViewport(GetPreviewPlayerSize());
        SetPreviewViewport(viewport);
        (TimeSpan time, long frameIndex) = GetPreviewFrame(project);
        SubtitleRenderOptions options = CreatePreviewRenderOptions(frameIndex);
        PreviewRenderKey key = new(editor?.Identity ?? 0, editor?.Revision ?? 0, viewport, frameIndex, options);
        if (lastPreviewRenderKey == key &&
            lastPreviewSelection is not null && lastPreviewSelection.SetEquals(selectedCueIds))
        {
            return;
        }

        RenderSubtitlePreview(project, viewport, time, options);
        lastPreviewRenderKey = key;
        lastPreviewSelection = [.. selectedCueIds];
        Interlocked.Increment(ref previewRenderCount);
    }

    private void ClearSubtitlePreview()
    {
        lastPreviewRenderKey = null;
        lastPreviewSelection = null;
        SubtitleImage = null;
        SetCanvasItems([]);
    }

    /// <summary>재생 위치를 프레임 격자에 맞춰 시각과 프레임 인덱스를 함께 돌려준다.</summary>
    /// <remarks>
    /// 시각을 재생 위치 그대로 쓰면 같은 프레임 안에서도 밀리초마다 값이 달라져 입력이
    /// 같은지 판정할 근거가 사라진다. 프레임 인덱스로 내림한 시각을 쓰면 한 프레임 안의
    /// 어느 위치에서 불러도 결과가 같으므로 건너뛰기 판정이 성립한다.
    ///
    /// 그 대가로 프리뷰가 보여주는 시각이 재생 위치보다 최대 한 프레임(30fps 기준 33ms)
    /// 이르다. 큐 경계가 그 사이에 걸리면 이전 프레임 상태로 보인다. 영상도 같은 프레임을
    /// 띄우고 있으므로 자막과 영상이 서로 어긋나지는 않는다.
    /// </remarks>
    private (TimeSpan Time, long FrameIndex) GetPreviewFrame(SubtitleProject currentProject)
    {
        double framesPerSecond = currentProject.Video?.NominalFps is > 0
            ? currentProject.Video.NominalFps
            : 30;
        long frameIndex = checked((long)Math.Floor(
            TimeSpan.FromMilliseconds(PositionMilliseconds).TotalSeconds * framesPerSecond));
        return (TimeSpan.FromSeconds(frameIndex / framesPerSecond), frameIndex);
    }

    private SubtitleRenderOptions CreatePreviewRenderOptions(long frameIndex)
        => new()
        {
            DocumentRevision = editor?.Revision,
            FrameIndex = frameIndex,
            ShowSafeArea = showSafeArea,
            ShowAnchorPoints = showAnchors,
            EditingCueId = isInlineEditing ? inlineEditCueId : null,
        };

    private void RenderSubtitlePreview(
        SubtitleProject currentProject,
        PlayerViewport viewport,
        TimeSpan time,
        SubtitleRenderOptions options)
    {
        int width = ToBitmapDimension(viewport.PlayerSize.Width);
        int height = ToBitmapDimension(viewport.PlayerSize.Height);
        WriteableBitmap target = GetSubtitleTarget(width, height);
        IReadOnlyList<CueHitBox> hitBoxes = DrawSubtitlePreview(
            target, width, height, viewport, currentProject, time, options);
        OnPropertyChanged(nameof(SubtitleImage));
        SetCanvasItems(CreateCanvasItems(hitBoxes));
    }

    private WriteableBitmap GetSubtitleTarget(int width, int height)
    {
        if (SubtitleImage is WriteableBitmap existing &&
            existing.PixelSize.Width == width && existing.PixelSize.Height == height)
        {
            return existing;
        }

        WriteableBitmap target = new(new PixelSize(width, height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        SubtitleImage = target;
        return target;
    }

    private IReadOnlyList<CueHitBox> DrawSubtitlePreview(
        WriteableBitmap target,
        int width,
        int height,
        PlayerViewport viewport,
        SubtitleProject currentProject,
        TimeSpan time,
        SubtitleRenderOptions options)
    {
        using (ILockedFramebuffer framebuffer = target.Lock())
        {
            SKImageInfo info = new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using SKSurface surface = SKSurface.Create(info, framebuffer.Address, framebuffer.RowBytes);
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            return renderer.RenderAndMeasure(canvas, viewport, currentProject, time, options);
        }
    }

    private CanvasCueItem[] CreateCanvasItems(IReadOnlyList<CueHitBox> hitBoxes)
        => hitBoxes
            .Select(hit => new CanvasCueItem(
                hit.Cue.Id,
                new CanvasRect(hit.Bounds.Left, hit.Bounds.Top, hit.Bounds.Width, hit.Bounds.Height),
                new CanvasPoint(hit.AnchorScreenPoint.X, hit.AnchorScreenPoint.Y),
                hit.Cue.Anchor,
                selectedCueIds.Contains(hit.Cue.Id)))
            .ToArray();

    private void SetCanvasItems(IReadOnlyList<CanvasCueItem> items)
    {
        if (CanvasItems.SequenceEqual(items))
        {
            return;
        }

        CanvasItems = items;
        OnPropertyChanged(nameof(CanvasItems));
    }

    private readonly record struct PreviewRenderKey(
        long EditorIdentity,
        long ProjectRevision,
        PlayerViewport Viewport,
        long FrameIndex,
        SubtitleRenderOptions Options);

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
