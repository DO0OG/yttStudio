namespace YttStudio.Core.Editing;

/// <summary>실행 취소 스택에 쌓이는 편집 명령들의 구현을 모은다.</summary>
public sealed partial class DocumentEditor
{

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

    private sealed class SetVideoCommand(
        SubtitleProject project,
        string? path,
        VideoInfo? video) : IUndoableCommand
    {
        private readonly string? oldPath = project.VideoPath;
        private readonly VideoInfo? oldVideo = project.Video;

        public string Label => "영상 정보 변경";
        public IReadOnlyCollection<Guid> AffectedCueIds { get; } = [];

        public void Execute()
        {
            project.VideoPath = path;
            project.Video = video;
        }

        public void Undo()
        {
            project.VideoPath = oldPath;
            project.Video = oldVideo;
        }

        public bool TryMergeWith(IUndoableCommand previous) => false;
    }
}
