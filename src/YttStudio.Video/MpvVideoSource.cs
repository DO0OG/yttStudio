using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace YttStudio.Video;

/// <summary>네이티브 자식 창을 붙이지 않고 콜백 렌더링으로 libmpv 재생을 제공한다.</summary>
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
    private int playbackScaleDivisor = 1;
    private long seekCount;
    private long renderedFrameCount;
    private long skippedFrameCount;
    private long renderTicks;
    private long alphaTicks;
    private long pixelBytes;
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

    // TryCreate 를 통해 생성해 탐색과 초기화 실패를 보고할 수 있게 한다.
    // 생성자에서 던지지 않는다. internal 이라 공개 API 에는 드러나지 않는다.
    internal MpvVideoSource(MpvNativeLibrary native)
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
            // [API] 콜백 렌더링이 필수다. 네이티브 창 핸들을 붙이지 않는다.
            SetOption("vo", "libmpv");
            SetOption("audio-display", "no");
            SetOption("keep-open", "yes");
            SetOption("pause", "yes");
            // 디코딩을 GPU 로 넘긴다. 화면 합성은 여전히 소프트웨어 렌더 API 로 받으므로
            // 프레임을 시스템 메모리로 되돌리는 copy-back 방식이어야 한다. 쓸 수 있는
            // 하드웨어 디코더가 없으면 mpv 가 소프트웨어 디코딩으로 되돌아간다.
            SetOption("hwdec", "auto-copy");
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
            CleanupAfterInitializationFailure();
            throw;
        }
    }

    private void CleanupAfterInitializationFailure()
    {
        if (renderThread?.IsAlive == true)
        {
            Volatile.Write(ref stopping, true);
            renderSignal.Set();
            renderThread.Join();
        }

        native.TerminateDestroy(mpvHandle);
        native.Dispose();
    }

    public event Action? FrameReady;

    public VideoInfo Info => Volatile.Read(ref info);
    public TimeSpan Position => TimeSpan.FromSeconds(Math.Max(0, Volatile.Read(ref positionSeconds)));
    public bool IsPlaying => Volatile.Read(ref playing);
    public string LibraryVersion { get; }
    public string LibraryPath => native.LoadedPath;

    /// <summary>
    /// 크래시 보고서에 쓸 네이티브 라이브러리 설명 한 줄을 가져온다.
    /// </summary>
    public string CrashMetadata { get; private set; } = string.Empty;

    /// <summary>탐색으로 호환되는 네이티브 라이브러리를 찾으면 libmpv 소스를 만든다.</summary>
    public static bool TryCreate(out MpvVideoSource? source, out string diagnostic)
    {
        if (!MpvNativeLibrary.TryLoad(out MpvNativeLibrary? library, out diagnostic))
        {
            source = null;
            return false;
        }

        try
        {
            // render API 를 건드리기 전에 client API 버전을 검사한다. 그래야
            // 지원하지 않는 빌드가 나중에 크래시하지 않고 조치 가능한 메시지로 실패한다.
            uint apiVersion = (uint)library!.ClientApiVersion();
            if (!MpvCompatibility.IsSupported(apiVersion))
            {
                diagnostic = MpvCompatibility.DescribeUnsupported(apiVersion, library.LoadedPath);
                library.Dispose();
                source = null;
                return false;
            }

            source = new MpvVideoSource(library);
            // 크래시 보고서에서 libmpv 빌드를 되짚을 수 있어야 한다.
            source.CrashMetadata = MpvCompatibility.DescribeForCrashLog(apiVersion, library.LoadedPath);
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
                Pause();
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
            Interlocked.Increment(ref seekCount);
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

    public void SetVolume(double volume)
    {
        ThrowIfStopping();
        if (!double.IsFinite(volume) || volume is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be from 0 through 100.");
        }

        SetProperty("volume", volume.ToString("R", CultureInfo.InvariantCulture));
    }

    public void SetMuted(bool muted)
    {
        ThrowIfStopping();
        SetProperty("mute", muted ? "yes" : "no");
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

            // [API] 콜백 해제, RenderThread 조인, mpv 코어 파괴 순서를 지킨다.
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
            CleanupRenderContext();
        }
    }

    private void CleanupRenderContext()
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

        // 정리 과정도 실패하면 원래의 렌더 루프 실패를 보존한다.
        renderFailure ??= cleanupFailure;
        renderReady.Set();
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
        // [API] 이 스레드는 mpv_render_* 만 호출한다. 콜백은 renderSignal 만 설정한다.
        if ((native.RenderContextUpdate(renderContext) & RenderUpdateFrame) == 0)
        {
            return;
        }

        VideoInfo current = Info;
        // 배수를 나눠 더 작은 화면으로 받는다. 디코딩 뒤의 변환 · 전송 · 알파 채우기 ·
        // 화면 합성이 전부 이 크기를 따르므로 부하가 배수의 제곱에 가깝게 줄어든다.
        int divisor = Math.Max(1, Volatile.Read(ref playbackScaleDivisor));
        int width = Math.Max(1, (current.Width > 0 ? current.Width : 1280) / divisor);
        int height = Math.Max(1, (current.Height > 0 ? current.Height : 720) / divisor);
        long epoch = frames.SeekEpoch;
        if (!frames.TryBeginWrite(width, height, out int index, out byte[] pixels, out int stride))
        {
            RenderSkippedFrame();
            return;
        }

        try
        {
            RenderSoftwareFrame(width, height, stride, pixels);

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

    private unsafe void RenderSkippedFrame()
    {
        Interlocked.Increment(ref skippedFrameCount);
        int skip = 1;
        MpvRenderParam* skipParameters = stackalloc MpvRenderParam[2];
        skipParameters[0] = new MpvRenderParam(13, (nint)(&skip));
        skipParameters[1] = default;
        Check(native.RenderContextRender(renderContext, (nint)skipParameters), "mpv_render_context_render(skip)");
    }

    private unsafe void RenderSoftwareFrame(int width, int height, int stride, byte[] pixels)
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
            long startedAt = Stopwatch.GetTimestamp();
            Check(native.RenderContextRender(renderContext, (nint)parameters), "mpv_render_context_render(sw)");
            long renderedAt = Stopwatch.GetTimestamp();

            // mpv SW 의 "bgr0" 은 네 번째 바이트를 정의하지 않는다. Avalonia 는 불투명
            // BGRA 를 기대하므로 알파를 채워야 한다. 다만 화소마다 바이트 하나씩 쓰면
            // 1080p 기준 프레임당 이백만 번이 넘어 재생 내내 렌더 스레드를 붙잡는다.
            // 네 바이트를 한 낱말로 묶어 SIMD 로 알파 비트만 세운다.
            FillOpaqueAlpha(pixelPointer, width, height, stride);
            long filledAt = Stopwatch.GetTimestamp();

            Interlocked.Add(ref renderTicks, renderedAt - startedAt);
            Interlocked.Add(ref alphaTicks, filledAt - renderedAt);
            Interlocked.Add(ref pixelBytes, (long)height * stride);
            Interlocked.Increment(ref renderedFrameCount);
        }
    }

    private void OnRenderUpdate(nint context)
    {
        // [API] libmpv 콜백은 신호 전용이다. 여기서 어떤 mpv API 도 호출하지 마라.
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
                // UI 구독자가 전용 네이티브 렌더 스레드를 끝내면 안 된다.
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
                // 파일을 열거나 닫는 동안에는 메타데이터가 없을 수 있다.
            }
        }
    }

    /// <inheritdoc />
    /// <summary>렌더 경로가 지금까지 한 일을 읽는다.</summary>
    /// <remarks>
    /// 구간의 비용을 알고 싶으면 앞뒤에서 한 번씩 읽어 <see cref="VideoRenderDiagnostics.Since"/>
    /// 로 빼라. 각 값은 원자적으로 읽지만 서로 같은 순간의 값은 아니다. 부하의 크기를 가늠하는
    /// 용도이지 회계 장부가 아니다.
    /// </remarks>
    public VideoRenderDiagnostics ReadDiagnostics()
        => new(
            Interlocked.Read(ref seekCount),
            Interlocked.Read(ref renderedFrameCount),
            Interlocked.Read(ref skippedFrameCount),
            TicksToMilliseconds(Interlocked.Read(ref renderTicks)),
            TicksToMilliseconds(Interlocked.Read(ref alphaTicks)),
            Interlocked.Read(ref pixelBytes));

    private static double TicksToMilliseconds(long ticks)
        => ticks * 1000.0 / Stopwatch.Frequency;

    public int PlaybackScaleDivisor
    {
        get => Volatile.Read(ref playbackScaleDivisor);
        set => Volatile.Write(ref playbackScaleDivisor, Math.Clamp(value, 1, 8));
    }

    /// <summary>BGRA 버퍼의 알파 바이트를 한꺼번에 불투명으로 세운다.</summary>
    /// <remarks>
    /// 화소를 <c>uint</c> 한 낱말로 보고 최상위 바이트에만 비트를 세운다. 하드웨어가
    /// 지원하면 여러 화소를 한 번에 처리하므로, 바이트 단위로 훑던 예전 방식보다 반복
    /// 횟수가 크게 줄어든다. 색 성분은 건드리지 않는다.
    /// </remarks>
    private static unsafe void FillOpaqueAlpha(byte* pixels, int width, int height, int stride)
    {
        const uint OpaqueAlpha = 0xFF000000u;
        Vector<uint> mask = new(OpaqueAlpha);
        int lanes = Vector<uint>.Count;
        for (int row = 0; row < height; row++)
        {
            uint* line = (uint*)(pixels + (row * stride));
            Span<uint> span = new(line, width);
            int index = 0;
            if (Vector.IsHardwareAccelerated)
            {
                for (; index <= width - lanes; index += lanes)
                {
                    Span<uint> chunk = span.Slice(index, lanes);
                    (new Vector<uint>(chunk) | mask).CopyTo(chunk);
                }
            }

            for (; index < width; index++)
            {
                line[index] |= OpaqueAlpha;
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
