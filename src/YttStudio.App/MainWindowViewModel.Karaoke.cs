using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using YttStudio.Core;
using YttStudio.Core.Editing;
using YttStudio.Core.Format;
using YttStudio.Core.Project;
using YttStudio.Core.Validation;
using YttStudio.Render;
using YttStudio.Video;
using SubtitleRenderOptions = YttStudio.Render.RenderOptions;

namespace YttStudio.App;

/// <summary>가라오케 구간 편집과 타임라인 조정을 담당한다.</summary>
public sealed partial class MainWindowViewModel
{

    public int KaraokeSectionCount(Guid cueId)
        => project?.Cues[cueId]?.Sections.Count ?? 0;

    public void SplitKaraokeSection(Guid cueId, int sectionIndex, int textOffset)
    {
        if (editor is null)
        {
            return;
        }

        editor.SplitKaraokeSection(cueId, sectionIndex, textOffset);
        AfterMutation(refreshRows: true);
    }

    public void RemoveKaraokeSection(Guid cueId, int sectionIndex)
    {
        int count = KaraokeSectionCount(cueId);
        if (count <= 1)
        {
            return;
        }

        MergeKaraokeSections(cueId, sectionIndex > 0 ? sectionIndex - 1 : 0);
    }

    public void MergeKaraokeSections(Guid cueId, int leftSectionIndex)
    {
        if (editor is null)
        {
            return;
        }

        editor.MergeKaraokeSections(cueId, leftSectionIndex);
        AfterMutation(refreshRows: true);
    }

    public void SetKaraokeOffset(Guid cueId, int sectionIndex, double milliseconds)
        => ApplyKaraokeOffset(cueId, sectionIndex, milliseconds);

    public void SetKaraokeOffsetFromTimeline(Guid cueId, int sectionIndex, double milliseconds)
        => ApplyKaraokeOffset(cueId, sectionIndex, milliseconds);

    public void BeginKaraokeTimelineAdjustment()
    {
        if (editor is null || karaokeTimelineTransaction)
        {
            return;
        }

        editor.BeginTransaction("가라오케 타이밍 미세 조정");
        karaokeTimelineTransaction = true;
    }

    public void PreviewKaraokeTimelineOffset(Guid cueId, int sectionIndex, double milliseconds)
        => ApplyKaraokeOffset(cueId, sectionIndex, milliseconds);

    public void EndKaraokeTimelineAdjustment()
    {
        if (editor is null || !karaokeTimelineTransaction)
        {
            return;
        }

        editor.EndTransaction();
        karaokeTimelineTransaction = false;
        NotifyCommandStates();
    }

    public void RecordKaraokeTabForSelectedCue()
    {
        if (editor is null || SelectedCue is not Cue cue)
        {
            return;
        }

        try
        {
            KaraokeEditResult result = editor.RecordKaraokeTab(
                cue.Id,
                TimeSpan.FromMilliseconds(Math.Max(0, PositionMilliseconds - cue.Start.TotalMilliseconds)));
            LogKaraokeCorrections(result);
            AfterMutation();
        }
        catch (InvalidOperationException exception)
        {
            Status = exception.Message;
        }
    }

    public void CancelLastKaraokeTabForSelectedCue()
    {
        if (editor is null || SelectedKaraokeCueId is not Guid cueId)
        {
            return;
        }

        try
        {
            editor.CancelLastKaraokeTab(cueId);
            AfterMutation();
        }
        catch (InvalidOperationException exception)
        {
            Status = exception.Message;
        }
    }

    private void AutoSplitSelectedKaraokeCue()
    {
        if (editor is null || SelectedKaraokeCueId is not Guid cueId)
        {
            return;
        }

        KaraokeEditResult result = editor.SplitCueIntoKaraokeSections(cueId);
        LogKaraokeCorrections(result);
        AfterMutation(refreshRows: true);
    }

    private void ApplyKaraokeOffset(Guid cueId, int sectionIndex, double milliseconds)
    {
        if (editor is null || !double.IsFinite(milliseconds))
        {
            return;
        }

        KaraokeEditResult result = editor.SetKaraokeOffset(
            cueId,
            sectionIndex,
            TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)));
        LogKaraokeCorrections(result);
        AfterMutation();
    }

    private static void LogKaraokeCorrections(KaraokeEditResult result)
    {
        foreach (KaraokeOffsetCorrection correction in result.OffsetCorrections)
        {
            Serilog.Log.Information(
                "Karaoke offset auto-corrected for cue {CueId}, section {SectionIndex}: {PreviousOffset} -> {CorrectedOffset}",
                result.CueId,
                correction.SectionIndex,
                correction.PreviousOffset,
                correction.CorrectedOffset);
        }
    }
}
