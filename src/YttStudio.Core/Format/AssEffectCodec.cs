using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YttStudio.Core.Format;

/// <summary>효과 모델을 고정된 변환기의 ASS 태그로 인코딩한다.</summary>
internal static partial class AssEffectCodec
{
    private static readonly string[] EffectNames = ["fad", "fade", "move", "t", "ytshake", "ytchroma", "ytkt", "ytmotion"];

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
            List<CueEffect> blockEffects = [];
            MoveEffect? authoritativeMotion = null;
            foreach (Match tag in EffectTagRegex().Matches(block.Groups[1].Value))
            {
                string name = tag.Groups["name"].Value.ToLowerInvariant();
                string arg = tag.Groups["arg"].Value.Trim();
                if (name == "ytmotion")
                {
                    // 잘못된 메타데이터 태그는 무시하여 일반 이동 태그를 대체 수단으로 남긴다.
                    authoritativeMotion ??= ParseMotionMetadata(arg);
                    continue;
                }

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
                    blockEffects.Add(effect);
            }

            if (authoritativeMotion is not null)
            {
                bool companionReplaced = false;
                foreach (CueEffect effect in blockEffects)
                {
                    if (effect is MoveEffect)
                    {
                        if (!companionReplaced)
                        {
                            AddParsedEffect(result, authoritativeMotion);
                            companionReplaced = true;
                        }

                        continue;
                    }

                    AddParsedEffect(result, effect);
                }

                if (!companionReplaced)
                {
                    AddParsedEffect(result, authoritativeMotion);
                }
            }
            else
            {
                foreach (CueEffect effect in blockEffects)
                {
                    AddParsedEffect(result, effect);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// ASS에는 경로 기본형이 없으므로 여러 이동 태그로 경로를 표현한다.
    /// 끝점과 경계 시각이 같은 인접 시간 구간만 합친다. 나머지 구간은 별도
    /// MoveEffect로 유지하여 보간을 임의로 만들지 않고 단절을 보존한다.
    /// </summary>
    private static void AddParsedEffect(List<CueEffect> result, CueEffect effect)
    {
        if (effect is MoveEffect current && result.LastOrDefault() is MoveEffect previous &&
            TryGetKeyframes(previous, out IReadOnlyList<MotionKeyframe> previousPath) &&
            TryGetKeyframes(current, out IReadOnlyList<MotionKeyframe> currentPath) &&
            IsContinuous(previousPath[^1], currentPath[0]))
        {
            MotionKeyframe[] merged = [.. previousPath, .. currentPath.Skip(1)];
            result[^1] = new MoveEffect(merged);
            return;
        }

        result.Add(effect);
    }

    private static bool TryGetKeyframes(MoveEffect move, out IReadOnlyList<MotionKeyframe> path)
    {
        if (move.Keyframes.Count > 0)
        {
            path = move.Keyframes;
            return true;
        }

        if (move.StartTime is TimeSpan start && move.EndTime is TimeSpan end)
        {
            path =
            [
                new MotionKeyframe(start, move.FromX, move.FromY),
                new MotionKeyframe(end, move.ToX, move.ToY),
            ];
            return true;
        }

        path = [];
        return false;
    }

    private static bool IsContinuous(MotionKeyframe previous, MotionKeyframe current)
        => previous.RelativeTime == current.RelativeTime &&
            previous.X.Equals(current.X) && previous.Y.Equals(current.Y);

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
            AppendEffect(tags, effect);
        }

        return tags.Length == 0 ? string.Empty : "{" + tags + "}";
    }

    private static void AppendEffect(StringBuilder tags, CueEffect effect)
    {
        switch (effect)
        {
            case MoveEffect move:
                AppendMove(tags, move);
                break;
            case FadeEffect fade:
                AppendFade(tags, fade);
                break;
            case ShakeEffect shake:
                AppendShake(tags, shake);
                break;
            case ChromaEffect chroma:
                AppendChroma(tags, chroma);
                break;
            case AnimateEffect animate:
                AppendAnimate(tags, animate);
                break;
            case KaraokeSettings karaoke:
                AppendKaraoke(tags, karaoke);
                break;
            default:
                // ASS 표현이 없는 효과 종류는 태그를 만들지 않는다.
                break;
        }
    }

    private static void AppendMove(StringBuilder tags, MoveEffect move)
    {
        if (move.Keyframes.Count > 0)
        {
            AppendMotionMetadata(tags, move.Keyframes);

            if (move.Keyframes.Count == 1)
            {
                MotionKeyframe point = move.Keyframes[0];
                AppendMoveSegment(tags, point.X, point.Y, point.X, point.Y,
                    point.RelativeTime, point.RelativeTime);
                return;
            }

            // ASS는 각 변을 독립적으로 표현한다. 키프레임 경계를 공유하므로
            // 출력 구간은 인접하지만 서로 겹치지 않는다.
            for (int index = 0; index < move.Keyframes.Count - 1; index++)
            {
                MotionKeyframe from = move.Keyframes[index];
                MotionKeyframe to = move.Keyframes[index + 1];
                AppendMoveSegment(tags, from.X, from.Y, to.X, to.Y,
                    from.RelativeTime, to.RelativeTime);
            }

            return;
        }

        AppendMoveSegment(tags, move.FromX, move.FromY, move.ToX, move.ToY,
            move.StartTime, move.EndTime);
    }

    private static void AppendMotionMetadata(
        StringBuilder tags,
        IReadOnlyList<MotionKeyframe> keyframes)
    {
        MotionMetadataEnvelope metadata = new()
        {
            Version = 1,
            Keyframes = keyframes.Select(keyframe => new MotionMetadataKeyframe
            {
                Time = Ms(keyframe.RelativeTime),
                X = keyframe.X,
                Y = keyframe.Y,
                Interpolation = keyframe.Interpolation.ToString(),
                Acceleration = keyframe.Acceleration,
            }).ToList(),
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(metadata, MotionMetadataJsonOptions);
        string token = Convert.ToBase64String(json)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        tags.Append("\\ytmotion(v1.").Append(token).Append(')');
    }

    private static void AppendMoveSegment(
        StringBuilder tags,
        double fromX,
        double fromY,
        double toX,
        double toY,
        TimeSpan? startTime,
        TimeSpan? endTime)
    {
        tags.Append("\\move(").Append(F(fromX)).Append(',').Append(F(fromY)).Append(',')
            .Append(F(toX)).Append(',').Append(F(toY));
        if (startTime.HasValue && endTime.HasValue)
            tags.Append(',').Append(Ms(startTime.Value)).Append(',').Append(Ms(endTime.Value));
        tags.Append(')');
    }

    private static void AppendFade(StringBuilder tags, FadeEffect fade)
    {
        if (fade.Alpha1 is int alpha1 && fade.Alpha2 is int alpha2 && fade.Alpha3 is int alpha3 &&
            fade.T1 is TimeSpan t1 && fade.T2 is TimeSpan t2 &&
            fade.T3 is TimeSpan t3 && fade.T4 is TimeSpan t4)
        {
            tags.Append("\\fade(").Append(alpha1).Append(',').Append(alpha2).Append(',')
                .Append(alpha3).Append(',').Append(Ms(t1)).Append(',').Append(Ms(t2))
                .Append(',').Append(Ms(t3)).Append(',').Append(Ms(t4)).Append(')');
            return;
        }

        tags.Append("\\fad(").Append(Ms(fade.FadeIn)).Append(',').Append(Ms(fade.FadeOut)).Append(')');
    }

    private static void AppendShake(StringBuilder tags, ShakeEffect shake)
    {
        tags.Append("\\ytshake(").Append(F(shake.RadiusX)).Append(',').Append(F(shake.RadiusY));
        if (shake.StartTime.HasValue && shake.EndTime.HasValue)
            tags.Append(',').Append(Ms(shake.StartTime.Value)).Append(',').Append(Ms(shake.EndTime.Value));
        tags.Append(')');
    }

    private static void AppendChroma(StringBuilder tags, ChromaEffect chroma)
    {
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
    }

    private static void AppendAnimate(StringBuilder tags, AnimateEffect animate)
    {
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
    }

    private static void AppendKaraoke(StringBuilder tags, KaraokeSettings karaoke)
    {
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
        if (a.Length < 4 || !D(a[0], out double x1) || !D(a[1], out double y1) ||
            !D(a[2], out double x2) || !D(a[3], out double y2))
        {
            return null;
        }

        if (a.Length >= 6 && D(a[4], out double start) && D(a[5], out double end))
        {
            // 시간 지정 기존 이동은 가져올 때 두 키프레임으로 표현한다.
            // MoveEffect가 기존 스칼라 속성도 채우므로 기존 호출부도 동작한다.
            return new MoveEffect(
            [
                new MotionKeyframe(T(start), x1, y1),
                new MotionKeyframe(T(end), x2, y2),
            ]);
        }

        return new MoveEffect(x1, y1, x2, y2);
    }

    private static MoveEffect? ParseMotionMetadata(string arg)
    {
        string token = arg.Trim().TrimStart('(').TrimEnd(')');
        if (!token.StartsWith("v1.", StringComparison.Ordinal) || token.Length <= 3)
        {
            return null;
        }

        try
        {
            string encoded = token[3..];
            encoded = encoded.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
            MotionMetadataEnvelope? metadata = JsonSerializer.Deserialize<MotionMetadataEnvelope>(
                Convert.FromBase64String(encoded), MotionMetadataJsonOptions);
            if (metadata is not { Version: 1, Keyframes.Count: > 0 })
            {
                return null;
            }

            List<MotionKeyframe> keyframes = [];
            foreach (MotionMetadataKeyframe item in metadata.Keyframes)
            {
                if (!Enum.TryParse(item.Interpolation, ignoreCase: true, out MotionInterpolation interpolation) ||
                    !double.IsFinite(item.X) || !double.IsFinite(item.Y) ||
                    !double.IsFinite(item.Acceleration))
                {
                    return null;
                }

                keyframes.Add(new MotionKeyframe(
                    TimeSpan.FromMilliseconds(item.Time),
                    item.X,
                    item.Y,
                    interpolation,
                    item.Acceleration));
            }

            return new MoveEffect(keyframes);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
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

    private static readonly JsonSerializerOptions MotionMetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
    };

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

    [GeneratedRegex(@"\\(?<name>fad|fade|move|t|ytshake|ytchroma|ytkt|ytmotion)(?<arg>\([^)]*\)|[^\\}]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EffectTagRegex();

    private sealed class MotionMetadataEnvelope
    {
        public int Version { get; set; }
        public List<MotionMetadataKeyframe> Keyframes { get; set; } = [];
    }

    private sealed class MotionMetadataKeyframe
    {
        public int Time { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public string Interpolation { get; set; } = nameof(MotionInterpolation.Linear);
        public double Acceleration { get; set; } = 1.0;
    }
}
