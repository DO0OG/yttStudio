namespace YttStudio.Core.Editing;

/// <summary>자막 프로젝트의 유일한 공개 변경 경계를 제공한다.</summary>
public sealed partial class DocumentEditor
{
    private static long nextIdentity;
    private readonly SubtitleProject project;
    private readonly List<IUndoableCommand> undoStack = [];
    private readonly List<IUndoableCommand> redoStack = [];
    private List<IUndoableCommand>? transactionCommands;
    private string? transactionLabel;
    private int undoFreeDepth;
    private readonly Dictionary<Guid, KaraokeTabCursor> karaokeTabCursors = [];
    private long revision;
    private readonly long identity = Interlocked.Increment(ref nextIdentity);

    public DocumentEditor(SubtitleProject project)
    {
        this.project = project ?? throw new ArgumentNullException(nameof(project));
    }

    /// <summary>이 편집기 인스턴스를 식별하는 단조 증가 값이다.</summary>
    /// <remarks>
    /// 새 프로젝트를 열면 Revision 이 다시 시작하므로, 이전 프로젝트에 대해 예약된
    /// 프리뷰가 새 프로젝트와 같은 입력으로 오인되지 않도록 인스턴스 identity 를 함께
    /// 사용한다.
    /// </remarks>
    public long Identity => identity;

    /// <summary>성공적으로 적용된 모델 변경의 현재 Revision이다.</summary>
    /// <remarks>
    /// 실행·취소·재실행 모두 모델을 바꾸는 성공 경로에서만 증가한다. 명령이 예외를
    /// 던지면 호출 전 값이 유지되므로 프리뷰 입력 키가 유효한 상태를 가리킨다.
    /// </remarks>
    public long Revision => Interlocked.Read(ref revision);

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;
    public string? UndoLabel => CanUndo ? undoStack[^1].Label : null;
    public string? RedoLabel => CanRedo ? redoStack[^1].Label : null;

    /// <summary>실행 중인 변경 그룹이 있는지 가져온다.</summary>
    public bool IsTransactionActive => transactionCommands is not null;

    /// <summary>큐를 만들어 추가하는 것을 하나의 되돌릴 수 있는 작업으로 처리한다.</summary>
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

    /// <summary>큐를 제거한다.</summary>
    public void RemoveCue(Guid cueId)
    {
        Cue cue = GetCue(cueId);
        Execute(new RemoveCueCommand(project.Cues, cue));
    }

    /// <summary>여러 큐를 하나의 되돌릴 수 있는 작업으로 제거한다.</summary>
    public void RemoveCues(IEnumerable<Guid> cueIds)
        => ExecuteForCues("자막 삭제", cueIds, cue => new RemoveCueCommand(project.Cues, cue));

    /// <summary>선택한 큐를 복제하고 만들어진 사본을 돌려준다.</summary>
    public IReadOnlyList<Cue> DuplicateCues(IEnumerable<Guid> cueIds)
    {
        List<Cue> copies = cueIds.Distinct().Select(GetCue).Select(CloneCue).ToList();
        Execute(new CompositeCommand("자막 복제", copies.Select(cue => new AddCueCommand(project.Cues, cue))));
        return copies;
    }

    /// <summary>큐 앵커를 새 YTT 좌표로 옮긴다.</summary>
    public void MoveCue(Guid cueId, double positionX, double positionY)
    {
        if (positionX is < 0 or > 100 || positionY is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(positionX), "YTT positions must be in the range 0 through 100.");
        }

        Execute(new MoveCueCommand(GetCue(cueId), positionX, positionY));
    }

    /// <summary>여러 큐를 하나의 되돌릴 수 있는 작업으로 옮긴다.</summary>
    public void MoveCues(IReadOnlyDictionary<Guid, CanvasPoint> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        Execute(new CompositeCommand("자막 이동", positions.Select(item =>
            new MoveCueCommand(GetCue(item.Key), item.Value.X, item.Value.Y))));
    }

    /// <summary>호출자가 측정한 박스 위치를 유지하면서 큐의 앵커와 좌표를 바꾼다.</summary>
    public void SetAnchor(Guid cueId, AnchorPoint anchor, double positionX, double positionY)
        => Execute(new SetAnchorCommand(GetCue(cueId), anchor, positionX, positionY));

    /// <summary>박스 내부 텍스트 정렬을 바꾼다.</summary>
    public void SetJustification(IEnumerable<Guid> cueIds, Justification justification)
        => ExecuteForCues("내부 정렬 변경", cueIds, cue => new SetJustificationCommand(cue, justification));

    /// <summary>선택한 큐의 문자 진행 방향을 바꾼다.</summary>
    public void SetDirection(IEnumerable<Guid> cueIds, TextDirection direction)
        => ExecuteForCues("텍스트 방향 변경", cueIds, cue => new SetDirectionCommand(cue, direction));

    /// <summary>선택한 큐의 그리기 순서를 바꾼다.</summary>
    public void SetZOrder(IEnumerable<Guid> cueIds, int zOrder)
        => ExecuteForCues("그리기 순서 변경", cueIds, cue => new SetZOrderCommand(cue, zOrder));

    /// <summary>큐의 시간 범위와 트랙을 바꾼다.</summary>
    public void SetTiming(Guid cueId, TimeSpan start, TimeSpan end, int track)
    {
        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "Cue end must be later than its start.");
        }

        Execute(new SetTimingCommand(project.Cues, GetCue(cueId), start, end, Math.Max(0, track)));
    }

    /// <summary>선택한 큐를 백분율 증분만큼 옮긴다.</summary>
    public void Nudge(IEnumerable<Guid> cueIds, double deltaX, double deltaY)
    {
        Dictionary<Guid, CanvasPoint> positions = cueIds.Select(GetCue).ToDictionary(
            cue => cue.Id,
            cue => new CanvasPoint(Math.Clamp(cue.PositionX + deltaX, 0, 100),
                Math.Clamp(cue.PositionY + deltaY, 0, 100)));
        MoveCues(positions);
    }

    /// <summary>선택한 큐의 모든 섹션에 명시적 서식 값을 적용한다.</summary>
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

    /// <summary>선택한 큐에 스타일 프리셋을 적용한다.</summary>
    public void ApplyStyle(IEnumerable<Guid> cueIds, Guid? styleId)
    {
        if (styleId is Guid id && project.Styles[id] is null)
        {
            throw new KeyNotFoundException($"Style {id} does not exist.");
        }

        ExecuteForCues("스타일 적용", cueIds, cue => new SetStyleCommand(cue, styleId));
    }

    /// <summary>이름이 있는 스타일 프리셋을 만든다.</summary>
    public StylePreset AddStyle(string name)
    {
        StylePreset style = new(Guid.NewGuid()) { Name = NormalizeStyleName(name) };
        Execute(new AddStyleCommand(project.Styles, style));
        return style;
    }

    /// <summary>스타일 프리셋의 이름을 바꾼다.</summary>
    public void RenameStyle(Guid styleId, string name)
    {
        StylePreset style = GetMutableStyle(styleId);
        Execute(new RenameStyleCommand(style, NormalizeStyleName(name)));
    }

    /// <summary>스타일 프리셋의 선택한 필드를 갱신한다.</summary>
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

    /// <summary>해석된 외형을 섹션 재정의로 굳히면서 스타일을 삭제한다.</summary>
    public void DeleteStyle(Guid styleId)
    {
        StylePreset style = GetMutableStyle(styleId);
        Execute(new DeleteStyleCommand(project, style));
    }

    /// <summary>섹션 하나의 텍스트를 바꾼다.</summary>
    public void SetText(Guid cueId, int sectionIndex, string text)
    {
        Section section = GetSection(cueId, sectionIndex);
        Execute(new SetTextCommand(cueId, section, text ?? string.Empty));
    }

    /// <summary>
    /// 섹션 하나의 루비 역할과 루비 텍스트를 설정한다.
    /// [UPSTREAM] <c>rb</c> 는 PC 전용이므로 호출자가 호환성 배지를 노출한다.
    /// 모델은 내보내기를 위해 값을 그대로 기록한다.
    /// </summary>
    public void SetRuby(Guid cueId, int sectionIndex, RubyRole role, string? rubyText)
    {
        Section section = GetSection(cueId, sectionIndex);
        Execute(new SetRubyCommand(cueId, section, role, rubyText));
    }

    /// <summary>섹션 하나의 명시적 서식 재정의를 교체한다.</summary>
    public void SetFormatOverrides(Guid cueId, int sectionIndex, SectionOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        Section section = GetSection(cueId, sectionIndex);
        Execute(new SetOverridesCommand(cueId, section, overrides.Clone()));
    }

    /// <summary>섹션 텍스트의 리터럴 또는 정규식 일치를 하나의 실행 취소 단위로 치환한다.</summary>
    /// <returns>치환된 일치 개수다.</returns>
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

    /// <summary>길이와 트랙을 유지하면서 선택한 큐를 공통 증분만큼 옮긴다.</summary>
    /// <returns>가장 이른 큐를 1 ms 경계로 보정한 뒤의 실제 이동량이다.</returns>
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

    /// <summary>지원되는 검증 보정을 하나의 되돌릴 수 있는 작업으로 적용한다.</summary>
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
                // 위에 나열한 코드만 자동 수정을 제공한다.
                break;
        }

        if (commands.Count == 0)
        {
            return false;
        }

        Execute(new CompositeCommand($"{issue.Code} 자동 수정", commands));
        return true;
    }

    /// <summary>선택한 모든 큐에서 큐 효과 하나를 켜거나 제거한다.</summary>
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

    /// <summary>이후 커맨드를 하나의 실행 취소 단위로 묶기 시작한다.</summary>
    public void BeginTransaction(string label)
    {
        if (transactionCommands is not null)
        {
            throw new InvalidOperationException("A transaction is already active.");
        }

        transactionLabel = string.IsNullOrWhiteSpace(label) ? "변경" : label;
        transactionCommands = [];
    }

    /// <summary>현재 그룹을 하나의 실행 취소 단위로 확정한다.</summary>
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

    /// <summary>
    /// 현재 그룹의 실행을 역순으로 되돌리고 그룹을 폐기한다.
    /// 취소는 undo/redo 스택에 기록을 만들거나 기존 기록을 변경하지 않는다.
    /// </summary>
    public void CancelTransaction()
    {
        if (transactionCommands is null)
        {
            throw new InvalidOperationException("No transaction is active.");
        }

        List<IUndoableCommand> commands = transactionCommands;
        transactionCommands = null;
        transactionLabel = null;

        for (int index = commands.Count - 1; index >= 0; index--)
        {
            commands[index].Undo();
        }

        if (commands.Count > 0)
        {
            Interlocked.Increment(ref revision);
        }
    }

    /// <summary>변경이 실행 취소 기록을 만들지 않는 범위를 연다.</summary>
    public IDisposable BeginUndoFreeMutation()
    {
        undoFreeDepth++;
        return new UndoFreeScope(this);
    }

    /// <summary>가장 최근 변경을 되돌린다.</summary>
    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        IUndoableCommand command = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);
        command.Undo();
        Interlocked.Increment(ref revision);
        redoStack.Add(command);
    }

    /// <summary>가장 최근에 취소한 변경을 다시 적용한다.</summary>
    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        IUndoableCommand command = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);
        command.Execute();
        Interlocked.Increment(ref revision);
        PushUndo(command, clearRedo: false);
    }

    private void Execute(IUndoableCommand command)
    {
        command.Execute();
        Interlocked.Increment(ref revision);
        if (undoFreeDepth > 0)
        {
            return;
        }

        if (transactionCommands is not null)
        {
            if (transactionCommands.Count == 0 || !command.TryMergeWith(transactionCommands[^1]))
            {
                transactionCommands.Add(command);
            }

            return;
        }

        redoStack.Clear();
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

}
