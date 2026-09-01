using System.IO;

namespace YttStudio.App;

/// <summary>업데이트 프로세스 검증에 사용하는 운영체제 종류다.</summary>
internal enum AppUpdateProcessPlatform
{
    Windows,
    MacOS,
    Linux,
}

/// <summary>업데이트 프로세스의 실행 파일과 작업 경계를 검증한다.</summary>
internal static class AppUpdateProcessSecurity
{
    internal static AppUpdateProcessRequest ValidateAndNormalize(
        AppUpdateProcessRequest request,
        AppUpdateProcessPlatform? platformOverride = null,
        Func<string, FileAttributes>? attributesReader = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        AppUpdateProcessPlatform platform = platformOverride ?? DetectPlatform();
        Func<string, FileAttributes> readAttributes = attributesReader ?? File.GetAttributes;
        if (request.Arguments is null)
        {
            throw Reject("프로세스 인자가 비어 있다.");
        }

        List<string> trustedRoots = NormalizeTrustedRoots(request.TrustedRoots, readAttributes, platform);
        string fileName = NormalizeFileName(request.FileName, trustedRoots, readAttributes, platform);
        string? workingDirectory = NormalizeWorkingDirectory(
            request.WorkingDirectory,
            trustedRoots,
            readAttributes,
            platform);
        ValidateInterpreterArguments(
            fileName,
            request.Arguments,
            trustedRoots,
            readAttributes,
            platform);
        return request with
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
        };
    }

    internal static string GetSystemToolPath(
        string toolName,
        AppUpdateProcessPlatform? platformOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        AppUpdateProcessPlatform platform = platformOverride ?? DetectPlatform();
        return TryGetAllowedSystemToolPath(toolName, platform)
            ?? throw Reject($"허용되지 않은 시스템 도구다: {toolName}");
    }

    private static string NormalizeFileName(
        string fileName,
        IReadOnlyList<string> trustedRoots,
        Func<string, FileAttributes> attributesReader,
        AppUpdateProcessPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(fileName) || ContainsParentSegment(fileName))
        {
            throw Reject($"프로세스 경로가 올바르지 않다: {fileName}");
        }

        string? systemToolPath = TryGetAllowedSystemToolPath(fileName, platform);
        if (systemToolPath is not null)
        {
            return systemToolPath;
        }

        string normalizedPath = NormalizeAbsolutePath(fileName, "프로세스 경로");
        EnsureTrustedFile(normalizedPath, trustedRoots, attributesReader, platform);
        return normalizedPath;
    }

    private static List<string> NormalizeTrustedRoots(
        IReadOnlyList<string>? roots,
        Func<string, FileAttributes> attributesReader,
        AppUpdateProcessPlatform platform)
    {
        List<string> normalizedRoots = [];
        if (roots is null)
        {
            return normalizedRoots;
        }

        foreach (string root in roots)
        {
            string normalizedRoot = NormalizeAbsolutePath(root, "신뢰 업데이트 루트");
            if (!Directory.Exists(normalizedRoot))
            {
                throw Reject($"신뢰 업데이트 루트가 없다: {normalizedRoot}");
            }

            EnsureNoReparseComponents(normalizedRoot, attributesReader, platform);
            if (!normalizedRoots.Any(existing =>
                    string.Equals(existing, normalizedRoot, GetPathComparison(platform))))
            {
                normalizedRoots.Add(normalizedRoot);
            }
        }

        return normalizedRoots;
    }

    private static string? NormalizeWorkingDirectory(
        string? workingDirectory,
        IReadOnlyList<string> trustedRoots,
        Func<string, FileAttributes> attributesReader,
        AppUpdateProcessPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return null;
        }

        string normalizedDirectory = NormalizeAbsolutePath(workingDirectory, "프로세스 작업 디렉터리");
        if (!trustedRoots.Any(root => IsWithinRoot(normalizedDirectory, root, platform)))
        {
            throw Reject($"프로세스 작업 디렉터리가 신뢰 루트 밖에 있다: {workingDirectory}");
        }

        if (!Directory.Exists(normalizedDirectory))
        {
            throw Reject($"프로세스 작업 디렉터리가 없다: {normalizedDirectory}");
        }

        EnsureNoReparseComponents(normalizedDirectory, attributesReader, platform);
        return normalizedDirectory;
    }

    private static void ValidateInterpreterArguments(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> trustedRoots,
        Func<string, FileAttributes> attributesReader,
        AppUpdateProcessPlatform platform)
    {
        if (platform == AppUpdateProcessPlatform.Linux && IsUnixShell(fileName))
        {
            if (arguments.Count == 0 ||
                string.IsNullOrWhiteSpace(arguments[0]) ||
                arguments[0].StartsWith("-", StringComparison.Ordinal))
            {
                throw Reject("쉘 실행 인자가 신뢰 스크립트가 아니다.");
            }

            string scriptPath = NormalizeAbsolutePath(arguments[0], "쉘 스크립트 경로");
            EnsureTrustedFile(scriptPath, trustedRoots, attributesReader, platform);
            return;
        }

        if (platform == AppUpdateProcessPlatform.Windows && IsWindowsCommandShell(fileName))
        {
            int commandIndex = -1;
            for (int index = 0; index < arguments.Count; index++)
            {
                if (string.Equals(arguments[index], "/c", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arguments[index], "/k", StringComparison.OrdinalIgnoreCase))
                {
                    commandIndex = index;
                    break;
                }
            }

            if (commandIndex < 0 || commandIndex + 1 >= arguments.Count)
            {
                throw Reject("cmd 실행 인자가 신뢰 helper 스크립트가 아니다.");
            }

            string scriptPath = NormalizeAbsolutePath(arguments[commandIndex + 1], "cmd helper 경로");
            EnsureTrustedFile(scriptPath, trustedRoots, attributesReader, platform);
        }
    }

    private static void EnsureTrustedFile(
        string path,
        IReadOnlyList<string> trustedRoots,
        Func<string, FileAttributes> attributesReader,
        AppUpdateProcessPlatform platform)
    {
        if (!File.Exists(path) ||
            !trustedRoots.Any(root => IsWithinRoot(path, root, platform)))
        {
            throw Reject($"실행 파일이 신뢰 업데이트 루트 밖에 있다: {path}");
        }

        EnsureNoReparseComponents(path, attributesReader, platform);
    }

    private static void EnsureNoReparseComponents(
        string path,
        Func<string, FileAttributes> attributesReader,
        AppUpdateProcessPlatform platform)
    {
        string current = path;
        while (true)
        {
            FileAttributes attributes;
            try
            {
                attributes = attributesReader(current);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException or
                    NotSupportedException or System.Security.SecurityException)
            {
                throw Reject($"업데이트 프로세스 경로를 확인하지 못했다: {current}", exception);
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw Reject($"업데이트 프로세스 경로의 reparse 지점을 차단했다: {current}");
            }

            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            if (info.LinkTarget is not null)
            {
                throw Reject($"업데이트 프로세스 경로의 symbolic link를 차단했다: {current}");
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, current, GetPathComparison(platform)))
            {
                return;
            }

            current = parent;
        }
    }

    private static string NormalizeAbsolutePath(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path) ||
            ContainsParentSegment(path))
        {
            throw Reject($"{description}가 정규화된 절대 경로가 아니다: {path}");
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw Reject($"{description}를 정규화하지 못했다: {path}", exception);
        }
    }

    private static bool IsWithinRoot(
        string path,
        string root,
        AppUpdateProcessPlatform platform)
    {
        StringComparison comparison = GetPathComparison(platform);
        if (string.Equals(path, root, comparison))
        {
            return true;
        }

        string separator = root.EndsWith(Path.DirectorySeparatorChar) ||
            root.EndsWith(Path.AltDirectorySeparatorChar)
            ? string.Empty
            : Path.DirectorySeparatorChar.ToString();
        return path.StartsWith(root + separator, comparison);
    }

    private static bool ContainsParentSegment(string path)
        => path.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, "..", StringComparison.Ordinal));

    private static string? TryGetAllowedSystemToolPath(
        string fileName,
        AppUpdateProcessPlatform platform)
    {
        return platform switch
        {
            AppUpdateProcessPlatform.Windows => TryGetWindowsSystemToolPath(fileName),
            AppUpdateProcessPlatform.MacOS => TryGetMacSystemToolPath(fileName),
            AppUpdateProcessPlatform.Linux => TryGetLinuxSystemToolPath(fileName),
            _ => null,
        };
    }

    private static string? TryGetWindowsSystemToolPath(string fileName)
    {
        string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (string.IsNullOrWhiteSpace(systemDirectory))
        {
            systemDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32");
        }

        if (string.IsNullOrWhiteSpace(systemDirectory) ||
            !Path.IsPathFullyQualified(systemDirectory))
        {
            systemDirectory = @"C:\Windows\System32";
        }

        string normalizedSystemDirectory = systemDirectory.TrimEnd('\\', '/');
        string commandShell = normalizedSystemDirectory + @"\cmd.exe";
        string explorer = normalizedSystemDirectory + @"\explorer.exe";
        if (string.Equals(fileName, "cmd.exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, commandShell, StringComparison.OrdinalIgnoreCase))
        {
            return commandShell;
        }

        if (string.Equals(fileName, "explorer.exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, explorer, StringComparison.OrdinalIgnoreCase))
        {
            return explorer;
        }

        return null;
    }

    private static string? TryGetMacSystemToolPath(string fileName)
    {
        string? path = fileName switch
        {
            "hdiutil" => "/usr/bin/hdiutil",
            "open" => "/usr/bin/open",
            _ => null,
        };
        if (path is not null ||
            string.Equals(fileName, "/usr/bin/hdiutil", StringComparison.Ordinal) ||
            string.Equals(fileName, "/usr/bin/open", StringComparison.Ordinal))
        {
            return path ?? fileName;
        }

        return null;
    }

    private static string? TryGetLinuxSystemToolPath(string fileName)
        => fileName switch
        {
            "/bin/sh" or "sh" => "/bin/sh",
            "/usr/bin/sh" => "/usr/bin/sh",
            "xdg-open" or "/usr/bin/xdg-open" => "/usr/bin/xdg-open",
            _ => null,
        };

    private static bool IsWindowsCommandShell(string path)
        => path.EndsWith("\\cmd.exe", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/cmd.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnixShell(string path)
        => string.Equals(path, "/bin/sh", StringComparison.Ordinal) ||
            string.Equals(path, "/usr/bin/sh", StringComparison.Ordinal);

    private static AppUpdateProcessPlatform DetectPlatform()
        => OperatingSystem.IsWindows()
            ? AppUpdateProcessPlatform.Windows
            : OperatingSystem.IsMacOS()
                ? AppUpdateProcessPlatform.MacOS
                : OperatingSystem.IsLinux()
                    ? AppUpdateProcessPlatform.Linux
                    : throw Reject("현재 운영체제의 업데이트 프로세스를 지원하지 않는다.");

    private static StringComparison GetPathComparison(AppUpdateProcessPlatform platform)
        => platform == AppUpdateProcessPlatform.Windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static AppUpdateException Reject(string message, Exception? innerException = null)
        => new(AppUpdateErrorKind.InstallationFailed, message, innerException);
}
