using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render;

/// <summary>Resolved, deterministic visual state for one cue and frame.</summary>
public sealed record CueEffectState(
    SKPoint Translation,
    float Alpha,
    float Scale,
    RgbaColor? Foreground,
    RgbaColor? EdgeColor,
    float ChromaAmount,
    ChromaEffect? Chroma);

/// <summary>Evaluates M3 cue effects without retaining frame-to-frame random state.</summary>
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

        foreach (CueEffect effect in cue.Effects)
        {
            switch (effect)
            {
                case MoveEffect move:
                    double moveProgress = Progress(elapsed, move.StartTime ?? TimeSpan.Zero,
                        move.EndTime ?? duration);
                    translation += new SKPoint(
                        (float)(Lerp(move.FromX, move.ToX, moveProgress) - baseAnchor.X),
                        (float)(Lerp(move.FromY, move.ToY, moveProgress) - baseAnchor.Y));
                    break;
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
                    break;
                default:
                    // Inactive or non-visual effects contribute nothing to this frame.
                    break;
            }
        }

        return new CueEffectState(translation, Math.Clamp(alpha, 0, 1), Math.Max(0.01f, scale),
            foreground, edge, chromaAmount, chromaEffect);
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
}
