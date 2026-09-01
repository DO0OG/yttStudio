

namespace YttStudio.Core.Format;

/// <summary>고정된 변환기를 통해 자막 형식을 가져오고 내보낸다.</summary>
public sealed partial class SubtitleFileService
{
    private static IReadOnlyList<ExportCue> ExpandMotionCues(IReadOnlyList<Cue> cues)
    {
        List<ExportCue> result = [];
        foreach (Cue cue in cues)
        {
            if (TryExpandMotionCue(cue, out IReadOnlyList<ExportCue> expanded))
                result.AddRange(expanded);
            else
                result.Add(ExportCue.FromCue(cue));
        }

        return result;
    }

    private static bool TryExpandMotionCue(Cue cue, out IReadOnlyList<ExportCue> expanded)
    {
        expanded = [];
        TimeSpan duration = cue.End - cue.Start;
        if (duration <= TimeSpan.Zero)
            return false;

        List<MotionSpan> spans = [];
        foreach (MoveEffect move in cue.Effects.OfType<MoveEffect>())
        {
            if (move.Keyframes.Count > 1)
            {
                for (int index = 0; index < move.Keyframes.Count - 1; index++)
                {
                    MotionKeyframe from = move.Keyframes[index];
                    MotionKeyframe to = move.Keyframes[index + 1];
                    spans.Add(new MotionSpan(
                        from.RelativeTime,
                        to.RelativeTime,
                        from.X,
                        from.Y,
                        to.X,
                        to.Y,
                        from.Interpolation,
                        from.Acceleration,
                        spans.Count));
                }
            }
            else if (move.Keyframes.Count == 1)
            {
                MotionKeyframe point = move.Keyframes[0];
                spans.Add(new MotionSpan(
                    point.RelativeTime,
                    point.RelativeTime,
                    point.X,
                    point.Y,
                    point.X,
                    point.Y,
                    MotionInterpolation.Step,
                    1,
                    spans.Count));
            }
            else
            {
                spans.Add(new MotionSpan(
                    move.StartTime ?? TimeSpan.Zero,
                    move.EndTime ?? duration,
                    move.FromX,
                    move.FromY,
                    move.ToX,
                    move.ToY,
                    MotionInterpolation.Linear,
                    1,
                    spans.Count));
            }
        }

        // 기존 단일 시간 이동은 외부 변환기에 애니메이션 하나로 전달된다.
        // 구간이 여러 개인 경로만 대사 구간으로 나눈다.
        if (spans.Count <= 1)
            return false;

        List<TimeSpan> boundaries = [TimeSpan.Zero, duration];
        foreach (MotionSpan span in spans)
        {
            TimeSpan start = Clamp(span.Start, TimeSpan.Zero, duration);
            TimeSpan end = Clamp(span.End, TimeSpan.Zero, duration);
            if (end > start)
            {
                boundaries.Add(start);
                boundaries.Add(end);
            }
        }

        boundaries.Sort();
        List<TimeSpan> distinctBoundaries = [];
        foreach (TimeSpan boundary in boundaries)
        {
            if (distinctBoundaries.Count == 0 || distinctBoundaries[^1] != boundary)
                distinctBoundaries.Add(boundary);
        }

        if (distinctBoundaries.Count < 2)
            return false;

        List<ExportCue> slices = [];
        for (int index = 0; index < distinctBoundaries.Count - 1; index++)
        {
            TimeSpan start = distinctBoundaries[index];
            TimeSpan end = distinctBoundaries[index + 1];
            if (end <= start)
                continue;

            MotionSpan? active = FindActiveSpan(spans, start, end);
            MotionPoint from;
            MotionPoint to;
            if (active is MotionSpan activeSpan)
            {
                from = EvaluateMotionPoint(activeSpan, start);
                to = EvaluateMotionPoint(activeSpan, end);
            }
            else
            {
                // 경로가 끊긴 구간은 가장 가까운 끝점에서 정지한다.
                // 존재하지 않는 보간 연결 구간은 만들지 않는다.
                MotionPoint hold = FindHoldPoint(spans, start, end,
                    new MotionPoint(cue.PositionX, cue.PositionY));
                from = hold;
                to = hold;
            }

            TimeSpan sliceDuration = end - start;
            MoveEffect segment = new(
                from.X,
                from.Y,
                to.X,
                to.Y,
                TimeSpan.Zero,
                sliceDuration);
            slices.Add(new ExportCue(
                cue,
                cue.Start + start,
                cue.Start + end,
                new MotionPoint(from.X, from.Y),
                ReplaceMoveEffects(cue, segment)));
        }

        if (slices.Count <= 1)
            return false;

        expanded = slices;
        return true;
    }

    private static MotionSpan? FindActiveSpan(
        IReadOnlyList<MotionSpan> spans,
        TimeSpan start,
        TimeSpan end)
    {
        foreach (MotionSpan span in spans.OrderBy(span => span.Order))
        {
            if (span.End > span.Start && span.Start <= start && span.End >= end)
                return span;
        }

        return null;
    }

    private static MotionPoint FindHoldPoint(
        IReadOnlyList<MotionSpan> spans,
        TimeSpan start,
        TimeSpan end,
        MotionPoint fallback)
    {
        MotionSpan? previous = spans
            .Where(span => span.End >= span.Start && span.End <= start)
            .OrderByDescending(span => span.End)
            .ThenBy(span => span.Order)
            .FirstOrDefault();
        if (previous is MotionSpan previousSpan)
            return EvaluateMotionPoint(previousSpan, previousSpan.End);

        MotionSpan? next = spans
            .Where(span => span.End >= span.Start && span.Start >= end)
            .OrderBy(span => span.Start)
            .ThenBy(span => span.Order)
            .FirstOrDefault();
        return next is MotionSpan nextSpan
            ? EvaluateMotionPoint(nextSpan, nextSpan.Start)
            : fallback;
    }

    private static MotionPoint EvaluateMotionPoint(MotionSpan span, TimeSpan time)
    {
        double progress = span.End <= span.Start
            ? time >= span.End ? 1 : 0
            : Math.Clamp((time - span.Start).TotalMilliseconds /
                (span.End - span.Start).TotalMilliseconds, 0, 1);
        double exponent = double.IsFinite(span.Acceleration) && span.Acceleration > 0
            ? span.Acceleration
            : 1;
        progress = span.Interpolation switch
        {
            MotionInterpolation.Step => progress >= 1 ? 1 : 0,
            MotionInterpolation.EaseIn => Math.Pow(progress, exponent),
            MotionInterpolation.EaseOut => 1 - Math.Pow(1 - progress, exponent),
            MotionInterpolation.EaseInOut => progress < 0.5
                ? 0.5 * Math.Pow(progress * 2, exponent)
                : 1 - (0.5 * Math.Pow((1 - progress) * 2, exponent)),
            _ => exponent == 1 ? progress : Math.Pow(progress, exponent),
        };

        return new MotionPoint(
            Lerp(span.FromX, span.ToX, progress),
            Lerp(span.FromY, span.ToY, progress));
    }

    private static IReadOnlyList<CueEffect> ReplaceMoveEffects(Cue cue, MoveEffect replacement)
    {
        List<CueEffect> effects = [];
        bool replaced = false;
        foreach (CueEffect effect in cue.Effects)
        {
            if (effect is MoveEffect)
            {
                if (!replaced)
                {
                    effects.Add(replacement);
                    replaced = true;
                }

                continue;
            }

            effects.Add(effect);
        }

        if (!replaced)
            effects.Add(replacement);
        return effects;
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan minimum, TimeSpan maximum)
        => value < minimum ? minimum : value > maximum ? maximum : value;

    private static double Lerp(double from, double to, double progress)
        => from + ((to - from) * progress);

    private sealed record ExportCue(
        Cue Cue,
        TimeSpan Start,
        TimeSpan End,
        MotionPoint? PixelPosition,
        IReadOnlyList<CueEffect> Effects)
    {
        public static ExportCue FromCue(Cue cue)
            => new(cue, cue.Start, cue.End, null, cue.Effects);
    }

    private sealed record MotionSpan(
        TimeSpan Start,
        TimeSpan End,
        double FromX,
        double FromY,
        double ToX,
        double ToY,
        MotionInterpolation Interpolation,
        double Acceleration,
        int Order);

    private readonly record struct MotionPoint(double X, double Y);
}
