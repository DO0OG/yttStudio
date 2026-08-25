using System.Collections.ObjectModel;

namespace YttStudio.Core;

/// <summary>Represents one timed subtitle cue.</summary>
public sealed class Cue
{
    private readonly List<Section> sections = [];
    private readonly ReadOnlyCollection<Section> readOnlySections;
    private readonly List<CueEffect> effects = [];
    private readonly ReadOnlyCollection<CueEffect> readOnlyEffects;

    internal Cue(Guid id)
    {
        Id = id;
        readOnlySections = sections.AsReadOnly();
        readOnlyEffects = effects.AsReadOnly();
    }

    public Guid Id { get; }
    public TimeSpan Start { get; internal set; }
    public TimeSpan End { get; internal set; }
    public int Track { get; internal set; }
    public int ZOrder { get; internal set; }
    public AnchorPoint Anchor { get; internal set; } = AnchorPoint.BottomCenter;
    public double PositionX { get; internal set; } = 50;
    public double PositionY { get; internal set; } = 90;
    public Justification Justify { get; internal set; } = Justification.Center;
    public TextDirection Direction { get; internal set; } = TextDirection.Horizontal;
    public Guid? StyleId { get; internal set; }
    public IReadOnlyList<Section> Sections => readOnlySections;
    public IReadOnlyList<CueEffect> Effects => readOnlyEffects;

    internal void AddSection(Section section) => sections.Add(section);
    internal void InsertSection(int index, Section section) => sections.Insert(index, section);
    internal void RemoveSectionAt(int index) => sections.RemoveAt(index);
    internal void ReplaceSections(IEnumerable<Section> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        sections.Clear();
        sections.AddRange(replacements);
    }
    internal void AddEffect(CueEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        effects.Add(effect);
    }

    internal void InsertEffect(int index, CueEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        effects.Insert(index, effect);
    }

    internal void RemoveEffectAt(int index) => effects.RemoveAt(index);
    internal void ReplaceEffects(IEnumerable<CueEffect> replacements)
    {
        effects.Clear();
        effects.AddRange(replacements);
    }
}

/// <summary>Represents one independently formatted span inside a cue.</summary>
public sealed class Section
{
    public string Text { get; internal set; } = string.Empty;
    public TimeSpan? KaraokeOffset { get; internal set; }
    public SectionOverrides Overrides { get; internal set; } = new();
    public RubyRole Ruby { get; internal set; } = RubyRole.None;
    public string? RubyText { get; internal set; }
    public Guid? StyleIdOverride { get; internal set; }
}
