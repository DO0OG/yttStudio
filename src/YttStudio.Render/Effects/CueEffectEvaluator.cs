using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render;

/// <summary>큐와 프레임 하나에 대한 해석된 결정적 시각 상태다.</summary>
public sealed record CueEffectState(
    SKPoint Translation,
    float Alpha,
    float Scale,
    RgbaColor? Foreground,
    RgbaColor? EdgeColor,
    float ChromaAmount,
    ChromaEffect? Chroma);

/// <summary>프레임 간 난수 상태를 남기지 않고 큐 효과를 평가한다.</summary>
public static class CueEffectEvaluator
{
    public static CueEffectState Evaluate(Cue cue, TimeSpan time, long frameIndex, SKPoint baseAnchor)
    {
        ArgumentNullException.ThrowIfNull(cue);
        TimeSpan elapsed = time - cue.Start;
        TimeSpan duration = cue.End - cue.Start;
        SKPoint translation = SKPoint.Empty;
        float alpha = 1;
        float scale = 1;
        RgbaColor? foreground = null;
        RgbaColor? edge = null;
        float chromaAmount = 0;
        ChromaEffect? chromaEffect = null;
        MoveEvaluation nearestMove = MoveEvaluation.Invalid;
        TimeSpan nearestDistance = TimeSpan.MaxValue;
        bool hasNearestMove = false;
        bool activeMoveFound = false;

        foreach (CueEffect effect in cue.Effects)
        {
            if (effect is MoveEffect move)
            {
                MoveEvaluation evaluation = EvaluateMove(move, elapsed, duration, baseAnchor);
                if (evaluation.IsValid)
                {
                    if (evaluation.IsActive && !activeMoveFound)
                    {
                        nearestMove = evaluation;
                        hasNearestMove = true;
                        nearestDistance = TimeSpan.Zero;
                        activeMoveFound = true;
                    }
                    else if (!activeMoveFound)
                    {
                        TimeSpan distance = elapsed < evaluation.Start
                            ? evaluation.Start - elapsed
                            : elapsed - evaluation.End;
                        if (!hasNearestMove || distance < nearestDistance)
                        {
                            nearestMove = evaluation;
                            nearestDistance = distance;
                            hasNearestMove = true;
                        }
                    }
                }
                continue;
            }

            ApplyEffect(effect, cue, elapsed, duration, frameIndex,
                ref translation, ref alpha, ref scale, ref foreground, ref edge,
                ref chromaAmount, ref chromaEffect);
        }

        // 이동 좌표는 ASS 공간의 절대값이고 흔들림은 변화량이다.
        // 모든 효과를 적용한 뒤 인접한 이동 구간을 해석하여 적용 순서와 무관하게
        // 동일한 결과를 만든다.
        if (hasNearestMove)
        {
            translation += nearestMove.Translation;
        }

        return new CueEffectState(translation, Math.Clamp(alpha, 0, 1), Math.Max(0.01f, scale),
            foreground, edge, chromaAmount, chromaEffect);
    }

    private static void ApplyEffect(
        CueEffect effect,
        Cue cue,
        TimeSpan elapsed,
        TimeSpan duration,
        long frameIndex,
        ref SKPoint translation,
        ref float alpha,
        ref float scale,
        ref RgbaColor? foreground,
        ref RgbaColor? edge,
        ref float chromaAmount,
        ref ChromaEffect? chromaEffect)
    {
        switch (effect)
        {
            case FadeEffect fade:
                alpha *= EvaluateFade(fade, elapsed, duration);
                break;
            case ShakeEffect shake when IsActive(elapsed, shake.StartTime, shake.EndTime, duration):
                translation += DeterministicShake(cue.Id, frameIndex, shake.RadiusX, shake.RadiusY);
                break;
            case ChromaEffect chroma:
                chromaAmount = Math.Max(chromaAmount, EvaluateChroma(chroma, elapsed, duration));
                chromaEffect = chroma;
                break;
            case AnimateEffect animate:
                ApplyAnimation(animate, elapsed, ref scale, ref foreground, ref edge);
                break;
            default:
                // 비활성이거나 시각 효과가 아닌 항목은 이 프레임에 기여하지 않는다.
                break;
        }
    }

    private static MoveEvaluation EvaluateMove(
        MoveEffect move,
        TimeSpan elapsed,
        TimeSpan duration,
        SKPoint baseAnchor)
    {
        if (move.Keyframes.Count == 0)
        {
            TimeSpan start = move.StartTime ?? TimeSpan.Zero;
            TimeSpan end = move.EndTime ?? duration;
            if (end < start)
            {
                return MoveEvaluation.Invalid;
            }

            double progress = Progress(elapsed, start, end);
            SKPoint translation = ToTranslation(
                Lerp(move.FromX, move.ToX, progress),
                Lerp(move.FromY, move.ToY, progress),
                baseAnchor);
            return new MoveEvaluation(translation, elapsed >= start && elapsed <= end, start, end);
        }

        IReadOnlyList<MotionKeyframe> keyframes = move.Keyframes;
        if (keyframes.Count == 1)
        {
            MotionKeyframe point = keyframes[0];
            return new MoveEvaluation(
                ToTranslation(point.X, point.Y, baseAnchor),
                elapsed == point.RelativeTime,
                point.RelativeTime,
                point.RelativeTime);
        }

        TimeSpan startTime = keyframes[0].RelativeTime;
        TimeSpan endTime = keyframes[^1].RelativeTime;
        SKPoint clampedTranslation = ToTranslation(
            elapsed < startTime ? keyframes[0].X : keyframes[^1].X,
            elapsed < startTime ? keyframes[0].Y : keyframes[^1].Y,
            baseAnchor);

        for (int index = 0; index < keyframes.Count - 1; index++)
        {
            MotionKeyframe from = keyframes[index];
            MotionKeyframe to = keyframes[index + 1];
            if (elapsed < from.RelativeTime || elapsed > to.RelativeTime)
            {
                continue;
            }

            double progress = Progress(elapsed, from.RelativeTime, to.RelativeTime);
            progress = Ease(progress, from.Interpolation, from.Acceleration);
            SKPoint translation = ToTranslation(
                Lerp(from.X, to.X, progress),
                Lerp(from.Y, to.Y, progress),
                baseAnchor);
            return new MoveEvaluation(translation, true, startTime, endTime);
        }

        // 첫 키프레임 이전이나 마지막 이후에는 ASS의 끝점 고정 동작을 유지한다.
        // 중복 시각은 안전한 길이 0 구간으로 남겨 다른 구간과 연결하지 않는다.
        return new MoveEvaluation(clampedTranslation, false, startTime, endTime);
    }

    private static SKPoint ToTranslation(double x, double y, SKPoint baseAnchor)
        => new((float)(x - baseAnchor.X), (float)(y - baseAnchor.Y));

    private static double Ease(double progress, MotionInterpolation interpolation, double acceleration)
    {
        double exponent = double.IsFinite(acceleration) && acceleration > 0 ? acceleration : 1;
        return interpolation switch
        {
            MotionInterpolation.Step => progress >= 1 ? 1 : 0,
            MotionInterpolation.EaseIn => Math.Pow(progress, exponent),
            MotionInterpolation.EaseOut => 1 - Math.Pow(1 - progress, exponent),
            MotionInterpolation.EaseInOut => progress < 0.5
                ? 0.5 * Math.Pow(progress * 2, exponent)
                : 1 - (0.5 * Math.Pow((1 - progress) * 2, exponent)),
            _ => exponent == 1 ? progress : Math.Pow(progress, exponent),
        };
    }

    private static void ApplyAnimation(
        AnimateEffect animate,
        TimeSpan elapsed,
        ref float scale,
        ref RgbaColor? foreground,
        ref RgbaColor? edge)
    {
        double raw = Progress(elapsed, animate.Start, animate.End);
        double eased = Math.Pow(raw, Math.Max(0, animate.Accel));
        if (animate.ToSizePercent is int targetSize)
        {
            scale *= (float)Lerp(1, targetSize / 100.0, eased);
        }

        if (animate.ToForeground is RgbaColor targetForeground)
        {
            foreground = Interpolate(RgbaColor.White, targetForeground, eased);
        }

        if (animate.ToEdgeColor is RgbaColor targetEdge)
        {
            edge = Interpolate(RgbaColor.EdgeDefault, targetEdge, eased);
        }
    }

    public static SKPoint DeterministicShake(Guid cueId, long frameIndex, double radiusX, double radiusY)
    {
        Span<byte> bytes = stackalloc byte[16];
        cueId.TryWriteBytes(bytes);
        ulong seed = unchecked((ulong)frameIndex);
        for (int index = 0; index < bytes.Length; index++)
        {
            seed ^= (ulong)bytes[index] << ((index % 8) * 8);
            seed = Mix(seed);
        }
        double x = ToSignedUnit(Mix(seed));
        double y = ToSignedUnit(Mix(seed ^ 0x9E3779B97F4A7C15UL));
        return new SKPoint((float)(x * radiusX), (float)(y * radiusY));
    }

    private static float EvaluateFade(FadeEffect fade, TimeSpan elapsed, TimeSpan duration)
    {
        if (fade.Alpha1.HasValue && fade.Alpha2.HasValue && fade.Alpha3.HasValue &&
            fade.T1.HasValue && fade.T2.HasValue && fade.T3.HasValue && fade.T4.HasValue)
        {
            double alpha = elapsed <= fade.T2 ? Lerp(fade.Alpha1.Value, fade.Alpha2.Value,
                Progress(elapsed, fade.T1.Value, fade.T2.Value)) : Lerp(fade.Alpha2.Value, fade.Alpha3.Value,
                Progress(elapsed, fade.T3.Value, fade.T4.Value));
            return (float)Math.Clamp(1 - (alpha / 255.0), 0, 1);
        }
        if (fade.FadeIn > TimeSpan.Zero && elapsed < fade.FadeIn)
        {
            return (float)Progress(elapsed, TimeSpan.Zero, fade.FadeIn);
        }
        if (fade.FadeOut > TimeSpan.Zero && elapsed > duration - fade.FadeOut)
        {
            return (float)(1 - Progress(elapsed, duration - fade.FadeOut, duration));
        }
        return 1;
    }

    private static float EvaluateChroma(ChromaEffect effect, TimeSpan elapsed, TimeSpan duration)
    {
        if (effect.InTime > TimeSpan.Zero && elapsed < effect.InTime)
        {
            return (float)(1 - Progress(elapsed, TimeSpan.Zero, effect.InTime));
        }
        if (effect.OutTime > TimeSpan.Zero && elapsed > duration - effect.OutTime)
        {
            return (float)Progress(elapsed, duration - effect.OutTime, duration);
        }
        return 0;
    }

    private static bool IsActive(TimeSpan elapsed, TimeSpan? start, TimeSpan? end, TimeSpan duration)
        => elapsed >= (start ?? TimeSpan.Zero) && elapsed <= (end ?? duration);

    private static double Progress(TimeSpan value, TimeSpan start, TimeSpan end)
        => end <= start ? (value >= end ? 1 : 0) : Math.Clamp((value - start).TotalMilliseconds /
            (end - start).TotalMilliseconds, 0, 1);

    private static double Lerp(double from, double to, double progress) => from + ((to - from) * progress);

    private static RgbaColor Interpolate(RgbaColor from, RgbaColor to, double progress)
        => new(
            (byte)Math.Round(Lerp(from.Red, to.Red, progress)),
            (byte)Math.Round(Lerp(from.Green, to.Green, progress)),
            (byte)Math.Round(Lerp(from.Blue, to.Blue, progress)),
            (byte)Math.Round(Lerp(from.Alpha, to.Alpha, progress)));

    private static ulong Mix(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private static double ToSignedUnit(ulong value) => ((value >> 11) * (1.0 / (1UL << 53)) * 2) - 1;

    private readonly record struct MoveEvaluation(
        SKPoint Translation,
        bool IsActive,
        TimeSpan Start,
        TimeSpan End)
    {
        public bool IsValid => End >= Start;
        public static MoveEvaluation Invalid => new(SKPoint.Empty, false, TimeSpan.Zero, TimeSpan.FromTicks(-1));
    }
}
