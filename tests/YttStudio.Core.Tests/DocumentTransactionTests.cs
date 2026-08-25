using YttStudio.Core.Editing;

namespace YttStudio.Core.Tests;

public sealed class DocumentTransactionTests
{
    [Fact]
    public void DragTransactionCreatesOneUndoStep()
    {
        SubtitleProject project = new();
        DocumentEditor editor = new(project);
        Cue cue = editor.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text");
        editor.BeginTransaction("자막 이동");
        editor.MoveCue(cue.Id, 55, 80);
        editor.MoveCue(cue.Id, 60, 70);
        editor.EndTransaction();

        editor.Undo();

        Assert.Equal(50, cue.PositionX);
        Assert.Equal(90, cue.PositionY);
    }

    [Fact]
    public void MultiSelectionMoveUsesOneCompositeCommand()
    {
        SubtitleProject project = new();
        DocumentEditor editor = new(project);
        Cue first = editor.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "one");
        Cue second = editor.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "two");
        editor.MoveCues(new Dictionary<Guid, CanvasPoint>
        {
            [first.Id] = new(25, 25),
            [second.Id] = new(75, 75),
        });

        editor.Undo();

        Assert.Equal(50, first.PositionX);
        Assert.Equal(50, second.PositionX);
    }

    [Fact]
    public void RedoReappliesCompositeCommand()
    {
        SubtitleProject project = new();
        DocumentEditor editor = new(project);
        Cue first = editor.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "one");
        Cue second = editor.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "two");
        editor.SetJustification([first.Id, second.Id], Justification.Left);
        editor.Undo();

        editor.Redo();

        Assert.Equal(Justification.Left, first.Justify);
        Assert.Equal(Justification.Left, second.Justify);
    }
}
