using YttStudio.Video;

namespace YttStudio.Video.Tests;

public sealed class MpvLoadGateTests
{
    [Fact]
    public void EventsBeforeFirstRequestAreIgnored()
    {
        MpvLoadGate gate = new();

        Assert.False(gate.Observe(MpvEventId.StartFile));
        Assert.False(gate.Observe(MpvEventId.FileLoaded));
        Assert.False(gate.IsLoaded(0));
    }

    [Fact]
    public void FileLoadedBeforeStartIsIgnored()
    {
        MpvLoadGate gate = new();
        long generation = gate.BeginLoad();

        Assert.False(gate.Observe(MpvEventId.FileLoaded));
        Assert.False(gate.IsLoaded(generation));
        Assert.False(gate.Observe(MpvEventId.StartFile));
        Assert.True(gate.Observe(MpvEventId.FileLoaded));
        Assert.True(gate.IsLoaded(generation));
    }

    [Fact]
    public void NewGenerationRejectsPreviousFileLoadedEvent()
    {
        MpvLoadGate gate = new();
        long firstGeneration = gate.BeginLoad();
        gate.Observe(MpvEventId.StartFile);
        Assert.True(gate.Observe(MpvEventId.FileLoaded));

        long secondGeneration = gate.BeginLoad();
        Assert.NotEqual(firstGeneration, secondGeneration);
        Assert.False(gate.Observe(MpvEventId.FileLoaded));
        Assert.False(gate.IsLoaded(firstGeneration));
        Assert.False(gate.IsLoaded(secondGeneration));
        gate.Observe(MpvEventId.StartFile);
        Assert.True(gate.Observe(MpvEventId.FileLoaded));
        Assert.True(gate.IsLoaded(secondGeneration));
    }

    [Fact]
    public void EndFilePreventsLateStaleFileLoadedFromCompletingRequest()
    {
        MpvLoadGate gate = new();
        long generation = gate.BeginLoad();
        gate.Observe(MpvEventId.StartFile);
        Assert.False(gate.Observe(MpvEventId.EndFile));
        Assert.False(gate.Observe(MpvEventId.FileLoaded));
        Assert.False(gate.IsLoaded(generation));
    }

    [Fact]
    public void SameUrlReloadRequiresASecondStartAndFileLoadedPair()
    {
        MpvLoadGate gate = new();
        long firstGeneration = gate.BeginLoad();
        gate.Observe(MpvEventId.StartFile);
        gate.Observe(MpvEventId.FileLoaded);

        long secondGeneration = gate.BeginLoad();
        Assert.False(gate.Observe(MpvEventId.FileLoaded));
        Assert.False(gate.IsLoaded(secondGeneration));
        gate.Observe(MpvEventId.StartFile);
        Assert.True(gate.Observe(MpvEventId.FileLoaded));
        Assert.True(gate.IsLoaded(secondGeneration));
        Assert.False(gate.IsLoaded(firstGeneration));
    }
}
