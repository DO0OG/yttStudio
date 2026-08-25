namespace YttStudio.Core;

/// <summary>Represents a subtitle project independent of its source file format.</summary>
public sealed class SubtitleProject
{
    public SubtitleProject()
    {
        Styles = new StylePresetCollection();
        Cues = new CueCollection();
        Settings = new ProjectSettings();
    }

    public string? VideoPath { get; internal set; }
    public VideoInfo? Video { get; internal set; }
    public StylePresetCollection Styles { get; }
    public CueCollection Cues { get; }
    public ProjectSettings Settings { get; internal set; }

    /// <summary>Gets the style used for a cue or section, falling back to Default.</summary>
    public StylePreset GetStyle(Guid? styleId)
        => styleId is Guid id && Styles[id] is StylePreset style ? style : Styles.Default;
}

/// <summary>Contains optional metadata for an associated video.</summary>
public sealed class VideoInfo
{
    public VideoInfo(int width, int height, TimeSpan duration, double nominalFps)
    {
        Width = width;
        Height = height;
        Duration = duration;
        NominalFps = nominalFps;
    }

    public int Width { get; }
    public int Height { get; }
    public TimeSpan Duration { get; }
    public double NominalFps { get; }
}

/// <summary>Contains editor settings that travel with an in-memory project.</summary>
public sealed class ProjectSettings
{
    public RgbaColor PreviewBackground { get; internal set; } = new(32, 32, 32, byte.MaxValue);
    public bool UseCheckerboard { get; internal set; }
}

/// <summary>Defines a named reusable subtitle style.</summary>
public sealed class StylePreset
{
    public StylePreset(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; }
    public string Name { get; internal set; } = "Default";
    public SectionFormat BaseFormat { get; internal set; } = new();
    public AnchorPoint DefaultAnchor { get; internal set; } = AnchorPoint.BottomCenter;
    public Justification DefaultJustify { get; internal set; } = Justification.Center;
    public IReadOnlyList<EdgeType> ExtraEdges { get; internal set; } = [];
}

/// <summary>Stores style presets by stable identifier.</summary>
public sealed class StylePresetCollection : IReadOnlyCollection<StylePreset>
{
    private readonly Dictionary<Guid, StylePreset> byId = [];

    internal StylePresetCollection()
    {
        Default = new StylePreset(Guid.Empty);
        byId.Add(Default.Id, Default);
    }

    public int Count => byId.Count;
    public StylePreset Default { get; }
    public StylePreset? this[Guid id] => byId.GetValueOrDefault(id);

    internal void Add(StylePreset style) => byId.Add(style.Id, style);
    internal bool Remove(Guid id) => id != Guid.Empty && byId.Remove(id);

    public IEnumerator<StylePreset> GetEnumerator() => byId.Values.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
