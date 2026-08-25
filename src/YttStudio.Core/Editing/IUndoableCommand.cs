namespace YttStudio.Core.Editing;

/// <summary>Represents one reversible domain mutation.</summary>
public interface IUndoableCommand
{
    string Label { get; }
    IReadOnlyCollection<Guid> AffectedCueIds { get; }
    void Execute();
    void Undo();
    bool TryMergeWith(IUndoableCommand previous);
}

/// <summary>Groups multiple commands into one undo step.</summary>
public sealed class CompositeCommand : IUndoableCommand
{
    private readonly List<IUndoableCommand> commands;
    private readonly IReadOnlyCollection<Guid> affectedCueIds;

    public CompositeCommand(string label, IEnumerable<IUndoableCommand> commands)
    {
        Label = label;
        this.commands = commands.ToList();
        affectedCueIds = this.commands.SelectMany(command => command.AffectedCueIds).Distinct().ToArray();
    }

    public string Label { get; }
    public IReadOnlyCollection<Guid> AffectedCueIds => affectedCueIds;
    public void Execute()
    {
        foreach (IUndoableCommand command in commands)
        {
            command.Execute();
        }
    }

    public void Undo()
    {
        for (int index = commands.Count - 1; index >= 0; index--)
        {
            commands[index].Undo();
        }
    }

    public bool TryMergeWith(IUndoableCommand previous) => false;
}
