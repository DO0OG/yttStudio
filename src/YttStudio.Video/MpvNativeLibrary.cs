using System.Runtime.InteropServices;

namespace YttStudio.Video;

internal sealed class MpvNativeLibrary : IDisposable
{
    private readonly nint libraryHandle;
    private readonly Func<nint, string, nint> getExport;
    private readonly Action<nint> freeLibrary;
    private bool disposed;

    private MpvNativeLibrary(
        nint libraryHandle,
        string loadedPath,
        Func<nint, string, nint> getExport,
        Action<nint> freeLibrary)
    {
        this.libraryHandle = libraryHandle;
        this.getExport = getExport;
        this.freeLibrary = freeLibrary;
        LoadedPath = loadedPath;
        Create = GetExport<MpvCreate>("mpv_create");
        Initialize = GetExport<MpvInitialize>("mpv_initialize");
        TerminateDestroy = GetExport<MpvTerminateDestroy>("mpv_terminate_destroy");
        SetOptionString = GetExport<MpvSetOptionString>("mpv_set_option_string");
        SetPropertyString = GetExport<MpvSetPropertyString>("mpv_set_property_string");
        GetProperty = GetExport<MpvGetProperty>("mpv_get_property");
        GetPropertyString = GetExport<MpvGetPropertyString>("mpv_get_property_string");
        Free = GetExport<MpvFree>("mpv_free");
        Command = GetExport<MpvCommand>("mpv_command");
        ErrorString = GetExport<MpvErrorString>("mpv_error_string");
        ClientApiVersion = GetExport<MpvClientApiVersion>("mpv_client_api_version");
        RenderContextCreate = GetExport<MpvRenderContextCreate>("mpv_render_context_create");
        RenderContextSetUpdateCallback = GetExport<MpvRenderContextSetUpdateCallback>(
            "mpv_render_context_set_update_callback");
        RenderContextUpdate = GetExport<MpvRenderContextUpdate>("mpv_render_context_update");
        RenderContextRender = GetExport<MpvRenderContextRender>("mpv_render_context_render");
        RenderContextFree = GetExport<MpvRenderContextFree>("mpv_render_context_free");
    }

    public string LoadedPath { get; }
    public MpvCreate Create { get; }
    public MpvInitialize Initialize { get; }
    public MpvTerminateDestroy TerminateDestroy { get; }
    public MpvSetOptionString SetOptionString { get; }
    public MpvSetPropertyString SetPropertyString { get; }
    public MpvGetProperty GetProperty { get; }
    public MpvGetPropertyString GetPropertyString { get; }
    public MpvFree Free { get; }
    public MpvCommand Command { get; }
    public MpvErrorString ErrorString { get; }
    public MpvClientApiVersion ClientApiVersion { get; }
    public MpvRenderContextCreate RenderContextCreate { get; }
    public MpvRenderContextSetUpdateCallback RenderContextSetUpdateCallback { get; }
    public MpvRenderContextUpdate RenderContextUpdate { get; }
    public MpvRenderContextRender RenderContextRender { get; }
    public MpvRenderContextFree RenderContextFree { get; }

    public static bool TryLoad(out MpvNativeLibrary? library, out string diagnostic)
        => TryLoadCandidates(
            EnumerateCandidates(),
            TryLoadNative,
            NativeLibrary.GetExport,
            NativeLibrary.Free,
            out library,
            out diagnostic);

    internal static bool TryLoadCandidatesForTest(
        IReadOnlyList<string> candidates,
        Func<string, nint?> tryLoad,
        Func<nint, string, nint> getExport,
        Action<nint> freeLibrary,
        out MpvNativeLibrary? library,
        out string diagnostic)
        => TryLoadCandidates(candidates, tryLoad, getExport, freeLibrary, out library, out diagnostic);

    private static bool TryLoadCandidates(
        IEnumerable<string> candidates,
        Func<string, nint?> tryLoad,
        Func<nint, string, nint> getExport,
        Action<nint> freeLibrary,
        out MpvNativeLibrary? library,
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

            if (TryLoadCandidate(candidate, tryLoad, getExport, freeLibrary, out library, out string? error))
            {
                diagnostic = $"libmpv loaded from {candidate}";
                return true;
            }

            attempted.Add($"{candidate} ({error ?? "not found or could not load"})");
        }

        library = null;
        diagnostic = "libmpv was not found. Probed: " + string.Join("; ", attempted);
        return false;
    }

    private static nint? TryLoadNative(string candidate)
        => NativeLibrary.TryLoad(candidate, out nint handle) ? handle : null;

    private static bool TryLoadCandidate(
        string candidate,
        Func<string, nint?> tryLoad,
        Func<nint, string, nint> getExport,
        Action<nint> freeLibrary,
        out MpvNativeLibrary? library,
        out string? error)
    {
        library = null;
        error = null;
        try
        {
            nint? loadedHandle = tryLoad(candidate);
            if (loadedHandle is null)
            {
                return false;
            }

            nint handle = loadedHandle.Value;
            try
            {
                library = new MpvNativeLibrary(handle, candidate, getExport, freeLibrary);
                return true;
            }
            catch
            {
                freeLibrary(handle);
                throw;
            }
        }
        catch (Exception exception) when (
            exception is BadImageFormatException
            or DllNotFoundException
            or EntryPointNotFoundException)
        {
            error = exception.Message;
            return false;
        }
    }

    public string GetError(int code)
        => Marshal.PtrToStringUTF8(ErrorString(code)) ?? $"mpv error {code}";

    public void Dispose()
    {
        if (!disposed)
        {
            freeLibrary(libraryHandle);
            disposed = true;
        }
    }

    private T GetExport<T>(string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(getExport(libraryHandle, name));

    private static IEnumerable<string> EnumerateCandidates()
    {
        string? overridePath = Environment.GetEnvironmentVariable("YTTSTUDIO_MPV_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            string expanded = Environment.ExpandEnvironmentVariables(overridePath.Trim().Trim('"'));
            if (Directory.Exists(expanded))
            {
                foreach (string name in GetLibraryNames())
                {
                    yield return Path.Combine(expanded, name);
                }
            }
            else
            {
                yield return expanded;
            }
        }

        foreach (string name in GetLibraryNames())
        {
            yield return Path.Combine(AppContext.BaseDirectory, name);
        }

        // 리눅스와 macOS 는 시스템 라이브러리 경로에 설치하는 것이 정상적인 배포 방식이라
        // 로더의 탐색에 맡긴다. 윈도우는 다르다. LoadLibrary 의 기본 탐색 순서에 현재 작업
        // 디렉터리가 들어 있어, 쓸 수 있는 폴더에서 앱을 실행하면 그 자리에 놓인 DLL 이
        // 로드된다. 버전 검사는 방어가 되지 않는다 — 로드가 끝난 뒤에 도는 검사라 그 시점엔
        // 이미 진입점이 실행된 뒤다. 그래서 윈도우에서는 맨 이름으로 찾지 않는다.
        if (!OperatingSystem.IsWindows())
        {
            foreach (string name in GetLibraryNames())
            {
                yield return name;
            }
        }
    }

    private static IReadOnlyList<string> GetLibraryNames()
        => OperatingSystem.IsWindows()
            ? ["libmpv-2.dll", "mpv-2.dll"]
            : OperatingSystem.IsMacOS()
                ? ["libmpv.2.dylib", "libmpv.dylib"]
                : ["libmpv.so.2", "libmpv.so"];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint MpvCreate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int MpvInitialize(nint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void MpvTerminateDestroy(nint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int MpvSetOptionString(nint handle, nint name, nint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int MpvSetPropertyString(nint handle, nint name, nint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int MpvGetProperty(nint handle, nint name, int format, nint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint MpvGetPropertyString(nint handle, nint name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void MpvFree(nint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int MpvCommand(nint handle, nint arguments);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint MpvErrorString(int error);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate ulong MpvClientApiVersion();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int MpvRenderContextCreate(out nint context, nint handle, nint parameters);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void MpvRenderContextSetUpdateCallback(nint context, nint callback, nint callbackContext);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate ulong MpvRenderContextUpdate(nint context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int MpvRenderContextRender(nint context, nint parameters);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void MpvRenderContextFree(nint context);
}
