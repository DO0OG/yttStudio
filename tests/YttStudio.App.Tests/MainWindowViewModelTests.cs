using YttStudio.App;
using YttStudio.Core;
using YttStudio.Core.Editing;

namespace YttStudio.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void ReconcileSelectionDropsCueRemovedByUndo()
    {
        SubtitleProject project = new();
        DocumentEditor editor = new(project);
        Cue cue = editor.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(2), "text");
        HashSet<Guid> selectedCueIds = [cue.Id];

        editor.RemoveCue(cue.Id);
        Guid? lastSelectedCueId = MainWindowViewModel.ReconcileCueSelection(
            project, selectedCueIds, cue.Id);

        Assert.Empty(selectedCueIds);
        Assert.Null(lastSelectedCueId);
    }
}
