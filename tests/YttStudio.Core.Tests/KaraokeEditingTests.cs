using YttStudio.Core.Editing;

namespace YttStudio.Core.Tests;

public sealed class KaraokeEditingTests
{
    [Fact]
    public void EqualAdjacentOffsetIsAdvancedByOneMillisecond()
    {
        (DocumentEditor editor, Cue cue) = CreateEditor();

        KaraokeEditResult result = editor.SetKaraokeOffset(cue.Id, 1, TimeSpan.FromMilliseconds(100));

        Assert.True(result.AutoCorrectedOffsets);
        Assert.Equal(TimeSpan.FromMilliseconds(101), cue.Sections[1].KaraokeOffset);
        Assert.Single(result.OffsetCorrections);
    }

    [Fact]
    public void UndoManualOffsetRestoresTabHistory()
    {
        (DocumentEditor editor, Cue cue) = CreateEditor();

        editor.RecordKaraokeTab(cue.Id, TimeSpan.FromMilliseconds(200));
        editor.SetKaraokeOffset(cue.Id, 1, TimeSpan.FromMilliseconds(300));
        editor.Undo();
        editor.CancelLastKaraokeTab(cue.Id);

        Assert.Equal(TimeSpan.FromMilliseconds(100), cue.Sections[0].KaraokeOffset);
        Assert.Null(cue.Sections[1].KaraokeOffset);
    }

    private static (DocumentEditor Editor, Cue Cue) CreateEditor()
    {
        SubtitleProject project = new();
        DocumentEditor editor = new(project);
        Cue cue = editor.AddCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "가나");
        editor.SplitCueIntoKaraokeSections(cue.Id);
        editor.SetKaraokeOffset(cue.Id, 0, TimeSpan.FromMilliseconds(100));
        return (editor, cue);
    }
}
