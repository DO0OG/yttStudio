namespace YttStudio.Core;

/// <summary>Base type for an effect applied to a complete cue.</summary>
public abstract class CueEffect
{
}

/// <summary>Identifies an effect exposed by the M3 property panel.</summary>
public enum CueEffectKind
{
    Move,
    Fade,
    Shake,
    Chroma,
    Animate,
}

/// <summary>Interpolates a cue position between two ASS coordinates.</summary>
public sealed class MoveEffect : CueEffect
{
    public MoveEffect()
    {
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
    }

    public double FromX { get; internal set; }
    public double FromY { get; internal set; }
    public double ToX { get; internal set; }
    public double ToY { get; internal set; }
    public TimeSpan? StartTime { get; internal set; }
    public TimeSpan? EndTime { get; internal set; }
}

/// <summary>Fades a cue in and out, optionally using the four-point ASS fade form.</summary>
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

/// <summary>Applies deterministic bounded position jitter to a cue.</summary>
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

/// <summary>Creates offset coloured copies of a cue while it enters or exits.</summary>
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

/// <summary>Animates section colour, edge colour, or size using the ASS transform tag.</summary>
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

/// <summary>Identifies the karaoke progression used for a cue.</summary>
public enum KaraokeType
{
    None,
    Simple,
    Fade,
    Glitch,
    Cursor,
    LeftCursor,
}

/// <summary>Stores the karaoke effect mode and optional cursor settings.</summary>
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
