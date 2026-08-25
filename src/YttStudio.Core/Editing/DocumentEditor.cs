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
    private readonly Dictionary<Guid, KaraokeTabCursor> karaokeTabCursors = [];

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

    /// <summary>Changes text progression direction of selected cues.</summary>
    public void SetDirection(IEnumerable<Guid> cueIds, TextDirection direction)
        => ExecuteForCues("텍스트 방향 변경", cueIds, cue => new SetDirectionCommand(cue, direction));

    /// <summary>Changes drawing order of selected cues.</summary>
    public void SetZOrder(IEnumerable<Guid> cueIds, int zOrder)
        => ExecuteForCues("그리기 순서 변경", cueIds, cue => new SetZOrderCommand(cue, zOrder));

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

    /// <summary>Updates selected fields of a style preset.</summary>
    public void UpdateStyle(
        Guid styleId,
        SectionFormatPatch patch,
        AnchorPoint? defaultAnchor = null,
        Justification? defaultJustify = null)
    {
        ArgumentNullException.ThrowIfNull(patch);
        StylePreset style = GetMutableStyle(styleId);
        Execute(new UpdateStyleCommand(style, patch, defaultAnchor, defaultJustify));
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

    /// <summary>
    /// Sets the ruby role and ruby text of one section.
    /// SPEC §5.4 [UPSTREAM]: <c>rb</c> is PC-only, so callers surface a compatibility badge;
    /// the model still records it faithfully for export.
    /// </summary>
    public void SetRuby(Guid cueId, int sectionIndex, RubyRole role, string? rubyText)
    {
        Section section = GetSection(cueId, sectionIndex);
        Execute(new SetRubyCommand(cueId, section, role, rubyText));
    }

    /// <summary>Replaces the explicit format overrides of one section.</summary>
    public void SetFormatOverrides(Guid cueId, int sectionIndex, SectionOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        Section section = GetSection(cueId, sectionIndex);
        Execute(new SetOverridesCommand(cueId, section, overrides.Clone()));
    }

    /// <summary>Replaces literal or regular-expression matches in section text as one undo step.</summary>
    /// <returns>The number of matches replaced.</returns>
    public int ReplaceText(string pattern, string replacement, TextSearchOptions? options = null)
    {
        IReadOnlyList<TextSearch.TextReplacementPlan> plans =
            TextSearch.PlanReplacement(project, pattern, replacement, options);
        if (plans.Count == 0)
        {
            return 0;
        }

        IUndoableCommand[] commands = plans
            .Select(plan => (IUndoableCommand)new SetTextCommand(
                plan.CueId,
                plan.Section,
                plan.ReplacementText))
            .ToArray();
        Execute(new CompositeCommand("검색 및 치환", commands));
        return plans.Sum(plan => plan.MatchCount);
    }

    /// <summary>Moves selected cues by one common delta while preserving duration and track.</summary>
    /// <returns>The effective delta after clamping the earliest cue to the 1 ms format boundary.</returns>
    public TimeSpan ShiftCueTimes(IEnumerable<Guid> cueIds, TimeSpan requestedDelta)
    {
        ArgumentNullException.ThrowIfNull(cueIds);
        Cue[] cues = cueIds.Distinct().Select(GetCue).ToArray();
        if (cues.Length == 0)
        {
            return TimeSpan.Zero;
        }

        TimeSpan minimumStart = TimeSpan.FromMilliseconds(YttConstants.MinimumCueStartMilliseconds);
        TimeSpan earliest = cues.Min(cue => cue.Start);
        TimeSpan effectiveDelta = earliest + requestedDelta < minimumStart
            ? minimumStart - earliest
            : requestedDelta;
        IUndoableCommand[] commands = cues.Select(cue => (IUndoableCommand)new SetTimingCommand(
            project.Cues,
            cue,
            cue.Start + effectiveDelta,
            cue.End + effectiveDelta,
            cue.Track)).ToArray();
        Execute(new CompositeCommand("자막 일괄 시간 이동", commands));
        return effectiveDelta;
    }

    /// <summary>Replaces a cue's sections with the M4 karaoke chips produced by the splitter.</summary>
    /// <remarks>
    /// The source section's formatting is copied to every generated chip. Existing karaoke offsets
    /// are retained on the first chip only; later chips are recorded by the tab or manual offset APIs.
    /// </remarks>
    public KaraokeEditResult SplitCueIntoKaraokeSections(Guid cueId)
    {
        Cue cue = GetCue(cueId);
        KaraokeSplitter splitter = new();
        List<Section> replacements = [];
        foreach (Section source in cue.Sections)
        {
            IReadOnlyList<string> chips = splitter.Split(source.Text);
            if (chips.Count == 0)
            {
                replacements.Add(CloneSection(source, string.Empty, source.KaraokeOffset));
                continue;
            }

            replacements.AddRange(chips.Select((chip, index) => CloneSection(
                source,
                chip,
                index == 0 ? source.KaraokeOffset : null)));
        }

        return ReplaceKaraokeSections(cue, replacements);
    }

    /// <summary>Alias for <see cref="SplitCueIntoKaraokeSections"/> used by editor clients.</summary>
    public KaraokeEditResult AutoSplitKaraokeSections(Guid cueId)
        => SplitCueIntoKaraokeSections(cueId);

    /// <summary>Splits one karaoke chip at a UTF-16 text boundary.</summary>
    public KaraokeEditResult SplitKaraokeSection(Guid cueId, int sectionIndex, int textOffset)
    {
        Cue cue = GetCue(cueId);
        Section source = GetSection(cueId, sectionIndex);
        if ((uint)textOffset >= (uint)source.Text.Length || textOffset == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textOffset), "The split must be inside the section text.");
        }

        if (!System.Globalization.StringInfo.ParseCombiningCharacters(source.Text).Contains(textOffset))
        {
            throw new ArgumentException("The split must be on a Unicode text-element boundary.", nameof(textOffset));
        }

        List<Section> replacements = cue.Sections.ToList();
        Section left = CloneSection(source, source.Text[..textOffset], source.KaraokeOffset);
        Section right = CloneSection(source, source.Text[textOffset..], null);
        replacements.RemoveAt(sectionIndex);
        replacements.InsertRange(sectionIndex, [left, right]);
        return ReplaceKaraokeSections(cue, replacements);
    }

    /// <summary>Merges one karaoke chip with its immediate right neighbour.</summary>
    public KaraokeEditResult MergeKaraokeSections(Guid cueId, int leftSectionIndex)
    {
        Cue cue = GetCue(cueId);
        if ((uint)leftSectionIndex >= (uint)(cue.Sections.Count - 1))
        {
            throw new ArgumentOutOfRangeException(nameof(leftSectionIndex), "A right neighbour is required to merge sections.");
        }

        Section left = cue.Sections[leftSectionIndex];
        Section right = cue.Sections[leftSectionIndex + 1];
        List<Section> replacements = cue.Sections.ToList();
        replacements.RemoveRange(leftSectionIndex, 2);
        replacements.Insert(leftSectionIndex, CloneSection(
            left,
            left.Text + right.Text,
            left.KaraokeOffset));
        return ReplaceKaraokeSections(cue, replacements);
    }

    /// <summary>Sets one section's karaoke offset and repairs non-increasing neighbours.</summary>
    /// <remarks>
    /// <para>
    /// SPEC §5.5 [UPSTREAM]: adjacent equal or decreasing karaoke offsets are repaired by +1 ms
    /// so the exported YTT sections do not have zero-duration transitions.
    /// </para>
    /// </remarks>
    public KaraokeEditResult SetKaraokeOffset(Guid cueId, int sectionIndex, TimeSpan offset)
    {
        if (offset < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Karaoke offsets cannot be negative.");
        }

        Cue cue = GetCue(cueId);
        _ = GetSection(cueId, sectionIndex);
        TimeSpan?[] nextOffsets = cue.Sections.Select(section => section.KaraokeOffset).ToArray();
        nextOffsets[sectionIndex] = offset;
        List<KaraokeOffsetCorrection> corrections = NormalizeKaraokeOffsets(nextOffsets);
        Execute(new SetKaraokeOffsetsCommand(
            this,
            cue,
            nextOffsets,
            karaokeTabCursors.GetValueOrDefault(cue.Id),
            newCursor: null));
        return CreateKaraokeResult(cue, corrections);
    }

    /// <summary>Returns the tab-recording cursor for a cue.</summary>
    public KaraokeTabState GetKaraokeTabState(Guid cueId)
    {
        Cue cue = GetCue(cueId);
        KaraokeTabCursor? cursor = karaokeTabCursors.GetValueOrDefault(cueId);
        int nextIndex = cursor?.NextSectionIndex ?? FindNextUnrecordedSection(cue);
        int lastIndex = cursor?.History.LastOrDefault()?.SectionIndex ?? -1;
        return new KaraokeTabState(cueId, nextIndex, lastIndex, cursor?.History.Count > 0);
    }

    /// <summary>Records a tab/space timing against the next karaoke chip.</summary>
    public KaraokeEditResult RecordKaraokeTab(Guid cueId, TimeSpan offset)
    {
        Cue cue = GetCue(cueId);
        KaraokeTabCursor? oldCursor = karaokeTabCursors.GetValueOrDefault(cueId);
        KaraokeTabCursor cursor = oldCursor ?? new KaraokeTabCursor(FindNextUnrecordedSection(cue), []);
        if ((uint)cursor.NextSectionIndex >= (uint)cue.Sections.Count)
        {
            throw new InvalidOperationException("All karaoke sections already have recorded offsets.");
        }

        TimeSpan?[] previousOffsets = cue.Sections.Select(section => section.KaraokeOffset).ToArray();
        TimeSpan?[] nextOffsets = previousOffsets.ToArray();
        nextOffsets[cursor.NextSectionIndex] = offset;
        List<KaraokeOffsetCorrection> corrections = NormalizeKaraokeOffsets(nextOffsets);
        KaraokeTabCursor nextCursor = new(
            FindNextUnrecordedSection(nextOffsets),
            [.. cursor.History, new KaraokeTabEntry(cursor.NextSectionIndex, previousOffsets)]);
        Execute(new RecordKaraokeTabCommand(this, cue, nextOffsets, oldCursor, nextCursor));
        return CreateKaraokeResult(cue, corrections);
    }

    /// <summary>Cancels the most recent tab timing for a cue.</summary>
    public KaraokeEditResult CancelLastKaraokeTab(Guid cueId)
    {
        Cue cue = GetCue(cueId);
        if (!karaokeTabCursors.TryGetValue(cueId, out KaraokeTabCursor? cursor) ||
            cursor.History.Count == 0)
        {
            throw new InvalidOperationException("There is no karaoke tab timing to cancel.");
        }

        KaraokeTabEntry cancelled = cursor.History[^1];
        TimeSpan?[] nextOffsets = cancelled.PreviousOffsets.ToArray();
        KaraokeTabEntry[] remaining = cursor.History.Take(cursor.History.Count - 1).ToArray();
        KaraokeTabCursor? nextCursor = remaining.Length > 0
            ? new KaraokeTabCursor(cancelled.SectionIndex, remaining)
            : null;
        Execute(new RecordKaraokeTabCommand(this, cue, nextOffsets, cursor, nextCursor));
        return CreateKaraokeResult(cue, []);
    }

    /// <summary>Sets the karaoke effect mode on a cue as one undoable operation.</summary>
    public void SetKaraokeType(Guid cueId, KaraokeType type)
        => Execute(new SetKaraokeTypeCommand(GetCue(cueId), type));

    /// <summary>Applies a supported validation repair as one undoable operation.</summary>
    public bool ApplyValidationFix(Validation.ValidationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        if (!issue.HasAutoFix || issue.CueId is not Guid cueId || project.Cues[cueId] is not Cue cue)
        {
            return false;
        }

        List<IUndoableCommand> commands = [];
        switch (issue.Code)
        {
            case Validation.ValidationCodes.E001:
                TimeSpan minimumStart = TimeSpan.FromMilliseconds(YttConstants.MinimumStartTimeMilliseconds);
                if (cue.Start < minimumStart && cue.End > minimumStart)
                {
                    commands.Add(new SetTimingCommand(project.Cues, cue, minimumStart, cue.End, cue.Track));
                }
                break;
            case Validation.ValidationCodes.E003:
                TimeSpan? previous = null;
                foreach (Section section in cue.Sections)
                {
                    if (section.KaraokeOffset is not TimeSpan current)
                    {
                        continue;
                    }

                    TimeSpan repaired = previous is TimeSpan prior && current <= prior
                        ? prior + TimeSpan.FromMilliseconds(1)
                        : current;
                    if (repaired != current)
                    {
                        commands.Add(new SetKaraokeOffsetCommand(cue.Id, section, repaired));
                    }
                    previous = repaired;
                }
                break;
            case Validation.ValidationCodes.E004:
            case Validation.ValidationCodes.E005:
                foreach (Section section in cue.Sections)
                {
                    ResolvedFormat resolved = FormatResolver.Resolve(
                        project.GetStyle(section.StyleIdOverride ?? cue.StyleId).BaseFormat,
                        section.Overrides);
                    SectionOverrides repaired = section.Overrides.Clone();
                    bool changed = false;
                    if (issue.Code == Validation.ValidationCodes.E004 && IsPureWhite(resolved.Foreground))
                    {
                        repaired.Foreground = new RgbaColor(254, 254, 254, resolved.Foreground.Alpha);
                        changed = true;
                    }
                    else if (issue.Code == Validation.ValidationCodes.E005)
                    {
                        changed |= ClampAlpha(ref repaired, resolved);
                    }

                    if (changed)
                    {
                        commands.Add(new SetOverridesCommand(cue.Id, section, repaired));
                    }
                }
                break;
            default:
                // Only the codes listed above expose an automatic fix (SPEC §11).
                break;
        }

        if (commands.Count == 0)
        {
            return false;
        }

        Execute(new CompositeCommand($"{issue.Code} 자동 수정", commands));
        return true;
    }

    /// <summary>Enables or removes one M3 cue effect for all selected cues.</summary>
    public void SetEffectEnabled(IEnumerable<Guid> cueIds, CueEffectKind kind, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(cueIds);
        List<IUndoableCommand> commands = [];
        foreach (Cue cue in cueIds.Select(GetCue))
        {
            List<CueEffect> next = cue.Effects.Where(effect => GetEffectKind(effect) != kind).ToList();
            if (enabled)
            {
                next.Add(CreateDefaultEffect(kind, cue));
            }
            commands.Add(new ReplaceEffectsCommand(cue, next));
        }
        Execute(new CompositeCommand("효과 변경", commands));
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

    private static void ApplyPatch(SectionFormat target, SectionFormatPatch patch)
    {
        if (patch.Font.HasValue) target.Font = patch.Font.Value;
        if (patch.SizePercent.HasValue) target.SizePercent = Math.Max(75, patch.SizePercent.Value);
        if (patch.Bold.HasValue) target.Bold = patch.Bold.Value;
        if (patch.Italic.HasValue) target.Italic = patch.Italic.Value;
        if (patch.Underline.HasValue) target.Underline = patch.Underline.Value;
        if (patch.Offset.HasValue) target.Offset = patch.Offset.Value;
        if (patch.Foreground.HasValue) target.Foreground = patch.Foreground.Value;
        if (patch.Background.HasValue) target.Background = patch.Background.Value;
        if (patch.SecondaryColor.HasValue) target.SecondaryColor = patch.SecondaryColor.Value;
        if (patch.Edge.HasValue) target.Edge = patch.Edge.Value;
        if (patch.EdgeColor.HasValue) target.EdgeColor = patch.EdgeColor.Value;
        if (patch.Pack.HasValue) target.Pack = patch.Pack.Value;
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

        foreach (CueEffect effect in source.Effects)
        {
            copy.AddEffect(CloneEffect(effect));
        }

        return copy;
    }

    private static CueEffect CloneEffect(CueEffect effect) => effect switch
    {
        MoveEffect move => new MoveEffect(move.FromX, move.FromY, move.ToX, move.ToY, move.StartTime, move.EndTime),
        FadeEffect fade => new FadeEffect(fade.FadeIn, fade.FadeOut)
        {
            Alpha1 = fade.Alpha1,
            Alpha2 = fade.Alpha2,
            Alpha3 = fade.Alpha3,
            T1 = fade.T1,
            T2 = fade.T2,
            T3 = fade.T3,
            T4 = fade.T4,
        },
        ShakeEffect shake => new ShakeEffect(
            shake.RadiusX, shake.RadiusY, shake.StartTime, shake.EndTime),
        ChromaEffect chroma => new ChromaEffect(
            chroma.OffsetX,
            chroma.OffsetY,
            chroma.InTime,
            chroma.OutTime,
            chroma.CustomColors?.ToArray()),
        AnimateEffect animate => new AnimateEffect(animate.Start, animate.End, animate.Accel)
        {
            ToForeground = animate.ToForeground,
            ToEdgeColor = animate.ToEdgeColor,
            ToSizePercent = animate.ToSizePercent,
        },
        KaraokeSettings karaoke => new KaraokeSettings(karaoke.Type)
        {
            CursorText = karaoke.CursorText,
            CursorInterval = karaoke.CursorInterval,
        },
        _ => throw new NotSupportedException($"Unsupported cue effect type {effect.GetType().Name}."),
    };

    private KaraokeEditResult ReplaceKaraokeSections(Cue cue, IReadOnlyList<Section> replacements)
    {
        TimeSpan?[] offsets = replacements.Select(section => section.KaraokeOffset).ToArray();
        List<KaraokeOffsetCorrection> corrections = NormalizeKaraokeOffsets(offsets);
        Section[] normalized = replacements.Select((section, index) =>
            CloneSection(section, section.Text, offsets[index])).ToArray();
        Execute(new ReplaceSectionsCommand(cue, normalized));
        karaokeTabCursors.Remove(cue.Id);
        return CreateKaraokeResult(cue, corrections);
    }

    private static Section CloneSection(Section source, string text, TimeSpan? karaokeOffset)
        => new()
        {
            Text = text,
            KaraokeOffset = karaokeOffset,
            Overrides = source.Overrides.Clone(),
            Ruby = source.Ruby,
            RubyText = source.RubyText,
            StyleIdOverride = source.StyleIdOverride,
        };

    private static List<KaraokeOffsetCorrection> NormalizeKaraokeOffsets(TimeSpan?[] offsets)
    {
        List<KaraokeOffsetCorrection> corrections = [];
        TimeSpan? previous = null;
        TimeSpan step = TimeSpan.FromMilliseconds(YttConstants.KaraokeOffsetStepMilliseconds);
        for (int index = 0; index < offsets.Length; index++)
        {
            if (offsets[index] is not TimeSpan current)
            {
                previous = null;
                continue;
            }

            if (previous is TimeSpan previousValue && current <= previousValue)
            {
                TimeSpan corrected = previousValue + step;
                corrections.Add(new KaraokeOffsetCorrection(index, current, corrected));
                offsets[index] = corrected;
                current = corrected;
            }

            previous = current;
        }

        return corrections;
    }

    private static KaraokeEditResult CreateKaraokeResult(
        Cue cue,
        IReadOnlyList<KaraokeOffsetCorrection> corrections)
        => new(cue.Id, cue.Sections.ToArray(), corrections);

    private static int FindNextUnrecordedSection(Cue cue)
        => FindNextUnrecordedSection(cue.Sections.Select(section => section.KaraokeOffset).ToArray());

    private static int FindNextUnrecordedSection(IReadOnlyList<TimeSpan?> offsets)
    {
        for (int index = 0; index < offsets.Count; index++)
        {
            if (!offsets[index].HasValue)
            {
                return index;
            }
        }

        return offsets.Count;
    }

    private static bool ClampAlpha(ref SectionOverrides overrides, ResolvedFormat resolved)
    {
        bool changed = false;
        if (resolved.Foreground.Alpha == byte.MaxValue)
        {
            overrides.Foreground = WithAlpha(resolved.Foreground, YttConstants.MaximumOpacity);
            changed = true;
        }
        if (resolved.Background.Alpha == byte.MaxValue)
        {
            overrides.Background = WithAlpha(resolved.Background, YttConstants.MaximumOpacity);
            changed = true;
        }
        if (resolved.SecondaryColor.Alpha == byte.MaxValue)
        {
            overrides.SecondaryColor = WithAlpha(resolved.SecondaryColor, YttConstants.MaximumOpacity);
            changed = true;
        }
        if (resolved.EdgeColor.Alpha == byte.MaxValue)
        {
            overrides.EdgeColor = WithAlpha(resolved.EdgeColor, YttConstants.MaximumOpacity);
            changed = true;
        }
        return changed;
    }

    private static bool IsPureWhite(RgbaColor color)
        => color.Red == byte.MaxValue && color.Green == byte.MaxValue && color.Blue == byte.MaxValue;

    private static RgbaColor WithAlpha(RgbaColor color, byte alpha)
        => new(color.Red, color.Green, color.Blue, alpha);

    private static CueEffectKind? GetEffectKind(CueEffect effect) => effect switch
    {
        MoveEffect => CueEffectKind.Move,
        FadeEffect => CueEffectKind.Fade,
        ShakeEffect => CueEffectKind.Shake,
        ChromaEffect => CueEffectKind.Chroma,
        AnimateEffect => CueEffectKind.Animate,
        _ => null,
    };

    private static CueEffect CreateDefaultEffect(CueEffectKind kind, Cue cue) => kind switch
    {
        CueEffectKind.Move => new MoveEffect(
            YttMath.ToPixelCoordinate(checked((int)Math.Round(cue.PositionX)), YttConstants.ReferenceWidth),
            YttMath.ToPixelCoordinate(checked((int)Math.Round(cue.PositionY)), YttConstants.ReferenceHeight),
            YttMath.ToPixelCoordinate(checked((int)Math.Round(cue.PositionX)), YttConstants.ReferenceWidth) + 50,
            YttMath.ToPixelCoordinate(checked((int)Math.Round(cue.PositionY)), YttConstants.ReferenceHeight)),
        CueEffectKind.Fade => new FadeEffect(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250)),
        CueEffectKind.Shake => new ShakeEffect(),
        CueEffectKind.Chroma => new ChromaEffect(),
        CueEffectKind.Animate => new AnimateEffect(TimeSpan.Zero, cue.End - cue.Start) { ToSizePercent = 125 },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

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

    private sealed class SetDirectionCommand(Cue cue, TextDirection direction) : IUndoableCommand
    {
        private readonly TextDirection oldValue = cue.Direction;
        public string Label => "텍스트 방향 변경";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cue.Id];
        public void Execute() => cue.Direction = direction;
        public void Undo() => cue.Direction = oldValue;
        public bool TryMergeWith(IUndoableCommand previous) => false;
    }

    private sealed class SetZOrderCommand(Cue cue, int zOrder) : IUndoableCommand
    {
        private readonly int oldValue = cue.ZOrder;
        public string Label => "그리기 순서 변경";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cue.Id];
        public void Execute() => cue.ZOrder = zOrder;
        public void Undo() => cue.ZOrder = oldValue;
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

    private sealed class UpdateStyleCommand(
        StylePreset style,
        SectionFormatPatch patch,
        AnchorPoint? defaultAnchor,
        Justification? defaultJustify) : IUndoableCommand
    {
        private readonly SectionFormatSnapshot oldBaseFormat = SectionFormatSnapshot.Capture(style.BaseFormat);
        private readonly AnchorPoint oldDefaultAnchor = style.DefaultAnchor;
        private readonly Justification oldDefaultJustify = style.DefaultJustify;

        public string Label => "스타일 업데이트";
        public IReadOnlyCollection<Guid> AffectedCueIds => [];

        public void Execute()
        {
            ApplyPatch(style.BaseFormat, patch);
            if (defaultAnchor.HasValue)
            {
                style.DefaultAnchor = defaultAnchor.Value;
            }

            if (defaultJustify.HasValue)
            {
                style.DefaultJustify = defaultJustify.Value;
            }
        }

        public void Undo()
        {
            oldBaseFormat.Restore(style.BaseFormat);
            style.DefaultAnchor = oldDefaultAnchor;
            style.DefaultJustify = oldDefaultJustify;
        }

        public bool TryMergeWith(IUndoableCommand previous) => false;
    }

    private sealed class SectionFormatSnapshot
    {
        private readonly YtFont font;
        private readonly int sizePercent;
        private readonly bool bold;
        private readonly bool italic;
        private readonly bool underline;
        private readonly ScriptOffset offset;
        private readonly RgbaColor foreground;
        private readonly RgbaColor background;
        private readonly RgbaColor secondaryColor;
        private readonly EdgeType edge;
        private readonly RgbaColor edgeColor;
        private readonly bool pack;

        private SectionFormatSnapshot(SectionFormat source)
        {
            font = source.Font;
            sizePercent = source.SizePercent;
            bold = source.Bold;
            italic = source.Italic;
            underline = source.Underline;
            offset = source.Offset;
            foreground = source.Foreground;
            background = source.Background;
            secondaryColor = source.SecondaryColor;
            edge = source.Edge;
            edgeColor = source.EdgeColor;
            pack = source.Pack;
        }

        public static SectionFormatSnapshot Capture(SectionFormat source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return new SectionFormatSnapshot(source);
        }

        public void Restore(SectionFormat target)
        {
            target.Font = font;
            target.SizePercent = sizePercent;
            target.Bold = bold;
            target.Italic = italic;
            target.Underline = underline;
            target.Offset = offset;
            target.Foreground = foreground;
            target.Background = background;
            target.SecondaryColor = secondaryColor;
            target.Edge = edge;
            target.EdgeColor = edgeColor;
            target.Pack = pack;
        }
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

    private sealed class SetRubyCommand(Guid cueId, Section section, RubyRole role, string? rubyText)
        : IUndoableCommand
    {
        private readonly RubyRole oldRole = section.Ruby;
        private readonly string? oldText = section.RubyText;

        public string Label => "루비 변경";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cueId];

        public void Execute()
        {
            section.Ruby = role;
            section.RubyText = string.IsNullOrEmpty(rubyText) ? null : rubyText;
        }

        public void Undo()
        {
            section.Ruby = oldRole;
            section.RubyText = oldText;
        }

        public bool TryMergeWith(IUndoableCommand previous) => false;
    }

    private sealed class SetKaraokeOffsetCommand(Guid cueId, Section section, TimeSpan offset) : IUndoableCommand
    {
        private readonly TimeSpan? oldOffset = section.KaraokeOffset;

        public string Label => "가라오케 오프셋 수정";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cueId];
        public void Execute() => section.KaraokeOffset = offset;
        public void Undo() => section.KaraokeOffset = oldOffset;
        public bool TryMergeWith(IUndoableCommand previous) => false;
    }

    private sealed class ReplaceSectionsCommand(Cue cue, IReadOnlyList<Section> replacements) : IUndoableCommand
    {
        private readonly Section[] oldSections = cue.Sections.ToArray();
        private readonly Section[] newSections = replacements.ToArray();

        public string Label => "가라오케 음절 편집";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cue.Id];
        public void Execute() => cue.ReplaceSections(newSections);
        public void Undo() => cue.ReplaceSections(oldSections);
        public bool TryMergeWith(IUndoableCommand previous) => false;
    }

    private sealed class SetKaraokeOffsetsCommand(
        DocumentEditor owner,
        Cue cue,
        IReadOnlyList<TimeSpan?> offsets,
        KaraokeTabCursor? oldCursor,
        KaraokeTabCursor? newCursor) : IUndoableCommand
    {
        private readonly TimeSpan?[] oldOffsets = cue.Sections.Select(section => section.KaraokeOffset).ToArray();
        private readonly TimeSpan?[] newOffsets = offsets.ToArray();

        public string Label => "가라오케 타이밍 변경";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cue.Id];
        public void Execute() => Apply(newOffsets, newCursor);
        public void Undo() => Apply(oldOffsets, oldCursor);
        public bool TryMergeWith(IUndoableCommand previous) => false;

        private void Apply(IReadOnlyList<TimeSpan?> values, KaraokeTabCursor? cursor)
        {
            for (int index = 0; index < cue.Sections.Count; index++)
            {
                cue.Sections[index].KaraokeOffset = values[index];
            }

            if (cursor is null)
            {
                owner.karaokeTabCursors.Remove(cue.Id);
            }
            else
            {
                owner.karaokeTabCursors[cue.Id] = cursor;
            }
        }
    }

    private sealed class RecordKaraokeTabCommand(
        DocumentEditor owner,
        Cue cue,
        IReadOnlyList<TimeSpan?> offsets,
        KaraokeTabCursor? oldCursor,
        KaraokeTabCursor? newCursor) : IUndoableCommand
    {
        private readonly TimeSpan?[] oldOffsets = cue.Sections.Select(section => section.KaraokeOffset).ToArray();
        private readonly TimeSpan?[] newOffsets = offsets.ToArray();

        public string Label => "가라오케 탭 입력";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cue.Id];
        public void Execute() => Apply(newOffsets, newCursor);
        public void Undo() => Apply(oldOffsets, oldCursor);
        public bool TryMergeWith(IUndoableCommand previous) => false;

        private void Apply(IReadOnlyList<TimeSpan?> values, KaraokeTabCursor? cursor)
        {
            for (int index = 0; index < cue.Sections.Count; index++)
            {
                cue.Sections[index].KaraokeOffset = values[index];
            }

            if (cursor is null)
            {
                owner.karaokeTabCursors.Remove(cue.Id);
            }
            else
            {
                owner.karaokeTabCursors[cue.Id] = cursor;
            }
        }
    }

    private sealed class SetKaraokeTypeCommand : IUndoableCommand
    {
        private readonly Cue cue;
        private readonly CueEffect[] oldEffects;
        private readonly CueEffect[] newEffects;

        public SetKaraokeTypeCommand(Cue cue, KaraokeType type)
        {
            this.cue = cue;
            AffectedCueIds = [cue.Id];
            oldEffects = cue.Effects.ToArray();
            KaraokeSettings? existing = oldEffects.OfType<KaraokeSettings>().LastOrDefault();
            List<CueEffect> next = oldEffects.Where(effect => effect is not KaraokeSettings).ToList();
            if (type != KaraokeType.None)
            {
                next.Add(new KaraokeSettings(type)
                {
                    CursorText = existing?.CursorText,
                    CursorInterval = existing?.CursorInterval,
                });
            }

            newEffects = next.ToArray();
        }

        public string Label => "가라오케 타입 변경";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; }
        public void Execute() => cue.ReplaceEffects(newEffects);
        public void Undo() => cue.ReplaceEffects(oldEffects);
        public bool TryMergeWith(IUndoableCommand previous) => false;
    }

    private sealed record KaraokeTabCursor(int NextSectionIndex, IReadOnlyList<KaraokeTabEntry> History);

    private sealed record KaraokeTabEntry(int SectionIndex, IReadOnlyList<TimeSpan?> PreviousOffsets);

    private sealed class ReplaceEffectsCommand(Cue cue, IReadOnlyList<CueEffect> effects) : IUndoableCommand
    {
        private readonly CueEffect[] oldEffects = cue.Effects.ToArray();
        private readonly CueEffect[] newEffects = effects.ToArray();

        public string Label => "효과 변경";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [cue.Id];
        public void Execute() => cue.ReplaceEffects(newEffects);
        public void Undo() => cue.ReplaceEffects(oldEffects);
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
