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
    public async Task<bool> OpenPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return true;
        }

        OpenPathKind kind = OpenPathClassifier.Classify(path);
        if (kind == OpenPathKind.Unsupported)
        {
            return true;
        }

        if (!await ConfirmDocumentReplacementAsync())
        {
            return false;
        }

        if (kind == OpenPathKind.Project)
        {
            await LoadProjectPackageAsync(path, clearSnapshots: true);
            return true;
        }

        if (kind == OpenPathKind.Video)
        {
            await LoadVideoAsync(path);
            return true;
        }

        if (kind == OpenPathKind.Subtitle)
        {
            ImportSubtitle(path);
        }

        return true;
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

            if (!await OpenPathAsync(path))
            {
                break;
            }
        }
    }

    private async Task<bool> ConfirmDocumentReplacementAsync()
    {
        if (!unsavedChanges)
        {
            return true;
        }

        UnsavedChangesChoice choice = await dialogs.ConfirmUnsavedChangesAsync(
            "저장되지 않은 변경",
            "현재 문서에 저장되지 않은 변경이 있습니다.",
            Loc["SaveProject"],
            "버리기",
            "취소");
        return choice switch
        {
            UnsavedChangesChoice.Save => await TrySaveProjectAsync(),
            UnsavedChangesChoice.Discard => true,
            _ => false,
        };
    }

    private void ImportSubtitle(string path)
    {
        try
        {
            ImportResult result = fileService.Import(path);
            project = result.Project;
            editor = new DocumentEditor(project);
            sourcePath = path;
            projectPath = null;
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

        await OpenPathAsync(path);
    }

    /// <summary>공유 소스에 영상을 불러온다. 열기 명령과 프로젝트 재연결이 함께 쓴다.</summary>
    private async Task LoadVideoAsync(string path, bool undoFree = false)
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
            if (project is not null && editor is not null)
            {
                YttStudio.Video.VideoInfo sourceInfo = videoSource.Info;
                YttStudio.Core.VideoInfo documentInfo = new(
                    sourceInfo.Width,
                    sourceInfo.Height,
                    sourceInfo.Duration,
                    sourceInfo.NominalFps);
                bool changed;
                if (undoFree)
                {
                    using (editor.BeginUndoFreeMutation())
                    {
                        changed = editor.SetVideo(loadedVideoPath, documentInfo);
                    }
                }
                else
                {
                    changed = editor.SetVideo(loadedVideoPath, documentInfo);
                }

                if (changed)
                {
                    AfterMutation();
                }
            }
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

        await OpenPathAsync(path);
    }

    /// <summary>열려 있는 프로젝트를 <c>.yttproj</c> 패키지로 저장한다.</summary>
    private async Task SaveProjectAsync()
    {
        await TrySaveProjectAsync();
    }

    private async Task<bool> TrySaveProjectAsync()
    {
        if (project is null)
        {
            return true;
        }

        string suggested = Path.GetFileNameWithoutExtension(projectPath ?? sourcePath ?? "project") + ".yttproj";
        string? path = await dialogs.SaveProjectAsync(suggested);
        if (path is null)
        {
            return false;
        }

        try
        {
            ProjectPackage.Save(project, path, RenderThumbnailPng());
            projectPath = path;
            SetDirty(false);
            // 정상 저장은 크래시 스냅샷을 무효화한다.
            AutosaveService.ClearSnapshots();
            Status = $"{Loc["SaveProject"]}: {path}";
            return true;
        }
        catch (Exception exception)
        {
            Status = $"{Loc["SaveProject"]} — {exception.Message}";
            return false;
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
            SetDirty(false);

            await RelinkVideoIfMissingAsync();

            UpdateMaximum();
            selectedCueIds.Clear();
            lastSelectedCueId = null;
            RefreshRowsAndStyles();
            AfterMutation(refreshRows: false, markDirty: false);

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
    /// <summary>존재 확인이 네트워크를 타지 않도록 기다려 주는 한도다.</summary>
    private static readonly TimeSpan RemoteProbeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>경로가 이 기기 밖을 가리키는지 본다.</summary>
    private static bool IsRemotePath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        return Uri.TryCreate(path, UriKind.Absolute, out Uri? uri)
            && !uri.IsFile
            && !uri.IsLoopback;
    }

    private async Task RelinkVideoIfMissingAsync()
    {
        string? recorded = project?.VideoPath;
        if (project is null || string.IsNullOrEmpty(recorded))
        {
            return;
        }

        // 경로는 프로젝트 파일에서 온 값이라 신뢰할 수 없다. 남이 만든 .yttproj 를 열었을
        // 뿐인데 UNC 경로를 그대로 두드리면 윈도우가 SMB 접속을 시도한다.
        // 응답 없는 호스트면 UI 가 멈추고, 악의적인 호스트면 통합 인증이 오갈 수 있다.
        // 원격 경로는 건드리기 전에 물어본다.
        if (IsRemotePath(recorded) && !await dialogs.ConfirmAsync(
                Loc["VideoMissingTitle"],
                $"{Loc["RemoteVideoPathPrompt"]}\n\n{recorded}",
                Loc["Continue"]))
        {
            return;
        }

        if (await Task.Run(() => File.Exists(recorded)).WaitAsync(RemoteProbeTimeout)
            .ConfigureAwait(true))
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
            await LoadVideoAsync(replacement, undoFree: true);
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
        SetDirty(true);
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
