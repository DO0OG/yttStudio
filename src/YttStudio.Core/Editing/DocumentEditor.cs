namespace YttStudio.Core.Editing;

/// <summary>Provides the sole public mutation boundary for a subtitle project.</summary>
public sealed class DocumentEditor
{
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

    /// <summary>Removes multiple cues as one undoable operation.</summary>
    public void RemoveCues(IEnumerable<Guid> cueIds)
        => ExecuteForCues("자막 삭제", cueIds, cue => new RemoveCueCommand(project.Cues, cue));

    /// <summary>Duplicates selected cues and returns the created copies.</summary>
    public IReadOnlyList<Cue> DuplicateCues(IEnumerable<Guid> cueIds)
    {
        List<Cue> copies = cueIds.Distinct().Select(GetCue).Select(CloneCue).ToList();
        Execute(new CompositeCommand("자막 복제", copies.Select(cue => new AddCueCommand(project.Cues, cue))));
        return copies;
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

    /// <summary>Moves multiple cues as one undoable operation.</summary>
    public void MoveCues(IReadOnlyDictionary<Guid, CanvasPoint> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        Execute(new CompositeCommand("자막 이동", positions.Select(item =>
            new MoveCueCommand(GetCue(item.Key), item.Value.X, item.Value.Y))));
    }

    /// <summary>Changes a cue anchor and coordinates while preserving caller-measured box placement.</summary>
    public void SetAnchor(Guid cueId, AnchorPoint anchor, double positionX, double positionY)
        => Execute(new SetAnchorCommand(GetCue(cueId), anchor, positionX, positionY));

    /// <summary>Changes box-internal text justification.</summary>
    public void SetJustification(IEnumerable<Guid> cueIds, Justification justification)
        => ExecuteForCues("내부 정렬 변경", cueIds, cue => new SetJustificationCommand(cue, justification));

    /// <summary>Changes cue time bounds and track.</summary>
    public void SetTiming(Guid cueId, TimeSpan start, TimeSpan end, int track)
    {
        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "Cue end must be later than its start.");
        }

        Execute(new SetTimingCommand(project.Cues, GetCue(cueId), start, end, Math.Max(0, track)));
    }

    /// <summary>Moves selected cues by a percentage delta.</summary>
    public void Nudge(IEnumerable<Guid> cueIds, double deltaX, double deltaY)
    {
        Dictionary<Guid, CanvasPoint> positions = cueIds.Select(GetCue).ToDictionary(
            cue => cue.Id,
            cue => new CanvasPoint(Math.Clamp(cue.PositionX + deltaX, 0, 100),
                Math.Clamp(cue.PositionY + deltaY, 0, 100)));
        MoveCues(positions);
    }

    /// <summary>Applies explicit section format values to all sections of selected cues.</summary>
    public void ApplyFormat(IEnumerable<Guid> cueIds, SectionFormatPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        List<IUndoableCommand> commands = [];
        foreach (Cue cue in cueIds.Select(GetCue))
        {
            foreach (Section section in cue.Sections)
            {
                SectionOverrides next = section.Overrides.Clone();
                ApplyPatch(next, patch);
                commands.Add(new SetOverridesCommand(cue.Id, section, next));
            }
        }

        Execute(new CompositeCommand("자막 서식 변경", commands));
    }

    /// <summary>Applies a style preset to selected cues.</summary>
    public void ApplyStyle(IEnumerable<Guid> cueIds, Guid? styleId)
    {
        if (styleId is Guid id && project.Styles[id] is null)
        {
            throw new KeyNotFoundException($"Style {id} does not exist.");
        }

        ExecuteForCues("스타일 적용", cueIds, cue => new SetStyleCommand(cue, styleId));
    }

    /// <summary>Creates a named style preset.</summary>
    public StylePreset AddStyle(string name)
    {
        StylePreset style = new(Guid.NewGuid()) { Name = NormalizeStyleName(name) };
        Execute(new AddStyleCommand(project.Styles, style));
        return style;
    }

    /// <summary>Renames a style preset.</summary>
    public void RenameStyle(Guid styleId, string name)
    {
        StylePreset style = GetMutableStyle(styleId);
        Execute(new RenameStyleCommand(style, NormalizeStyleName(name)));
    }

    /// <summary>Deletes a style while freezing its resolved appearance into section overrides.</summary>
    public void DeleteStyle(Guid styleId)
    {
        StylePreset style = GetMutableStyle(styleId);
        Execute(new DeleteStyleCommand(project, style));
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
        if (undoStack.Count > YttConstants.MaximumUndoDepth)
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

    private void ExecuteForCues(
        string label,
        IEnumerable<Guid> cueIds,
        Func<Cue, IUndoableCommand> commandFactory)
    {
        ArgumentNullException.ThrowIfNull(cueIds);
        IUndoableCommand[] commands = cueIds.Distinct().Select(GetCue).Select(commandFactory).ToArray();
        Execute(new CompositeCommand(label, commands));
    }

    private StylePreset GetMutableStyle(Guid styleId)
    {
        if (styleId == Guid.Empty || project.Styles[styleId] is not StylePreset style)
        {
            throw new KeyNotFoundException($"Style {styleId} does not exist or cannot be changed.");
        }

        return style;
    }

    private static string NormalizeStyleName(string name)
        => string.IsNullOrWhiteSpace(name) ? "새 스타일" : name.Trim();

    private static void ApplyPatch(SectionOverrides target, SectionFormatPatch patch)
    {
        if (patch.Font.HasValue) target.Font = patch.Font;
        if (patch.SizePercent.HasValue) target.SizePercent = Math.Max(75, patch.SizePercent.Value);
        if (patch.Bold.HasValue) target.Bold = patch.Bold;
        if (patch.Italic.HasValue) target.Italic = patch.Italic;
        if (patch.Underline.HasValue) target.Underline = patch.Underline;
        if (patch.Offset.HasValue) target.Offset = patch.Offset;
        if (patch.Foreground.HasValue) target.Foreground = patch.Foreground;
        if (patch.Background.HasValue) target.Background = patch.Background;
        if (patch.SecondaryColor.HasValue) target.SecondaryColor = patch.SecondaryColor;
        if (patch.Edge.HasValue) target.Edge = patch.Edge;
        if (patch.EdgeColor.HasValue) target.EdgeColor = patch.EdgeColor;
        if (patch.Pack.HasValue) target.Pack = patch.Pack;
    }

    private static Cue CloneCue(Cue source)
    {
        Cue copy = new(Guid.NewGuid())
        {
            Start = source.Start,
            End = source.End,
            Track = source.Track,
            ZOrder = source.ZOrder + 1,
            Anchor = source.Anchor,
            PositionX = source.PositionX,
            PositionY = source.PositionY,
            Justify = source.Justify,
            Direction = source.Direction,
            StyleId = source.StyleId,
        };
        foreach (Section section in source.Sections)
        {
            copy.AddSection(new Section
            {
                Text = section.Text,
                KaraokeOffset = section.KaraokeOffset,
                Overrides = section.Overrides.Clone(),
                Ruby = section.Ruby,
                RubyText = section.RubyText,
                StyleIdOverride = section.StyleIdOverride,
            });
        }

        return copy;
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

    private sealed class SetAnchorCommand(
        Cue cue,
        AnchorPoint anchor,
        double positionX,
        double positionY) : IUndoableCommand
    {
        private readonly AnchorPoint oldAnchor = cue.Anchor;
        private readonly double oldX = cue.PositionX;
        private readonly double oldY = cue.PositionY;

        public string Label => "앵커 변경";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cue.Id];
        public void Execute() => Set(anchor, positionX, positionY);
        public void Undo() => Set(oldAnchor, oldX, oldY);
        public bool TryMergeWith(IUndoableCommand previous) => false;

        private void Set(AnchorPoint nextAnchor, double x, double y)
        {
            cue.Anchor = nextAnchor;
            cue.PositionX = Math.Clamp(x, 0, 100);
            cue.PositionY = Math.Clamp(y, 0, 100);
        }
    }

    private sealed class SetJustificationCommand(Cue cue, Justification justification) : IUndoableCommand
    {
        private readonly Justification oldValue = cue.Justify;
        public string Label => "내부 정렬 변경";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cue.Id];
        public void Execute() => cue.Justify = justification;
        public void Undo() => cue.Justify = oldValue;
        public bool TryMergeWith(IUndoableCommand previous) => false;
    }

    private sealed class SetTimingCommand(
        CueCollection cues,
        Cue cue,
        TimeSpan start,
        TimeSpan end,
        int track) : IUndoableCommand
    {
        private readonly TimeSpan oldStart = cue.Start;
        private readonly TimeSpan oldEnd = cue.End;
        private readonly int oldTrack = cue.Track;
        public string Label => "자막 시간 변경";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cue.Id];
        public void Execute() => Set(start, end, track);
        public void Undo() => Set(oldStart, oldEnd, oldTrack);
        public bool TryMergeWith(IUndoableCommand previous) => false;

        private void Set(TimeSpan nextStart, TimeSpan nextEnd, int nextTrack)
        {
            cue.Start = nextStart;
            cue.End = nextEnd;
            cue.Track = nextTrack;
            cues.OnStartChanged(cue);
        }
    }

    private sealed class SetStyleCommand(Cue cue, Guid? styleId) : IUndoableCommand
    {
        private readonly Guid? oldStyleId = cue.StyleId;
        public string Label => "스타일 적용";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cue.Id];
        public void Execute() => cue.StyleId = styleId;
        public void Undo() => cue.StyleId = oldStyleId;
        public bool TryMergeWith(IUndoableCommand previous) => false;
    }

    private sealed class AddStyleCommand(StylePresetCollection styles, StylePreset style) : IUndoableCommand
    {
        public string Label => "스타일 추가";
        public IReadOnlyCollection<Guid> AffectedCueIds => [];
        public void Execute() => styles.Add(style);
        public void Undo() => styles.Remove(style.Id);
        public bool TryMergeWith(IUndoableCommand previous) => false;
    }

    private sealed class RenameStyleCommand(StylePreset style, string name) : IUndoableCommand
    {
        private readonly string oldName = style.Name;
        public string Label => "스타일 이름 변경";
        public IReadOnlyCollection<Guid> AffectedCueIds => [];
        public void Execute() => style.Name = name;
        public void Undo() => style.Name = oldName;
        public bool TryMergeWith(IUndoableCommand previous) => false;
    }

    private sealed class DeleteStyleCommand : IUndoableCommand
    {
        private readonly SubtitleProject project;
        private readonly StylePreset style;
        private readonly List<SectionSnapshot> sections = [];
        private readonly Dictionary<Guid, Guid?> cueStyles = [];

        public DeleteStyleCommand(SubtitleProject project, StylePreset style)
        {
            this.project = project;
            this.style = style;
            foreach (Cue cue in project.Cues)
            {
                bool cueReferencesStyle = cue.StyleId == style.Id;
                if (cueReferencesStyle)
                {
                    cueStyles.Add(cue.Id, cue.StyleId);
                }

                foreach (Section section in cue.Sections)
                {
                    if (!cueReferencesStyle && section.StyleIdOverride != style.Id)
                    {
                        continue;
                    }

                    sections.Add(new SectionSnapshot(cue, section, section.Overrides.Clone(), section.StyleIdOverride));
                }
            }
        }

        public string Label => "스타일 삭제";
        public IReadOnlyCollection<Guid> AffectedCueIds => cueStyles.Keys
            .Concat(sections.Select(item => item.Cue.Id)).Distinct().ToArray();

        public void Execute()
        {
            foreach (SectionSnapshot snapshot in sections)
            {
                Guid? effectiveStyleId = snapshot.Section.StyleIdOverride ?? snapshot.Cue.StyleId;
                if (effectiveStyleId == style.Id)
                {
                    ResolvedFormat resolved = FormatResolver.Resolve(style.BaseFormat, snapshot.Section.Overrides);
                    snapshot.Section.Overrides = SectionOverrides.FromResolved(resolved);
                    snapshot.Section.StyleIdOverride = null;
                }
            }

            foreach (Guid cueId in cueStyles.Keys)
            {
                project.Cues[cueId]!.StyleId = null;
            }

            project.Styles.Remove(style.Id);
        }

        public void Undo()
        {
            project.Styles.Add(style);
            foreach ((Guid cueId, Guid? styleId) in cueStyles)
            {
                project.Cues[cueId]!.StyleId = styleId;
            }

            foreach (SectionSnapshot snapshot in sections)
            {
                snapshot.Section.Overrides = snapshot.Overrides.Clone();
                snapshot.Section.StyleIdOverride = snapshot.StyleIdOverride;
            }
        }

        public bool TryMergeWith(IUndoableCommand previous) => false;

        private sealed record SectionSnapshot(
            Cue Cue,
            Section Section,
            SectionOverrides Overrides,
            Guid? StyleIdOverride);
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
