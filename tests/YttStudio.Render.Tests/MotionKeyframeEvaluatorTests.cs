using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render.Tests;

public sealed class MotionKeyframeEvaluatorTests
{
    [Fact]
    public void EaseInAccelerationIsAppliedToTheAdjacentSegment()
    {
        Cue cue = CreateCue();
        cue.AddEffect(new MoveEffect(
        [
            new MotionKeyframe(TimeSpan.Zero, 100, 100, MotionInterpolation.EaseIn, 2),
            new MotionKeyframe(TimeSpan.FromSeconds(1), 200, 300),
            new MotionKeyframe(TimeSpan.FromSeconds(2), 400, 500),
        ]));

        CueEffectState state = CueEffectEvaluator.Evaluate(
            cue,
            TimeSpan.FromMilliseconds(500),
            0,
            new SKPoint(100, 100));

        Assert.InRange(state.Translation.X, 24.999f, 25.001f);
        Assert.InRange(state.Translation.Y, 49.999f, 50.001f);
    }

    [Fact]
    public void LinearAccelerationAtEitherToleranceBoundaryUsesLinearMidpoint()
    {
        double tolerance = YttConstants.MotionAccelerationExponentTolerance;
        double[] accelerations = [1 - tolerance, 1 + tolerance];
        foreach (double acceleration in accelerations)
        {
            Cue cue = CreateReferenceWidthLinearCue(acceleration);
            CueEffectState state = CueEffectEvaluator.Evaluate(
                cue,
                TimeSpan.FromMilliseconds(500),
                0,
                SKPoint.Empty);

            Assert.Equal(YttConstants.ReferenceWidth / 2f, state.Translation.X);
        }
    }

    [Fact]
    public void LinearAccelerationOutsideToleranceUsesPowerAtReferenceWidthMidpoint()
    {
        double tolerance = YttConstants.MotionAccelerationExponentTolerance;
        double acceleration = 1 + (2 * tolerance);
        Cue cue = CreateReferenceWidthLinearCue(acceleration);

        CueEffectState state = CueEffectEvaluator.Evaluate(
            cue,
            TimeSpan.FromMilliseconds(500),
            0,
            SKPoint.Empty);

        float expected = (float)(YttConstants.ReferenceWidth * Math.Pow(0.5, acceleration));
        Assert.Equal(expected, state.Translation.X);
        Assert.NotEqual(YttConstants.ReferenceWidth / 2f, state.Translation.X);
    }

    [Fact]
    public void EvaluationUsesOneAdjacentSegmentWithoutAccumulatingEarlierCoordinates()
    {
        Cue cue = CreateCue();
        cue.AddEffect(new MoveEffect(
        [
            new MotionKeyframe(TimeSpan.Zero, 100, 100),
            new MotionKeyframe(TimeSpan.FromSeconds(1), 200, 300),
            new MotionKeyframe(TimeSpan.FromSeconds(2), 400, 500),
        ]));

        CueEffectState state = CueEffectEvaluator.Evaluate(
            cue,
            TimeSpan.FromMilliseconds(1500),
            0,
            new SKPoint(100, 100));

        Assert.Equal(new SKPoint(200, 300), state.Translation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveAndShakeComposeIndependentlyOfEffectOrder(bool moveFirst)
    {
        Cue cue = CreateCue();
        MoveEffect move = new(100, 100, 200, 200, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        ShakeEffect shake = new(20, 10);
        if (moveFirst)
        {
            cue.AddEffect(move);
            cue.AddEffect(shake);
        }
        else
        {
            cue.AddEffect(shake);
            cue.AddEffect(move);
        }

        SKPoint expected = CueEffectEvaluator.DeterministicShake(cue.Id, 42, 20, 10);
        expected += new SKPoint(50, 50);
        CueEffectState state = CueEffectEvaluator.Evaluate(cue, TimeSpan.FromMilliseconds(500), 42,
            new SKPoint(100, 100));

        Assert.Equal(expected, state.Translation);
    }

    [Theory]
    [InlineData(0, 100, 100)]
    [InlineData(500, 100, 100)]
    [InlineData(1500, 150, 150)]
    [InlineData(2500, 200, 200)]
    public void TimedLegacyMoveClampsOutsideItsInterval(int milliseconds, float expectedX, float expectedY)
    {
        Cue cue = CreateCue();
        cue.AddEffect(new MoveEffect(100, 100, 200, 200,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));

        CueEffectState state = CueEffectEvaluator.Evaluate(cue, TimeSpan.FromMilliseconds(milliseconds), 0,
            SKPoint.Empty);

        Assert.Equal(new SKPoint(expectedX, expectedY), state.Translation);
    }

    [Fact]
    public void DiscontinuousRunsDoNotInventMotionDuringTheGap()
    {
        Cue cue = CreateCue();
        cue.AddEffect(new MoveEffect(
        [
            new MotionKeyframe(TimeSpan.Zero, 100, 100),
            new MotionKeyframe(TimeSpan.FromSeconds(1), 200, 200),
        ]));
        cue.AddEffect(new MoveEffect(
        [
            new MotionKeyframe(TimeSpan.FromSeconds(2), 500, 500),
            new MotionKeyframe(TimeSpan.FromSeconds(3), 600, 600),
        ]));

        CueEffectState state = CueEffectEvaluator.Evaluate(
            cue,
            TimeSpan.FromMilliseconds(1500),
            0,
            new SKPoint(100, 100));

        Assert.Equal(new SKPoint(100, 100), state.Translation);
    }

    private static Cue CreateCue() => new(Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"))
    {
        Start = TimeSpan.Zero,
        End = TimeSpan.FromSeconds(3),
    };

    private static Cue CreateReferenceWidthLinearCue(double acceleration)
    {
        Cue cue = CreateCue();
        cue.AddEffect(new MoveEffect(
        [
            new MotionKeyframe(TimeSpan.Zero, 0, 0, MotionInterpolation.Linear, acceleration),
            new MotionKeyframe(TimeSpan.FromSeconds(1), YttConstants.ReferenceWidth, 0),
        ]));
        return cue;
    }
}
