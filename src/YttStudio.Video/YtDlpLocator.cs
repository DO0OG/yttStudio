namespace YttStudio.Video;

/// <summary>설치된 yt-dlp 실행 파일을 찾아 경로를 반환한다.</summary>
public static class YtDlpLocator
{
    private static readonly string[] ExecutableNames =
        OperatingSystem.IsWindows()
            ? ["yt-dlp.exe", "yt-dlp", "yt-dlp.cmd", "yt-dlp.bat"]
            : ["yt-dlp", "yt-dlp.exe"];

    /// <summary>앱 폴더와 PATH에서 yt-dlp를 찾는다.</summary>
    public static bool TryFind(out string? executablePath, out string diagnostic)
    {
        try
        {
            string? overridePath = ReadOverridePath();
            IEnumerable<string> candidates = EnumerateCandidates(AppContext.BaseDirectory, overridePath,
                Environment.GetEnvironmentVariable("PATH"));
            return TryFindCandidates(candidates, File.Exists, out executablePath, out diagnostic);
        }
        catch (Exception exception)
        {
            executablePath = null;
            diagnostic = $"yt-dlp 탐색에 실패했습니다: {exception.Message}";
            return false;
        }
    }

    /// <summary>찾지 못하면 null을 반환하는 편의 메서드다.</summary>
    public static string? Find()
        => TryFind(out string? path, out _) ? path : null;

    internal static bool TryFindCandidatesForTest(
        IReadOnlyList<string> candidates,
        Func<string, bool> fileExists,
        out string? executablePath,
        out string diagnostic)
        => TryFindCandidates(candidates, fileExists, out executablePath, out diagnostic);

    private static bool TryFindCandidates(
        IEnumerable<string> candidates,
        Func<string, bool> fileExists,
        out string? executablePath,
        out string diagnostic)
    {
        List<string> attempted = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in candidates)
        {
            if (!seen.Add(candidate))
            {
                continue;
            }

            attempted.Add(candidate);
            if (fileExists(candidate))
            {
                executablePath = candidate;
                diagnostic = $"yt-dlp를 찾았습니다: {candidate}";
                return true;
            }
        }

        executablePath = null;
        diagnostic = attempted.Count == 0
            ? "yt-dlp를 찾을 경로가 없습니다."
            : "yt-dlp를 찾지 못했습니다. 확인한 경로: " + string.Join(", ", attempted);
        return false;
    }

    private static IEnumerable<string> EnumerateCandidates(
        string appDirectory,
        string? overridePath,
        string? pathVariable)
    {
        foreach (string candidate in ExpandOverride(overridePath))
        {
            yield return candidate;
        }

        foreach (string name in ExecutableNames)
        {
            yield return Path.Combine(appDirectory, name);
            yield return Path.Combine(appDirectory, "tools", name);
            yield return Path.Combine(appDirectory, "bin", name);
        }

        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            yield break;
        }

        foreach (string directory in pathVariable.Split(Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string cleaned = directory.Trim().Trim('"');
            if (cleaned.Length == 0)
            {
                continue;
            }

            foreach (string name in ExecutableNames)
            {
                yield return Path.Combine(cleaned, name);
            }
        }
    }

    private static IEnumerable<string> ExpandOverride(string? overridePath)
    {
        if (string.IsNullOrWhiteSpace(overridePath))
        {
            yield break;
        }

        string value = Environment.ExpandEnvironmentVariables(overridePath.Trim().Trim('"'));
        if (Directory.Exists(value))
        {
            foreach (string name in ExecutableNames)
            {
                yield return Path.Combine(value, name);
            }

            yield break;
        }

        yield return value;
    }

    private static string? ReadOverridePath()
        => Environment.GetEnvironmentVariable("YTTSTUDIO_YTDLP_PATH") ??
            Environment.GetEnvironmentVariable("YTDLP_PATH") ??
            Environment.GetEnvironmentVariable("YTDLP");
}
