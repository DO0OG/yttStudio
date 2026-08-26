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

        Assert.Same(first, second);
        Assert.Equal("A한字", string.Concat(first.Runs.Select(run => run.Text)));
        Assert.Contains(first.Runs, run => run.IsFallback);
        Assert.True(first.Width > 0);
    }
}
