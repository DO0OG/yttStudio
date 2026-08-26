using System.Reflection;

namespace YttStudio.App;

/// <summary>실행 중인 앱의 표시용 버전이다.</summary>
public static class AppVersion
{
    /// <summary>정보 창에 보여줄 버전 문자열이다.</summary>
    public static string Current { get; } = Resolve(
        typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        typeof(AppVersion).Assembly.GetName().Version);

    /// <summary>
    /// informational version 을 우선 쓰되 빌드 메타데이터는 떼어낸다.
    /// SDK 가 붙이는 "0.1.0+9a3f21c" 형태를 사용자에게 그대로 보여주지 않기 위해서다.
    /// </summary>
    internal static string Resolve(string? informationalVersion, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            int metadata = informationalVersion.IndexOf('+');
            string trimmed = metadata >= 0
                ? informationalVersion[..metadata]
                : informationalVersion;
            trimmed = trimmed.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return assemblyVersion?.ToString(3) ?? "0.0.0";
    }
}
