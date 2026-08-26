using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YttStudio.App;

/// <summary>
/// 자막 프로젝트가 아닌 로컬 YttStudio 설치에 속하는 설정이다.
/// 설정 파일은 저장소 밖에 두며 내려받은 네이티브 바이너리를 포함하지 않는다.
/// </summary>
public enum AppThemeMode
{
    Default,
    Light,
    Dark,
}

public sealed class AppPreferences
{
    public AppLanguage Language { get; set; } = AppLanguage.Korean;

    public AppThemeMode Theme { get; set; } = AppThemeMode.Default;

    /// <summary>타임라인에서 큐를 끌어당길 때 사용하는 스냅 임계값이다.</summary>
    public double SnapThreshold { get; set; } = 8;

    /// <summary>
    /// 사용자가 선택한 libmpv 파일 또는 폴더다. 비어 있으면 운영체제의
    /// 일반 검색 경로를 사용한다.
    /// </summary>
    public string MpvPath { get; set; } = string.Empty;

    /// <summary>마지막으로 선택한 재생 볼륨이다.</summary>
    public double Volume { get; set; } = 100;

    /// <summary>마지막으로 선택한 음소거 상태다.</summary>
    public bool IsMuted { get; set; }

    /// <summary>프로젝트 복구용 자동 저장을 사용할지 나타낸다.</summary>
    public bool AutosaveEnabled { get; set; } = true;

    /// <summary>자동 저장 간격(초)이다.</summary>
    public int AutosaveIntervalSeconds { get; set; } = 60;
}

/// <summary>사용자별 로컬 애플리케이션 데이터 폴더에 환경설정을 저장한다.</summary>
public sealed class PreferencesStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public PreferencesStore(string? path = null)
    {
        FilePath = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YttStudio",
            "preferences.json");
    }

    public string FilePath { get; }

    public AppPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppPreferences();
            }

            string json = File.ReadAllText(FilePath);
            AppPreferences? preferences = JsonSerializer.Deserialize<AppPreferences>(json, SerializerOptions);
            return preferences ?? new AppPreferences();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Serilog.Log.Warning(exception, "Unable to load YttStudio preferences from {Path}", FilePath);
            return new AppPreferences();
        }
    }

    public bool TrySave(AppPreferences preferences, out string? error)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        string? directory = Path.GetDirectoryName(FilePath);
        string temporaryPath = FilePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(preferences, SerializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, FilePath, overwrite: true);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            error = exception.Message;
            Serilog.Log.Warning(exception, "Unable to save YttStudio preferences to {Path}", FilePath);
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // 저장에 실패했다면 원래 저장 오류가 사용자에게 더 유용하다.
            }
        }
    }
}

/// <summary>
/// 공식 mpv 설치 안내 페이지를 연다. 라이선스와 빌드 선택은 사용자가 고른
/// 배포판에 속하므로 YttStudio는 libmpv를 직접 내려받거나 압축 해제하지 않는다.
/// </summary>
public static class MpvInstallationGuide
{
    public const string OfficialUrl = "https://mpv.io/installation/";

    public static bool TryOpen(out string? error)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = OfficialUrl,
                UseShellExecute = true,
            });
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
