using YttStudio.Core;
using YttStudio.Core.Project;

namespace YttStudio.App;

/// <summary>
/// Periodically writes a recovery copy of the open project.
/// SPEC §12: autosave runs every 60 seconds into <c>%TEMP%/YttStudio/autosave/</c> and must
/// never touch the undo stack, so it only reads the model and serialises it.
/// </summary>
public sealed class AutosaveService : IAsyncDisposable
{
    // SPEC §12 [PRODUCT]: autosave cadence is fixed at 60 seconds.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private const int MaxRetainedSnapshots = 5;

    private readonly Func<SubtitleProject?> projectAccessor;
    private readonly Func<bool> hasUnsavedChanges;
    private readonly Action<string> onError;
    private readonly CancellationTokenSource cancellation = new();
    private Task? loop;

    public AutosaveService(
        Func<SubtitleProject?> projectAccessor,
        Func<bool> hasUnsavedChanges,
        Action<string> onError)
    {
        this.projectAccessor = projectAccessor;
        this.hasUnsavedChanges = hasUnsavedChanges;
        this.onError = onError;
    }

    /// <summary>Gets the directory holding recovery snapshots.</summary>
    public static string AutosaveDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "YttStudio", "autosave");

    /// <summary>Starts the background timer. Repeat calls are ignored.</summary>
    public void Start() => loop ??= RunAsync(cancellation.Token);

    /// <summary>
    /// Returns the newest recovery snapshot, or <c>null</c> when none exists.
    /// A snapshot surviving on disk means the previous run did not shut down cleanly.
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

    /// <summary>Removes every snapshot. Called after a clean save or a completed recovery.</summary>
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
    /// Writes one snapshot immediately and returns its path, or <c>null</c> when no project is open.
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
            // A snapshot still held by another process is not worth failing over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        using PeriodicTimer timer = new(Interval);
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
                    // Autosave failure must never interrupt editing; surface it and keep going.
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
