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

/// <summary>수명 주기 정리와 변경 알림 기반 기능을 담당한다.</summary>
public sealed partial class MainWindowViewModel
{

    private void OnLanguageChanged()
    {
        // 인덱서 바인딩은 인덱서 자체가 무효화될 때만 갱신된다.
        OnPropertyChanged("Item[]");
        OnPropertyChanged(nameof(Loc));
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(PlayPauseActionText));
        OnPropertyChanged(nameof(SelectedViewportModeDisplayName));
        OnPropertyChanged(nameof(ViewportModeDisplayName));
        OnPropertyChanged(string.Empty);
    }

    public Cue? GetCue(Guid id) => project?.Cues[id];

    public void UpdateCueText(Guid id, string text)
    {
        Cue? cue = project?.Cues[id];
        if (editor is null || cue is null || cue.Sections.Count == 0)
        {
            return;
        }

        editor.SetText(id, 0, text);
        AfterMutation(refreshRows: false);
    }

    public void UpdateCueTiming(Guid id, double startMilliseconds, double endMilliseconds, int track)
    {
        if (editor is null)
        {
            return;
        }

        editor.SetTiming(id, TimeSpan.FromMilliseconds(Math.Max(0, startMilliseconds)),
            TimeSpan.FromMilliseconds(Math.Max(startMilliseconds + 1, endMilliseconds)), track);
        AfterMutation(refreshRows: false);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CancelUpdateOperations();
        Loc.LanguageChanged -= OnLanguageChanged;
        settingsWindow?.Close();
        settingsWindow = null;
        if (autosave is not null)
        {
            autosave.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        // 정상 종료는 스냅샷을 남기지 않아야 한다. 남기면 다음 실행에서
        // 필요하지도 않은 복구를 제안하게 된다.
        AutosaveService.ClearSnapshots();

        DisposeVideoSource();

        VideoFrameImage?.Dispose();
        SubtitleImage?.Dispose();
        renderer.Dispose();
    }

    private void SetImage(ref Bitmap? field, Bitmap? value, [CallerMemberName] string? propertyName = null)
    {
        if (ReferenceEquals(field, value))
        {
            return;
        }

        Bitmap? previous = field;
        field = value;
        OnPropertyChanged(propertyName);
        previous?.Dispose();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
