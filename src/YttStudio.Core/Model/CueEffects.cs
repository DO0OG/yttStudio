using System.Collections.ObjectModel;

namespace YttStudio.Core;

/// <summary>큐 전체에 적용되는 효과의 기반 타입이다.</summary>
public abstract class CueEffect
{
}

/// <summary>속성 패널이 노출하는 효과를 식별한다.</summary>
public enum CueEffectKind
{
    Move,
    Fade,
    Shake,
    Chroma,
    Animate,
}

/// <summary>두 ASS 좌표 사이에서 큐 위치를 보간한다.</summary>
public sealed class MoveEffect : CueEffect
{
    private readonly MotionKeyframe[] keyframes;
    private readonly ReadOnlyCollection<MotionKeyframe> readOnlyKeyframes;

    public MoveEffect()
    {
        keyframes = [];
        readOnlyKeyframes = Array.AsReadOnly(keyframes);
    }

    public MoveEffect(double fromX, double fromY, double toX, double toY,
        TimeSpan? startTime = null, TimeSpan? endTime = null)
    {
        FromX = fromX;
        FromY = fromY;
        ToX = toX;
        ToY = toY;
        StartTime = startTime;
        EndTime = endTime;
        keyframes = [];
        readOnlyKeyframes = Array.AsReadOnly(keyframes);
    }

    /// <summary>불변인 큐 상대 키프레임으로 동작 경로를 만들고 시각순으로 정렬한다.</summary>
    public MoveEffect(IEnumerable<MotionKeyframe> keyframes)
    {
        ArgumentNullException.ThrowIfNull(keyframes);
        this.keyframes = keyframes
            .Select(CloneKeyframe)
            .OrderBy(keyframe => keyframe.RelativeTime)
            .ToArray();
        readOnlyKeyframes = Array.AsReadOnly(this.keyframes);

        if (this.keyframes.Length > 0)
        {
            MotionKeyframe first = this.keyframes[0];
            MotionKeyframe last = this.keyframes[^1];
            FromX = first.X;
            FromY = first.Y;
            ToX = last.X;
            ToY = last.Y;
            StartTime = first.RelativeTime;
            EndTime = last.RelativeTime;
        }
    }

    public double FromX { get; internal set; }
    public double FromY { get; internal set; }
    public double ToX { get; internal set; }
    public double ToY { get; internal set; }
    public TimeSpan? StartTime { get; internal set; }
    public TimeSpan? EndTime { get; internal set; }

    /// <summary>불변 큐 상대 동작 경로다. 빈 목록이면 기존 구간 필드를 사용한다.</summary>
    public IReadOnlyList<MotionKeyframe> Keyframes => readOnlyKeyframes;


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

/// <summary>큐를 서서히 나타내고 사라지게 한다. 네 점 ASS 페이드 형식도 쓸 수 있다.</summary>
public sealed class FadeEffect : CueEffect
{
    public FadeEffect()
    {
    }

    public FadeEffect(TimeSpan fadeIn, TimeSpan fadeOut)
    {
        FadeIn = fadeIn;
        FadeOut = fadeOut;
    }

    public TimeSpan FadeIn { get; internal set; }
    public TimeSpan FadeOut { get; internal set; }
    public int? Alpha1 { get; internal set; }
    public int? Alpha2 { get; internal set; }
    public int? Alpha3 { get; internal set; }
    public TimeSpan? T1 { get; internal set; }
    public TimeSpan? T2 { get; internal set; }
    public TimeSpan? T3 { get; internal set; }
    public TimeSpan? T4 { get; internal set; }
}

/// <summary>큐에 결정적이고 범위가 제한된 위치 흔들림을 적용한다.</summary>
public sealed class ShakeEffect : CueEffect
{
    public ShakeEffect()
    {
        RadiusX = 20;
        RadiusY = 20;
    }

    public ShakeEffect(double radiusX, double radiusY,
        TimeSpan? startTime = null, TimeSpan? endTime = null)
    {
        RadiusX = radiusX;
        RadiusY = radiusY;
        StartTime = startTime;
        EndTime = endTime;
    }

    public double RadiusX { get; internal set; }
    public double RadiusY { get; internal set; }
    public TimeSpan? StartTime { get; internal set; }
    public TimeSpan? EndTime { get; internal set; }
}

/// <summary>큐가 진입하거나 이탈하는 동안 색을 어긋나게 한 복제본을 만든다.</summary>
public sealed class ChromaEffect : CueEffect
{
    public ChromaEffect()
    {
        OffsetX = 20;
        OffsetY = 0;
        InTime = TimeSpan.FromMilliseconds(270);
        OutTime = TimeSpan.FromMilliseconds(270);
    }

    public ChromaEffect(double offsetX, double offsetY, TimeSpan inTime, TimeSpan outTime,
        IReadOnlyList<RgbaColor>? customColors = null)
    {
        OffsetX = offsetX;
        OffsetY = offsetY;
        InTime = inTime;
        OutTime = outTime;
        CustomColors = customColors;
    }

    public double OffsetX { get; internal set; }
    public double OffsetY { get; internal set; }
    public TimeSpan InTime { get; internal set; }
    public TimeSpan OutTime { get; internal set; }
    public IReadOnlyList<RgbaColor>? CustomColors { get; internal set; }
}

/// <summary>ASS 변환 태그로 섹션 색과 엣지 색과 크기를 애니메이션한다.</summary>
public sealed class AnimateEffect : CueEffect
{
    public AnimateEffect()
    {
        Accel = 1.0;
    }

    public AnimateEffect(TimeSpan start, TimeSpan end, double accel = 1.0)
    {
        Start = start;
        End = end;
        Accel = accel;
    }

    public TimeSpan Start { get; internal set; }
    public TimeSpan End { get; internal set; }
    public double Accel { get; internal set; }
    public RgbaColor? ToForeground { get; internal set; }
    public RgbaColor? ToEdgeColor { get; internal set; }
    public int? ToSizePercent { get; internal set; }
}

/// <summary>큐에 쓰이는 가라오케 진행 방식을 식별한다.</summary>
public enum KaraokeType
{
    None,
    Simple,
    Fade,
    Glitch,
    Cursor,
    LeftCursor,
}

/// <summary>가라오케 효과 모드와 선택적 커서 설정을 담는다.</summary>
public sealed class KaraokeSettings : CueEffect
{
    public KaraokeSettings()
    {
        Type = KaraokeType.Simple;
    }

    public KaraokeSettings(KaraokeType type)
    {
        Type = type;
    }

    public KaraokeType Type { get; internal set; }
    public string? CursorText { get; internal set; }
    public TimeSpan? CursorInterval { get; internal set; }
}
