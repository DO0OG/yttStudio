using System;

namespace YttStudio.Core;

/// <summary>
/// 동작 키프레임에서 시작하는 구간의 보간 방식이다.
/// </summary>
public enum MotionInterpolation
{
    Linear,
    Step,
    EaseIn,
    EaseOut,
    EaseInOut,
}

/// <summary>
/// 큐 시작 시각을 기준으로 한 위치 키프레임이다.
/// 키프레임은 불변 값이며 편집 코드는 목록을 직접 바꾸지 않고
/// 키프레임을 담은 <see cref="MoveEffect"/> 전체를 교체한다.
/// 좌표는 기존 MoveEffect 필드와 같은 ASS 픽셀 공간을 사용한다.
/// </summary>
public sealed record class MotionKeyframe
{
    public MotionKeyframe()
    {
    }

    public MotionKeyframe(
        TimeSpan relativeTime,
        double x,
        double y,
        MotionInterpolation interpolation = MotionInterpolation.Linear,
        double acceleration = 1.0)
    {
        RelativeTime = relativeTime;
        X = x;
        Y = y;
        Interpolation = interpolation;
        Acceleration = acceleration;
    }

    /// <summary>큐 시작 시각을 기준으로 한 상대 시각이다.</summary>
    public TimeSpan RelativeTime { get; init; }

    /// <summary>ASS 좌표계의 가로 좌표다.</summary>
    public double X { get; init; }

    /// <summary>ASS 좌표계의 세로 좌표다.</summary>
    public double Y { get; init; }

    /// <summary>다음 키프레임까지 적용할 보간 방식이다.</summary>
    public MotionInterpolation Interpolation { get; init; } = MotionInterpolation.Linear;

    /// <summary>
    /// 보간 곡선의 형태를 정하는 값이다. 0 이하의 값은 렌더러에서 1로 처리한다.
    /// </summary>
    public double Acceleration { get; init; } = 1.0;

}
