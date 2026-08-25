using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace YttStudio.Core.Format;

/// <summary>효과 모델을 고정된 변환기의 ASS 태그로 인코딩한다.</summary>
internal static partial class AssEffectCodec
{
    private static readonly string[] EffectNames = ["fad", "fade", "move", "t", "ytshake", "ytchroma", "ytkt"];

    public static string SanitizeAndRead(string path, out List<IReadOnlyList<CueEffect>> effectsByLine)
    {
        string source = File.ReadAllText(path);
        effectsByLine = [];
        StringBuilder result = new(source.Length);
        string[] lines = source.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
            {
                int colon = line.IndexOf(':');
                string value = line[(colon + 1)..];
                string[] fields = value.Split(',', 10, StringSplitOptions.None);
                if (fields.Length == 10)
                {
                    IReadOnlyList<CueEffect> effects = Parse(fields[9]);
                    effectsByLine.Add(effects);
                    fields[9] = Strip(fields[9]);
                    line = line[..(colon + 1)] + string.Join(',', fields);
                }
            }

            if (i > 0)
                result.AppendLine();
            result.Append(line);
        }

        return result.ToString();
    }

    public static IReadOnlyList<CueEffect> Parse(string text)
    {
        List<CueEffect> result = [];
        foreach (Match block in OverrideBlockRegex().Matches(text))
        {
            foreach (Match tag in EffectTagRegex().Matches(block.Groups[1].Value))
            {
                string name = tag.Groups["name"].Value.ToLowerInvariant();
                string arg = tag.Groups["arg"].Value.Trim();
                CueEffect? effect = name switch
                {
                    "move" => ParseMove(arg),
                    "fad" or "fade" => ParseFade(name, arg),
                    "ytshake" => ParseShake(arg),
                    "ytchroma" => ParseChroma(arg),
                    "t" => ParseAnimate(arg),
                    "ytkt" => ParseKaraoke(arg),
                    _ => null,
                };
                if (effect is not null)
                    result.Add(effect);
            }
        }

        return result;
    }

    public static string Strip(string text)
    {
        string stripped = OverrideBlockRegex().Replace(text, match =>
        {
            string content = match.Groups[1].Value;
            content = EffectTagRegex().Replace(content, string.Empty);
            return string.IsNullOrWhiteSpace(content) ? string.Empty : "{" + content + "}";
        });
        return stripped;
    }

    public static string Encode(IReadOnlyList<CueEffect> effects)
    {
        StringBuilder tags = new();
        foreach (CueEffect effect in effects)
        {
            switch (effect)
            {
                case MoveEffect move:
                    tags.Append("\\move(").Append(F(move.FromX)).Append(',').Append(F(move.FromY)).Append(',')
                        .Append(F(move.ToX)).Append(',').Append(F(move.ToY));
                    if (move.StartTime.HasValue && move.EndTime.HasValue)
                        tags.Append(',').Append(Ms(move.StartTime.Value)).Append(',').Append(Ms(move.EndTime.Value));
                    tags.Append(')');
                    break;
                case FadeEffect fade when fade.Alpha1.HasValue && fade.Alpha2.HasValue && fade.Alpha3.HasValue &&
                    fade.T1.HasValue && fade.T2.HasValue && fade.T3.HasValue && fade.T4.HasValue:
                    tags.Append("\\fade(").Append(fade.Alpha1.Value).Append(',').Append(fade.Alpha2.Value).Append(',')
                        .Append(fade.Alpha3.Value).Append(',').Append(Ms(fade.T1.Value)).Append(',').Append(Ms(fade.T2.Value))
                        .Append(',').Append(Ms(fade.T3.Value)).Append(',').Append(Ms(fade.T4.Value)).Append(')');
                    break;
                case FadeEffect fade:
                    tags.Append("\\fad(").Append(Ms(fade.FadeIn)).Append(',').Append(Ms(fade.FadeOut)).Append(')');
                    break;
                case ShakeEffect shake:
                    tags.Append("\\ytshake(").Append(F(shake.RadiusX)).Append(',').Append(F(shake.RadiusY));
                    if (shake.StartTime.HasValue && shake.EndTime.HasValue)
                        tags.Append(',').Append(Ms(shake.StartTime.Value)).Append(',').Append(Ms(shake.EndTime.Value));
                    tags.Append(')');
                    break;
                case ChromaEffect chroma:
                    tags.Append("\\ytchroma(");
                    if (chroma.CustomColors is { Count: > 0 } colors)
                    {
                        foreach (RgbaColor color in colors)
                            tags.Append("&H").Append(color.Blue.ToString("X2", CultureInfo.InvariantCulture))
                                .Append(color.Green.ToString("X2", CultureInfo.InvariantCulture)).Append(color.Red.ToString("X2", CultureInfo.InvariantCulture)).Append('&').Append(',');
                        tags.Append("&H").Append((255 - colors[0].Alpha).ToString("X2", CultureInfo.InvariantCulture)).Append('&').Append(',');
                    }
                    tags.Append(F(chroma.OffsetX)).Append(',').Append(F(chroma.OffsetY)).Append(',')
                        .Append(Ms(chroma.InTime)).Append(',').Append(Ms(chroma.OutTime)).Append(')');
                    break;
                case AnimateEffect animate:
                    string modifiers = string.Empty;
                    if (animate.ToForeground is RgbaColor foreground)
                        modifiers += "\\1c" + Color(foreground);
                    if (animate.ToEdgeColor is RgbaColor edge)
                        modifiers += "\\3c" + Color(edge);
                    if (animate.ToSizePercent is int size)
                        modifiers += "\\fs" + size.ToString(CultureInfo.InvariantCulture);
                    if (modifiers.Length > 0)
                        tags.Append("\\t(").Append(Ms(animate.Start)).Append(',').Append(Ms(animate.End)).Append(',')
                            .Append(F(animate.Accel)).Append(',').Append(modifiers).Append(')');
                    break;
                case KaraokeSettings karaoke:
                    tags.Append("\\ytkt(").Append(karaoke.Type switch
                    {
                        KaraokeType.Fade => "fade",
                        KaraokeType.Glitch => "glitch",
                        KaraokeType.Cursor => "cursor",
                        KaraokeType.LeftCursor => "lcursor",
                        KaraokeType.None => "none",
                        _ => "simple",
                    });
                    if (karaoke.Type is KaraokeType.Cursor or KaraokeType.LeftCursor)
                    {
                        if (karaoke.CursorInterval.HasValue)
                            tags.Append(',').Append(Ms(karaoke.CursorInterval.Value));
                        if (karaoke.CursorText is not null)
                            tags.Append(',').Append(karaoke.CursorText);
                    }
                    tags.Append(')');
                    break;
                default:
                    // ASS 표현이 없는 효과 종류는 태그를 만들지 않는다.
                    break;
            }
        }

        return tags.Length == 0 ? string.Empty : "{" + tags + "}";
    }

    public static string Inject(string source, IReadOnlyList<IReadOnlyList<CueEffect>> effectsByLine)
    {
        StringBuilder result = new(source.Length + (effectsByLine.Count * 32));
        string[] lines = source.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        int dialogueIndex = 0;
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            if (line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
            {
                int colon = line.IndexOf(':');
                string[] fields = line[(colon + 1)..].Split(',', 10, StringSplitOptions.None);
                if (fields.Length == 10 && dialogueIndex < effectsByLine.Count)
                {
                    fields[9] = Encode(effectsByLine[dialogueIndex]) + fields[9];
                    line = line[..(colon + 1)] + string.Join(',', fields);
                }
                dialogueIndex++;
            }

            if (lineIndex > 0)
            {
                result.AppendLine();
            }
            result.Append(line);
        }
        return result.ToString();
    }

    private static CueEffect? ParseMove(string arg)
    {
        string[] a = Args(arg);
        return a.Length >= 4 && D(a[0], out double x1) && D(a[1], out double y1) && D(a[2], out double x2) && D(a[3], out double y2)
            ? new MoveEffect(x1, y1, x2, y2, a.Length >= 6 ? T(a[4]) : null, a.Length >= 6 ? T(a[5]) : null) : null;
    }

    private static CueEffect? ParseFade(string name, string arg)
    {
        string[] a = Args(arg);
        if (name == "fad" && a.Length == 2 && D(a[0], out double fadeIn) && D(a[1], out double fadeOut))
            return new FadeEffect(T(fadeIn), T(fadeOut));
        if (name == "fade" && a.Length == 7 && int.TryParse(a[0], out int a1) && int.TryParse(a[1], out int a2) && int.TryParse(a[2], out int a3))
        {
            FadeEffect fade = new() { Alpha1 = a1, Alpha2 = a2, Alpha3 = a3, T1 = T(a[3]), T2 = T(a[4]), T3 = T(a[5]), T4 = T(a[6]) };
            return fade;
        }
        return null;
    }

    private static CueEffect? ParseShake(string arg)
    {
        string[] a = Args(arg);
        if (a.Length == 0) return new ShakeEffect();
        if (a.Length >= 2 && D(a[0], out double x) && D(a[1], out double y))
            return new ShakeEffect(x, y, a.Length >= 4 ? T(a[2]) : null, a.Length >= 4 ? T(a[3]) : null);
        return D(a[0], out double radius) ? new ShakeEffect(radius, radius) : null;
    }

    private static CueEffect? ParseChroma(string arg)
    {
        string[] a = Args(arg);
        if (a.Length < 4 || !D(a[^4], out double x) || !D(a[^3], out double y) || !D(a[^2], out double inMs) || !D(a[^1], out double outMs)) return null;
        int colorCount = a.Length - 4;
        List<RgbaColor>? colors = colorCount > 1 ? [] : null;
        if (colors is not null)
            for (int i = 0; i < colorCount - 1; i++)
                if (TryColor(a[i], 255 - ParseHex(a[colorCount - 1]), out RgbaColor c)) colors.Add(c);
        return new ChromaEffect(x, y, T(inMs), T(outMs), colors);
    }

    private static CueEffect? ParseAnimate(string arg)
    {
        string[] a = Args(arg);
        if (a.Length < 4 || !D(a[0], out double start) || !D(a[1], out double end) || !D(a[2], out double accel)) return null;
        AnimateEffect effect = new(T(start), T(end), accel);
        string modifiers = string.Join(',', a.Skip(3));
        Match color = Regex.Match(modifiers, @"\\(?:1c|c)(?<v>&H[0-9A-Fa-f]+&)");
        if (color.Success && TryColor(color.Groups["v"].Value, 255, out RgbaColor foreground)) effect.ToForeground = foreground;
        color = Regex.Match(modifiers, @"\\3c(?<v>&H[0-9A-Fa-f]+&)");
        if (color.Success && TryColor(color.Groups["v"].Value, 255, out RgbaColor edge)) effect.ToEdgeColor = edge;
        Match size = Regex.Match(modifiers, @"\\fs(?<v>[-+]?\d+(?:\.\d+)?)");
        if (size.Success && int.TryParse(size.Groups["v"].Value, out int sizeValue)) effect.ToSizePercent = sizeValue;
        return effect;
    }

    private static CueEffect ParseKaraoke(string arg)
    {
        string[] a = Args(arg);
        string name = a.Length == 0 ? "simple" : a[0].ToLowerInvariant();
        KaraokeType type = name switch { "fade" => KaraokeType.Fade, "glitch" => KaraokeType.Glitch, "cursor" => KaraokeType.Cursor, "lcursor" => KaraokeType.LeftCursor, "none" => KaraokeType.None, _ => KaraokeType.Simple };
        KaraokeSettings result = new(type);
        if (type is KaraokeType.Cursor or KaraokeType.LeftCursor)
        {
            int? interval = a.Length > 1 && int.TryParse(a[1], out int parsedInterval)
                ? parsedInterval
                : null;
            int index = interval.HasValue ? 2 : 1;
            if (interval.HasValue) result.CursorInterval = TimeSpan.FromMilliseconds(interval.Value);
            if (a.Length > index) result.CursorText = a[index];
        }
        return result;
    }

    private static string[] Args(string value) => value.Trim().TrimStart('(').TrimEnd(')').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static bool D(string value, out double number) => double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    private static TimeSpan T(string value) => D(value, out double ms) ? T(ms) : TimeSpan.Zero;
    private static TimeSpan T(double ms) => TimeSpan.FromMilliseconds(ms);
    private static int Ms(TimeSpan value) => checked((int)Math.Round(value.TotalMilliseconds));
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Color(RgbaColor value) => $"&H{value.Blue:X2}{value.Green:X2}{value.Red:X2}&";
    private static int ParseHex(string value) => int.TryParse(value.Trim().Trim('&').TrimStart('H', 'h'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int n) ? n & 255 : 0;

    private static bool TryColor(string value, int alpha, out RgbaColor color)
    {
        string hex = value.Trim().Trim('&').TrimStart('H', 'h');
        if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int number))
        {
            color = new RgbaColor((byte)(number & 255), (byte)((number >> 8) & 255), (byte)((number >> 16) & 255), (byte)Math.Clamp(alpha, 0, 255));
            return true;
        }
        color = default;
        return false;
    }

    [GeneratedRegex(@"\{([^}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex OverrideBlockRegex();

    [GeneratedRegex(@"\\(?<name>fad|fade|move|t|ytshake|ytchroma|ytkt)(?<arg>\([^)]*\)|[^\\}]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EffectTagRegex();
}
