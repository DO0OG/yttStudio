using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render.Tests;

public sealed class ViewportTests
{
    [Fact]
    public void YouTubeDefaultUsesMeasuredPlayerSizeAndFullSubtitleSpace()
    {
        PlayerViewport viewport = PlayerViewport.YouTubeDefault();

        Assert.Equal(PreviewViewportMode.YouTubeDefault, viewport.Mode);
        Assert.InRange(Math.Abs(viewport.PlayerSize.Width - 794f), 0, 0.001f);
        Assert.InRange(Math.Abs(viewport.PlayerSize.Height - 437.5f), 0, 0.001f);
        AssertRect(viewport.SubtitleSpace, 0, 0, 794f, 437.5f);
        AssertRect(viewport.VideoContentRect, 0, 0, 794f, 437.5f);
    }

    [Fact]
    public void YouTubeTheaterUsesMeasuredPlayerSizeAndFullSubtitleSpace()
    {
        PlayerViewport viewport = PlayerViewport.YouTubeTheater();

        Assert.Equal(PreviewViewportMode.YouTubeTheater, viewport.Mode);
        Assert.InRange(Math.Abs(viewport.PlayerSize.Width - 1162f), 0, 0.001f);
        Assert.InRange(Math.Abs(viewport.PlayerSize.Height - 634f), 0, 0.001f);
        AssertRect(viewport.SubtitleSpace, 0, 0, 1162f, 634f);
        AssertRect(viewport.VideoContentRect, 0, 0, 1162f, 634f);
    }

    [Fact]
    public void YouTubeFullscreenRequiresCallerPlayerSize()
    {
        PlayerViewport viewport = PlayerViewport.YouTubeFullscreen(new SKSize(1920, 1080));

        Assert.Equal(PreviewViewportMode.YouTubeFullscreen, viewport.Mode);
        Assert.Equal(new SKSize(1920, 1080), viewport.PlayerSize);
        AssertRect(viewport.SubtitleSpace, 0, 0, 1920, 1080);
        AssertRect(viewport.VideoContentRect, 0, 0, 1920, 1080);
    }

    [Fact]
    public void AspectFitAddsPillarboxForNarrowVideo()
    {
        PlayerViewport viewport = PlayerViewport.YouTubeDefault(new SKSize(4, 3));

        AssertRect(viewport.VideoContentRect, 105.3333f, 0, 583.3334f, 437.5f);
        AssertRect(viewport.SubtitleSpace, 0, 0, 794, 437.5f);
    }

    [Fact]
    public void AspectFitAddsLetterboxForWideVideo()
    {
        PlayerViewport viewport = PlayerViewport.YouTubeTheater(new SKSize(21, 9));

        AssertRect(viewport.VideoContentRect, 0, 68, 1162, 498);
        AssertRect(viewport.SubtitleSpace, 0, 0, 1162, 634);
    }

    [Fact]
    public void LayoutFontScaleUsesSubtitleSpaceWidthInsteadOfHeight()
    {
        using BundledFontResolver fonts = new();
        SubtitleLayoutEngine engine = new(fonts);
        (SubtitleProject project, Cue cue) = LayoutTests.CreateProject(
            AnchorPoint.MiddleCenter,
            Justification.Center,
            "width based");

        CueLayout shortPlayer = engine.LayoutCue(new PlayerViewport(1000, 360), project, cue);
        CueLayout tallPlayer = engine.LayoutCue(new PlayerViewport(1000, 900), project, cue);

        Assert.InRange(Math.Abs(shortPlayer.ResolvedFontSize - 24.4907f), 0, 0.001f);
        Assert.InRange(Math.Abs(shortPlayer.ResolvedFontSize - tallPlayer.ResolvedFontSize), 0, 0.001f);
    }

    [Fact]
    public void LayoutFontScaleUsesSubtitleSpaceWidthForOffsetSpace()
    {
        using BundledFontResolver fonts = new();
        SubtitleLayoutEngine engine = new(fonts);
        (SubtitleProject project, Cue cue) = LayoutTests.CreateProject(
            AnchorPoint.MiddleCenter,
            Justification.Center,
            "subtitle space");
        PlayerViewport viewport = new(
            new SKSize(1280, 720),
            SKRect.Create(0, 0, 1280, 720),
            SKRect.Create(100, 40, 640, 360),
            PreviewViewportMode.VideoFrame);

        CueLayout layout = engine.LayoutCue(viewport, project, cue);

        Assert.InRange(Math.Abs(layout.ResolvedFontSize - 15.674048f), 0, 0.001f);
        Assert.InRange(Math.Abs(layout.AnchorScreenPoint.X - 420f), 0, 0.001f);
        Assert.InRange(Math.Abs(layout.AnchorScreenPoint.Y - 220f), 0, 0.001f);
    }

    [Fact]
    public void LayoutFontScaleKeepsReferenceSixteenByNineResult()
    {
        using BundledFontResolver fonts = new();
        SubtitleLayoutEngine engine = new(fonts);
        (SubtitleProject project, Cue cue) = LayoutTests.CreateProject(
            AnchorPoint.MiddleCenter,
            Justification.Center,
            "reference");

        CueLayout layout = engine.LayoutCue(new PlayerViewport(1280, 720), project, cue);

        Assert.InRange(Math.Abs(layout.ResolvedFontSize - 31.348096f), 0, 0.001f);
    }

    private static void AssertRect(SKRect actual, float left, float top, float width, float height)
    {
        Assert.InRange(Math.Abs(actual.Left - left), 0, 0.01f);
        Assert.InRange(Math.Abs(actual.Top - top), 0, 0.01f);
        Assert.InRange(Math.Abs(actual.Width - width), 0, 0.01f);
        Assert.InRange(Math.Abs(actual.Height - height), 0, 0.01f);
    }

    [Fact]
    public void ScaleToWidth_KeepsGeometryProportions()
    {
        PlayerViewport viewport = PlayerViewport.YouTubeDefault();
        float originalAspect = viewport.PlayerSize.Width / viewport.PlayerSize.Height;
        float subtitleShare = viewport.SubtitleSpace.Height / viewport.PlayerSize.Height;

        PlayerViewport scaled = viewport.ScaleToWidth(1280);

        Assert.Equal(1280, scaled.PlayerSize.Width, 3);
        Assert.Equal(originalAspect, scaled.PlayerSize.Width / scaled.PlayerSize.Height, 3);
        Assert.Equal(subtitleShare, scaled.SubtitleSpace.Height / scaled.PlayerSize.Height, 3);
        Assert.Equal(viewport.Mode, scaled.Mode);
    }

    [Fact]
    public void ScaleToWidth_RejectsNonPositiveWidth()
    {
        PlayerViewport viewport = PlayerViewport.YouTubeTheater();

        Assert.Throws<ArgumentOutOfRangeException>(() => viewport.ScaleToWidth(0));
    }
}
