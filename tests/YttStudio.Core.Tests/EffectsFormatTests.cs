using YttStudio.Core.Format;

namespace YttStudio.Core.Tests;

public sealed class EffectsFormatTests
{
    public static IEnumerable<object[]> EffectCases()
    {
        yield return [new MoveEffect(10, 20, 30, 40), typeof(MoveEffect), "\\move"];
        yield return [new FadeEffect(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200)), typeof(FadeEffect), "\\fad"];
        yield return [new ShakeEffect(5, 8), typeof(ShakeEffect), "\\ytshake"];
        yield return [new ChromaEffect(), typeof(ChromaEffect), "\\ytchroma"];
        yield return [new AnimateEffect(TimeSpan.Zero, TimeSpan.FromSeconds(1)) { ToSizePercent = 150 }, typeof(AnimateEffect), "\\t"];
    }

    [Theory]
    [MemberData(nameof(EffectCases))]
    public void AssRoundTripPreservesEffectTag(CueEffect effect, Type expectedType, string tag)
    {
        SubtitleProject project = new() { Video = new VideoInfo(1280, 720, TimeSpan.FromSeconds(2), 30) };
        Cue cue = new(Guid.NewGuid()) { Start = TimeSpan.FromSeconds(1), End = TimeSpan.FromSeconds(2) };
        cue.AddSection(new Section { Text = "effect" });
        cue.AddEffect(effect);
        project.Cues.Add(cue);
        string path = Path.Combine(Path.GetTempPath(), $"yttstudio-{Guid.NewGuid():N}.ass");
        try
        {
            SubtitleFileService service = new();
            service.Export(project, path);
            Assert.Contains(tag, File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
            Cue imported = Assert.Single(service.Import(path).Project.Cues);
            Assert.IsType(expectedType, Assert.Single(imported.Effects));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
