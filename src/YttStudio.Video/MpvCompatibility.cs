using System.Runtime.InteropServices;

namespace YttStudio.Video;

/// <summary>
/// Minimum-version gate and crash metadata for the native libmpv dependency.
/// SPEC §18: a native crash is usually a version or driver problem, so the loaded libmpv build
/// must be recorded, and an unsupported build must fail with a clear message rather than
/// crashing somewhere deep inside the render pipeline.
/// </summary>
public static class MpvCompatibility
{
    /// <summary>
    /// Lowest accepted <c>mpv_client_api_version()</c>, i.e. libmpv 2.0 (mpv 0.35+).
    /// The render API entry points this project uses are stable from that release on.
    /// </summary>
    public const uint MinimumClientApiVersion = 2u << 16;

    /// <summary>Splits a packed client API version into its major and minor parts.</summary>
    public static (uint Major, uint Minor) Decompose(uint version) =>
        (version >> 16, version & 0xFFFF);

    /// <summary>Formats a packed client API version as <c>major.minor</c>.</summary>
    public static string Format(uint version)
    {
        (uint major, uint minor) = Decompose(version);
        return $"{major}.{minor}";
    }

    /// <summary>Returns whether <paramref name="version"/> satisfies the minimum gate.</summary>
    public static bool IsSupported(uint version) => version >= MinimumClientApiVersion;

    /// <summary>
    /// Produces the message shown when the loaded build is too old.
    /// Naming the actual and required versions keeps the failure actionable.
    /// </summary>
    public static string DescribeUnsupported(uint version, string loadedPath) =>
        $"libmpv {Format(version)} is older than the required {Format(MinimumClientApiVersion)}. " +
        $"Loaded from: {loadedPath}";

    /// <summary>
    /// One line describing the native dependency, written to logs and crash reports.
    /// SPEC §18 requires the libmpv version to appear in crash logs.
    /// </summary>
    public static string DescribeForCrashLog(uint version, string loadedPath) =>
        $"libmpv client-api={Format(version)} path={loadedPath} " +
        $"os={Environment.OSVersion} arch={RuntimeInformation.ProcessArchitecture}";
}
