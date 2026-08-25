namespace YttStudio.Core.Editing;

/// <summary>Provides the sole public mutation boundary for a subtitle project.</summary>
public sealed class DocumentEditor
{
    private const int MaximumUndoDepth = 200;
    private readonly SubtitleProject project;
    private readonly List<IUndoableCommand> undoStack = [];
    private readonly List<IUndoableCommand> redoStack = [];
    private List<IUndoableCommand>? transactionCommands;
    private string? transactionLabel;
    private int undoFreeDepth;

    public DocumentEditor(SubtitleProject project)
    {
        this.project = project ?? throw new ArgumentNullException(nameof(project));
    }

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;
    public string? UndoLabel => CanUndo ? undoStack[^1].Label : null;
    public string? RedoLabel => CanRedo ? redoStack[^1].Label : null;

    /// <summary>Creates and adds a cue as one undoable operation.</summary>
    public Cue AddCue(TimeSpan start, TimeSpan end, string text)
    {
        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "Cue end must be later than its start.");
        }

        Cue cue = new(Guid.NewGuid()) { Start = start, End = end };
        cue.AddSection(new Section { Text = text ?? string.Empty });
        Execute(new AddCueCommand(project.Cues, cue));
        return cue;
    }

    /// <summary>Removes a cue.</summary>
    public void RemoveCue(Guid cueId)
    {
        Cue cue = GetCue(cueId);
        Execute(new RemoveCueCommand(project.Cues, cue));
    }

    /// <summary>Moves a cue anchor to a new YTT coordinate.</summary>
    public void MoveCue(Guid cueId, double positionX, double positionY)
    {
        if (positionX is < 0 or > 100 || positionY is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(positionX), "YTT positions must be in the range 0 through 100.");
        }

        Execute(new MoveCueCommand(GetCue(cueId), positionX, positionY));
    }

    /// <summary>Changes the text of one section.</summary>
    public void SetText(Guid cueId, int sectionIndex, string text)
    {
        Section section = GetSection(cueId, sectionIndex);
        Execute(new SetTextCommand(cueId, section, text ?? string.Empty));
    }

    /// <summary>Replaces the explicit format overrides of one section.</summary>
    public void SetFormatOverrides(Guid cueId, int sectionIndex, SectionOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        Section section = GetSection(cueId, sectionIndex);
        Execute(new SetOverridesCommand(cueId, section, overrides.Clone()));
    }

    /// <summary>Begins grouping subsequent commands into one undo step.</summary>
    public void BeginTransaction(string label)
    {
        if (transactionCommands is not null)
        {
            throw new InvalidOperationException("A transaction is already active.");
        }

        transactionLabel = string.IsNullOrWhiteSpace(label) ? "변경" : label;
        transactionCommands = [];
    }

    /// <summary>Commits the current group as one undo step.</summary>
    public void EndTransaction()
    {
        if (transactionCommands is null)
        {
            throw new InvalidOperationException("No transaction is active.");
        }

        List<IUndoableCommand> commands = transactionCommands;
        string label = transactionLabel!;
        transactionCommands = null;
        transactionLabel = null;

        if (commands.Count > 0 && undoFreeDepth == 0)
        {
            PushUndo(new CompositeCommand(label, commands));
        }
    }

    /// <summary>Creates a scope whose mutations do not create undo entries.</summary>
    public IDisposable BeginUndoFreeMutation()
    {
        undoFreeDepth++;
        return new UndoFreeScope(this);
    }

    /// <summary>Undoes the latest mutation.</summary>
    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        IUndoableCommand command = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);
        command.Undo();
        redoStack.Add(command);
    }

    /// <summary>Reapplies the latest undone mutation.</summary>
    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        IUndoableCommand command = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);
        command.Execute();
        PushUndo(command, clearRedo: false);
    }

    private void Execute(IUndoableCommand command)
    {
        command.Execute();
        if (undoFreeDepth > 0)
        {
            return;
        }

        redoStack.Clear();
        if (transactionCommands is not null)
        {
            if (transactionCommands.Count == 0 || !command.TryMergeWith(transactionCommands[^1]))
            {
                transactionCommands.Add(command);
            }

            return;
        }

        if (undoStack.Count == 0 || !command.TryMergeWith(undoStack[^1]))
        {
            PushUndo(command, clearRedo: false);
        }
    }

    private void PushUndo(IUndoableCommand command, bool clearRedo = true)
    {
        if (clearRedo)
        {
            redoStack.Clear();
        }

        undoStack.Add(command);
        if (undoStack.Count > MaximumUndoDepth)
        {
            undoStack.RemoveAt(0);
        }
    }

    private Cue GetCue(Guid cueId)
        => project.Cues[cueId] ?? throw new KeyNotFoundException($"Cue {cueId} does not exist.");

    private Section GetSection(Guid cueId, int sectionIndex)
    {
        Cue cue = GetCue(cueId);
        if ((uint)sectionIndex >= (uint)cue.Sections.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(sectionIndex));
        }

        return cue.Sections[sectionIndex];
    }

    private sealed class UndoFreeScope(DocumentEditor owner) : IDisposable
    {
        private DocumentEditor? owner = owner;

        public void Dispose()
        {
            if (owner is not null)
            {
                owner.undoFreeDepth--;
                owner = null;
            }
        }
    }

    private sealed class AddCueCommand(CueCollection cues, Cue cue) : IUndoableCommand
    {
        public string Label => "자막 추가";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cue.Id];
        public void Execute() => cues.Add(cue);
        public void Undo() => cues.Remove(cue.Id);
        public bool TryMergeWith(IUndoableCommand previous) => false;
    }

    private sealed class RemoveCueCommand(CueCollection cues, Cue cue) : IUndoableCommand
    {
        public string Label => "자막 삭제";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cue.Id];
        public void Execute() => cues.Remove(cue.Id);
        public void Undo() => cues.Add(cue);
        public bool TryMergeWith(IUndoableCommand previous) => false;
    }

    private sealed class MoveCueCommand(Cue cue, double positionX, double positionY) : IUndoableCommand
    {
        private readonly double oldX = cue.PositionX;
        private readonly double oldY = cue.PositionY;

        public string Label => "자막 이동";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cue.Id];
        public void Execute() => Set(positionX, positionY);
        public void Undo() => Set(oldX, oldY);
        public bool TryMergeWith(IUndoableCommand previous) => false;

        private void Set(double x, double y)
        {
            cue.PositionX = x;
            cue.PositionY = y;
        }
    }

    private sealed class SetTextCommand(Guid cueId, Section section, string text) : IUndoableCommand
    {
        private readonly string oldText = section.Text;

        public string Label => "자막 텍스트 변경";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cueId];
        public void Execute() => section.Text = text;
        public void Undo() => section.Text = oldText;
        public bool TryMergeWith(IUndoableCommand previous) => false;
    }

    private sealed class SetOverridesCommand(Guid cueId, Section section, SectionOverrides overrides) : IUndoableCommand
    {
        private readonly SectionOverrides oldOverrides = section.Overrides.Clone();

        public string Label => "자막 서식 변경";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cueId];
        public void Execute() => section.Overrides = overrides.Clone();
        public void Undo() => section.Overrides = oldOverrides.Clone();
        public bool TryMergeWith(IUndoableCommand previous) => false;
    }
}
