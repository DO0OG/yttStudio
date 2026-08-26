using System.Text;
using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render;

internal interface IFontFallbackLogSink
{
    void Log(string message);
}

/// <summary>요청 폰트에 글리프가 없을 때 코드포인트별 시스템 폰트를 찾아 측정과 그리기에 함께 제공한다.</summary>
public sealed class FontFallbackHelper : IDisposable
{
    private readonly IFontResolver fontResolver;
    private readonly Action<string>? log;
    private readonly Dictionary<FontTextLayoutKey, FontTextLayout> layoutCache = [];
    private readonly Dictionary<FontSegmentKey, IReadOnlyList<FontFallbackSegment>> segmentCache = [];
    private readonly Dictionary<FallbackTypefaceKey, SKTypeface?> fallbackCache = [];
    private readonly HashSet<SKTypeface> ownedFallbackTypefaces = [];
    private bool disposed;

    public FontFallbackHelper(IFontResolver fontResolver, Action<string>? log = null)
    {
        this.fontResolver = fontResolver ?? throw new ArgumentNullException(nameof(fontResolver));
        this.log = log ?? GetLogSink(fontResolver);
    }

    /// <summary>요청한 YTT 폰트의 기본 해석 결과를 반환한다.</summary>
    public FontResolution Resolve(YtFont requested)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return fontResolver.Resolve(requested);
    }

    /// <summary>코드포인트 단위 폴백 결과와 측정값을 함께 캐시해 반환한다.</summary>
    public FontTextLayout Layout(ResolvedFormat format, float fontSize, string text)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(text);
        if (fontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        }

        FontResolution resolution = Resolve(format.Font);
        FontTextLayoutKey key = new(format, fontSize, text, resolution.ActualFamilyName);
        if (layoutCache.TryGetValue(key, out FontTextLayout? cached))
        {
            return cached;
        }

        IReadOnlyList<FontFallbackSegment> segments = ResolveSegments(format, text, resolution);
        List<FontTextRun> runs = new(segments.Count);
        float width = 0;
        float ascent = 0;
        float descent = 0;
        foreach (FontFallbackSegment segment in segments)
        {
            using SKFont font = CreateFont(segment.Typeface, format, fontSize);
            using SKPaint paint = new() { IsAntialias = true };
            SKFontMetrics metrics = font.Metrics;
            float segmentWidth = format.Pack
                ? segment.CodePointCount * fontSize
                : font.MeasureText(segment.Text, paint);
            runs.Add(new FontTextRun(segment.Text, segment.Typeface, segmentWidth,
                segment.IsFallback, segment.CodePointCount));
            width += segmentWidth;
            ascent = Math.Min(ascent, metrics.Ascent);
            descent = Math.Max(descent, metrics.Descent);
        }

        FontTextLayout layout = new(runs, width, descent - ascent, ascent, descent);
        layoutCache.Add(key, layout);
        return layout;
    }

    /// <summary>측정과 그리기에서 동일하게 사용하는 SKFont을 만든다.</summary>
    internal static SKFont CreateFont(SKTypeface typeface, ResolvedFormat format, float fontSize)
        => new(typeface, fontSize)
        {
            Embolden = format.Bold,
            SkewX = format.Italic ? -0.25f : 0,
            Subpixel = true,
        };

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        layoutCache.Clear();
        segmentCache.Clear();
        fallbackCache.Clear();
        foreach (SKTypeface typeface in ownedFallbackTypefaces)
        {
            typeface.Dispose();
        }

        ownedFallbackTypefaces.Clear();
        disposed = true;
    }

    private IReadOnlyList<FontFallbackSegment> ResolveSegments(
        ResolvedFormat format,
        string text,
        FontResolution resolution)
    {
        FontSegmentKey key = new(format.Font, resolution.ActualFamilyName, format.Bold, format.Italic, text);
        if (segmentCache.TryGetValue(key, out IReadOnlyList<FontFallbackSegment>? cached))
        {
            return cached;
        }

        IReadOnlyList<FontFallbackSegment> result = BuildSegments(format, text, resolution);
        segmentCache.Add(key, result);
        return result;
    }

    private IReadOnlyList<FontFallbackSegment> BuildSegments(
        ResolvedFormat format,
        string text,
        FontResolution resolution)
    {
        List<FontFallbackSegment> segments = [];
        using SKFont requestedFont = CreateFont(resolution.Typeface, format, 12);
        SKTypeface? currentTypeface = null;
        bool currentIsFallback = false;
        StringBuilder currentText = new();
        int currentCodePointCount = 0;

        foreach (Rune rune in text.EnumerateRunes())
        {
            SKTypeface typeface = resolution.Typeface;
            bool isFallback = false;
            if (!requestedFont.ContainsGlyph(rune.Value))
            {
                SKTypeface? fallback = MatchFallback(resolution, format, rune.Value);
                if (fallback is not null)
                {
                    typeface = fallback;
                    isFallback = true;
                }
            }

            if (currentTypeface is not null &&
                ReferenceEquals(currentTypeface, typeface) &&
                currentIsFallback == isFallback)
            {
                currentText.Append(rune.ToString());
                currentCodePointCount++;
                continue;
            }

            AddSegment(segments, currentTypeface, currentIsFallback, currentText, currentCodePointCount);
            currentTypeface = typeface;
            currentIsFallback = isFallback;
            currentText.Clear();
            currentText.Append(rune.ToString());
            currentCodePointCount = 1;
        }

        AddSegment(segments, currentTypeface, currentIsFallback, currentText, currentCodePointCount);
        return segments.ToArray();
    }

    private SKTypeface? MatchFallback(FontResolution resolution, ResolvedFormat format, int codePoint)
    {
        SKFontStyle style = new(
            format.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            format.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        FallbackTypefaceKey key = new(
            format.Font,
            resolution.ActualFamilyName,
            (int)style.Weight,
            (int)style.Width,
            style.Slant,
            codePoint);
        if (fallbackCache.TryGetValue(key, out SKTypeface? cached))
        {
            return cached;
        }

        SKTypeface? candidate = FindFallbackTypeface(resolution, format, style, codePoint);

        if (candidate is not null)
        {
            ownedFallbackTypefaces.Add(candidate);
            log?.Invoke($"font fallback: {format.Font} U+{codePoint:X4} -> {candidate.FamilyName}");
        }
        else
        {
            log?.Invoke($"font fallback missing: {format.Font} U+{codePoint:X4}");
        }

        fallbackCache.Add(key, candidate);
        return candidate;
    }

    private SKTypeface? FindFallbackTypeface(
        FontResolution resolution,
        ResolvedFormat format,
        SKFontStyle style,
        int codePoint)
    {
        SKTypeface? candidate = null;
        try
        {
            candidate = SKFontManager.Default.MatchCharacter(
                resolution.ActualFamilyName,
                style,
                Array.Empty<string>(),
                codePoint);
            if (candidate is null || !ContainsGlyph(candidate, format, codePoint))
            {
                DisposeCandidate(candidate, resolution.Typeface);
                candidate = SKFontManager.Default.MatchCharacter(codePoint);
            }

            if (candidate is not null && !ContainsGlyph(candidate, format, codePoint))
            {
                DisposeCandidate(candidate, resolution.Typeface);
                candidate = null;
            }
        }
        catch (Exception exception)
        {
            DisposeCandidate(candidate, resolution.Typeface);
            candidate = null;
            log?.Invoke($"font fallback failed: {format.Font} U+{codePoint:X4} ({exception.Message})");
        }

        return candidate;
    }

    private static void AddSegment(
        ICollection<FontFallbackSegment> segments,
        SKTypeface? typeface,
        bool isFallback,
        StringBuilder text,
        int codePointCount)
    {
        if (typeface is not null && text.Length > 0)
        {
            segments.Add(new FontFallbackSegment(text.ToString(), typeface, isFallback, codePointCount));
        }
    }

    private static void DisposeCandidate(SKTypeface? candidate, SKTypeface requestedTypeface)
    {
        if (candidate is not null && !ReferenceEquals(candidate, requestedTypeface))
        {
            candidate.Dispose();
        }
    }

    private static bool ContainsGlyph(SKTypeface typeface, ResolvedFormat format, int codePoint)
    {
        using SKFont font = CreateFont(typeface, format, 12);
        return font.ContainsGlyph(codePoint);
    }

    private static Action<string>? GetLogSink(IFontResolver resolver)
        => resolver is IFontFallbackLogSink sink
            ? new Action<string>(sink.Log)
            : null;

    private readonly record struct FontTextLayoutKey(
        ResolvedFormat Format,
        float FontSize,
        string Text,
        string ActualFamilyName);

    private readonly record struct FontSegmentKey(
        YtFont Requested,
        string ActualFamilyName,
        bool Bold,
        bool Italic,
        string Text);

    private readonly record struct FallbackTypefaceKey(
        YtFont Requested,
        string ActualFamilyName,
        int Weight,
        int Width,
        SKFontStyleSlant Slant,
        int CodePoint);

    private sealed record FontFallbackSegment(
        string Text,
        SKTypeface Typeface,
        bool IsFallback,
        int CodePointCount);
}

/// <summary>한 폰트 요청에서 코드포인트별로 나뉜 텍스트와 측정 결과다.</summary>
public sealed record FontTextLayout(
    IReadOnlyList<FontTextRun> Runs,
    float Width,
    float Height,
    float Ascent,
    float Descent)
{
    public bool UsesFallback => Runs.Any(run => run.IsFallback);
}

/// <summary>동일 폰트로 그릴 수 있는 하나의 코드포인트 연속 구간이다.</summary>
public sealed record FontTextRun(
    string Text,
    SKTypeface Typeface,
    float Width,
    bool IsFallback,
    int CodePointCount);
