using YttStudio.Core.Editing;

namespace YttStudio.Core.Tests;

public sealed class DocumentTransactionTests
{
    [Fact]
    public void RevisionAdvancesForExecuteUndoRedoAndRollback()
    {
        DocumentEditor editor = new(new SubtitleProject());
        long initial = editor.Revision;
        Cue cue = editor.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text");
        long afterExecute = editor.Revision;

        editor.Undo();
        long afterUndo = editor.Revision;
        editor.Redo();
        long afterRedo = editor.Revision;
        editor.BeginTransaction("typing");
        editor.SetText(cue.Id, 0, "changed");
        long beforeRollback = editor.Revision;
        editor.CancelTransaction();

        Assert.True(afterExecute > initial);
        Assert.True(afterUndo > afterExecute);
        Assert.True(afterRedo > afterUndo);
        Assert.True(editor.Revision > beforeRollback);
        Assert.Equal("text", cue.Sections[0].Text);
    }

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

    [Fact]
    public void AddAndPositionTransactionUndoesAsOneStep()
    {
        SubtitleProject project = new();
        DocumentEditor editor = new(project);
        editor.BeginTransaction("add and position");
        Cue cue = editor.AddCue(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), "new");
        editor.MoveCue(cue.Id, 0, 100);
        editor.EndTransaction();

        Assert.Single(project.Cues);
        Assert.Equal(0, cue.PositionX);
        Assert.Equal(100, cue.PositionY);

        editor.Undo();

        Assert.Empty(project.Cues);
        Assert.True(editor.CanRedo);

        editor.Redo();

        Assert.Single(project.Cues);
        Assert.Equal(0, cue.PositionX);
        Assert.Equal(100, cue.PositionY);
    }

    [Fact]
    public void CancelTransactionRestoresCommandsWithoutChangingHistory()
    {
        SubtitleProject project = new();
        DocumentEditor editor = new(project);
        Cue cue = editor.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "before");
        editor.SetText(cue.Id, 0, "after");
        editor.Undo();

        Assert.True(editor.CanUndo);
        Assert.True(editor.CanRedo);
        string? undoLabel = editor.UndoLabel;
        string? redoLabel = editor.RedoLabel;

        editor.BeginTransaction("inline");
        editor.SetText(cue.Id, 0, "typing");
        editor.SetText(cue.Id, 0, "typing more");
        editor.CancelTransaction();

        Assert.Equal("before", cue.Sections[0].Text);
        Assert.Equal(undoLabel, editor.UndoLabel);
        Assert.Equal(redoLabel, editor.RedoLabel);
        Assert.True(editor.CanUndo);
        Assert.True(editor.CanRedo);

        editor.Redo();
        Assert.Equal("after", cue.Sections[0].Text);
    }

    [Fact]
    public void CancelAddAndPositionTransactionRemovesCueWithoutHistoryEntry()
    {
        SubtitleProject project = new();
        DocumentEditor editor = new(project);
        Cue existing = editor.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "existing");
        editor.Undo();
        Assert.True(editor.CanRedo);

        editor.BeginTransaction("new inline cue");
        Cue added = editor.AddCue(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "new");
        editor.MoveCue(added.Id, 15, 25);
        editor.SetText(added.Id, 0, "typed");
        editor.CancelTransaction();

        Assert.Empty(project.Cues);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        editor.Redo();
        Assert.Single(project.Cues);
        Assert.Equal("existing", project.Cues[existing.Id]!.Sections[0].Text);
    }
}
