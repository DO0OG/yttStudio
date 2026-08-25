using YttStudio.Core.Editing;

namespace YttStudio.Core.Tests;

public sealed class StyleDeletionTests
{
    [Fact]
    public void DeleteStyleFreezesResolvedCueAppearance()
    {
        SubtitleProject project = new();
        DocumentEditor editor = new(project);
        StylePreset style = editor.AddStyle("강조");
        style.BaseFormat.SizePercent = 150;
        style.BaseFormat.Bold = true;
        Cue cue = editor.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text");
        editor.ApplyStyle([cue.Id], style.Id);
        ResolvedFormat before = Resolve(project, cue);

        editor.DeleteStyle(style.Id);

        Assert.Null(cue.StyleId);
        Assert.Null(project.Styles[style.Id]);
        Assert.Equal(before, Resolve(project, cue));
    }

    [Fact]
    public void UndoStyleDeleteRestoresReferencesAndOverrides()
    {
        SubtitleProject project = new();
        DocumentEditor editor = new(project);
        StylePreset style = editor.AddStyle("강조");
        Cue cue = editor.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text");
        cue.Sections[0].StyleIdOverride = style.Id;
        cue.Sections[0].Overrides.SizePercent = 175;
        SectionOverrides before = cue.Sections[0].Overrides.Clone();

        editor.DeleteStyle(style.Id);
        editor.Undo();

        Assert.Same(style, project.Styles[style.Id]);
        Assert.Equal(style.Id, cue.Sections[0].StyleIdOverride);
        Assert.Equal(before.SizePercent, cue.Sections[0].Overrides.SizePercent);
    }

    private static ResolvedFormat Resolve(SubtitleProject project, Cue cue)
    {
        Section section = cue.Sections[0];
        return FormatResolver.Resolve(project.GetStyle(section.StyleIdOverride ?? cue.StyleId).BaseFormat,
            section.Overrides);
    }
}
