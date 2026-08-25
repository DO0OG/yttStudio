using System.Runtime.InteropServices;

namespace YttStudio.Video;

/// <summary>
/// 네이티브 libmpv 의존성의 최소 버전 검사와 크래시 메타데이터다.
/// 네이티브 크래시는 대개 버전이나 드라이버 문제이므로 불러온 libmpv 빌드를
/// 기록해야 하고, 지원하지 않는 빌드는 명확한 메시지로 실패해야 한다.
/// 렌더 파이프라인 깊은 곳에서 크래시하지 않게 한다.
/// </summary>
public static class MpvCompatibility
{
    /// <summary>
    /// 허용하는 최소 <c>mpv_client_api_version()</c> 이다. libmpv 2.0 (mpv 0.35+) 에 해당한다.
    /// 이 프로젝트가 쓰는 render API 진입점은 그 릴리스부터 안정적이다.
    /// </summary>
    public const uint MinimumClientApiVersion = 2u << 16;

    /// <summary>패킹된 client API 버전을 major 와 minor 로 나눈다.</summary>
    public static (uint Major, uint Minor) Decompose(uint version) =>
        (version >> 16, version & 0xFFFF);

    /// <summary>패킹된 client API 버전을 <c>major.minor</c> 형태로 만든다.</summary>
    public static string Format(uint version)
    {
        (uint major, uint minor) = Decompose(version);
        return $"{major}.{minor}";
    }

    /// <summary><paramref name="version"/> 이 최소 요구를 만족하는지 돌려준다.</summary>
    public static bool IsSupported(uint version) => version >= MinimumClientApiVersion;

    /// <summary>
    /// 불러온 빌드가 너무 오래되었을 때 보여줄 메시지를 만든다.
    /// 실제 버전과 요구 버전을 함께 적어야 조치가 가능하다.
    /// </summary>
    public static string DescribeUnsupported(uint version, string loadedPath) =>
        $"libmpv {Format(version)} is older than the required {Format(MinimumClientApiVersion)}. " +
        $"Loaded from: {loadedPath}";

    /// <summary>
    /// 로그와 크래시 보고서에 남기는 네이티브 의존성 설명 한 줄이다.
    /// 크래시 로그에 libmpv 버전이 나타나야 한다.
    /// </summary>
    public static string DescribeForCrashLog(uint version, string loadedPath) =>
        $"libmpv client-api={Format(version)} path={loadedPath} " +
        $"os={Environment.OSVersion} arch={RuntimeInformation.ProcessArchitecture}";
}
