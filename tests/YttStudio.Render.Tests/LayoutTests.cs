using YttStudio.Core;

namespace YttStudio.Render.Tests;

public sealed class LayoutTests
{
    public static IEnumerable<object[]> AnchorAndJustificationCases()
    {
        foreach (AnchorPoint anchor in Enum.GetValues<AnchorPoint>())
        {
            foreach (Justification justification in Enum.GetValues<Justification>())
            {
                yield return [anchor, justification];
            }
        }
    }

    [Theory]
    [MemberData(nameof(AnchorAndJustificationCases))]
    public void PlacesAllAnchorAndJustificationCombinations(AnchorPoint anchor, Justification justification)
    {
        using BundledFontResolver fonts = new();
        SubtitleLayoutEngine engine = new(fonts);
        (SubtitleProject project, Cue cue) = CreateProject(anchor, justification, "A\nLong line");

        CueLayout layout = engine.LayoutCue(new PlayerViewport(1280, 720), project, cue);

        Assert.InRange(Math.Abs(layout.AnchorScreenPoint.X - 640), 0, 0.001);
        Assert.InRange(Math.Abs(layout.AnchorScreenPoint.Y - 360), 0, 0.001);
        AssertAnchor(layout, anchor);
        AssertJustification(layout, justification);
    }

    [Theory]
    [InlineData(Justification.Left)]
    [InlineData(Justification.Right)]
    [InlineData(Justification.Center)]
    public void MultilineJustificationUsesTheWidestExplicitLine(Justification justification)
    {
        using BundledFontResolver fonts = new();
        SubtitleLayoutEngine engine = new(fonts);
        (SubtitleProject project, Cue cue) = CreateProject(
            AnchorPoint.MiddleCenter,
            justification,
            "short\nconsiderably wider\nmid");

        CueLayout layout = engine.LayoutCue(new PlayerViewport(1280, 720), project, cue);

        Assert.Equal(3, layout.Lines.Count);
        AssertJustification(layout, justification);
    }

    private static void AssertAnchor(CueLayout layout, AnchorPoint anchor)
    {
        int column = (int)anchor % 3;
        int row = (int)anchor / 3;
        float anchoredX = column switch
        {
            0 => layout.Bounds.Left,
            1 => layout.Bounds.MidX,
            _ => layout.Bounds.Right,
        };
        float anchoredY = row switch
        {
            0 => layout.Bounds.Top,
            1 => layout.Bounds.MidY,
            _ => layout.Bounds.Bottom,
        };

        Assert.InRange(Math.Abs(anchoredX - layout.AnchorScreenPoint.X), 0, 0.001);
        Assert.InRange(Math.Abs(anchoredY - layout.AnchorScreenPoint.Y), 0, 0.001);
    }

    private static void AssertJustification(CueLayout layout, Justification justification)
    {
        LineLayout widest = layout.Lines.MaxBy(line => line.Bounds.Width)!;
        foreach (LineLayout line in layout.Lines)
        {
            float delta = justification switch
            {
                Justification.Left => line.Bounds.Left - widest.Bounds.Left,
                Justification.Right => line.Bounds.Right - widest.Bounds.Right,
                _ => line.Bounds.MidX - widest.Bounds.MidX,
            };
            Assert.InRange(Math.Abs(delta), 0, 0.001);
        }
    }

    internal static (SubtitleProject Project, Cue Cue) CreateProject(
        AnchorPoint anchor,
        Justification justification,
        string text)
    {
        SubtitleProject project = new();
        Cue cue = new(Guid.NewGuid())
        {
            Start = TimeSpan.Zero,
            End = TimeSpan.FromSeconds(5),
            Anchor = anchor,
            Justify = justification,
            PositionX = 50,
            PositionY = 50,
        };
        cue.AddSection(new Section { Text = text });
        project.Cues.Add(cue);
        return (project, cue);
    }
}
