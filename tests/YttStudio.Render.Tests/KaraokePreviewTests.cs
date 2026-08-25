using YttStudio.Core;

namespace YttStudio.Render.Tests;

public sealed class KaraokePreviewTests
{
    [Fact]
    public void GlitchIsDeterministicForCueAndFrame()
    {
        Cue cue = CreateCue(KaraokeType.Glitch);

        string first = KaraokePreview.GetGlitchedText(cue, "Love사랑かな漢字", 42);
        string second = KaraokePreview.GetGlitchedText(cue, "Love사랑かな漢字", 42);

        Assert.Equal(first, second);
    }

    [Fact]
    public void GlitchKeepsCharactersInTheirSourceScript()
    {
        Cue cue = CreateCue(KaraokeType.Glitch);

        string result = KaraokePreview.GetGlitchedText(cue, "A가あア漢!", 7);

        Assert.InRange(result[0], 'A', 'Z');
        Assert.InRange(result[1], '\uAC00', '\uD7A3');
        Assert.InRange(result[2], '\u3041', '\u3096');
        Assert.InRange(result[3], '\u30A1', '\u30FA');
        Assert.InRange(result[4], '\u4E00', '\u9FFF');
        Assert.Equal('!', result[5]);
    }

    [Fact]
    public void UnsungSectionUsesSecondaryColor()
    {
        Cue cue = CreateCue(KaraokeType.Simple);
        ResolvedFormat format = CreateFormat();

        RgbaColor color = KaraokePreview.ResolveColor(cue, cue.Sections[0], format, cue.Start);

        Assert.Equal(format.SecondaryColor, color);
    }

    [Fact]
    public void SungSectionUsesForegroundAtBoundary()
    {
        Cue cue = CreateCue(KaraokeType.Simple);
        ResolvedFormat format = CreateFormat();

        RgbaColor color = KaraokePreview.ResolveColor(
            cue,
            cue.Sections[0],
            format,
            cue.Start + cue.Sections[0].KaraokeOffset!.Value);

        Assert.Equal(format.Foreground, color);
    }

    [Fact]
    public void FadeInterpolatesHalfwayThroughTransition()
    {
        Cue cue = CreateCue(KaraokeType.Fade);
        ResolvedFormat format = CreateFormat();
        TimeSpan time = cue.Start + cue.Sections[0].KaraokeOffset!.Value + TimeSpan.FromMilliseconds(250);

        RgbaColor color = KaraokePreview.ResolveColor(cue, cue.Sections[0], format, time);

        Assert.Equal(new RgbaColor(150, 100, 50, 255), color);
    }

    private static Cue CreateCue(KaraokeType type)
    {
        Cue cue = new(new Guid("11111111-2222-3333-4444-555555555555"))
        {
            Start = TimeSpan.FromSeconds(1),
            End = TimeSpan.FromSeconds(3),
        };
        cue.AddSection(new Section { Text = "text", KaraokeOffset = TimeSpan.FromMilliseconds(500) });
        cue.AddEffect(new KaraokeSettings(type));
        return cue;
    }

    private static ResolvedFormat CreateFormat()
    {
        SectionFormat format = new()
        {
            Foreground = new RgbaColor(200, 100, 0, 255),
            SecondaryColor = new RgbaColor(100, 100, 100, 255),
        };
        return FormatResolver.Resolve(format, new SectionOverrides());
    }
}
