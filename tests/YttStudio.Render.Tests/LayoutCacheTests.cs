using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render.Tests;

public sealed class LayoutCacheTests
{
    [Fact]
    public void ReusesLayoutsAndHitBoxesInsideStaticActiveInterval()
    {
        using TestRenderer test = CreateRenderer();

        IReadOnlyList<CueHitBox> first = test.RenderAt(1.0, revision: 1);
        IReadOnlyList<CueHitBox> second = test.RenderAt(1.5, revision: 1);

        Assert.Same(first, second);
        Assert.IsNotType<CueHitBox[]>(first);
    }

    [Fact]
    public void InvalidatesAtStartAndEndBoundaries()
    {
        using TestRenderer test = CreateRenderer(withSecondCue: true);

        IReadOnlyList<CueHitBox> beforeStart = test.RenderAt(1.9, revision: 1);
        IReadOnlyList<CueHitBox> atStart = test.RenderAt(2.0, revision: 1);
        IReadOnlyList<CueHitBox> beforeEnd = test.RenderAt(2.9, revision: 1);
        IReadOnlyList<CueHitBox> atEnd = test.RenderAt(3.0, revision: 1);

        Assert.NotSame(beforeStart, atStart);
        Assert.Same(atStart, beforeEnd);
        Assert.NotSame(beforeEnd, atEnd);
    }

    [Fact]
    public void RevisionViewportAndLayoutOptionsInvalidateCache()
    {
        using TestRenderer test = CreateRenderer();
        IReadOnlyList<CueHitBox> initial = test.RenderAt(1.0, revision: 1);

        IReadOnlyList<CueHitBox> revision = test.RenderAt(1.1, revision: 2);
        IReadOnlyList<CueHitBox> viewport = test.RenderAt(1.2, revision: 2, width: 640);
        IReadOnlyList<CueHitBox> scale = test.RenderAt(1.3, revision: 2, width: 640, fontScale: 1.1);

        Assert.NotSame(initial, revision);
        Assert.NotSame(revision, viewport);
        Assert.NotSame(viewport, scale);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DoesNotCacheTimeDependentCues(bool karaoke)
    {
        using TestRenderer test = CreateRenderer();
        if (karaoke)
        {
            test.Cue.Sections[0].KaraokeOffset = TimeSpan.Zero;
        }
        else
        {
            test.Cue.AddEffect(new MoveEffect(0, 0, 10, 10));
        }

        IReadOnlyList<CueHitBox> first = test.RenderAt(1.0, revision: 1);
        IReadOnlyList<CueHitBox> second = test.RenderAt(1.1, revision: 1);

        Assert.NotSame(first, second);
    }

    [Fact]
    public void DoesNotCacheWhenRevisionIsUnknown()
    {
        using TestRenderer test = CreateRenderer();

        IReadOnlyList<CueHitBox> first = test.RenderAt(1.0, revision: null);
        IReadOnlyList<CueHitBox> second = test.RenderAt(1.1, revision: null);

        Assert.NotSame(first, second);
    }

    private static TestRenderer CreateRenderer(bool withSecondCue = false)
    {
        (SubtitleProject project, Cue cue) = LayoutTests.CreateProject(
            AnchorPoint.MiddleCenter, Justification.Center, "cache");
        if (withSecondCue)
        {
            Cue second = new(Guid.NewGuid())
            {
                Start = TimeSpan.FromSeconds(2),
                End = TimeSpan.FromSeconds(3),
            };
            second.AddSection(new Section { Text = "boundary" });
            project.Cues.Add(second);
        }

        return new TestRenderer(project, cue);
    }

    private sealed class TestRenderer : IDisposable
    {
        private readonly BundledFontResolver fonts = new();
        private readonly SkiaSubtitleRenderer renderer;
        private readonly SKBitmap bitmap = new(new SKImageInfo(1280, 720));
        private readonly SKCanvas canvas;

        public TestRenderer(SubtitleProject project, Cue cue)
        {
            Project = project;
            Cue = cue;
            renderer = new SkiaSubtitleRenderer(fonts);
            canvas = new SKCanvas(bitmap);
        }

        public SubtitleProject Project { get; }
        public Cue Cue { get; }

        public IReadOnlyList<CueHitBox> RenderAt(
            double seconds,
            long? revision,
            float width = 1280,
            double fontScale = 1)
            => renderer.RenderAndMeasure(canvas, PlayerViewport.VideoFrame(width, 720), Project,
                TimeSpan.FromSeconds(seconds), new RenderOptions
                {
                    DocumentRevision = revision,
                    FontScaleBase = fontScale,
                });

        public void Dispose()
        {
            canvas.Dispose();
            bitmap.Dispose();
            renderer.Dispose();
            fonts.Dispose();
        }
    }
}
