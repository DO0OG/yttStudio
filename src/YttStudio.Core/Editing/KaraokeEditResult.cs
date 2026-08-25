namespace YttStudio.Core.Editing;

/// <summary>Describes one karaoke editing operation and any automatic offset repairs.</summary>
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

    /// <summary>Gets the cue changed by the operation.</summary>
    public Guid CueId { get; }

    /// <summary>Gets the cue sections after the operation.</summary>
    public IReadOnlyList<Section> Sections { get; }

    /// <summary>Gets the sections whose offsets were repaired by the operation.</summary>
    public IReadOnlyList<KaraokeOffsetCorrection> OffsetCorrections { get; }

    /// <summary>Gets whether the operation applied one or more automatic +1 ms repairs.</summary>
    public bool AutoCorrectedOffsets => OffsetCorrections.Count > 0;

    /// <summary>Alias for <see cref="AutoCorrectedOffsets"/> for callers reporting repair status.</summary>
    public bool AppliedOffsetCorrection => AutoCorrectedOffsets;

    /// <summary>Gets the number of offsets repaired automatically.</summary>
    public int OffsetCorrectionCount => OffsetCorrections.Count;
}

/// <summary>Records one automatic karaoke offset repair.</summary>
public sealed record KaraokeOffsetCorrection(
    int SectionIndex,
    TimeSpan PreviousOffset,
    TimeSpan CorrectedOffset);

/// <summary>Describes the transient cursor used while tab-recording karaoke timings.</summary>
public sealed record KaraokeTabState(
    Guid CueId,
    int NextSectionIndex,
    int LastRecordedSectionIndex,
    bool CanCancelLastTab);
