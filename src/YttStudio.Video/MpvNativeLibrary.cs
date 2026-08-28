using System.Runtime.InteropServices;

namespace YttStudio.Video;

internal sealed class MpvNativeLibrary : IDisposable
{
    private readonly nint libraryHandle;
    private bool disposed;

    private MpvNativeLibrary(nint libraryHandle, string loadedPath)
    {
        this.libraryHandle = libraryHandle;
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
    {
        List<string> attempted = [];
        foreach (string candidate in EnumerateCandidates())
        {
            if (!attempted.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                attempted.Add(candidate);
            }

            if (TryLoadCandidate(candidate, out library, out string? error))
            {
                diagnostic = $"libmpv loaded from {candidate}";
                return true;
            }

            if (error is not null)
            {
                attempted[^1] = $"{candidate} ({error})";
            }
        }

        library = null;
        diagnostic = "libmpv was not found. Probed: " + string.Join("; ", attempted);
        return false;
    }

    private static bool TryLoadCandidate(
        string candidate,
        out MpvNativeLibrary? library,
        out string? error)
    {
        library = null;
        error = null;
        try
        {
            if (!NativeLibrary.TryLoad(candidate, out nint handle))
            {
                return false;
            }

            try
            {
                library = new MpvNativeLibrary(handle, candidate);
                return true;
            }
            catch
            {
                NativeLibrary.Free(handle);
                throw;
            }
        }
        catch (Exception exception) when (exception is BadImageFormatException or DllNotFoundException)
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
            NativeLibrary.Free(libraryHandle);
            disposed = true;
        }
    }

    private T GetExport<T>(string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(libraryHandle, name));

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

        foreach (string name in GetLibraryNames())
        {
            yield return name;
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
