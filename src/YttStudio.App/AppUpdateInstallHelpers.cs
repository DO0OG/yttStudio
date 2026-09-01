using System.Text.Json;

namespace YttStudio.App;

/// <summary>업데이트 helper가 기존 설치를 교체하고 결과를 기록하도록 스크립트를 만든다.</summary>
internal static class AppUpdateInstallHelpers
{
    private const int MaximumReplacementAttempts = 30;

    private static readonly JsonSerializerOptions ResultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static string BuildWindowsDirectoryReplacementScript(
        int processId,
        string targetDirectory,
        string stagingDirectory,
        string relativeExecutable,
        string scriptPath,
        string? downloadedAssetPath = null,
        string? resultPath = null)
    {
        string target = EscapeCmdValue(targetDirectory);
        string staging = EscapeCmdValue(stagingDirectory);
        string relative = EscapeCmdValue(relativeExecutable);
        string backup = EscapeCmdValue(targetDirectory + $".backup-{Guid.NewGuid():N}");
        string script = EscapeCmdValue(scriptPath);
        string result = EscapeCmdValue(
            resultPath ?? AppUpdateInstallResultStore.GetResultPath(targetDirectory));
        string downloaded = EscapeCmdValue(downloadedAssetPath ?? string.Empty);
        return string.Join(
            "\r\n",
            "@echo off",
            "setlocal EnableExtensions EnableDelayedExpansion",
            $"set \"ytt_pid={processId}\"",
            $"set \"ytt_target={target}\"",
            $"set \"ytt_staging={staging}\"",
            $"set \"ytt_backup={backup}\"",
            $"set \"ytt_relative={relative}\"",
            $"set \"ytt_script={script}\"",
            $"set \"ytt_result={result}\"",
            $"set \"ytt_downloaded={downloaded}\"",
            "set \"YTT_UPDATE_RESULT=%ytt_result%\"",
            "set \"YTT_UPDATE_DOWNLOADED=%ytt_downloaded%\"",
            "set \"YTT_UPDATE_TARGET=%ytt_target%\"",
            "set \"YTT_UPDATE_BACKUP=%ytt_backup%\"",
            ":ytt_wait_for_exit",
            "tasklist /FI \"PID eq %ytt_pid%\" /NH | findstr /C:\"%ytt_pid%\" >nul",
            "if errorlevel 1 goto ytt_move_target",
            "timeout /t 1 /nobreak >nul",
            "goto ytt_wait_for_exit",
            ":ytt_move_target",
            "set /a ytt_attempts=0",
            ":ytt_move_target_retry",
            "if exist \"%ytt_backup%\" rmdir /s /q \"%ytt_backup%\" >nul 2>&1",
            "if not exist \"%ytt_target%\" goto ytt_move_staging",
            "move \"%ytt_target%\" \"%ytt_backup%\" >nul 2>&1",
            "if not errorlevel 1 goto ytt_move_staging",
            "set /a ytt_attempts+=1",
            $"if %ytt_attempts% GEQ {MaximumReplacementAttempts} goto ytt_fail",
            "timeout /t 1 /nobreak >nul",
            "goto ytt_move_target_retry",
            ":ytt_move_staging",
            "move \"%ytt_staging%\" \"%ytt_target%\" >nul 2>&1",
            "if errorlevel 1 goto ytt_restore",
            "if not exist \"%ytt_target%\\%ytt_relative%\" goto ytt_restore",
            "start \"\" \"%ytt_target%\\%ytt_relative%\"",
            "if errorlevel 1 goto ytt_restore",
            "call :ytt_write_result succeeded false \"\"",
            "del /f /q \"%ytt_script%\" >nul 2>&1",
            "exit /b 0",
            ":ytt_restore",
            "if exist \"%ytt_target%\" rmdir /s /q \"%ytt_target%\" >nul 2>&1",
            "if not exist \"%ytt_backup%\" goto ytt_restore_failed",
            "move \"%ytt_backup%\" \"%ytt_target%\" >nul 2>&1",
            "if errorlevel 1 goto ytt_restore_failed",
            "if not exist \"%ytt_target%\\%ytt_relative%\" goto ytt_restore_failed",
            "start \"\" \"%ytt_target%\\%ytt_relative%\"",
            "if errorlevel 1 goto ytt_restore_relaunch_failed",
            "call :ytt_write_result rolled_back true \"new application launch failed; old application restored\"",
            "del /f /q \"%ytt_script%\" >nul 2>&1",
            "exit /b 1",
            ":ytt_restore_relaunch_failed",
            "call :ytt_write_result failed true \"old application was restored but could not be relaunched\"",
            "del /f /q \"%ytt_script%\" >nul 2>&1",
            "exit /b 1",
            ":ytt_restore_failed",
            "call :ytt_write_result failed false \"old application could not be restored\"",
            "del /f /q \"%ytt_script%\" >nul 2>&1",
            "exit /b 1",
            ":ytt_fail",
            "if exist \"%ytt_target%\\%ytt_relative%\" start \"\" \"%ytt_target%\\%ytt_relative%\"",
            "call :ytt_write_result failed false \"existing application replacement failed\"",
            "del /f /q \"%ytt_script%\" >nul 2>&1",
            "exit /b 1",
            ":ytt_write_result",
            "set \"YTT_UPDATE_STATUS=%~1\"",
            "set \"YTT_UPDATE_RESTORED=%~2\"",
            "set \"YTT_UPDATE_ERROR=%~3\"",
            "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"$result=[ordered]@{status=$env:YTT_UPDATE_STATUS;downloadedAssetPath=$env:YTT_UPDATE_DOWNLOADED;targetPath=$env:YTT_UPDATE_TARGET;backupPath=$env:YTT_UPDATE_BACKUP;existingInstallationRestored=([bool]::Parse($env:YTT_UPDATE_RESTORED));error=$env:YTT_UPDATE_ERROR};[IO.File]::WriteAllText($env:YTT_UPDATE_RESULT,($result|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))\"",
            "exit /b 0",
            "");
    }

    internal static string BuildUnixDirectoryReplacementScript(
        int processId,
        string targetDirectory,
        string stagingDirectory,
        string relativeExecutable,
        string scriptPath,
        string? downloadedAssetPath = null,
        string? resultPath = null)
    {
        string backupPath = targetDirectory + $".backup-{Guid.NewGuid():N}";
        string effectiveResultPath = resultPath ?? AppUpdateInstallResultStore.GetResultPath(targetDirectory);
        string successResult = ShellQuote(SerializeResult(
            AppUpdateInstallResultStore.SucceededStatus,
            downloadedAssetPath ?? string.Empty,
            targetDirectory,
            backupPath,
            restored: false,
            error: null));
        string rollbackResult = ShellQuote(SerializeResult(
            AppUpdateInstallResultStore.RolledBackStatus,
            downloadedAssetPath ?? string.Empty,
            targetDirectory,
            backupPath,
            restored: true,
            error: "new application launch failed; old application restored"));
        string failedResult = ShellQuote(SerializeResult(
            AppUpdateInstallResultStore.FailedStatus,
            downloadedAssetPath ?? string.Empty,
            targetDirectory,
            backupPath,
            restored: false,
            error: "old application could not be restored"));
        string restoredResult = ShellQuote(SerializeResult(
            AppUpdateInstallResultStore.FailedStatus,
            downloadedAssetPath ?? string.Empty,
            targetDirectory,
            backupPath,
            restored: true,
            error: "old application was restored but could not be relaunched"));
        return string.Join(
            "\n",
            "#!/bin/sh",
            "set -u",
            $"ytt_pid={processId}",
            $"ytt_target={ShellQuote(targetDirectory)}",
            $"ytt_staging={ShellQuote(stagingDirectory)}",
            $"ytt_backup={ShellQuote(backupPath)}",
            $"ytt_relative={ShellQuote(relativeExecutable)}",
            $"ytt_script={ShellQuote(scriptPath)}",
            $"ytt_result={ShellQuote(effectiveResultPath)}",
            "ytt_write_success() {",
            $"    printf '%s\\n' {successResult} > \"$ytt_result\"",
            "}",
            "ytt_write_rollback() {",
            $"    printf '%s\\n' {rollbackResult} > \"$ytt_result\"",
            "}",
            "ytt_write_failed() {",
            $"    printf '%s\\n' {failedResult} > \"$ytt_result\"",
            "}",
            "ytt_write_restored() {",
            $"    printf '%s\\n' {restoredResult} > \"$ytt_result\"",
            "}",
            "ytt_relaunch_existing() {",
            "    ytt_old_executable=\"$ytt_target/$ytt_relative\"",
            "    if [ -f \"$ytt_old_executable\" ]; then",
            "        \"$ytt_old_executable\" >/dev/null 2>&1 &",
            "        return 0",
            "    fi",
            "    return 1",
            "}",
            "ytt_restore() {",
            "    if [ ! -e \"$ytt_backup\" ]; then",
            "        ytt_relaunch_existing",
            "        ytt_write_failed",
            "        rm -f \"$ytt_script\"",
            "        exit 1",
            "    fi",
            "    rm -rf \"$ytt_target\" 2>/dev/null || true",
            "    if ! mv \"$ytt_backup\" \"$ytt_target\" 2>/dev/null; then",
            "        ytt_write_failed",
            "        rm -f \"$ytt_script\"",
            "        exit 1",
            "    fi",
            "    if ytt_relaunch_existing; then ytt_write_rollback; else ytt_write_restored; fi",
            "    rm -f \"$ytt_script\"",
            "    exit 1",
            "}",
            "while kill -0 \"$ytt_pid\" 2>/dev/null; do sleep 1; done",
            "ytt_attempts=0",
            "if [ -e \"$ytt_target\" ]; then",
            "    while ! mv \"$ytt_target\" \"$ytt_backup\" 2>/dev/null; do",
            "        ytt_attempts=$((ytt_attempts + 1))",
            "        if [ \"$ytt_attempts\" -ge " + MaximumReplacementAttempts + " ]; then",
            "            ytt_relaunch_existing",
            "            ytt_write_failed",
            "            rm -f \"$ytt_script\"",
            "            exit 1",
            "        fi",
            "        sleep 1",
            "    done",
            "fi",
            "if ! mv \"$ytt_staging\" \"$ytt_target\" 2>/dev/null; then ytt_restore; fi",
            "ytt_executable=\"$ytt_target/$ytt_relative\"",
            "if [ ! -f \"$ytt_executable\" ]; then ytt_restore; fi",
            "if ! chmod +x \"$ytt_executable\"; then ytt_restore; fi",
            "\"$ytt_executable\" >/dev/null 2>&1 &",
            "ytt_new_pid=$!",
            "sleep 1",
            "if ! kill -0 \"$ytt_new_pid\" 2>/dev/null; then ytt_restore; fi",
            "ytt_write_success",
            "rm -f \"$ytt_script\"",
            "exit 0",
            "");
    }

    internal static string BuildUnixFileReplacementScript(
        int processId,
        string targetFile,
        string stagingFile,
        string scriptPath,
        string? downloadedAssetPath = null,
        string? resultPath = null)
    {
        string backupPath = targetFile + $".backup-{Guid.NewGuid():N}";
        string effectiveResultPath = resultPath ?? AppUpdateInstallResultStore.GetResultPath(targetFile);
        string successResult = ShellQuote(SerializeResult(
            AppUpdateInstallResultStore.SucceededStatus,
            downloadedAssetPath ?? string.Empty,
            targetFile,
            backupPath,
            restored: false,
            error: null));
        string rollbackResult = ShellQuote(SerializeResult(
            AppUpdateInstallResultStore.RolledBackStatus,
            downloadedAssetPath ?? string.Empty,
            targetFile,
            backupPath,
            restored: true,
            error: "new application launch failed; old application restored"));
        string failedResult = ShellQuote(SerializeResult(
            AppUpdateInstallResultStore.FailedStatus,
            downloadedAssetPath ?? string.Empty,
            targetFile,
            backupPath,
            restored: false,
            error: "old application could not be restored"));
        string restoredResult = ShellQuote(SerializeResult(
            AppUpdateInstallResultStore.FailedStatus,
            downloadedAssetPath ?? string.Empty,
            targetFile,
            backupPath,
            restored: true,
            error: "old application was restored but could not be relaunched"));
        return string.Join(
            "\n",
            "#!/bin/sh",
            "set -u",
            $"ytt_pid={processId}",
            $"ytt_target={ShellQuote(targetFile)}",
            $"ytt_staging={ShellQuote(stagingFile)}",
            $"ytt_backup={ShellQuote(backupPath)}",
            $"ytt_script={ShellQuote(scriptPath)}",
            $"ytt_result={ShellQuote(effectiveResultPath)}",
            "ytt_write_success() {",
            $"    printf '%s\\n' {successResult} > \"$ytt_result\"",
            "}",
            "ytt_write_rollback() {",
            $"    printf '%s\\n' {rollbackResult} > \"$ytt_result\"",
            "}",
            "ytt_write_failed() {",
            $"    printf '%s\\n' {failedResult} > \"$ytt_result\"",
            "}",
            "ytt_write_restored() {",
            $"    printf '%s\\n' {restoredResult} > \"$ytt_result\"",
            "}",
            "ytt_relaunch_existing() {",
            "    if [ -f \"$ytt_target\" ]; then",
            "        \"$ytt_target\" >/dev/null 2>&1 &",
            "        return 0",
            "    fi",
            "    return 1",
            "}",
            "ytt_restore() {",
            "    if [ ! -e \"$ytt_backup\" ]; then",
            "        ytt_relaunch_existing",
            "        ytt_write_failed",
            "        rm -f \"$ytt_script\"",
            "        exit 1",
            "    fi",
            "    rm -f \"$ytt_target\" 2>/dev/null || true",
            "    if ! mv \"$ytt_backup\" \"$ytt_target\" 2>/dev/null; then",
            "        ytt_write_failed",
            "        rm -f \"$ytt_script\"",
            "        exit 1",
            "    fi",
            "    if ytt_relaunch_existing; then ytt_write_rollback; else ytt_write_restored; fi",
            "    rm -f \"$ytt_script\"",
            "    exit 1",
            "}",
            "while kill -0 \"$ytt_pid\" 2>/dev/null; do sleep 1; done",
            "ytt_attempts=0",
            "while ! mv \"$ytt_target\" \"$ytt_backup\" 2>/dev/null; do",
            "    ytt_attempts=$((ytt_attempts + 1))",
            "    if [ \"$ytt_attempts\" -ge " + MaximumReplacementAttempts + " ]; then",
            "        ytt_relaunch_existing",
            "        ytt_write_failed",
            "        rm -f \"$ytt_script\"",
            "        exit 1",
            "    fi",
            "    sleep 1",
            "done",
            "if ! mv \"$ytt_staging\" \"$ytt_target\" 2>/dev/null; then ytt_restore; fi",
            "if [ ! -f \"$ytt_target\" ]; then ytt_restore; fi",
            "if ! chmod +x \"$ytt_target\"; then ytt_restore; fi",
            "\"$ytt_target\" >/dev/null 2>&1 &",
            "ytt_new_pid=$!",
            "sleep 1",
            "if ! kill -0 \"$ytt_new_pid\" 2>/dev/null; then ytt_restore; fi",
            "ytt_write_success",
            "rm -f \"$ytt_script\"",
            "exit 0",
            "");
    }

    private static string SerializeResult(
        string status,
        string downloadedAssetPath,
        string targetPath,
        string backupPath,
        bool restored,
        string? error)
        => JsonSerializer.Serialize(
            new
            {
                status,
                downloadedAssetPath,
                targetPath,
                backupPath,
                existingInstallationRestored = restored,
                error,
            },
            ResultJsonOptions);

    private static string EscapeCmdValue(string value)
        => value.Replace("%", "%%", StringComparison.Ordinal)
            .Replace("\"", "\"\"", StringComparison.Ordinal);

    private static string ShellQuote(string value)
        => $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
}
