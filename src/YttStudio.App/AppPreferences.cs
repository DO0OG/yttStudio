using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using YttStudio.Render;

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
    private PreviewViewportMode previewViewportMode = PreviewViewportMode.VideoFrame;

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

    /// <summary>프리뷰에 사용할 데스크톱 플레이어 모드다.</summary>
    /// <remarks>실측되지 않은 모바일 세로 모드는 저장하지 않고 기본 모드로 되돌린다.</remarks>
    public PreviewViewportMode PreviewViewportMode
    {
        get => previewViewportMode;
        set => previewViewportMode = NormalizePreviewViewportMode(value);
    }

    /// <summary>프로젝트 복구용 자동 저장을 사용할지 나타낸다.</summary>
    public bool AutosaveEnabled { get; set; } = true;

    /// <summary>자동 저장 간격(초)이다.</summary>
    public int AutosaveIntervalSeconds { get; set; } = 60;

    /// <summary>환경설정에서 사용할 수 있는 데스크톱 뷰포트 모드인지 확인한다.</summary>
    public static bool IsSelectablePreviewViewportMode(PreviewViewportMode mode)
        => mode is PreviewViewportMode.VideoFrame
            or PreviewViewportMode.YouTubeDefault
            or PreviewViewportMode.YouTubeTheater
            or PreviewViewportMode.YouTubeFullscreen;

    /// <summary>알 수 없거나 아직 측정하지 않은 모드를 안전한 기본 모드로 바꾼다.</summary>
    public static PreviewViewportMode NormalizePreviewViewportMode(PreviewViewportMode mode)
        => IsSelectablePreviewViewportMode(mode) ? mode : PreviewViewportMode.VideoFrame;
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
            if (preferences is null)
            {
                return new AppPreferences();
            }

            // 숫자로 저장된 알 수 없는 열거형 값도 다음 실행에서 안전하게 복구한다.
            preferences.PreviewViewportMode = AppPreferences.NormalizePreviewViewportMode(
                preferences.PreviewViewportMode);
            return preferences;
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
