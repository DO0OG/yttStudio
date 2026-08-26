using YttStudio.Core;
using YttStudio.Core.Project;

namespace YttStudio.App;

/// <summary>
/// 열려 있는 프로젝트의 복구본을 주기적으로 기록한다.
/// 자동 저장은 지정한 간격마다 <c>%TEMP%/YttStudio/autosave/</c> 에 기록하며
/// 실행 취소 기록을 건드리지 않는다. 모델을 읽어 직렬화만 한다.
/// </summary>
public sealed class AutosaveService : IAsyncDisposable
{
    private const int MaxRetainedSnapshots = 5;
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaximumInterval = TimeSpan.FromDays(1);

    private readonly Func<SubtitleProject?> projectAccessor;
    private readonly Func<bool> hasUnsavedChanges;
    private readonly Action<string> onError;
    private readonly TimeSpan interval;
    private readonly CancellationTokenSource cancellation = new();
    private Task? loop;

    public AutosaveService(
        Func<SubtitleProject?> projectAccessor,
        Func<bool> hasUnsavedChanges,
        Action<string> onError,
        TimeSpan interval)
    {
        this.projectAccessor = projectAccessor;
        this.hasUnsavedChanges = hasUnsavedChanges;
        this.onError = onError;
        this.interval = interval > TimeSpan.Zero && interval <= MaximumInterval
            ? interval
            : DefaultInterval;
    }

    /// <summary>복구 스냅샷을 보관하는 디렉터리를 가져온다.</summary>
    public static string AutosaveDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "YttStudio", "autosave");

    /// <summary>백그라운드 타이머를 시작한다. 반복 호출은 무시한다.</summary>
    public void Start() => loop ??= RunAsync(cancellation.Token);

    /// <summary>
    /// 가장 최근 복구 스냅샷을 돌려준다. 없으면 <c>null</c> 이다.
    /// 디스크에 남은 스냅샷은 직전 실행이 정상 종료하지 않았다는 뜻이다.
    /// </summary>
    public static string? FindLatestSnapshot()
    {
        if (!Directory.Exists(AutosaveDirectory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(AutosaveDirectory, "autosave-*.yttproj")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>모든 스냅샷을 제거한다. 정상 저장이나 복구 완료 후 호출한다.</summary>
    public static void ClearSnapshots()
    {
        if (!Directory.Exists(AutosaveDirectory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(AutosaveDirectory, "autosave-*.yttproj"))
        {
            TryDelete(file);
        }
    }

    /// <summary>
    /// 스냅샷 하나를 즉시 기록하고 경로를 돌려준다. 열린 프로젝트가 없으면 <c>null</c> 이다.
    /// </summary>
    public string? WriteSnapshot()
    {
        SubtitleProject? project = projectAccessor();
        if (project is null)
        {
            return null;
        }

        Directory.CreateDirectory(AutosaveDirectory);
        string path = Path.Combine(
            AutosaveDirectory,
            $"autosave-{DateTime.Now:yyyyMMdd-HHmmss-fff}.yttproj");

        ProjectPackage.Save(project, path);
        TrimOldSnapshots();
        return path;
    }

    private static void TrimOldSnapshots()
    {
        foreach (string stale in Directory
            .EnumerateFiles(AutosaveDirectory, "autosave-*.yttproj")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(MaxRetainedSnapshots)
            .ToArray())
        {
            TryDelete(stale);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 다른 프로세스가 잡고 있는 스냅샷 때문에 실패시킬 이유는 없다.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        using PeriodicTimer timer = new(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                if (!hasUnsavedChanges())
                {
                    continue;
                }

                try
                {
                    WriteSnapshot();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // 자동 저장 실패가 편집을 끊으면 안 된다. 알리기만 하고 계속 진행한다.
                    onError($"autosave failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await cancellation.CancelAsync().ConfigureAwait(false);
        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation.Dispose();
    }
}
