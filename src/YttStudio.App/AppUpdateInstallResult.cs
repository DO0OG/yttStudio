using System.Text.Json;

namespace YttStudio.App;

/// <summary>종료 후 업데이트 helper가 남긴 설치 결과를 나타낸다.</summary>
internal sealed record AppUpdateInstallResult(
    string Status,
    string DownloadedAssetPath,
    string TargetPath,
    string? BackupPath,
    bool ExistingInstallationRestored,
    string? Error);

/// <summary>업데이트 helper 결과를 대상 경로 밖에 안전하게 기록하고 소비한다.</summary>
internal static class AppUpdateInstallResultStore
{
    internal const string SucceededStatus = "succeeded";
    internal const string RolledBackStatus = "rolled_back";
    internal const string FailedStatus = "failed";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    internal static string GetResultPath(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        return Path.GetFullPath(targetPath) + ".update-result.json";
    }

    internal static void Write(string resultPath, AppUpdateInstallResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultPath);
        ArgumentNullException.ThrowIfNull(result);
        string path = Path.GetFullPath(resultPath);
        string? parent = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new IOException("업데이트 결과 파일의 상위 디렉터리를 확인하지 못했다.");
        }

        Directory.CreateDirectory(parent);
        string temporaryPath = path + $".tmp-{Guid.NewGuid():N}";
        try
        {
            string json = JsonSerializer.Serialize(result, JsonOptions);
            File.WriteAllText(
                temporaryPath,
                json,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    internal static bool TryRead(string resultPath, out AppUpdateInstallResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(resultPath) || !File.Exists(resultPath))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(resultPath);
            result = JsonSerializer.Deserialize<AppUpdateInstallResult>(json, JsonOptions);
            if (result is null ||
                string.IsNullOrWhiteSpace(result.Status) ||
                string.IsNullOrWhiteSpace(result.DownloadedAssetPath) ||
                string.IsNullOrWhiteSpace(result.TargetPath))
            {
                result = null;
                return false;
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
                NotSupportedException or ArgumentException)
        {
            Serilog.Log.Warning(exception, "업데이트 설치 결과 파일을 읽지 못했다: {ResultPath}", resultPath);
            return false;
        }
    }

    internal static bool TryConsume(
        string resultPath,
        out AppUpdateInstallResult? result)
    {
        if (!TryRead(resultPath, out result) || result is null)
        {
            return false;
        }

        TryDelete(resultPath);
        return true;
    }

    internal static bool TryConsumeForCurrentProcess(out AppUpdateInstallResult? result)
    {
        foreach (string resultPath in GetCurrentProcessResultPaths())
        {
            if (TryConsume(resultPath, out result))
            {
                return true;
            }
        }

        result = null;
        return false;
    }

    private static IEnumerable<string> GetCurrentProcessResultPaths()
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        string? appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrWhiteSpace(appImagePath))
        {
            AddTargetPath(paths, appImagePath);
        }

        string basePath = Path.GetFullPath(AppContext.BaseDirectory);
        string? bundle = FindMacBundle(basePath);
        if (bundle is not null)
        {
            AddTargetPath(paths, bundle);
        }

        AddTargetPath(paths, Path.TrimEndingDirectorySeparator(basePath));

        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            string fullProcessPath = Path.GetFullPath(processPath);
            string? processBundle = FindMacBundle(fullProcessPath);
            if (processBundle is not null)
            {
                AddTargetPath(paths, processBundle);
            }

            if (Path.GetExtension(fullProcessPath).Equals(
                    ".AppImage",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddTargetPath(paths, fullProcessPath);
            }
            else if (Path.GetDirectoryName(fullProcessPath) is string processDirectory)
            {
                AddTargetPath(paths, processDirectory);
            }
        }

        return paths;
    }

    private static string? FindMacBundle(string path)
    {
        string directoryPath = Directory.Exists(path)
            ? path
            : Path.GetDirectoryName(path) ?? path;
        DirectoryInfo? directory = new(directoryPath);
        while (directory is not null)
        {
            if (directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static void AddPath(HashSet<string> paths, string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            paths.Add(path);
        }
    }

    private static void AddTargetPath(HashSet<string> paths, string targetPath)
    {
        try
        {
            AddPath(paths, GetResultPath(targetPath));
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
                NotSupportedException)
        {
            Serilog.Log.Warning(exception, "업데이트 결과 경로를 확인하지 못했다: {TargetPath}", targetPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(exception, "업데이트 임시 결과 파일을 삭제하지 못했다: {Path}", path);
        }
    }
}
