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

/// <summary>환경설정 창 · 자동 저장 설정과 문서 검증을 담당한다.</summary>
public sealed partial class MainWindowViewModel
{

    private static void RequestShutdown()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private async Task OpenSettingsAsync()
    {
        if (settingsWindow is not null)
        {
            settingsWindow.Activate();
            return;
        }

        Window? owner = GetMainWindow();
        if (owner is null)
        {
            return;
        }

        await ShowSettingsDialogAsync(owner);
    }

    private static Window? GetMainWindow()
        => Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

    private SettingsViewModel CreateSettingsViewModel()
        => new(
            Loc,
            preferences,
            dialogs,
            language => Language = language,
            theme => ThemeMode = theme,
            ApplyMpvPathFromSettingsAsync,
            () => SnapThreshold,
            value => SnapThreshold = value,
            ApplyAutosaveSettings,
            () => VideoStatus,
            mpvAutoInstaller is null ? null : InstallMpvAndApplyAsync,
            value => MaxSubtitleLines = value,
            ApplyCheckForUpdatesSettings);

    private async Task ShowSettingsDialogAsync(Window owner)
    {
        SettingsViewModel settingsViewModel = CreateSettingsViewModel();
        SettingsWindow window = new() { DataContext = settingsViewModel };
        settingsWindow = window;
        settingsViewModel.CloseRequested += window.Close;
        window.Closed += (_, _) =>
        {
            settingsViewModel.Dispose();
            if (ReferenceEquals(settingsWindow, window))
            {
                settingsWindow = null;
            }
        };

        try
        {
            await window.ShowDialog(owner);
        }
        finally
        {
            settingsViewModel.Dispose();
            if (ReferenceEquals(settingsWindow, window))
            {
                settingsWindow = null;
            }
        }
    }

    private async Task ShowAboutAsync()
    {
        string body = string.Join(
            Environment.NewLine + Environment.NewLine,
            Loc["AboutBody"],
            $"{Loc["AboutVersion"]} v{AppVersion.Current}");
        await dialogs.ConfirmAsync(Loc["MenuAbout"], body, Loc["Close"]);
    }

    private Dictionary<Guid, ValidationMetrics> BuildValidationMetrics(
        SubtitleProject currentProject, SKRect subtitleSpace)
    {
        double horizontalInset = subtitleSpace.Width * EditorSafeAreaInsetPercent / 100.0;
        double verticalInset = subtitleSpace.Height * EditorSafeAreaInsetPercent / 100.0;
        Dictionary<Guid, ValidationMetrics> metrics = [];
        foreach (Cue cue in currentProject.Cues)
        {
            CanvasCueItem? item = CanvasItems.FirstOrDefault(candidate => candidate.Id == cue.Id);
            bool outside = item is not null && (
                item.Bounds.Left < subtitleSpace.Left + horizontalInset ||
                item.Bounds.Top < subtitleSpace.Top + verticalInset ||
                item.Bounds.Right > subtitleSpace.Right - horizontalInset ||
                item.Bounds.Bottom > subtitleSpace.Bottom - verticalInset);
            metrics[cue.Id] = new ValidationMetrics
            {
                MobileEffectRisk = cue.Effects.Count >= 3,
                IsOutsideSafeArea = outside,
                ViewportModeDisplayName = SelectedViewportModeDisplayName,
                BoxWidth = item?.Bounds.Width,
                SubtitleSpaceWidth = subtitleSpace.Width,
            };
        }

        return metrics;
    }

    private void RunValidation()
    {
        ValidationIssues.Clear();
        if (project is null)
        {
            return;
        }

        validationHasRun = true;
        SKRect subtitleSpace = previewViewport.SubtitleSpace;
        Dictionary<Guid, ValidationMetrics> metrics = BuildValidationMetrics(project, subtitleSpace);

        byte[]? exportedXml = null;
        string temporaryPath = Path.Combine(Path.GetTempPath(), $"YttStudio-{Guid.NewGuid():N}.ytt");
        try
        {
            fileService.Export(project, temporaryPath);
            exportedXml = File.ReadAllBytes(temporaryPath);
        }
        catch (Exception exception)
        {
            Status = $"크기 근사 계산 실패: {exception.Message}";
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        ValidationContext context = new(project)
        {
            VideoDuration = project.Video?.Duration,
            ExportedXmlBytes = exportedXml,
            CueMetrics = metrics,
        };
        foreach (ValidationIssue issue in new DocumentValidator().Validate(project, context))
        {
            ValidationIssues.Add(issue);
        }
        Status = $"검증 {ValidationIssues.Count}건 · 크기는 실제 JSON3와 다른 근사치이며 업로드 후 확인 필요";
    }

    private void ApplySelectedValidationFix()
    {
        if (editor is null || SelectedValidationIssue is not ValidationIssue issue ||
            !new DocumentValidator().ApplyAutoFix(editor, issue))
        {
            return;
        }
        AfterMutation();
        RunValidation();
    }

    private void GoToSelectedValidationIssue()
    {
        if (SelectedValidationIssue?.CueId is not Guid cueId || project?.Cues[cueId] is null)
        {
            return;
        }
        SelectCue(cueId, toggle: false);
        Cue cue = project.Cues[cueId]!;
        PositionMilliseconds = Math.Clamp(cue.Start.TotalMilliseconds + 1, 0, MaximumMilliseconds);
    }

    private void SavePreferences()
    {
        if (!preferencesStore.TrySave(preferences, out string? error))
        {
            Status = $"{Loc["Settings"]}: {error}";
        }
    }

    private void ApplyAutosaveSettings(bool enabled, int intervalSeconds)
    {
        int normalizedInterval = NormalizeAutosaveIntervalSeconds(intervalSeconds);
        if (preferences.AutosaveEnabled == enabled
            && preferences.AutosaveIntervalSeconds == normalizedInterval)
        {
            return;
        }

        preferences.AutosaveEnabled = enabled;
        preferences.AutosaveIntervalSeconds = normalizedInterval;
        RestartAutosave(enabled, normalizedInterval);
        SavePreferences();
    }

    private void ApplyCheckForUpdatesSettings(bool enabled)
    {
        if (preferences.CheckForUpdatesEnabled == enabled)
        {
            return;
        }

        preferences.CheckForUpdatesEnabled = enabled;
        SavePreferences();
    }

    private void RestartAutosave(bool enabled, int intervalSeconds)
    {
        AutosaveService? previous = autosave;
        autosave = null;
        previous?.DisposeAsync().AsTask().GetAwaiter().GetResult();

        if (!enabled)
        {
            return;
        }

        autosave = new AutosaveService(
            () => project,
            () => unsavedChanges,
            message => Serilog.Log.Warning("{Autosave}", message),
            TimeSpan.FromSeconds(NormalizeAutosaveIntervalSeconds(intervalSeconds)),
            SerializeProjectOnUiThreadAsync);
        autosave.Start();
    }

    /// <summary>UI 스레드에서 프로젝트를 직렬화해 배경이 쓸 바이트를 만든다.</summary>
    /// <remarks>
    /// 편집은 전부 UI 스레드에서 일어난다. 그 스레드에서 직렬화하면 열거 도중 컬렉션이 바뀌는
    /// 일이 없다. 파일에 쓰는 것은 배경이 맡으므로 UI 가 묶이는 시간은 직렬화뿐이다.
    /// </remarks>
    private Task<byte[]?> SerializeProjectOnUiThreadAsync()
        => Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (project is null || disposed)
            {
                return null;
            }

            using MemoryStream buffer = new();
            ProjectPackage.Save(project, buffer);
            return buffer.ToArray();
        }).GetTask();

    private static int NormalizeAutosaveIntervalSeconds(int seconds)
        => seconds is 15 or 30 or 60 or 120 or 300 or 600 ? seconds : 60;

    private static string NormalizeMpvPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Trim('"');
}
