namespace YttStudio.Core;

/// <summary>원본 파일 형식과 무관한 자막 프로젝트다.</summary>
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

    /// <summary>큐나 섹션이 쓰는 스타일을 가져온다. 없으면 Default 로 되돌아간다.</summary>
    public StylePreset GetStyle(Guid? styleId)
        => styleId is Guid id && Styles[id] is StylePreset style ? style : Styles.Default;
}

/// <summary>연결된 영상의 선택적 메타데이터를 담는다.</summary>
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

/// <summary>메모리상의 프로젝트와 함께 이동하는 편집기 설정을 담는다.</summary>
public sealed class ProjectSettings
{
    public RgbaColor PreviewBackground { get; internal set; } = new(32, 32, 32, byte.MaxValue);
    public bool UseCheckerboard { get; internal set; }
}

/// <summary>이름이 있는 재사용 가능한 자막 스타일을 정의한다.</summary>
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

/// <summary>스타일 프리셋을 안정적인 식별자로 저장한다.</summary>
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
