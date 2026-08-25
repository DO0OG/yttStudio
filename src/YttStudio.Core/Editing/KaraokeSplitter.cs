using System.Buffers;
using System.Globalization;
using System.Text;

namespace YttStudio.Core.Editing;

/// <summary>자막 텍스트를 가라오케에 적합한 유니코드 단위로 분할한다.</summary>
public sealed class KaraokeSplitter
{
    /// <summary>
    /// 분할 규칙은 다음과 같다. 한글과 한자는 글자 단위, 가나는 가나 단위,
    /// 라틴 텍스트는 단어 단위로 묶고 공백은 유지한다.
    /// </summary>
    public IReadOnlyList<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return [];
        }

        List<KaraokeToken> tokens = [];
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            string element = (string)elements.Current!;
            Rune firstRune = FirstRune(element);
            KaraokeTokenKind kind = Classify(firstRune, element);

            if (kind == KaraokeTokenKind.KanaSmall && tokens.Count > 0 &&
                tokens[^1].Kind == KaraokeTokenKind.Kana)
            {
                tokens[^1] = tokens[^1] with { Text = tokens[^1].Text + element };
                continue;
            }

            if (kind == KaraokeTokenKind.Latin && tokens.Count > 0 &&
                tokens[^1].Kind == KaraokeTokenKind.Latin)
            {
                tokens[^1] = tokens[^1] with { Text = tokens[^1].Text + element };
                continue;
            }

            if (kind == KaraokeTokenKind.Whitespace && tokens.Count > 0 &&
                tokens[^1].Kind == KaraokeTokenKind.Whitespace)
            {
                tokens[^1] = tokens[^1] with { Text = tokens[^1].Text + element };
                continue;
            }

            tokens.Add(new KaraokeToken(element, kind));
        }

        return tokens.Select(token => token.Text).ToArray();
    }

    /// <summary>텍스트만 다루는 호출자를 위한 상태 없는 진입점을 제공한다.</summary>
    public static IReadOnlyList<string> SplitText(string text) => new KaraokeSplitter().Split(text);

    private static KaraokeTokenKind Classify(Rune rune, string element)
    {
        if (element.All(char.IsWhiteSpace))
        {
            return KaraokeTokenKind.Whitespace;
        }

        if (IsKana(rune))
        {
            return IsSmallKana(rune) ? KaraokeTokenKind.KanaSmall : KaraokeTokenKind.Kana;
        }

        if (IsLatin(rune))
        {
            return KaraokeTokenKind.Latin;
        }

        if (IsHan(rune))
        {
            return KaraokeTokenKind.Han;
        }

        if (IsHangul(rune))
        {
            return KaraokeTokenKind.Hangul;
        }

        return KaraokeTokenKind.Other;
    }

    private static Rune FirstRune(string element)
    {
        OperationStatus status = Rune.DecodeFromUtf16(element, out Rune rune, out _);
        return status == OperationStatus.Done ? rune : Rune.ReplacementChar;
    }

    private static bool IsLatin(Rune rune)
    {
        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        bool isLetter = category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter;
        bool isNumber = category == UnicodeCategory.DecimalDigitNumber;
        if (!isLetter && !isNumber)
        {
            return false;
        }

        int value = rune.Value;
        return value is >= 0x0030 and <= 0x0039 or
            >= 0x0041 and <= 0x005A or
            >= 0x0061 and <= 0x007A or
            >= 0x00C0 and <= 0x02AF or
            >= 0x1E00 and <= 0x1EFF or
            >= 0x2C60 and <= 0x2C7F or
            >= 0xA720 and <= 0xA7FF or
            >= 0xAB30 and <= 0xAB6F or
            >= 0xFF10 and <= 0xFF19 or
            >= 0xFF21 and <= 0xFF3A or
            >= 0xFF41 and <= 0xFF5A;
    }

    private static bool IsKana(Rune rune)
    {
        int value = rune.Value;
        return value is >= 0x3041 and <= 0x3096 or
            >= 0x309D and <= 0x309F or
            >= 0x30A1 and <= 0x30FF or
            >= 0x31F0 and <= 0x31FF or
            >= 0xFF66 and <= 0xFF9D or
            >= 0x1B000 and <= 0x1B0FF;
    }

    private static bool IsSmallKana(Rune rune)
    {
        int value = rune.Value;
        return value is 0x3041 or 0x3043 or 0x3045 or 0x3047 or 0x3049 or
            0x3063 or 0x3083 or 0x3085 or 0x3087 or 0x308E or 0x3095 or 0x3096 or
            0x30A1 or 0x30A3 or 0x30A5 or 0x30A7 or 0x30A9 or 0x30C3 or
            0x30E3 or 0x30E5 or 0x30E7 or 0x30EE or 0x30F5 or 0x30F6 or
            >= 0xFF67 and <= 0xFF6B or 0xFF6C or 0xFF6D or 0xFF6E or 0xFF6F or
            >= 0x31F0 and <= 0x31FF or
            >= 0x1B130 and <= 0x1B13F;
    }

    private static bool IsHan(Rune rune)
    {
        int value = rune.Value;
        return value is >= 0x3400 and <= 0x4DBF or
            >= 0x4E00 and <= 0x9FFF or
            >= 0xF900 and <= 0xFAFF or
            >= 0x20000 and <= 0x323AF or
            >= 0x2F800 and <= 0x2FA1F;
    }

    private static bool IsHangul(Rune rune)
    {
        int value = rune.Value;
        return value is >= 0x1100 and <= 0x11FF or
            >= 0x3130 and <= 0x318F or
            >= 0xA960 and <= 0xA97F or
            >= 0xAC00 and <= 0xD7A3 or
            >= 0xD7B0 and <= 0xD7FF;
    }

    private enum KaraokeTokenKind
    {
        Other,
        Whitespace,
        Latin,
        Kana,
        KanaSmall,
        Han,
        Hangul,
    }

    private sealed record KaraokeToken(string Text, KaraokeTokenKind Kind);
}
