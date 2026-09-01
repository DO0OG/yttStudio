using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render.Tests;

public sealed class CueEffectEvaluatorTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(500, 0.5)]
    [InlineData(1000, 1)]
    public void MoveInterpolatesAtBoundaryAndMidpoint(int milliseconds, double progress)
    {
        Cue cue = CreateCue();
        cue.AddEffect(new MoveEffect(100, 100, 200, 300, TimeSpan.Zero, TimeSpan.FromSeconds(1)));

        CueEffectState state = CueEffectEvaluator.Evaluate(cue, TimeSpan.FromMilliseconds(milliseconds), 0,
            new SKPoint(100, 100));

        Assert.InRange(Math.Abs(state.Translation.X - (100 * progress)), 0, 0.001);
        Assert.InRange(Math.Abs(state.Translation.Y - (200 * progress)), 0, 0.001);
    }

    [Fact]
    public void FadeAndAnimateUseExpectedMidpoint()
    {
        Cue cue = CreateCue();
        cue.AddEffect(new FadeEffect(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        AnimateEffect animate = new(TimeSpan.Zero, TimeSpan.FromSeconds(1), 2) { ToSizePercent = 200 };
        cue.AddEffect(animate);

        CueEffectState state = CueEffectEvaluator.Evaluate(cue, TimeSpan.FromMilliseconds(500), 0, SKPoint.Empty);

        Assert.InRange(state.Alpha, 0.499f, 0.501f);
        Assert.InRange(state.Scale, 1.249f, 1.251f);
    }

    [Fact]
    public void ShakeIsStableForSameCueAndFrame()
    {
        Cue cue = CreateCue();
        cue.AddEffect(new ShakeEffect(20, 10));

        CueEffectState first = CueEffectEvaluator.Evaluate(cue, TimeSpan.FromSeconds(1), 42, SKPoint.Empty);
        CueEffectState second = CueEffectEvaluator.Evaluate(cue, TimeSpan.FromSeconds(1), 42, SKPoint.Empty);

        Assert.Equal(first.Translation, second.Translation);
    }

    [Fact]
    public void ShakeChangesAcrossFramesWithinRadius()
    {
        Cue cue = CreateCue();
        cue.AddEffect(new ShakeEffect(20, 10));

        SKPoint first = CueEffectEvaluator.Evaluate(cue, TimeSpan.FromSeconds(1), 42, SKPoint.Empty).Translation;
        SKPoint second = CueEffectEvaluator.Evaluate(cue, TimeSpan.FromSeconds(1), 43, SKPoint.Empty).Translation;

        Assert.NotEqual(first, second);
        Assert.InRange(first.X, -20, 20);
        Assert.InRange(first.Y, -10, 10);
    }

    [Fact]
    public void MoveEvaluationDoesNotAllocatePerFrameCollection()
    {
        Cue cue = CreateCue();
        cue.AddEffect(new MoveEffect(100, 100, 200, 300, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        for (int index = 0; index < 10; index++)
            _ = CueEffectEvaluator.Evaluate(cue, TimeSpan.FromMilliseconds(index), index, SKPoint.Empty);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
            _ = CueEffectEvaluator.Evaluate(cue, TimeSpan.FromMilliseconds(index % 1000), index, SKPoint.Empty);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // CueEffectState 자체는 참조 record이므로 이 불가피한 객체 외에
        // 프레임마다 이동 평가 컬렉션이 생기지 않는지 확인한다.
        Assert.InRange(allocated, 0, 160_000);
    }

    private static Cue CreateCue() => new(Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"))
    {
        Start = TimeSpan.Zero,
        End = TimeSpan.FromSeconds(3),
    };
}
