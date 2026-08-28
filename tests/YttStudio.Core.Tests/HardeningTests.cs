using System.Text.Json;
using YttStudio.Core;
using YttStudio.Core.Editing;
using YttStudio.Core.Format;
using YttStudio.Core.Project;

namespace YttStudio.Core.Tests;

/// <summary>검토에서 나온 결함이 되돌아오지 않는지 지킨다.</summary>
public sealed class HardeningTests
{
    [Fact]
    public void ReverseOrderedCuesSurviveRoundTripInStartOrder()
    {
        // 불러오기가 큐를 하나씩 삽입하다가 대량 구성으로 바뀌었다. 순서가 그대로여야 한다.
        SubtitleProject project = new();
        DocumentEditor editor = new(project);
        for (int index = 9; index >= 0; index--)
        {
            editor.AddCue(TimeSpan.FromSeconds(index), TimeSpan.FromSeconds(index + 1), $"cue{index}");
        }

        string[] before = [.. project.Cues.Select(cue => cue.Sections[0].Text)];

        using MemoryStream buffer = new();
        ProjectPackage.Save(project, buffer);
        buffer.Position = 0;
        SubtitleProject loaded = ProjectPackage.Read(buffer).Project;

        Assert.Equal(before, loaded.Cues.Select(cue => cue.Sections[0].Text));
        Assert.Equal(
            loaded.Cues.Select(cue => cue.Start).OrderBy(start => start),
            loaded.Cues.Select(cue => cue.Start));
    }

    [Fact]
    public void ProjectWithExplicitNullSettingsReportsFieldName()
    {
        ProjectJsonDto? dto = JsonSerializer.Deserialize<ProjectJsonDto>(
            """{"schemaVersion":2,"videoPath":null,"video":null,"settings":null,"styles":[],"cues":[]}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => dto!.ToModel());
        Assert.Contains("settings", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OversizedSubtitleFileIsRejectedBeforeParsing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ytt-oversize-{Guid.NewGuid():N}.ass");
        try
        {
            using (FileStream stream = File.Create(path))
            {
                stream.SetLength(SubtitleFileService.MaximumImportBytes + 1);
            }

            SubtitleFileService service = new();
            Assert.Throws<InvalidDataException>(() => service.Import(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FailedSaveLeavesTheExistingProjectIntact()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ytt-atomic-{Guid.NewGuid():N}.yttproj");
        try
        {
            ProjectPackage.Save(new SubtitleProject(), path);
            byte[] original = File.ReadAllBytes(path);
            Assert.NotEmpty(original);

            // 썸네일 한도를 넘겨 저장 도중 실패시킨다. 원본이 그대로 남아야 한다.
            byte[] tooLarge = new byte[ProjectPackage.MaximumThumbnailBytes + 1];
            Assert.ThrowsAny<ArgumentException>(
                () => ProjectPackage.Save(new SubtitleProject(), path, tooLarge));

            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(
                Path.GetDirectoryName(path)!,
                $".{Path.GetFileName(path)}.*.tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
