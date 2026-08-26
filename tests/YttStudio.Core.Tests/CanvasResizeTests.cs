using YttStudio.Core.Editing;

namespace YttStudio.Core.Tests;

public sealed class CanvasResizeTests
{
    [Fact]
    public void SizeOverrideCopiesExistingValuesAndUndoGroupsAllSections()
    {
        SubtitleProject project = new();
        DocumentEditor editor = new(project);
        Cue cue = editor.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "one");
        cue.AddSection(new Section { Text = "two" });
        editor.ApplyFormat([cue.Id], new SectionFormatPatch
        {
            Bold = true,
            SizePercent = 125,
        });

        AnchorPoint anchor = cue.Anchor;
        double positionX = cue.PositionX;
        double positionY = cue.PositionY;
        editor.BeginTransaction("resize");
        for (int index = 0; index < cue.Sections.Count; index++)
        {
            Section section = cue.Sections[index];
            editor.SetFormatOverrides(cue.Id, index,
                section.Overrides.WithSizePercent(250));
        }
        editor.EndTransaction();

        Assert.All(cue.Sections, section =>
        {
            Assert.Equal(250, section.Overrides.SizePercent);
            Assert.Equal(true, section.Overrides.Bold);
        });
        Assert.Equal(anchor, cue.Anchor);
        Assert.Equal(positionX, cue.PositionX);
        Assert.Equal(positionY, cue.PositionY);

        editor.Undo();

        Assert.All(cue.Sections, section =>
        {
            Assert.Equal(125, section.Overrides.SizePercent);
            Assert.Equal(true, section.Overrides.Bold);
        });
    }
}
