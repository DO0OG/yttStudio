using System.Text;
using YttStudio.Core.Format;

namespace YttStudio.Core.Tests;

public sealed class FormatRoundTripTests
{
    [Theory]
    [InlineData("BoldItalicUnderline.ytt")]
    [InlineData("Offset.ytt")]
    public void YttRoundTripPreservesRepresentativeCueContent(string fixtureName)
    {
        SubtitleFileService service = new();
        string source = Path.Combine(FindRepositoryRoot(), "external", "YTSubConverter",
            "YTSubConverter.Tests", "Ass", "Files", fixtureName);
        string output = Path.Combine(Path.GetTempPath(), $"yttstudio-{Guid.NewGuid():N}.ytt");
        try
        {
            ImportResult first = service.Import(source);
            service.Export(first.Project, output);
            ImportResult second = service.Import(output);

            Assert.Equal(
                first.Project.Cues.Select(CueText).Where(text => text.Length > 0),
                second.Project.Cues.Select(CueText).Where(text => text.Length > 0));
            Assert.All(second.Project.Cues, cue => Assert.True(cue.End > cue.Start));
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void YttRoundTripPreservesIndependentAnchorAndJustification()
    {
        SubtitleProject project = new()
        {
            Video = new VideoInfo(1280, 720, TimeSpan.Zero, 0),
        };
        Cue cue = new(Guid.NewGuid())
        {
            Start = TimeSpan.FromSeconds(1),
            End = TimeSpan.FromSeconds(2),
            Anchor = AnchorPoint.BottomCenter,
            Justify = Justification.Left,
        };
        cue.AddSection(new Section { Text = "independent alignment" });
        project.Cues.Add(cue);

        string output = Path.Combine(Path.GetTempPath(), $"yttstudio-{Guid.NewGuid():N}.ytt");
        try
        {
            SubtitleFileService service = new();
            service.Export(project, output);

            Cue importedCue = Assert.Single(service.Import(output).Project.Cues);
            Assert.Equal(AnchorPoint.BottomCenter, importedCue.Anchor);
            Assert.Equal(Justification.Left, importedCue.Justify);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void AssRoundTripPreservesSupportedTextAndTiming()
    {
        SubtitleFileService service = new();
        string source = Path.Combine(FindRepositoryRoot(), "external", "YTSubConverter",
            "YTSubConverter.Tests", "Ass", "Files", "BoldItalicUnderline.ass");
        string output = Path.Combine(Path.GetTempPath(), $"yttstudio-{Guid.NewGuid():N}.ass");
        try
        {
            ImportResult first = service.Import(source);
            service.Export(first.Project, output);
            ImportResult second = service.Import(output);

            Assert.Equal(first.Project.Cues.Select(CueText), second.Project.Cues.Select(CueText));
            Assert.Equal(first.Project.Cues.Select(cue => cue.Start), second.Project.Cues.Select(cue => cue.Start));
            Assert.Equal(first.Project.Cues.Select(cue => cue.End), second.Project.Cues.Select(cue => cue.End));
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void AssImportReportsUnsupportedTagAndLineNumber()
    {
        string path = Path.Combine(Path.GetTempPath(), $"yttstudio-{Guid.NewGuid():N}.ass");
        string content = """
            [Script Info]
            PlayResX: 1280
            PlayResY: 720

            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Default,Arial,40,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,{\frz15}text
            """;
        File.WriteAllText(path, content, new UTF8Encoding(false));

        try
        {
            ImportResult result = new SubtitleFileService().Import(path);
            ImportWarning warning = Assert.Single(result.Warnings);

            Assert.Equal("\\frz", warning.TagName);
            Assert.Equal(11, warning.LineNumber);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CueText(Cue cue) => string.Concat(cue.Sections.Select(section => section.Text));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YttStudio.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
