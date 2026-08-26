using SkiaSharp;
using YttStudio.Core;

namespace YttStudio.Render.Tests;

public sealed class FontFallbackTests
{
    [Fact]
    public void CjkTextUsesCodePointFallbackRunsAndCachesMeasurement()
    {
        using BundledFontResolver fonts = new();
        FontResolution requested = fonts.Resolve(YtFont.Default);
        using SKFont requestedFont = new(requested.Typeface, 24);
        Assert.False(requestedFont.ContainsGlyph('한'));

        ResolvedFormat format = FormatResolver.Resolve(new SectionFormat(), new SectionOverrides());
        using FontFallbackHelper fallback = new(fonts);
        FontTextLayout first = fallback.Layout(format, 24, "A한字");
        FontTextLayout second = fallback.Layout(format, 24, "A한字");

        // 폰트가 없는 환경에서도 지켜져야 하는 불변식이다.
        Assert.Same(first, second);
        Assert.Equal("A한字", string.Concat(first.Runs.Select(run => run.Text)));
        Assert.True(first.Width > 0);

        // 폴백은 시스템에 설치된 폰트에 의존한다. CJK 글리프를 가진 폰트가 없는
        // 환경(다수의 CI 러너)에서는 폴백이 일어날 수 없으므로 검사하지 않는다.
        using SKTypeface? systemCjk = SKFontManager.Default.MatchCharacter('한');
        if (systemCjk is not null)
        {
            Assert.Contains(first.Runs, run => run.IsFallback);
        }
    }
}
