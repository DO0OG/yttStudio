using YttStudio.Core.Editing;

namespace YttStudio.Core.Tests;

public sealed class SearchAndShiftTests
{
    [Fact]
    public void RegexSearchReturnsOrderedSectionMatches()
    {
        (SubtitleProject project, DocumentEditor editor) = CreateEditor();
        Cue later = AddCue(editor, 2000, "hello 42");
        Cue earlier = AddCue(editor, 1000, "HELLO 7");

        IReadOnlyList<TextSearchResult> results = TextSearch.Search(
            project,
            @"hello\s+\d+",
            new TextSearchOptions { UseRegex = true });

        Assert.Equal([earlier.Id, later.Id], results.Select(result => result.CueId));
        Assert.All(results, result => Assert.Single(result.Matches));
    }

    [Fact]
    public void RegexReplacePreservesMetadataAndUsesOneUndoStep()
    {
        (_, DocumentEditor editor) = CreateEditor();
        Cue cue = AddCue(editor, 1000, "cat cat");
        cue.Sections[0].Ruby = RubyRole.Base;
        cue.Sections[0].RubyText = "ruby";
        cue.Sections[0].KaraokeOffset = TimeSpan.FromMilliseconds(100);

        int count = editor.ReplaceText(
            "(cat)",
            "<$1>",
            new TextSearchOptions { UseRegex = true, CaseSensitive = true });
        editor.Undo();

        Assert.Equal(2, count);
        Assert.Equal("cat cat", cue.Sections[0].Text);
        Assert.Equal(RubyRole.Base, cue.Sections[0].Ruby);
        Assert.Equal("ruby", cue.Sections[0].RubyText);
        Assert.Equal(TimeSpan.FromMilliseconds(100), cue.Sections[0].KaraokeOffset);
    }

    [Fact]
    public void InvalidRegexDoesNotMutateOrCreateUndoEntry()
    {
        (_, DocumentEditor editor) = CreateEditor();
        Cue cue = AddCue(editor, 1000, "unchanged");

        Assert.ThrowsAny<ArgumentException>(() => editor.ReplaceText(
            "[",
            "x",
            new TextSearchOptions { UseRegex = true }));
        Assert.Equal("unchanged", cue.Sections[0].Text);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void ShiftPreservesDurationTrackOrderAndUsesOneUndoStep()
    {
        (SubtitleProject project, DocumentEditor editor) = CreateEditor();
        Cue first = AddCue(editor, 1000, "first", track: 2);
        Cue second = AddCue(editor, 2000, "second", track: 3);

        editor.ShiftCueTimes([first.Id, second.Id], TimeSpan.FromMilliseconds(500));

        Assert.Equal([first.Id, second.Id], project.Cues.Select(cue => cue.Id));
        Assert.Equal(TimeSpan.FromSeconds(1), first.End - first.Start);
        Assert.Equal(2, first.Track);
        editor.Undo();
        Assert.Equal(TimeSpan.FromMilliseconds(1000), first.Start);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), second.Start);
    }

    [Fact]
    public void NegativeShiftUsesCommonDeltaAtMinimumBoundary()
    {
        (_, DocumentEditor editor) = CreateEditor();
        Cue first = AddCue(editor, 100, "first");
        Cue second = AddCue(editor, 300, "second");

        TimeSpan effective = editor.ShiftCueTimes(
            [first.Id, second.Id],
            TimeSpan.FromSeconds(-1));

        Assert.Equal(TimeSpan.FromMilliseconds(-99), effective);
        Assert.Equal(TimeSpan.FromMilliseconds(1), first.Start);
        Assert.Equal(TimeSpan.FromMilliseconds(201), second.Start);
    }

    private static (SubtitleProject Project, DocumentEditor Editor) CreateEditor()
    {
        SubtitleProject project = new();
        return (project, new DocumentEditor(project));
    }

    private static Cue AddCue(DocumentEditor editor, double startMilliseconds, string text, int track = 0)
    {
        using (editor.BeginUndoFreeMutation())
        {
            Cue cue = editor.AddCue(
                TimeSpan.FromMilliseconds(startMilliseconds),
                TimeSpan.FromMilliseconds(startMilliseconds + 1000),
                text);
            cue.Track = track;
            return cue;
        }
    }
}
