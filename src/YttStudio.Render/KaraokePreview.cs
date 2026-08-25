using System.Text;
using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render;

/// <summary>Provides deterministic karaoke preview calculations shared by the renderer and tests.</summary>
public static class KaraokePreview
{
    private static readonly TimeSpan FadeTransitionDuration = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DefaultCursorInterval = TimeSpan.FromHours(1);

    private const int LatinUpperStart = 'A';
    private const int LatinUpperEnd = 'Z';
    private const int LatinLowerStart = 'a';
    private const int LatinLowerEnd = 'z';
    private const int HangulStart = 0xAC00;
    private const int HangulEnd = 0xD7A3;
    private const int HiraganaStart = 0x3041;
    private const int HiraganaEnd = 0x3096;
    private const int KatakanaStart = 0x30A1;
    private const int KatakanaEnd = 0x30FA;
    private const ulong CharacterIndexMix = 0x9E3779B97F4A7C15UL;

    /// <summary>Gets the last karaoke settings effect, if the cue has one.</summary>
    public static KaraokeSettings? GetSettings(Cue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        return cue.Effects.OfType<KaraokeSettings>().LastOrDefault();
    }

    /// <summary>
    /// Gets the effective preview type. Cues with offsets but no explicit settings retain the
    /// legacy Simple preview behavior.
    /// </summary>
    public static KaraokeType GetType(Cue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        return GetSettings(cue)?.Type ??
            (cue.Sections.Any(section => section.KaraokeOffset.HasValue)
                ? KaraokeType.Simple
                : KaraokeType.None);
    }

    /// <summary>Determines whether a section has reached its karaoke start offset.</summary>
    public static bool IsSung(Cue cue, Section section, TimeSpan time)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(section);

        if (GetType(cue) == KaraokeType.None || section.KaraokeOffset is not TimeSpan offset)
        {
            return true;
        }

        return time - cue.Start >= offset;
    }

    /// <summary>
    /// Gets the deterministic Fade progress for a section in the range 0..1.
    /// </summary>
    /// <remarks>
    /// SPEC §7.6 [PRODUCT]: the editor preview uses a named 500 ms transition at each karaoke
    /// boundary, matching the upstream FadeKaraokeType's fade-in window.
    /// </remarks>
    public static double GetFadeProgress(Cue cue, Section section, TimeSpan time)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(section);

        if (section.KaraokeOffset is not TimeSpan offset)
        {
            return 1;
        }

        TimeSpan elapsed = time - cue.Start;
        if (elapsed <= offset)
        {
            return 0;
        }

        TimeSpan fadeEnd = offset + FadeTransitionDuration;
        if (elapsed >= fadeEnd)
        {
            return 1;
        }

        return (elapsed - offset).TotalMilliseconds / FadeTransitionDuration.TotalMilliseconds;
    }

    /// <summary>Resolves a section's preview color for the cue's effective karaoke type.</summary>
    public static RgbaColor ResolveColor(Cue cue, Section section, ResolvedFormat format, TimeSpan time)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(format);

        KaraokeType type = GetType(cue);
        if (type == KaraokeType.None || section.KaraokeOffset is null)
        {
            return format.Foreground;
        }

        if (!IsSung(cue, section, time))
        {
            return format.SecondaryColor;
        }

        return type == KaraokeType.Fade
            ? Interpolate(format.SecondaryColor, format.Foreground, GetFadeProgress(cue, section, time))
            : format.Foreground;
    }

    /// <summary>Gets the cursor text, using the upstream underscore fallback when unspecified.</summary>
    public static string GetCursorText(KaraokeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return string.IsNullOrEmpty(settings.CursorText) ? "_" : settings.CursorText;
    }

    /// <summary>
    /// Gets the cursor animation frame index for a cue-relative elapsed time. A single
    /// <see cref="KaraokeSettings.CursorText"/> is intentionally stable across frames because
    /// the editor model stores one cursor frame; the interval still defines its frame cadence.
    /// </summary>
    public static long GetCursorFrameIndex(TimeSpan elapsed, TimeSpan? interval)
    {
        TimeSpan effectiveInterval = interval is TimeSpan value && value > TimeSpan.Zero
            ? value
            : DefaultCursorInterval;
        if (elapsed <= TimeSpan.Zero)
        {
            return 0;
        }

        return elapsed.Ticks / effectiveInterval.Ticks;
    }

    /// <summary>
    /// Replaces only supported unsung-script characters with deterministic characters from the
    /// same script. The cue identifier, frame index, and character index all participate in the
    /// seed so scrubbing the same frame reproduces the same glitch.
    /// </summary>
    /// <remarks>
    /// SPEC §7.6 [PRODUCT]: Latin, Hangul, Hiragana, Katakana, and Han characters are kept in
    /// their source script; punctuation and unsupported symbols remain unchanged.
    /// </remarks>
    public static string GetGlitchedText(Cue cue, string text, long frameIndex)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return text;
        }

        ulong seed = Seed(cue.Id, frameIndex);
        StringBuilder result = new(text.Length);
        int characterIndex = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            result.Append(GetGlitchedRune(rune, seed, characterIndex));
            characterIndex++;
        }

        return result.ToString();
    }

    private static Rune GetGlitchedRune(Rune source, ulong seed, int characterIndex)
    {
        if (!TryGetRange(source, out int rangeStart, out int rangeEnd))
        {
            return source;
        }

        ulong characterSeed = Mix(seed ^ ((ulong)characterIndex * CharacterIndexMix));
        int rangeLength = rangeEnd - rangeStart + 1;
        int selected = rangeStart + (int)(characterSeed % (ulong)rangeLength);
        if (rangeLength > 1 && selected == source.Value)
        {
            selected = rangeStart + ((selected - rangeStart + 1) % rangeLength);
        }

        return new Rune(selected);
    }

    private static bool TryGetRange(Rune rune, out int rangeStart, out int rangeEnd)
    {
        int value = rune.Value;
        if (value is >= LatinUpperStart and <= LatinUpperEnd)
        {
            rangeStart = LatinUpperStart;
            rangeEnd = LatinUpperEnd;
            return true;
        }

        if (value is >= LatinLowerStart and <= LatinLowerEnd)
        {
            rangeStart = LatinLowerStart;
            rangeEnd = LatinLowerEnd;
            return true;
        }

        if (value is >= HangulStart and <= HangulEnd)
        {
            rangeStart = HangulStart;
            rangeEnd = HangulEnd;
            return true;
        }

        if (value is >= HiraganaStart and <= HiraganaEnd)
        {
            rangeStart = HiraganaStart;
            rangeEnd = HiraganaEnd;
            return true;
        }

        if (value is >= KatakanaStart and <= KatakanaEnd)
        {
            rangeStart = KatakanaStart;
            rangeEnd = KatakanaEnd;
            return true;
        }

        if (value is >= 0x3400 and <= 0x4DBF)
        {
            rangeStart = 0x3400;
            rangeEnd = 0x4DBF;
            return true;
        }

        if (value is >= 0x4E00 and <= 0x9FFF)
        {
            rangeStart = 0x4E00;
            rangeEnd = 0x9FFF;
            return true;
        }

        if (value is >= 0xF900 and <= 0xFAFF)
        {
            rangeStart = 0xF900;
            rangeEnd = 0xFAFF;
            return true;
        }

        if (value is >= 0x20000 and <= 0x2FA1F)
        {
            rangeStart = 0x20000;
            rangeEnd = 0x2FA1F;
            return true;
        }

        rangeStart = 0;
        rangeEnd = 0;
        return false;
    }

    private static RgbaColor Interpolate(RgbaColor from, RgbaColor to, double progress)
        => new(
            (byte)Math.Round(from.Red + ((to.Red - from.Red) * progress)),
            (byte)Math.Round(from.Green + ((to.Green - from.Green) * progress)),
            (byte)Math.Round(from.Blue + ((to.Blue - from.Blue) * progress)),
            (byte)Math.Round(from.Alpha + ((to.Alpha - from.Alpha) * progress)));

    private static ulong Seed(Guid cueId, long frameIndex)
    {
        Span<byte> bytes = stackalloc byte[16];
        cueId.TryWriteBytes(bytes);
        ulong seed = unchecked((ulong)frameIndex);
        for (int index = 0; index < bytes.Length; index++)
        {
            seed ^= (ulong)bytes[index] << ((index % 8) * 8);
            seed = Mix(seed);
        }

        return seed;
    }

    private static ulong Mix(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
