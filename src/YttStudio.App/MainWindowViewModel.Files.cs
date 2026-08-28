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

/// <summary>파일 열기 · 저장과 프로젝트 패키지 입출력을 담당한다.</summary>
public sealed partial class MainWindowViewModel
{

    private async Task OpenSubtitleAsync()
    {
        string? path = await dialogs.OpenSubtitleAsync();
        if (path is null)
        {
            return;
        }

        await OpenPathAsync(path);
    }

    /// <summary>
    /// 명령줄 인자나 파일 연결로 전달된 경로를 연다.
    /// 확장자로 프로젝트 패키지와 자막 파일을 구분한다.
    /// </summary>
    public async Task OpenPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        OpenPathKind kind = OpenPathClassifier.Classify(path);
        if (kind == OpenPathKind.Project)
        {
            await LoadProjectPackageAsync(path, clearSnapshots: true);
            return;
        }

        if (kind == OpenPathKind.Video)
        {
            await LoadVideoAsync(path);
            return;
        }

        if (kind == OpenPathKind.Subtitle)
        {
            ImportSubtitle(path);
        }
    }

    /// <summary>드롭된 자막·영상 파일을 전달된 순서대로 기존 열기 경로로 처리한다.</summary>
    public async Task OpenDroppedPathsAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        foreach (string path in paths)
        {
            if (!OpenPathClassifier.IsDropSupported(path))
            {
                continue;
            }

            await OpenPathAsync(path);
        }
    }

    private void ImportSubtitle(string path)
    {
        try
        {
            ImportResult result = fileService.Import(path);
            project = result.Project;
            editor = new DocumentEditor(project);
            sourcePath = path;
            UpdateMaximum();
            PositionMilliseconds = Math.Min(
                project.Cues.Select(cue => cue.Start.TotalMilliseconds).DefaultIfEmpty(0).Min() + 1,
                MaximumMilliseconds);
            Status = result.Warnings.Count == 0
                ? $"{Path.GetFileName(path)} — 큐 {project.Cues.Count}개"
                : $"{Path.GetFileName(path)} — {string.Join(" · ", result.Warnings.Select(warning => warning.Message))}";
            selectedCueIds.Clear();
            lastSelectedCueId = null;
            RefreshRowsAndStyles();
            AfterMutation(refreshRows: false);
        }
        catch (Exception exception)
        {
            Status = $"열기 실패: {exception.Message}";
        }
    }

    private async Task OpenVideoAsync()
    {
        if (videoSource is null)
        {
            return;
        }

        string? path = await dialogs.OpenVideoAsync();
        if (path is null)
        {
            return;
        }

        await LoadVideoAsync(path);
    }

    /// <summary>공유 소스에 영상을 불러온다. 열기 명령과 프로젝트 재연결이 함께 쓴다.</summary>
    private async Task LoadVideoAsync(string path)
    {
        if (videoSource is null)
        {
            return;
        }

        try
        {
            Status = "영상 메타데이터 읽는 중…";
            await videoSource.LoadAsync(path, CancellationToken.None);
            videoLoaded = true;
            videoSource.SetVolume(volume);
            videoSource.SetMuted(isMuted);
            loadedVideoPath = Path.GetFullPath(path);
            UpdateMaximum();
            VideoStatus = $"{Path.GetFileName(path)} · {videoSource.Info.Width}×{videoSource.Info.Height} · " +
                $"{videoSource.Info.NominalFps:0.###} fps (표시용)";
            Status = "영상 로드 완료";
            NotifyVideoState();
        }
        catch (Exception exception)
        {
            videoLoaded = false;
            Status = $"영상 열기 실패: {exception.Message}";
            RenderFallbackFrame();
            NotifyVideoState();
        }
    }

    /// <summary><c>.yttproj</c> 패키지를 열고 필요하면 사라진 영상을 다시 연결한다.</summary>
    private async Task OpenProjectAsync()
    {
        string? path = await dialogs.OpenProjectAsync();
        if (path is null)
        {
            return;
        }

        await LoadProjectPackageAsync(path, clearSnapshots: true);
    }

    /// <summary>열려 있는 프로젝트를 <c>.yttproj</c> 패키지로 저장한다.</summary>
    private async Task SaveProjectAsync()
    {
        if (project is null)
        {
            return;
        }

        string suggested = Path.GetFileNameWithoutExtension(projectPath ?? sourcePath ?? "project") + ".yttproj";
        string? path = await dialogs.SaveProjectAsync(suggested);
        if (path is null)
        {
            return;
        }

        try
        {
            ProjectPackage.Save(project, path, RenderThumbnailPng());
            projectPath = path;
            unsavedChanges = false;
            // 정상 저장은 크래시 스냅샷을 무효화한다.
            AutosaveService.ClearSnapshots();
            Status = $"{Loc["SaveProject"]}: {path}";
        }
        catch (Exception exception)
        {
            Status = $"{Loc["SaveProject"]} — {exception.Message}";
        }
    }

    private async Task LoadProjectPackageAsync(string path, bool clearSnapshots)
    {
        try
        {
            ProjectPackageReadResult result = ProjectPackage.Read(path);
            project = result.Project;
            // 패키지 로드는 undo 를 만들지 않는 문맥이므로 편집기를 새로 시작한다.
            editor = new DocumentEditor(project);
            projectPath = clearSnapshots ? path : null;
            sourcePath = path;
            unsavedChanges = false;

            await RelinkVideoIfMissingAsync();

            UpdateMaximum();
            selectedCueIds.Clear();
            lastSelectedCueId = null;
            RefreshRowsAndStyles();
            AfterMutation(refreshRows: false);

            string migrated = result.WasMigrated
                ? $" (v{result.SourceSchemaVersion} → v{result.SchemaVersion})"
                : string.Empty;
            Status = $"{Loc["OpenProject"]}: {Path.GetFileName(path)}{migrated}";
            if (clearSnapshots)
            {
                AutosaveService.ClearSnapshots();
            }
        }
        catch (Exception exception)
        {
            Status = $"{Loc["OpenProject"]} — {exception.Message}";
        }
    }

    /// <summary>
    /// 패키지는 영상 경로만 저장하므로 끊어진 연결을 복구할 수 있어야 하고
    /// 조용히 영상 없는 프로젝트로 두지 않는다.
    /// </summary>
    private async Task RelinkVideoIfMissingAsync()
    {
        string? recorded = project?.VideoPath;
        if (project is null || string.IsNullOrEmpty(recorded) || File.Exists(recorded))
        {
            return;
        }

        bool relink = await dialogs.ConfirmAsync(
            Loc["VideoMissingTitle"],
            $"{Loc["VideoMissingPrompt"]}\n\n{recorded}",
            Loc["Relink"]);
        if (!relink)
        {
            return;
        }

        string? replacement = await dialogs.RelinkVideoAsync(recorded);
        if (replacement is not null)
        {
            await LoadVideoAsync(replacement);
        }
    }

    /// <summary>
    /// 비정상 종료가 남긴 스냅샷의 복구를 제안한다.
    /// 시작 시점에 실행되므로 작업 중인 문서의 실행 취소 기록을 지우지 않는다.
    /// </summary>
    public async Task OfferCrashRecoveryAsync()
    {
        string? snapshot = AutosaveService.FindLatestSnapshot();
        if (snapshot is null)
        {
            return;
        }

        bool recover = await dialogs.ConfirmAsync(
            Loc["RecoveryTitle"],
            Loc["RecoveryPrompt"],
            Loc["Recover"]);
        if (!recover)
        {
            AutosaveService.ClearSnapshots();
            return;
        }

        await LoadProjectPackageAsync(snapshot, clearSnapshots: false);
        // 복구된 문서는 정의상 저장되지 않은 상태다.
        unsavedChanges = true;
    }

    /// <summary>현재 프레임을 썸네일로 렌더한다. 아직 그릴 것이 없으면 <c>null</c> 이다.</summary>
    private byte[]? RenderThumbnailPng()
    {
        if (project is null)
        {
            return null;
        }

        try
        {
            const int width = 320;
            const int height = 180;
            PlayerViewport viewport = CreatePlayerViewport(new SKSize(width, height));
            using SKSurface surface = SKSurface.Create(new SKImageInfo(
                ToBitmapDimension(viewport.PlayerSize.Width),
                ToBitmapDimension(viewport.PlayerSize.Height)));
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(new SKColor(24, 24, 24));
            renderer.Render(
                canvas,
                viewport,
                project,
                TimeSpan.FromMilliseconds(PositionMilliseconds),
                new SubtitleRenderOptions());
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 90);
            return data.ToArray();
        }
        catch (Exception exception)
        {
            // 썸네일은 부가 정보다. 이것 때문에 저장을 막지 말고, 가짜로 만들지도 마라.
            Serilog.Log.Warning("{ThumbnailFailure}", exception.Message);
            return null;
        }
    }

    private async Task SaveAsync()
    {
        if (project is null)
        {
            return;
        }

        string suggestedName = Path.GetFileNameWithoutExtension(sourcePath ?? "subtitles") + ".ytt";
        string? path = await dialogs.SaveYttAsync(suggestedName);
        if (path is null)
        {
            return;
        }

        try
        {
            fileService.Export(project, path);
            Status = $"저장 완료: {path}";
        }
        catch (Exception exception)
        {
            Status = $"저장 실패: {exception.Message}";
        }
    }
}
