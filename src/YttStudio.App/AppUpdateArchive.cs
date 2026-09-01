using SharpCompress.Archives;

namespace YttStudio.App;

/// <summary>업데이트 압축 자산을 안전한 임시 디렉터리에 풀고 파일을 교체한다.</summary>
internal sealed record AppUpdateRollbackResult(
    bool Restored,
    string BackupPath,
    string? Error);

internal static class AppUpdateArchiveOperations
{
    private const int BufferSize = 128 * 1024;
    private const int MaximumEntryCount = 20_000;
    private const long MaximumArchiveEntryBytes = 2L * 1024 * 1024 * 1024;

    public static async Task ExtractAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(
                    () => Extract(archivePath, destinationDirectory, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AppUpdateException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or
                ArgumentException or NotSupportedException)
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                "업데이트 압축 파일을 해제하지 못했다.",
                exception);
        }
    }

    public static string RequireFile(string rootDirectory, string relativePath)
    {
        string path = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
        string root = EnsureRootWithSeparator(rootDirectory);
        if (!path.StartsWith(root, GetPathComparison()) || !File.Exists(path))
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                $"업데이트 압축 파일에 필요한 실행 파일이 없다: {relativePath}");
        }

        return Path.GetRelativePath(rootDirectory, path);
    }

    public static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDirectory, directory);
                Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
            }

            foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDirectory, file);
                string destination = Path.Combine(destinationDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: false);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            TryDeleteDirectory(destinationDirectory);
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                "업데이트 앱을 임시 위치에 복사하지 못했다.",
                exception);
        }
    }

    public static string MoveDirectoryWithBackup(
        string targetDirectory,
        string replacementDirectory)
    {
        string backupDirectory = targetDirectory + $".backup-{Guid.NewGuid():N}";
        bool movedOriginal = false;
        try
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Move(targetDirectory, backupDirectory);
                movedOriginal = true;
            }

            Directory.Move(replacementDirectory, targetDirectory);
            return backupDirectory;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            if (movedOriginal && !Directory.Exists(targetDirectory) && Directory.Exists(backupDirectory))
            {
                if (!TryMoveDirectory(backupDirectory, targetDirectory))
                {
                    throw new AppUpdateException(
                        AppUpdateErrorKind.InstallationFailed,
                        $"기존 설치를 복원하지 못했다. 백업을 수동으로 복구해야 한다: {backupDirectory}",
                        exception);
                }
            }

            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                "기존 앱을 업데이트 파일로 교체하지 못했다.",
                exception);
        }
    }

    public static AppUpdateRollbackResult RollbackDirectory(
        string targetDirectory,
        string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            return new(
                Restored: false,
                backupDirectory,
                "업데이트 백업 디렉터리가 없어 기존 설치를 복원하지 못했다.");
        }

        string failedDirectory = targetDirectory + $".failed-{Guid.NewGuid():N}";
        try
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Move(targetDirectory, failedDirectory);
            }

            Directory.Move(backupDirectory, targetDirectory);
            TryDeleteDirectory(failedDirectory);
            return new(true, backupDirectory, null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            Serilog.Log.Error(
                exception,
                "업데이트 실패 후 기존 앱 복원에 실패했다: {TargetDirectory}",
                targetDirectory);
            return new(false, backupDirectory, exception.Message);
        }
    }

    public static void DeleteBackup(string backupDirectory)
    {
        try
        {
            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(
                exception,
                "업데이트 성공 후 기존 앱 백업을 정리하지 못했다: {BackupDirectory}",
                backupDirectory);
        }
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(exception, "업데이트 임시 디렉터리를 정리하지 못했다: {Path}", path);
        }
    }

    public static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(exception, "업데이트 임시 파일을 정리하지 못했다: {Path}", path);
        }
    }

    private static void Extract(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(root);
        string rootWithSeparator = EnsureRootWithSeparator(root);
        using IArchive archive = ArchiveFactory.OpenArchive(archivePath);
        int entryCount = 0;
        foreach (IArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++entryCount > MaximumEntryCount)
            {
                throw new AppUpdateException(
                    AppUpdateErrorKind.InstallationFailed,
                    "업데이트 압축 항목 수가 허용 한도를 초과했다.");
            }

            string entryKey = entry.Key?.Replace('/', Path.DirectorySeparatorChar)
                ?? throw InvalidEntry("이름이 없는 압축 항목");
            string destinationPath = Path.GetFullPath(Path.Combine(root, entryKey));
            if (string.IsNullOrWhiteSpace(entryKey) ||
                Path.IsPathRooted(entryKey) ||
                !destinationPath.StartsWith(rootWithSeparator, GetPathComparison()))
            {
                throw InvalidEntry(entry.Key);
            }

            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            string? parent = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(parent))
            {
                throw InvalidEntry(entry.Key);
            }

            Directory.CreateDirectory(parent);
            using Stream input = entry.OpenEntryStream();
            using FileStream output = new(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.SequentialScan);
            byte[] buffer = new byte[BufferSize];
            long copied = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = input.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                copied = checked(copied + read);
                if (copied > MaximumArchiveEntryBytes)
                {
                    throw new AppUpdateException(
                        AppUpdateErrorKind.InstallationFailed,
                        "업데이트 압축 항목 크기가 허용 한도를 초과했다.");
                }

                output.Write(buffer, 0, read);
            }
        }
    }

    private static string EnsureRootWithSeparator(string rootDirectory)
    {
        string root = Path.GetFullPath(rootDirectory);
        return root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
    }

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static AppUpdateException InvalidEntry(string? entryKey)
        => new(
            AppUpdateErrorKind.InstallationFailed,
            $"안전하지 않은 업데이트 압축 항목을 차단했다: {entryKey}");

    private static bool TryMoveDirectory(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            Serilog.Log.Error(
                exception,
                "업데이트 교체 실패 후 기존 앱 이동에 실패했다: {Destination}",
                destination);
            return false;
        }
    }
}

/// <summary>업데이트 설치 위치와 Unix 실행 권한을 다룬다.</summary>
internal static class AppUpdatePathOperations
{
    public static string FindMacAppBundle(string executablePath)
    {
        DirectoryInfo? directory = new(Path.GetDirectoryName(executablePath) ?? executablePath);
        while (directory is not null)
        {
            if (directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new AppUpdateException(
            AppUpdateErrorKind.InstallationUnsupported,
            "현재 macOS 앱 번들을 확인하지 못했다.");
    }

    public static string FindSingleAppBundle(string rootDirectory)
    {
        string[] apps = Directory
            .EnumerateDirectories(rootDirectory, "*.app", SearchOption.TopDirectoryOnly)
            .ToArray();
        if (apps.Length != 1)
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                "업데이트 디스크 이미지에서 앱 번들을 하나 찾지 못했다.");
        }

        return apps[0];
    }

    public static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(
                path,
                mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new AppUpdateException(
                AppUpdateErrorKind.InstallationFailed,
                $"업데이트 실행 파일의 실행 권한을 설정하지 못했다: {path}",
                exception);
        }
    }
}
