using YttStudio.Core;

namespace YttStudio.Core.Tests;

public sealed class CueCollectionTests
{
    [Fact]
    public void GetActiveAtUsesHalfOpenTimeRange()
    {
        CueCollection cues = CreateCollection((0, 1000));

        Assert.Single(cues.GetActiveAt(TimeSpan.Zero));
        Assert.Empty(cues.GetActiveAt(TimeSpan.FromMilliseconds(1000)));
    }

    [Fact]
    public void GetActiveAtReturnsOverlappingCuesInZOrder()
    {
        CueCollection cues = CreateCollection((0, 3000), (1000, 2000), (1500, 2500));
        cues.ElementAt(0).ZOrder = 3;
        cues.ElementAt(1).ZOrder = 1;
        cues.ElementAt(2).ZOrder = 2;

        IReadOnlyList<Cue> active = cues.GetActiveAt(TimeSpan.FromMilliseconds(1750));

        Assert.Equal([1, 2, 3], active.Select(cue => cue.ZOrder));
    }

    [Fact]
    public void AdvanceToReportsEntriesAndExits()
    {
        CueCollection cues = CreateCollection((0, 1000), (500, 1500));

        ActiveSetDelta first = cues.AdvanceTo(TimeSpan.FromMilliseconds(250));
        ActiveSetDelta second = cues.AdvanceTo(TimeSpan.FromMilliseconds(750));
        ActiveSetDelta third = cues.AdvanceTo(TimeSpan.FromMilliseconds(1250));

        Assert.Single(first.Entered);
        Assert.Single(second.Entered);
        Assert.Single(third.Exited);
        Assert.Single(third.Active);
    }

    [Fact]
    public void AdvanceToCanSeekBackward()
    {
        CueCollection cues = CreateCollection((0, 1000), (1000, 2000));
        cues.AdvanceTo(TimeSpan.FromMilliseconds(1500));

        ActiveSetDelta delta = cues.AdvanceTo(TimeSpan.FromMilliseconds(500));

        Assert.Single(delta.Entered);
        Assert.Single(delta.Exited);
        Assert.Equal(TimeSpan.Zero, delta.Active[0].Start);
    }

    [Fact]
    public void StartIndexHandlesLargeCollectionsAndReordering()
    {
        CueCollection cues = new();
        for (int index = 0; index < 500; index++)
        {
            Cue cue = CreateCue(index * 100, (index * 100) + 1000);
            cues.Add(cue);
        }

        Cue moved = cues.ElementAt(400);
        moved.Start = TimeSpan.FromMilliseconds(50);
        cues.OnStartChanged(moved);

        Assert.Equal(7, cues.GetActiveAt(TimeSpan.FromMilliseconds(500)).Count);
        Assert.Same(moved, cues.ElementAt(1));
    }

    private static CueCollection CreateCollection(params (int Start, int End)[] ranges)
    {
        CueCollection cues = new();
        foreach ((int start, int end) in ranges)
        {
            cues.Add(CreateCue(start, end));
        }

        return cues;
    }

    private static Cue CreateCue(int start, int end)
    {
        Cue cue = new(Guid.NewGuid())
        {
            Start = TimeSpan.FromMilliseconds(start),
            End = TimeSpan.FromMilliseconds(end),
        };
        cue.AddSection(new Section { Text = "test" });
        return cue;
    }
}
