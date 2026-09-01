namespace YttStudio.Core.Editing;

/// <summary>모션 키프레임을 불변 경로 교체 방식으로 편집한다.</summary>
public sealed partial class DocumentEditor
{
    /// <summary>큐의 첫 모션 경로에 키프레임을 추가한다.</summary>
    public void AddKeyframe(Guid cueId, MotionKeyframe keyframe)
        => AddKeyframe(cueId, 0, keyframe);

    /// <summary>지정한 모션 경로에 키프레임을 추가한다.</summary>
    public void AddKeyframe(Guid cueId, int effectIndex, MotionKeyframe keyframe)
    {
        ArgumentNullException.ThrowIfNull(keyframe);
        Cue cue = GetCue(cueId);
        List<CueEffect> next = cue.Effects.Select(CloneEffect).ToList();
        int moveIndex = FindMoveEffectIndex(next, effectIndex, allowCreate: true);
        MotionKeyframe[] path = moveIndex < 0
            ? []
            : ToKeyframes((MoveEffect)next[moveIndex], cue);
        MotionKeyframe[] updated = [.. path, CloneKeyframe(keyframe)];

        if (moveIndex < 0)
        {
            next.Add(new MoveEffect(updated));
        }
        else
        {
            next[moveIndex] = new MoveEffect(updated);
        }

        Execute(new ReplaceEffectsCommand(cue, next));
    }

    /// <summary>ASS 좌표로 첫 모션 경로에 키프레임을 추가한다.</summary>
    public void AddKeyframe(
        Guid cueId,
        TimeSpan relativeTime,
        double x,
        double y,
        MotionInterpolation interpolation = MotionInterpolation.Linear,
        double acceleration = 1.0)
        => AddKeyframe(cueId, new MotionKeyframe(relativeTime, x, y, interpolation, acceleration));

    /// <summary>ASS 좌표로 지정한 모션 경로에 키프레임을 추가한다.</summary>
    public void AddKeyframe(
        Guid cueId,
        int effectIndex,
        TimeSpan relativeTime,
        double x,
        double y,
        MotionInterpolation interpolation = MotionInterpolation.Linear,
        double acceleration = 1.0)
        => AddKeyframe(cueId, effectIndex,
            new MotionKeyframe(relativeTime, x, y, interpolation, acceleration));

    /// <summary>
    /// 키프레임 하나를 교체한다. 새 불변 경로는 상대 시각순으로 정렬되므로
    /// 키프레임 시각을 바꾸면 경로 안의 위치도 함께 바뀐다.
    /// </summary>
    public void MoveKeyframe(Guid cueId, int keyframeIndex, MotionKeyframe replacement)
        => MoveKeyframe(cueId, 0, keyframeIndex, replacement);

    /// <summary>지정한 모션 경로의 키프레임 하나를 교체한다.</summary>
    public void MoveKeyframe(
        Guid cueId,
        int effectIndex,
        int keyframeIndex,
        MotionKeyframe replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        Cue cue = GetCue(cueId);
        List<CueEffect> next = cue.Effects.Select(CloneEffect).ToList();
        int moveIndex = FindMoveEffectIndex(next, effectIndex, allowCreate: false);
        MotionKeyframe[] path = ToKeyframes((MoveEffect)next[moveIndex], cue);
        if ((uint)keyframeIndex >= (uint)path.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(keyframeIndex));
        }

        path[keyframeIndex] = CloneKeyframe(replacement);
        next[moveIndex] = new MoveEffect(path);
        Execute(new ReplaceEffectsCommand(cue, next));
    }

    /// <summary>명시한 값으로 첫 모션 경로의 키프레임 하나를 교체한다.</summary>
    public void MoveKeyframe(
        Guid cueId,
        int keyframeIndex,
        TimeSpan relativeTime,
        double x,
        double y,
        MotionInterpolation interpolation = MotionInterpolation.Linear,
        double acceleration = 1.0)
        => MoveKeyframe(cueId, keyframeIndex,
            new MotionKeyframe(relativeTime, x, y, interpolation, acceleration));

    /// <summary>첫 모션 경로에서 키프레임 하나를 제거한다.</summary>
    public void DeleteKeyframe(Guid cueId, int keyframeIndex)
        => DeleteKeyframe(cueId, 0, keyframeIndex);

    /// <summary>지정한 모션 경로에서 키프레임 하나를 제거한다.</summary>
    public void DeleteKeyframe(Guid cueId, int effectIndex, int keyframeIndex)
    {
        Cue cue = GetCue(cueId);
        List<CueEffect> next = cue.Effects.Select(CloneEffect).ToList();
        int moveIndex = FindMoveEffectIndex(next, effectIndex, allowCreate: false);
        MotionKeyframe[] path = ToKeyframes((MoveEffect)next[moveIndex], cue);
        if ((uint)keyframeIndex >= (uint)path.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(keyframeIndex));
        }

        if (path.Length == 1)
        {
            // 마지막 키프레임을 지우면 경로도 제거한다. 빈 MoveEffect를 남기면
            // 기존 형식의 (0,0)->(0,0) 이동으로 조용히 바뀌기 때문이다.
            next.RemoveAt(moveIndex);
        }
        else
        {
            next[moveIndex] = new MoveEffect(path.Where((_, index) => index != keyframeIndex));
        }
        Execute(new ReplaceEffectsCommand(cue, next));
    }

    /// <summary>첫 모션 경로의 모든 키프레임을 교체한다.</summary>
    public void ReplaceKeyframes(Guid cueId, IEnumerable<MotionKeyframe> replacements)
        => ReplaceKeyframes(cueId, 0, replacements);

    /// <summary>지정한 모션 경로의 모든 키프레임을 교체한다.</summary>
    public void ReplaceKeyframes(
        Guid cueId,
        int effectIndex,
        IEnumerable<MotionKeyframe> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        Cue cue = GetCue(cueId);
        List<CueEffect> next = cue.Effects.Select(CloneEffect).ToList();
        int moveIndex = FindMoveEffectIndex(next, effectIndex, allowCreate: true);
        MotionKeyframe[] path = replacements.Select(CloneKeyframe).ToArray();
        if (moveIndex < 0)
        {
            if (path.Length > 0)
            {
                next.Add(new MoveEffect(path));
            }
        }
        else if (path.Length == 0)
        {
            next.RemoveAt(moveIndex);
        }
        else
        {
            next[moveIndex] = new MoveEffect(path);
        }

        Execute(new ReplaceEffectsCommand(cue, next));
    }

    private static int FindMoveEffectIndex(
        IReadOnlyList<CueEffect> effects,
        int effectIndex,
        bool allowCreate)
    {
        if (effectIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(effectIndex));
        }

        int moveIndex = -1;
        int current = 0;
        for (int index = 0; index < effects.Count; index++)
        {
            if (effects[index] is not MoveEffect)
            {
                continue;
            }

            if (current++ == effectIndex)
            {
                moveIndex = index;
                break;
            }
        }

        if (moveIndex < 0 && !allowCreate)
        {
            throw new KeyNotFoundException($"Move effect {effectIndex} does not exist.");
        }

        if (moveIndex < 0 && effectIndex != 0)
        {
            throw new KeyNotFoundException($"Move effect {effectIndex} does not exist.");
        }

        return moveIndex;
    }

    private static MotionKeyframe[] ToKeyframes(MoveEffect move, Cue cue)
    {
        if (move.Keyframes.Count > 0)
        {
            return move.Keyframes.Select(CloneKeyframe).ToArray();
        }

        TimeSpan start = move.StartTime ?? TimeSpan.Zero;
        TimeSpan end = move.EndTime ?? (cue.End - cue.Start);
        return
        [
            new MotionKeyframe(start, move.FromX, move.FromY),
            new MotionKeyframe(end, move.ToX, move.ToY),
        ];
    }

    private static MotionKeyframe CloneKeyframe(MotionKeyframe source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new MotionKeyframe(
            source.RelativeTime,
            source.X,
            source.Y,
            source.Interpolation,
            source.Acceleration);
    }
}
