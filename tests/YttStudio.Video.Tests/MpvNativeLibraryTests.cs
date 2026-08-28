using System.Runtime.InteropServices;
using YttStudio.Video;

namespace YttStudio.Video.Tests;

public sealed class MpvNativeLibraryTests
{
    [Fact]
    public void MissingExportReleasesCandidateAndContinuesToNext()
    {
        List<nint> freedHandles = [];
        ExportStubs exportStubs = new();

        nint? TryLoad(string candidate)
            => candidate switch
            {
                "candidate-one" => 101,
                "candidate-two" => 202,
                _ => null,
            };

        nint GetExport(nint handle, string name)
        {
            if (handle == 101)
            {
                throw new EntryPointNotFoundException($"missing export {name}");
            }

            return exportStubs.Get(name);
        }

        MpvNativeLibrary? library = null;
        try
        {
            bool loaded = MpvNativeLibrary.TryLoadCandidatesForTest(
                ["candidate-one", "candidate-two"],
                TryLoad,
                GetExport,
                freedHandles.Add,
                out library,
                out string diagnostic);

            Assert.True(loaded);
            Assert.NotNull(library);
            Assert.Equal("libmpv loaded from candidate-two", diagnostic);
            Assert.Equal([101], freedHandles);
        }
        finally
        {
            library?.Dispose();
        }

        Assert.Equal([101, 202], freedHandles);
    }

    private sealed class ExportStubs
    {
        private readonly Dictionary<string, nint> pointers = [];
        private readonly List<Delegate> delegates = [];

        public ExportStubs()
        {
            Add("mpv_create", (MpvNativeLibrary.MpvCreate)(static () => 0));
            Add("mpv_initialize", (MpvNativeLibrary.MpvInitialize)(static _ => 0));
            Add("mpv_terminate_destroy", (MpvNativeLibrary.MpvTerminateDestroy)(static _ => { }));
            Add("mpv_set_option_string", (MpvNativeLibrary.MpvSetOptionString)(static (_, _, _) => 0));
            Add("mpv_set_property_string", (MpvNativeLibrary.MpvSetPropertyString)(static (_, _, _) => 0));
            Add("mpv_get_property", (MpvNativeLibrary.MpvGetProperty)(static (_, _, _, _) => 0));
            Add("mpv_get_property_string", (MpvNativeLibrary.MpvGetPropertyString)(static (_, _) => 0));
            Add("mpv_free", (MpvNativeLibrary.MpvFree)(static _ => { }));
            Add("mpv_command", (MpvNativeLibrary.MpvCommand)(static (_, _) => 0));
            Add("mpv_error_string", (MpvNativeLibrary.MpvErrorString)(static _ => 0));
            Add("mpv_client_api_version", (MpvNativeLibrary.MpvClientApiVersion)(static () => 0));
            Add("mpv_render_context_create", (MpvNativeLibrary.MpvRenderContextCreate)(
                static (out nint context, nint _, nint __) =>
                {
                    context = 0;
                    return 0;
                }));
            Add("mpv_render_context_set_update_callback", (MpvNativeLibrary.MpvRenderContextSetUpdateCallback)(
                static (_, _, _) => { }));
            Add("mpv_render_context_update", (MpvNativeLibrary.MpvRenderContextUpdate)(static _ => 0));
            Add("mpv_render_context_render", (MpvNativeLibrary.MpvRenderContextRender)(static (_, _) => 0));
            Add("mpv_render_context_free", (MpvNativeLibrary.MpvRenderContextFree)(static _ => { }));
        }

        public nint Get(string name) => pointers[name];

        private void Add<T>(string name, T export) where T : Delegate
        {
            delegates.Add(export);
            pointers.Add(name, Marshal.GetFunctionPointerForDelegate(export));
        }
    }

    [Fact]
    public void RejectedCandidatesRemainInFailureDiagnosticAndAreReleased()
    {
        List<nint> freedHandles = [];
        nint? TryLoad(string candidate)
            => candidate == "candidate-one" ? 101 : 202;

        nint GetExport(nint handle, string name)
            => throw new EntryPointNotFoundException($"missing export {name} from {handle}");

        bool loaded = MpvNativeLibrary.TryLoadCandidatesForTest(
            ["candidate-one", "candidate-two"],
            TryLoad,
            GetExport,
            freedHandles.Add,
            out MpvNativeLibrary? library,
            out string diagnostic);

        library?.Dispose();

        Assert.False(loaded);
        Assert.Null(library);
        Assert.Contains("candidate-one", diagnostic, StringComparison.Ordinal);
        Assert.Contains("candidate-two", diagnostic, StringComparison.Ordinal);
        Assert.Contains("missing export", diagnostic, StringComparison.Ordinal);
        Assert.Equal([101, 202], freedHandles);
    }
}
