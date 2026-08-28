namespace YttStudio.Core.Editing;

/// <summary>가라오케 구간 분할 · 병합과 탭 기록 편집을 담당한다.</summary>
public sealed partial class DocumentEditor
{

    /// <summary>분할기가 만든 가라오케 칩으로 큐의 섹션을 교체한다.</summary>
    /// <remarks>
    /// 원본 섹션의 서식은 생성된 모든 칩에 복사된다. 기존 가라오케 오프셋은
    /// 첫 칩에만 유지된다. 이후 칩은 탭이나 수동 오프셋 API 로 기록한다.
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

    /// <summary>편집기 클라이언트가 쓰는 <see cref="SplitCueIntoKaraokeSections"/> 별칭이다.</summary>
    public KaraokeEditResult AutoSplitKaraokeSections(Guid cueId)
        => SplitCueIntoKaraokeSections(cueId);

    /// <summary>가라오케 칩 하나를 UTF-16 텍스트 경계에서 분할한다.</summary>
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

    /// <summary>가라오케 칩 하나를 바로 오른쪽 이웃과 병합한다.</summary>
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

    /// <summary>섹션 하나의 가라오케 오프셋을 설정하고 증가하지 않는 이웃을 보정한다.</summary>
    /// <remarks>
    /// <para>
    /// [UPSTREAM] 인접한 가라오케 오프셋이 같거나 줄어들면 +1 ms 로 보정해
    /// 내보낸 YTT 섹션에 길이 0 인 전환이 생기지 않게 한다.
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

    /// <summary>큐의 탭 기록 커서를 돌려준다.</summary>
    public KaraokeTabState GetKaraokeTabState(Guid cueId)
    {
        Cue cue = GetCue(cueId);
        KaraokeTabCursor? cursor = karaokeTabCursors.GetValueOrDefault(cueId);
        int nextIndex = cursor?.NextSectionIndex ?? FindNextUnrecordedSection(cue);
        int lastIndex = cursor?.History.LastOrDefault()?.SectionIndex ?? -1;
        return new KaraokeTabState(cueId, nextIndex, lastIndex, cursor?.History.Count > 0);
    }

    /// <summary>다음 가라오케 칩에 탭 타이밍을 기록한다.</summary>
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

    /// <summary>큐의 가장 최근 탭 타이밍을 취소한다.</summary>
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

    /// <summary>큐의 가라오케 효과 모드를 하나의 되돌릴 수 있는 작업으로 설정한다.</summary>
    public void SetKaraokeType(Guid cueId, KaraokeType type)
        => Execute(new SetKaraokeTypeCommand(GetCue(cueId), type));

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
}
