using System.Runtime.InteropServices;
using YttStudio.Video;

namespace YttStudio.Video.Tests;

public sealed class MpvVideoSourceTests
{
    [Fact]
    public async Task ConstructorSetsYtdlFormatThatExcludesStoryboardCodecs()
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        FakeNativeExports exports = new(options);
        MpvNativeLibrary? library = null;
        MpvVideoSource? source = null;

        try
        {
            Assert.True(MpvNativeLibrary.TryLoadCandidatesForTest(
                ["test-mpv"],
                static _ => 1,
                exports.Get,
                static _ => { },
                out library,
                out string diagnostic), diagnostic);

            source = new MpvVideoSource(library!);
            library = null;

            Assert.Equal(
                "bestvideo[vcodec!=none][vcodec!=images]+bestaudio/best[vcodec!=none][vcodec!=images]",
                options["ytdl-format"]);
        }
        finally
        {
            if (source is not null)
            {
                await source.DisposeAsync();
            }
            else
            {
                library?.Dispose();
            }
        }
    }

    private sealed class FakeNativeExports
    {
        private readonly Dictionary<string, nint> pointers = [];
        private readonly List<Delegate> delegates = [];

        public FakeNativeExports(Dictionary<string, string> options)
        {
            Add("mpv_create", (MpvNativeLibrary.MpvCreate)(static () => 1));
            Add("mpv_initialize", (MpvNativeLibrary.MpvInitialize)(static _ => 0));
            Add("mpv_terminate_destroy", (MpvNativeLibrary.MpvTerminateDestroy)(static _ => { }));
            Add("mpv_set_option_string", (MpvNativeLibrary.MpvSetOptionString)(
                (_, name, value) =>
                {
                    options[Marshal.PtrToStringUTF8(name)!] = Marshal.PtrToStringUTF8(value)!;
                    return 0;
                }));
            Add("mpv_set_property_string", (MpvNativeLibrary.MpvSetPropertyString)(static (_, _, _) => 0));
            Add("mpv_get_property", (MpvNativeLibrary.MpvGetProperty)(static (_, _, _, _) => -1));
            Add("mpv_get_property_string", (MpvNativeLibrary.MpvGetPropertyString)(static (_, _) => 0));
            Add("mpv_free", (MpvNativeLibrary.MpvFree)(static _ => { }));
            Add("mpv_command", (MpvNativeLibrary.MpvCommand)(static (_, _) => 0));
            Add("mpv_wait_event", (MpvNativeLibrary.MpvWaitEvent)(static (_, _) => 0));
            Add("mpv_error_string", (MpvNativeLibrary.MpvErrorString)(static _ => 0));
            Add("mpv_client_api_version", (MpvNativeLibrary.MpvClientApiVersion)(static () => 0));
            Add("mpv_render_context_create", (MpvNativeLibrary.MpvRenderContextCreate)(
                static (out nint context, nint _, nint __) =>
                {
                    context = 1;
                    return 0;
                }));
            Add("mpv_render_context_set_update_callback", (MpvNativeLibrary.MpvRenderContextSetUpdateCallback)(
                static (_, _, _) => { }));
            Add("mpv_render_context_update", (MpvNativeLibrary.MpvRenderContextUpdate)(static _ => 0));
            Add("mpv_render_context_render", (MpvNativeLibrary.MpvRenderContextRender)(static (_, _) => 0));
            Add("mpv_render_context_free", (MpvNativeLibrary.MpvRenderContextFree)(static _ => { }));
        }

        public nint Get(nint _, string name) => pointers[name];

        private void Add<T>(string name, T export) where T : Delegate
        {
            delegates.Add(export);
            pointers.Add(name, Marshal.GetFunctionPointerForDelegate(export));
        }
    }
}
