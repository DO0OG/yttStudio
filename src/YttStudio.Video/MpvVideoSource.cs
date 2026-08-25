using System.Globalization;
using System.Runtime.InteropServices;

namespace YttStudio.Video;

/// <summary>Provides callback-rendered libmpv playback without native child-window embedding.</summary>
public sealed class MpvVideoSource : IVideoSource
{
    private const int MpvFormatFlag = 3;
    private const int MpvFormatInt64 = 4;
    private const int MpvFormatDouble = 5;
    private const int RenderParamApiType = 1;
    private const int RenderParamSoftwareSize = 17;
    private const int RenderParamSoftwareFormat = 18;
    private const int RenderParamSoftwareStride = 19;
    private const int RenderParamSoftwarePointer = 20;
    private const ulong RenderUpdateFrame = 1;
    private readonly object controlGate = new();
    private readonly MpvNativeLibrary native;
    private readonly nint mpvHandle;
    private readonly LatestFrameBuffer frames = new();
    private readonly AutoResetEvent renderSignal = new(false);
    private readonly ManualResetEventSlim renderReady = new(false);
    private readonly Thread renderThread;
    private readonly Timer stateTimer;
    private readonly MpvRenderUpdateCallback renderUpdateCallback;
    private nint renderContext;
    private Exception? renderFailure;
    private Task? disposeTask;
    private VideoInfo info = new(0, 0, TimeSpan.Zero, 0);
    private double positionSeconds;
    private string? requestedPath;
    private long sequenceNumber;
    private bool playing;
    private bool stopping;
    private bool disposed;

    private MpvVideoSource(MpvNativeLibrary native)
    {
        this.native = native;
        mpvHandle = native.Create();
        if (mpvHandle == 0)
        {
            native.Dispose();
            throw new InvalidOperationException("mpv_create failed.");
        }

        try
        {
            // SPEC §8.1 [API]: callback rendering is mandatory; no native window handle is assigned.
            SetOption("vo", "libmpv");
            SetOption("audio-display", "no");
            SetOption("keep-open", "yes");
            SetOption("hwdec", "no");
            Check(native.Initialize(mpvHandle), "mpv_initialize");
            LibraryVersion = ReadStringProperty("mpv-version") ?? GetApiVersionText(native.ClientApiVersion());

            renderUpdateCallback = OnRenderUpdate;
            renderThread = new Thread(RenderLoop)
            {
                IsBackground = true,
                Name = "YttStudio libmpv RenderThread",
            };
            renderThread.Start();
            renderReady.Wait();
            if (renderFailure is not null)
            {
                throw new InvalidOperationException("libmpv software render context initialization failed.", renderFailure);
            }

            stateTimer = new Timer(PollState, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(33));
        }
        catch
        {
            if (renderThread?.IsAlive == true)
            {
                Volatile.Write(ref stopping, true);
                renderSignal.Set();
                renderThread.Join();
            }

            native.TerminateDestroy(mpvHandle);
            native.Dispose();
            throw;
        }
    }

    public event Action? FrameReady;

    public VideoInfo Info => Volatile.Read(ref info);
    public TimeSpan Position => TimeSpan.FromSeconds(Math.Max(0, Volatile.Read(ref positionSeconds)));
    public bool IsPlaying => Volatile.Read(ref playing);
    public string LibraryVersion { get; }
    public string LibraryPath => native.LoadedPath;

    /// <summary>Creates a libmpv source when probing finds a compatible native library.</summary>
    public static bool TryCreate(out MpvVideoSource? source, out string diagnostic)
    {
        if (!MpvNativeLibrary.TryLoad(out MpvNativeLibrary? library, out diagnostic))
        {
            source = null;
            return false;
        }

        try
        {
            source = new MpvVideoSource(library!);
            diagnostic = $"{diagnostic}; version {source.LibraryVersion}";
            return true;
        }
        catch (Exception exception)
        {
            library?.Dispose();
            source = null;
            diagnostic = $"libmpv initialization failed: {exception.Message}";
            return false;
        }
    }

    public async Task LoadAsync(string path, CancellationToken cancellationToken)
    {
        ThrowIfStopping();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Video file was not found.", path);
        }

        string fullPath = Path.GetFullPath(path);
        lock (controlGate)
        {
            ThrowIfStoppingLocked();
            Volatile.Write(ref requestedPath, fullPath);
            Volatile.Write(ref info, new VideoInfo(0, 0, TimeSpan.Zero, 0));
            Volatile.Write(ref positionSeconds, 0);
            Volatile.Write(ref playing, false);
            frames.BeginSeek();
            InvokeCommand("loadfile", fullPath, "replace");
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfStopping();
            PollState(null);
            VideoInfo current = Info;
            if (current.Width > 0 && current.Height > 0 && current.Duration > TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException("libmpv did not expose video metadata within 15 seconds.");
    }

    public void Play()
    {
        SetProperty("pause", "no");
        Volatile.Write(ref playing, true);
    }

    public void Pause()
    {
        SetProperty("pause", "yes");
        Volatile.Write(ref playing, false);
    }

    public Task SeekAsync(TimeSpan position, bool exact = true, CancellationToken cancellationToken = default)
    {
        ThrowIfStopping();
        cancellationToken.ThrowIfCancellationRequested();
        string mode = exact ? "absolute+exact" : "absolute+keyframes";
        double targetSeconds = Math.Max(0, position.TotalSeconds);
        lock (controlGate)
        {
            ThrowIfStoppingLocked();
            frames.BeginSeek();
            InvokeCommand("seek", targetSeconds.ToString("R", CultureInfo.InvariantCulture), mode);
            Volatile.Write(ref positionSeconds, targetSeconds);
        }

        return Task.CompletedTask;
    }

    public void StepFrame(int delta)
    {
        ThrowIfStopping();
        if (delta == 0)
        {
            return;
        }

        Pause();
        string command = delta > 0 ? "frame-step" : "frame-back-step";
        for (int index = 0; index < Math.Abs(delta); index++)
        {
            InvokeCommand(command);
        }
    }

    public void SetSpeed(double speed)
    {
        ThrowIfStopping();
        if (speed is < 0.25 or > 2.0)
        {
            throw new ArgumentOutOfRangeException(nameof(speed), "Playback speed must be from 0.25 through 2.0.");
        }

        SetProperty("speed", speed.ToString("R", CultureInfo.InvariantCulture));
    }

    public bool TryLockLatestFrame(out VideoFrameLock frame)
    {
        lock (controlGate)
        {
            if (stopping || disposed)
            {
                frame = default;
                return false;
            }

            return frames.TryLockLatestFrame(out frame);
        }
    }

    public async ValueTask DisposeAsync()
    {
        TaskCompletionSource<object?>? starter = null;
        Task task;
        lock (controlGate)
        {
            if (disposeTask is null)
            {
                Volatile.Write(ref stopping, true);
                Volatile.Write(ref disposed, true);
                TaskCompletionSource<object?> completion = new(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                disposeTask = completion.Task;
                starter = completion;
            }

            task = disposeTask!;
        }

        if (starter is not null)
        {
            await CompleteDisposeAsync(starter).ConfigureAwait(false);
        }

        await task.ConfigureAwait(false);
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource<object?> completion)
    {
        try
        {
            await stateTimer.DisposeAsync().ConfigureAwait(false);

            // SPEC §8.4 [API]: detach callback, join RenderThread, then destroy the mpv core.
            renderSignal.Set();
            await Task.Run(renderThread.Join).ConfigureAwait(false);
            frames.Dispose();
            lock (controlGate)
            {
                native.TerminateDestroy(mpvHandle);
            }

            renderSignal.Dispose();
            renderReady.Dispose();
            native.Dispose();
            completion.SetResult(null);
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
        }
    }

    private void RenderLoop()
    {
        try
        {
            CreateSoftwareRenderContext();
            renderReady.Set();
            while (true)
            {
                renderSignal.WaitOne();
                if (Volatile.Read(ref stopping))
                {
                    break;
                }

                RenderLatestFrame();
            }
        }
        catch (Exception exception)
        {
            renderFailure = exception;
        }
        finally
        {
            Exception? cleanupFailure = null;
            nint context = renderContext;
            if (context != 0)
            {
                try
                {
                    native.RenderContextSetUpdateCallback(context, 0, 0);
                }
                catch (Exception exception)
                {
                    cleanupFailure = exception;
                }

                try
                {
                    native.RenderContextFree(context);
                }
                catch (Exception exception)
                {
                    cleanupFailure ??= exception;
                }
                finally
                {
                    renderContext = 0;
                }
            }

            // Preserve the render-loop failure if cleanup also fails.
            renderFailure ??= cleanupFailure;
            renderReady.Set();
        }
    }

    private unsafe void CreateSoftwareRenderContext()
    {
        byte* api = stackalloc byte[] { (byte)'s', (byte)'w', 0 };
        MpvRenderParam* parameters = stackalloc MpvRenderParam[2];
        parameters[0] = new MpvRenderParam(RenderParamApiType, (nint)api);
        parameters[1] = default;
        Check(native.RenderContextCreate(out renderContext, mpvHandle, (nint)parameters),
            "mpv_render_context_create(sw)");
        nint callback = Marshal.GetFunctionPointerForDelegate(renderUpdateCallback);
        native.RenderContextSetUpdateCallback(renderContext, callback, 0);
    }

    private unsafe void RenderLatestFrame()
    {
        // SPEC §8.2 [API]: this thread calls mpv_render_* only. The callback merely sets renderSignal.
        if ((native.RenderContextUpdate(renderContext) & RenderUpdateFrame) == 0)
        {
            return;
        }

        VideoInfo current = Info;
        int width = current.Width > 0 ? current.Width : 1280;
        int height = current.Height > 0 ? current.Height : 720;
        long epoch = frames.SeekEpoch;
        if (!frames.TryBeginWrite(width, height, out int index, out byte[] pixels, out int stride))
        {
            int skip = 1;
            MpvRenderParam* skipParameters = stackalloc MpvRenderParam[2];
            skipParameters[0] = new MpvRenderParam(13, (nint)(&skip));
            skipParameters[1] = default;
            Check(native.RenderContextRender(renderContext, (nint)skipParameters), "mpv_render_context_render(skip)");
            return;
        }

        try
        {
            fixed (byte* pixelPointer = pixels)
            {
                int* size = stackalloc int[2] { width, height };
                nuint nativeStride = (nuint)stride;
                byte* format = stackalloc byte[] { (byte)'b', (byte)'g', (byte)'r', (byte)'0', 0 };
                MpvRenderParam* parameters = stackalloc MpvRenderParam[5];
                parameters[0] = new MpvRenderParam(RenderParamSoftwareSize, (nint)size);
                parameters[1] = new MpvRenderParam(RenderParamSoftwareFormat, (nint)format);
                parameters[2] = new MpvRenderParam(RenderParamSoftwareStride, (nint)(&nativeStride));
                parameters[3] = new MpvRenderParam(RenderParamSoftwarePointer, (nint)pixelPointer);
                parameters[4] = default;
                Check(native.RenderContextRender(renderContext, (nint)parameters), "mpv_render_context_render(sw)");

                // MPV SW "bgr0" leaves the fourth byte unspecified; Avalonia expects opaque BGRA.
                for (int row = 0; row < height; row++)
                {
                    int rowOffset = row * stride;
                    for (int column = 0; column < width; column++)
                    {
                        pixels[rowOffset + (column * 4) + 3] = byte.MaxValue;
                    }
                }
            }

            long sequence = Interlocked.Increment(ref sequenceNumber);
            if (frames.Publish(index, Position, sequence, epoch))
            {
                RaiseFrameReady();
            }
        }
        catch
        {
            frames.CancelWrite(index);
            throw;
        }
    }

    private void OnRenderUpdate(nint context)
    {
        // SPEC §8.2 [API]: the libmpv callback is signal-only. Do not call any mpv API here.
        renderSignal.Set();
    }

    private void RaiseFrameReady()
    {
        Action? handlers = FrameReady;
        if (handlers is null)
        {
            return;
        }

        foreach (Action handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch
            {
                // A UI subscriber must not terminate the dedicated native render thread.
            }
        }
    }

    private void PollState(object? state)
    {
        lock (controlGate)
        {
            if (Volatile.Read(ref stopping) || Volatile.Read(ref disposed))
            {
                return;
            }

        try
        {
            string? expectedPath = Volatile.Read(ref requestedPath);
            string? loadedPath = ReadStringProperty("path");
            if (expectedPath is not null && !PathsEqual(expectedPath, loadedPath))
            {
                return;
            }

            double? position = ReadDoubleProperty("playback-time");
                if (position.HasValue)
                {
                    Volatile.Write(ref positionSeconds, position.Value);
                }

                long? paused = ReadInt64Property("pause");
                if (paused.HasValue)
                {
                    Volatile.Write(ref playing, paused.Value == 0);
                }

                int width = checked((int)(ReadInt64Property("width") ?? 0));
                int height = checked((int)(ReadInt64Property("height") ?? 0));
                double duration = ReadDoubleProperty("duration") ?? 0;
                double nominalFps = ReadDoubleProperty("estimated-vf-fps") ??
                    ReadDoubleProperty("container-fps") ?? 0;
                if (width > 0 && height > 0 && duration > 0)
                {
                    Volatile.Write(ref info, new VideoInfo(width, height, TimeSpan.FromSeconds(duration), nominalFps));
                }
            }
            catch when (!Volatile.Read(ref stopping) && !Volatile.Read(ref disposed))
            {
                // Metadata may be unavailable while a file is opening or closing.
            }
        }
    }

    private void SetOption(string name, string value)
    {
        using Utf8String nativeName = new(name);
        using Utf8String nativeValue = new(value);
        lock (controlGate)
        {
            ThrowIfStoppingLocked();
            Check(native.SetOptionString(mpvHandle, nativeName.Pointer, nativeValue.Pointer), $"set option {name}");
        }
    }

    private void SetProperty(string name, string value)
    {
        using Utf8String nativeName = new(name);
        using Utf8String nativeValue = new(value);
        lock (controlGate)
        {
            ThrowIfStoppingLocked();
            Check(native.SetPropertyString(mpvHandle, nativeName.Pointer, nativeValue.Pointer), $"set property {name}");
        }
    }

    private unsafe double? ReadDoubleProperty(string name)
    {
        using Utf8String nativeName = new(name);
        double value = 0;
        lock (controlGate)
        {
            ThrowIfStoppingLocked();
            int result = native.GetProperty(mpvHandle, nativeName.Pointer, MpvFormatDouble, (nint)(&value));
            return result >= 0 ? value : null;
        }
    }

    private unsafe long? ReadInt64Property(string name)
    {
        using Utf8String nativeName = new(name);
        long value = 0;
        lock (controlGate)
        {
            ThrowIfStoppingLocked();
            int result = native.GetProperty(mpvHandle, nativeName.Pointer, MpvFormatInt64, (nint)(&value));
            if (result < 0 && name == "pause")
            {
                int flag = 0;
                result = native.GetProperty(mpvHandle, nativeName.Pointer, MpvFormatFlag, (nint)(&flag));
                return result >= 0 ? flag : null;
            }

            return result >= 0 ? value : null;
        }
    }

    private string? ReadStringProperty(string name)
    {
        using Utf8String nativeName = new(name);
        lock (controlGate)
        {
            ThrowIfStoppingLocked();
            nint value = native.GetPropertyString(mpvHandle, nativeName.Pointer);
            if (value == 0)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUTF8(value);
            }
            finally
            {
                native.Free(value);
            }
        }
    }

    private void InvokeCommand(params string[] arguments)
    {
        nint[] strings = new nint[arguments.Length];
        nint array = 0;
        try
        {
            for (int index = 0; index < arguments.Length; index++)
            {
                strings[index] = Marshal.StringToCoTaskMemUTF8(arguments[index]);
            }

            array = Marshal.AllocHGlobal((arguments.Length + 1) * nint.Size);
            for (int index = 0; index < arguments.Length; index++)
            {
                Marshal.WriteIntPtr(array, index * nint.Size, strings[index]);
            }

            Marshal.WriteIntPtr(array, arguments.Length * nint.Size, 0);
            lock (controlGate)
            {
                ThrowIfStoppingLocked();
                Check(native.Command(mpvHandle, array), $"command {arguments[0]}");
            }
        }
        finally
        {
            if (array != 0)
            {
                Marshal.FreeHGlobal(array);
            }

            foreach (nint value in strings)
            {
                if (value != 0)
                {
                    Marshal.FreeCoTaskMem(value);
                }
            }
        }
    }

    private void ThrowIfStopping()
    {
        lock (controlGate)
        {
            ThrowIfStoppingLocked();
        }
    }

    private void ThrowIfStoppingLocked()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref stopping) || Volatile.Read(ref disposed),
            this);
    }

    private void Check(int result, string operation)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"{operation} failed: {native.GetError(result)} ({result}).");
        }
    }

    private static string GetApiVersionText(ulong version)
        => $"client API {version >> 16}.{version & 0xffff}";

    private static bool PathsEqual(string expectedPath, string? loadedPath)
    {
        if (string.IsNullOrWhiteSpace(loadedPath))
        {
            return false;
        }

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(expectedPath), Path.GetFullPath(loadedPath), comparison);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct MpvRenderParam(int Type, nint Data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MpvRenderUpdateCallback(nint context);

    private sealed class Utf8String : IDisposable
    {
        public Utf8String(string value)
        {
            Pointer = Marshal.StringToCoTaskMemUTF8(value);
        }

        public nint Pointer { get; }
        public void Dispose() => Marshal.FreeCoTaskMem(Pointer);
    }
}
