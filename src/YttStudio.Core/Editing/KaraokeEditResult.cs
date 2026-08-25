namespace YttStudio.Core.Editing;

/// <summary>가라오케 편집 작업 하나와 자동 오프셋 보정 내역을 기술한다.</summary>
public sealed class KaraokeEditResult
{
    internal KaraokeEditResult(
        Guid cueId,
        IReadOnlyList<Section> sections,
        IReadOnlyList<KaraokeOffsetCorrection> offsetCorrections)
    {
        CueId = cueId;
        Sections = sections;
        OffsetCorrections = offsetCorrections;
    }

    /// <summary>작업으로 바뀐 큐를 가져온다.</summary>
    public Guid CueId { get; }

    /// <summary>작업 후의 큐 섹션을 가져온다.</summary>
    public IReadOnlyList<Section> Sections { get; }

    /// <summary>작업으로 오프셋이 보정된 섹션을 가져온다.</summary>
    public IReadOnlyList<KaraokeOffsetCorrection> OffsetCorrections { get; }

    /// <summary>작업이 자동 +1 ms 보정을 한 번 이상 적용했는지 가져온다.</summary>
    public bool AutoCorrectedOffsets => OffsetCorrections.Count > 0;

    /// <summary>보정 상태를 보고하는 호출자를 위한 <see cref="AutoCorrectedOffsets"/> 별칭이다.</summary>
    public bool AppliedOffsetCorrection => AutoCorrectedOffsets;

    /// <summary>자동으로 보정된 오프셋 개수를 가져온다.</summary>
    public int OffsetCorrectionCount => OffsetCorrections.Count;
}

/// <summary>자동 가라오케 오프셋 보정 하나를 기록한다.</summary>
public sealed record KaraokeOffsetCorrection(
    int SectionIndex,
    TimeSpan PreviousOffset,
    TimeSpan CorrectedOffset);

/// <summary>탭으로 가라오케 타이밍을 기록할 때 쓰는 임시 커서를 기술한다.</summary>
public sealed record KaraokeTabState(
    Guid CueId,
    int NextSectionIndex,
    int LastRecordedSectionIndex,
    bool CanCancelLastTab);
