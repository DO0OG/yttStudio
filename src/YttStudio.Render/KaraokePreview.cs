using System.Text;
using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render;

/// <summary>렌더러와 테스트가 함께 쓰는 결정적 가라오케 미리보기 계산을 제공한다.</summary>
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

    /// <summary>큐에 있다면 마지막 가라오케 설정 효과를 가져온다.</summary>
    public static KaraokeSettings? GetSettings(Cue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        return cue.Effects.OfType<KaraokeSettings>().LastOrDefault();
    }

    /// <summary>
    /// 실효 미리보기 타입을 가져온다. 오프셋만 있고 설정이 없는 큐는
    /// 기존 Simple 미리보기 동작을 유지한다.
    /// </summary>
    public static KaraokeType GetType(Cue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        return GetSettings(cue)?.Type ??
            (cue.Sections.Any(section => section.KaraokeOffset.HasValue)
                ? KaraokeType.Simple
                : KaraokeType.None);
    }

    /// <summary>섹션이 가라오케 시작 오프셋에 도달했는지 판단한다.</summary>
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
    /// 섹션의 결정적 페이드 진행률을 0..1 범위로 가져온다.
    /// </summary>
    /// <remarks>
    /// [PRODUCT] 편집기 미리보기는 각 가라오케 경계에서 500 ms 전환을 쓴다.
    /// upstream 의 FadeKaraokeType 페이드인 구간과 맞춘다.
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

    /// <summary>큐의 실효 가라오케 타입에 맞춰 섹션의 미리보기 색을 해석한다.</summary>
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

    /// <summary>커서 텍스트를 가져온다. 지정하지 않으면 upstream 의 밑줄 기본값을 쓴다.</summary>
    public static string GetCursorText(KaraokeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return string.IsNullOrEmpty(settings.CursorText) ? "_" : settings.CursorText;
    }

    /// <summary>
    /// 큐 기준 경과 시간에 대한 커서 애니메이션 프레임 인덱스를 가져온다.
    /// <see cref="KaraokeSettings.CursorText"/> 는 프레임 간 의도적으로 고정된다.
    /// 편집기 모델은 커서 프레임 하나만 저장하고 간격 값이 프레임 주기를 정한다.
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
    /// 지원되는 미창 구간 문자만 같은 스크립트의 결정적 문자로 바꾼다.
    /// 큐 식별자와 프레임 인덱스와 문자 인덱스가 모두 시드에 참여하므로
    /// 같은 프레임으로 되감으면 같은 글리치가 재현된다.
    /// </summary>
    /// <remarks>
    /// [PRODUCT] 라틴과 한글과 히라가나와 가타카나와 한자는
    /// 원래 스크립트 안에서 유지하고, 문장부호와 미지원 기호는 바꾸지 않는다.
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
        if (TryGetPrimaryScriptRange(value, out rangeStart, out rangeEnd) ||
            TryGetCjkScriptRange(value, out rangeStart, out rangeEnd))
        {
            return true;
        }

        rangeStart = 0;
        rangeEnd = 0;
        return false;
    }

    private static bool TryGetPrimaryScriptRange(int value, out int rangeStart, out int rangeEnd)
    {
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

        rangeStart = 0;
        rangeEnd = 0;
        return false;
    }

    private static bool TryGetCjkScriptRange(int value, out int rangeStart, out int rangeEnd)
    {
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
