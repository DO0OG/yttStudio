using System.IO.Compression;
using System.Text;
using YttStudio.Core.Project;

namespace YttStudio.Core.Tests;

public sealed class ProjectPackageTests
{
    [Fact]
    public void RoundTripPreservesVideoPathCueAndThumbnail()
    {
        SubtitleProject project = CreateProject();
        project.VideoPath = @"D:\media\song.mp4";
        project.Video = new VideoInfo(1920, 1080, TimeSpan.FromMinutes(3), 29.97);
        byte[] thumbnail = [137, 80, 78, 71];

        using MemoryStream stream = new();
        ProjectPackage.Save(project, stream, thumbnail);
        ProjectPackageReadResult result = ProjectPackage.Read(stream);

        Assert.Equal(project.VideoPath, result.Project.VideoPath);
        Assert.Equal(1920, result.Project.Video!.Width);
        Assert.Equal("text", Assert.Single(result.Project.Cues).Sections[0].Text);
        Assert.Equal(thumbnail, result.ThumbnailPng);
    }

    [Fact]
    public void RoundTripPreservesStylesRubyAndOverrides()
    {
        SubtitleProject project = CreateProject();
        StylePreset style = new(Guid.NewGuid()) { Name = "Title", DefaultAnchor = AnchorPoint.TopCenter };
        style.BaseFormat.Bold = true;
        project.Styles.Add(style);
        Cue cue = Assert.Single(project.Cues);
        cue.StyleId = style.Id;
        cue.Sections[0].Ruby = RubyRole.Above;
        cue.Sections[0].RubyText = "ruby";
        cue.Sections[0].Overrides.Pack = true;

        SubtitleProject loaded = RoundTrip(project);
        Cue loadedCue = Assert.Single(loaded.Cues);

        Assert.Equal("Title", loaded.Styles[style.Id]!.Name);
        Assert.Equal(RubyRole.Above, loadedCue.Sections[0].Ruby);
        Assert.Equal("ruby", loadedCue.Sections[0].RubyText);
        Assert.True(loadedCue.Sections[0].Overrides.Pack);
    }

    [Fact]
    public void RoundTripPreservesAllEffectKinds()
    {
        SubtitleProject project = CreateProject();
        Cue cue = Assert.Single(project.Cues);
        cue.AddEffect(new MoveEffect(1, 2, 3, 4));
        cue.AddEffect(new FadeEffect(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20)));
        cue.AddEffect(new ShakeEffect(3, 4));
        cue.AddEffect(new ChromaEffect());
        cue.AddEffect(new AnimateEffect(TimeSpan.Zero, TimeSpan.FromSeconds(1)) { ToSizePercent = 120 });
        cue.AddEffect(new KaraokeSettings(KaraokeType.Cursor) { CursorText = "_", CursorInterval = TimeSpan.FromMilliseconds(50) });

        Cue loaded = Assert.Single(RoundTrip(project).Cues);

        Assert.Collection(loaded.Effects,
            effect => Assert.IsType<MoveEffect>(effect),
            effect => Assert.IsType<FadeEffect>(effect),
            effect => Assert.IsType<ShakeEffect>(effect),
            effect => Assert.IsType<ChromaEffect>(effect),
            effect => Assert.IsType<AnimateEffect>(effect),
            effect => Assert.IsType<KaraokeSettings>(effect));
    }

    [Fact]
    public void MigratesVersionZeroMediaPath()
    {
        using MemoryStream package = CreateLegacyPackage(0,
            "{\"mediaPath\":\"old.mp4\",\"video\":null,\"styles\":[],\"cues\":[]}");

        ProjectPackageReadResult result = ProjectPackage.Read(package);

        Assert.True(result.WasMigrated);
        Assert.Equal("old.mp4", result.Project.VideoPath);
        Assert.Equal(0, result.SourceSchemaVersion);
    }

    [Fact]
    public void MigratesVersionOneDefaults()
    {
        using MemoryStream package = CreateLegacyPackage(1,
            "{\"schemaVersion\":1,\"videoPath\":null,\"video\":null,\"styles\":[],\"cues\":[]}");

        ProjectPackageReadResult result = ProjectPackage.Read(package);

        Assert.True(result.WasMigrated);
        Assert.False(result.Project.Settings.UseCheckerboard);
        Assert.Equal(1, result.SourceSchemaVersion);
    }

    private static SubtitleProject CreateProject()
    {
        SubtitleProject project = new();
        Cue cue = new(Guid.NewGuid()) { Start = TimeSpan.FromSeconds(1), End = TimeSpan.FromSeconds(2) };
        cue.AddSection(new Section { Text = "text" });
        project.Cues.Add(cue);
        return project;
    }

    private static SubtitleProject RoundTrip(SubtitleProject project)
    {
        using MemoryStream stream = new();
        ProjectPackage.Save(project, stream);
        return ProjectPackage.Load(stream);
    }

    private static MemoryStream CreateLegacyPackage(int version, string projectJson)
    {
        MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "manifest.json", $"{{\"schemaVersion\":{version}}}");
            Write(archive, "project.json", projectJson);
            Write(archive, "thumbnail.png", string.Empty);
        }
        stream.Position = 0;
        return stream;
    }

    private static void Write(ZipArchive archive, string name, string value)
    {
        using Stream stream = archive.CreateEntry(name).Open();
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes);
    }
}
