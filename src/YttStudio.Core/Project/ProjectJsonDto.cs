using System.Text.Json.Serialization;

namespace YttStudio.Core.Project;

internal sealed class ProjectJsonDto
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }
    public string? VideoPath { get; set; }
    public VideoJsonDto? Video { get; set; }
    public ProjectSettingsJsonDto Settings { get; set; } = new();
    public List<StyleJsonDto> Styles { get; set; } = [];
    public List<CueJsonDto> Cues { get; set; } = [];

    public static ProjectJsonDto FromModel(SubtitleProject project, int schemaVersion) => new()
    {
        SchemaVersion = schemaVersion,
        VideoPath = project.VideoPath,
        Video = project.Video is null ? null : VideoJsonDto.FromModel(project.Video),
        Settings = ProjectSettingsJsonDto.FromModel(project.Settings),
        Styles = project.Styles.Select(StyleJsonDto.FromModel).ToList(),
        Cues = project.Cues.Select(CueJsonDto.FromModel).ToList(),
    };

    public SubtitleProject ToModel()
    {
        // JSON 의 명시적 null 은 속성 초기값을 덮는다. 그대로 역참조하면 어느 필드가 문제인지
        // 알 수 없는 NullReferenceException 이 사용자에게 그대로 나간다. 필드 이름을 담아
        // 실패한다.
        RequireField(Settings, "settings");
        RequireField(Styles, "styles");
        RequireField(Cues, "cues");

        SubtitleProject project = new()
        {
            VideoPath = VideoPath,
            Video = Video?.ToModel(),
            Settings = Settings.ToModel(),
        };
        foreach (StyleJsonDto style in Styles)
        {
            RequireField(style, "styles[]");
            if (style.Id == Guid.Empty)
            {
                style.ApplyTo(project.Styles.Default);
            }
            else
            {
                StylePreset preset = new(style.Id);
                style.ApplyTo(preset);
                project.Styles.Add(preset);
            }
        }
        foreach (CueJsonDto cue in Cues)
        {
            RequireField(cue, "cues[]");
        }

        project.Cues.AddRange(Cues.Select(cue => cue.ToModel()));
        return project;
    }

    private static void RequireField(object? value, string name)
    {
        if (value is null)
        {
            throw new InvalidDataException($"프로젝트 파일의 '{name}' 항목이 비어 있습니다.");
        }
    }
}

internal sealed class VideoJsonDto
{
    public int Width { get; set; }
    public int Height { get; set; }
    public TimeSpan Duration { get; set; }
    public double NominalFps { get; set; }
    public static VideoJsonDto FromModel(VideoInfo value) => new()
    {
        Width = value.Width, Height = value.Height, Duration = value.Duration, NominalFps = value.NominalFps,
    };
    public VideoInfo ToModel() => new(Width, Height, Duration, NominalFps);
}

internal sealed class ProjectSettingsJsonDto
{
    public ColorJsonDto PreviewBackground { get; set; } = ColorJsonDto.FromModel(new RgbaColor(32, 32, 32, 255));
    public bool UseCheckerboard { get; set; }
    public static ProjectSettingsJsonDto FromModel(ProjectSettings value) => new()
    {
        PreviewBackground = ColorJsonDto.FromModel(value.PreviewBackground),
        UseCheckerboard = value.UseCheckerboard,
    };
    public ProjectSettings ToModel() => new()
    {
        PreviewBackground = PreviewBackground.ToModel(),
        UseCheckerboard = UseCheckerboard,
    };
}

internal sealed class StyleJsonDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "Default";
    public SectionFormatJsonDto BaseFormat { get; set; } = new();
    public AnchorPoint DefaultAnchor { get; set; } = AnchorPoint.BottomCenter;
    public Justification DefaultJustify { get; set; } = Justification.Center;
    public List<EdgeType> ExtraEdges { get; set; } = [];
    public static StyleJsonDto FromModel(StylePreset value) => new()
    {
        Id = value.Id,
        Name = value.Name,
        BaseFormat = SectionFormatJsonDto.FromModel(value.BaseFormat),
        DefaultAnchor = value.DefaultAnchor,
        DefaultJustify = value.DefaultJustify,
        ExtraEdges = value.ExtraEdges.ToList(),
    };
    public void ApplyTo(StylePreset value)
    {
        value.Name = Name;
        value.BaseFormat = BaseFormat.ToModel();
        value.DefaultAnchor = DefaultAnchor;
        value.DefaultJustify = DefaultJustify;
        value.ExtraEdges = ExtraEdges.ToArray();
    }
}

internal sealed class CueJsonDto
{
    public Guid Id { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public int Track { get; set; }
    public int ZOrder { get; set; }
    public AnchorPoint Anchor { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public Justification Justify { get; set; }
    public TextDirection Direction { get; set; }
    public Guid? StyleId { get; set; }
    public List<SectionJsonDto> Sections { get; set; } = [];
    public List<EffectJsonDto> Effects { get; set; } = [];

    public static CueJsonDto FromModel(Cue value) => new()
    {
        Id = value.Id, Start = value.Start, End = value.End, Track = value.Track, ZOrder = value.ZOrder,
        Anchor = value.Anchor, PositionX = value.PositionX, PositionY = value.PositionY,
        Justify = value.Justify, Direction = value.Direction, StyleId = value.StyleId,
        Sections = value.Sections.Select(SectionJsonDto.FromModel).ToList(),
        Effects = value.Effects.Select(EffectJsonDto.FromModel).ToList(),
    };

    public Cue ToModel()
    {
        if (End <= Start)
        {
            throw new InvalidDataException("A project cue must end after it starts.");
        }
        Cue cue = new(Id == Guid.Empty ? Guid.NewGuid() : Id)
        {
            Start = Start, End = End, Track = Math.Max(0, Track), ZOrder = ZOrder, Anchor = Anchor,
            PositionX = PositionX, PositionY = PositionY, Justify = Justify, Direction = Direction,
            StyleId = StyleId,
        };
        foreach (SectionJsonDto section in Sections)
        {
            cue.AddSection(section.ToModel());
        }
        foreach (EffectJsonDto effect in Effects)
        {
            cue.AddEffect(effect.ToModel());
        }
        return cue;
    }
}

internal sealed class SectionJsonDto
{
    public string Text { get; set; } = string.Empty;
    public TimeSpan? KaraokeOffset { get; set; }
    public SectionOverridesJsonDto Overrides { get; set; } = new();
    public RubyRole Ruby { get; set; }
    public string? RubyText { get; set; }
    public Guid? StyleIdOverride { get; set; }
    public static SectionJsonDto FromModel(Section value) => new()
    {
        Text = value.Text, KaraokeOffset = value.KaraokeOffset,
        Overrides = SectionOverridesJsonDto.FromModel(value.Overrides), Ruby = value.Ruby,
        RubyText = value.RubyText, StyleIdOverride = value.StyleIdOverride,
    };
    public Section ToModel() => new()
    {
        Text = Text, KaraokeOffset = KaraokeOffset, Overrides = Overrides.ToModel(), Ruby = Ruby,
        RubyText = RubyText, StyleIdOverride = StyleIdOverride,
    };
}

internal sealed class SectionFormatJsonDto
{
    public YtFont Font { get; set; }
    public int SizePercent { get; set; } = 100;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public ScriptOffset Offset { get; set; } = ScriptOffset.Regular;
    public ColorJsonDto Foreground { get; set; } = ColorJsonDto.FromModel(RgbaColor.White);
    public ColorJsonDto Background { get; set; } = ColorJsonDto.FromModel(RgbaColor.Transparent);
    public ColorJsonDto SecondaryColor { get; set; } = ColorJsonDto.FromModel(RgbaColor.SecondaryDefault);
    public EdgeType Edge { get; set; } = EdgeType.Glow;
    public ColorJsonDto EdgeColor { get; set; } = ColorJsonDto.FromModel(RgbaColor.EdgeDefault);
    public bool Pack { get; set; }
    public static SectionFormatJsonDto FromModel(SectionFormat value) => new()
    {
        Font = value.Font, SizePercent = value.SizePercent, Bold = value.Bold, Italic = value.Italic,
        Underline = value.Underline, Offset = value.Offset, Foreground = ColorJsonDto.FromModel(value.Foreground),
        Background = ColorJsonDto.FromModel(value.Background),
        SecondaryColor = ColorJsonDto.FromModel(value.SecondaryColor), Edge = value.Edge,
        EdgeColor = ColorJsonDto.FromModel(value.EdgeColor), Pack = value.Pack,
    };
    public SectionFormat ToModel() => new()
    {
        Font = Font, SizePercent = SizePercent, Bold = Bold, Italic = Italic, Underline = Underline,
        Offset = Offset, Foreground = Foreground.ToModel(), Background = Background.ToModel(),
        SecondaryColor = SecondaryColor.ToModel(), Edge = Edge, EdgeColor = EdgeColor.ToModel(), Pack = Pack,
    };
}

internal sealed class SectionOverridesJsonDto
{
    public YtFont? Font { get; set; }
    public int? SizePercent { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Underline { get; set; }
    public ScriptOffset? Offset { get; set; }
    public ColorJsonDto? Foreground { get; set; }
    public ColorJsonDto? Background { get; set; }
    public ColorJsonDto? SecondaryColor { get; set; }
    public EdgeType? Edge { get; set; }
    public ColorJsonDto? EdgeColor { get; set; }
    public bool? Pack { get; set; }
    public static SectionOverridesJsonDto FromModel(SectionOverrides value) => new()
    {
        Font = value.Font, SizePercent = value.SizePercent, Bold = value.Bold, Italic = value.Italic,
        Underline = value.Underline, Offset = value.Offset,
        Foreground = value.Foreground is RgbaColor fg ? ColorJsonDto.FromModel(fg) : null,
        Background = value.Background is RgbaColor bg ? ColorJsonDto.FromModel(bg) : null,
        SecondaryColor = value.SecondaryColor is RgbaColor sc ? ColorJsonDto.FromModel(sc) : null,
        Edge = value.Edge, EdgeColor = value.EdgeColor is RgbaColor ec ? ColorJsonDto.FromModel(ec) : null,
        Pack = value.Pack,
    };
    public SectionOverrides ToModel() => new()
    {
        Font = Font, SizePercent = SizePercent, Bold = Bold, Italic = Italic, Underline = Underline,
        Offset = Offset, Foreground = Foreground?.ToModel(), Background = Background?.ToModel(),
        SecondaryColor = SecondaryColor?.ToModel(), Edge = Edge, EdgeColor = EdgeColor?.ToModel(), Pack = Pack,
    };
}

internal sealed class ColorJsonDto
{
    public byte Red { get; set; }
    public byte Green { get; set; }
    public byte Blue { get; set; }
    public byte Alpha { get; set; }
    public static ColorJsonDto FromModel(RgbaColor value) => new()
    {
        Red = value.Red, Green = value.Green, Blue = value.Blue, Alpha = value.Alpha,
    };
    public RgbaColor ToModel() => new(Red, Green, Blue, Alpha);
}

internal sealed class EffectJsonDto
{
    public string Kind { get; set; } = string.Empty;
    public double? FromX { get; set; }
    public double? FromY { get; set; }
    public double? ToX { get; set; }
    public double? ToY { get; set; }
    public double? RadiusX { get; set; }
    public double? RadiusY { get; set; }
    public double? OffsetX { get; set; }
    public double? OffsetY { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public TimeSpan? FadeIn { get; set; }
    public TimeSpan? FadeOut { get; set; }
    public int? Alpha1 { get; set; }
    public int? Alpha2 { get; set; }
    public int? Alpha3 { get; set; }
    public TimeSpan? T1 { get; set; }
    public TimeSpan? T2 { get; set; }
    public TimeSpan? T3 { get; set; }
    public TimeSpan? T4 { get; set; }
    public TimeSpan? InTime { get; set; }
    public TimeSpan? OutTime { get; set; }
    public List<ColorJsonDto>? CustomColors { get; set; }
    public TimeSpan? Start { get; set; }
    public TimeSpan? End { get; set; }
    public double? Accel { get; set; }
    public ColorJsonDto? ToForeground { get; set; }
    public ColorJsonDto? ToEdgeColor { get; set; }
    public int? ToSizePercent { get; set; }
    public KaraokeType? KaraokeType { get; set; }
    public string? CursorText { get; set; }
    public TimeSpan? CursorInterval { get; set; }

    public static EffectJsonDto FromModel(CueEffect effect) => effect switch
    {
        MoveEffect value => new() { Kind = "move", FromX = value.FromX, FromY = value.FromY, ToX = value.ToX, ToY = value.ToY, StartTime = value.StartTime, EndTime = value.EndTime },
        FadeEffect value => new() { Kind = "fade", FadeIn = value.FadeIn, FadeOut = value.FadeOut, Alpha1 = value.Alpha1, Alpha2 = value.Alpha2, Alpha3 = value.Alpha3, T1 = value.T1, T2 = value.T2, T3 = value.T3, T4 = value.T4 },
        ShakeEffect value => new() { Kind = "shake", RadiusX = value.RadiusX, RadiusY = value.RadiusY, StartTime = value.StartTime, EndTime = value.EndTime },
        ChromaEffect value => new() { Kind = "chroma", OffsetX = value.OffsetX, OffsetY = value.OffsetY, InTime = value.InTime, OutTime = value.OutTime, CustomColors = value.CustomColors?.Select(ColorJsonDto.FromModel).ToList() },
        AnimateEffect value => new() { Kind = "animate", Start = value.Start, End = value.End, Accel = value.Accel, ToForeground = value.ToForeground is RgbaColor fg ? ColorJsonDto.FromModel(fg) : null, ToEdgeColor = value.ToEdgeColor is RgbaColor ec ? ColorJsonDto.FromModel(ec) : null, ToSizePercent = value.ToSizePercent },
        KaraokeSettings value => new() { Kind = "karaoke", KaraokeType = value.Type, CursorText = value.CursorText, CursorInterval = value.CursorInterval },
        _ => throw new NotSupportedException($"Unsupported cue effect type {effect.GetType().Name}."),
    };

    public CueEffect ToModel() => Kind switch
    {
        "move" => new MoveEffect(FromX ?? 0, FromY ?? 0, ToX ?? 0, ToY ?? 0, StartTime, EndTime),
        "fade" => new FadeEffect(FadeIn ?? TimeSpan.Zero, FadeOut ?? TimeSpan.Zero) { Alpha1 = Alpha1, Alpha2 = Alpha2, Alpha3 = Alpha3, T1 = T1, T2 = T2, T3 = T3, T4 = T4 },
        "shake" => new ShakeEffect(RadiusX ?? 0, RadiusY ?? 0, StartTime, EndTime),
        "chroma" => new ChromaEffect(OffsetX ?? 0, OffsetY ?? 0, InTime ?? TimeSpan.Zero, OutTime ?? TimeSpan.Zero, CustomColors?.Select(color => color.ToModel()).ToArray()),
        "animate" => new AnimateEffect(Start ?? TimeSpan.Zero, End ?? TimeSpan.Zero, Accel ?? 1) { ToForeground = ToForeground?.ToModel(), ToEdgeColor = ToEdgeColor?.ToModel(), ToSizePercent = ToSizePercent },
        "karaoke" => new KaraokeSettings(KaraokeType ?? YttStudio.Core.KaraokeType.Simple) { CursorText = CursorText, CursorInterval = CursorInterval },
        _ => throw new InvalidDataException($"Unknown cue effect kind '{Kind}'."),
    };
}
