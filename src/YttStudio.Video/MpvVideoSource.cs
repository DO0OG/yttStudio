using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace YttStudio.Video;

/// <summary>네이티브 자식 창을 붙이지 않고 콜백 렌더링으로 libmpv 재생을 제공한다.</summary>
public sealed partial class MpvVideoSource : IVideoSource
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
    private readonly nint mpvHandle;
    private readonly LatestFrameBuffer frames = new();
    private readonly MpvLoadGate loadGate = new();
    private readonly AutoResetEvent renderSignal = new(false);
    private readonly ManualResetEventSlim renderReady = new(false);
    private readonly Thread renderThread;
    private readonly Timer stateTimer;
    private readonly MpvRenderUpdateCallback renderUpdateCallback;
    private readonly string? ytdlpPath;
    private nint renderContext;
    private Exception? renderFailure;
    private bool renderStopped;
    private Task? disposeTask;
    private VideoInfo info = new(0, 0, TimeSpan.Zero, 0);
    private double positionSeconds;
    private string? requestedPath;
    private int requestedSourceKind = (int)VideoSourceKind.LocalFile;
    private long sequenceNumber;
    private bool playing;
    private bool stopping;
    private bool disposed;

    // TryCreate 를 통해 생성해 탐색과 초기화 실패를 보고할 수 있게 한다.
    // 생성자에서 던지지 않는다. internal 이라 공개 API 에는 드러나지 않는다.
    internal MpvVideoSource(MpvNativeLibrary native, string? ytdlpPath = null)
    {
        this.native = native;
        this.ytdlpPath = ytdlpPath;
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
            SetOption("ytdl", "yes");
            if (!string.IsNullOrWhiteSpace(ytdlpPath))
            {
                SetOption("script-opts", $"ytdl_hook-ytdl_path={ytdlpPath}");
            }
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

    /// <summary>초기화 이후 렌더 스레드가 끝난 실패를 알린다.</summary>
    /// <remarks>이 신호가 오면 더 이상 프레임이 오지 않는다. 재생을 되살릴 방법은 없다.</remarks>
    public event Action<Exception>? RenderFailed;

    /// <summary>렌더 스레드가 실패로 끝났는지 가져온다.</summary>
    public bool IsRenderStopped => Volatile.Read(ref renderStopped);

    public VideoInfo Info => Volatile.Read(ref info);
    public TimeSpan Position => TimeSpan.FromSeconds(Math.Max(0, Volatile.Read(ref positionSeconds)));
    public bool IsPlaying => Volatile.Read(ref playing);
    public string LibraryVersion { get; }
    public string LibraryPath => native.LoadedPath;

    /// <summary>libmpv ytdl 훅에 전달한 yt-dlp 경로를 가져온다.</summary>
    public string? YtDlpPath => ytdlpPath;

    /// <summary>
    /// 크래시 보고서에 쓸 네이티브 라이브러리 설명 한 줄을 가져온다.
    /// </summary>
    public string CrashMetadata { get; private set; } = string.Empty;

    /// <summary>탐색으로 호환되는 네이티브 라이브러리를 찾으면 libmpv 소스를 만든다.</summary>
    public static bool TryCreate(out MpvVideoSource? source, out string diagnostic)
        => TryCreate(out source, out diagnostic, YtDlpLocator.Find());

    /// <summary>지정한 yt-dlp 경로를 사용해 libmpv 소스를 만든다.</summary>
    public static bool TryCreate(
        out MpvVideoSource? source,
        out string diagnostic,
        string? ytdlpPath)
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

            source = new MpvVideoSource(library, ytdlpPath);
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
        (VideoSourceKind sourceKind, string sourceAddress) = CreateSourceRequest(path);
        long requestedGeneration;
        lock (controlGate)
        {
            ThrowIfStoppingLocked();
            DrainMpvEventsLocked();
            requestedGeneration = loadGate.BeginLoad();
            Volatile.Write(ref requestedSourceKind, (int)sourceKind);
            Volatile.Write(ref requestedPath, sourceAddress);
            Volatile.Write(ref info, new VideoInfo(0, 0, TimeSpan.Zero, 0));
            Volatile.Write(ref positionSeconds, 0);
            Volatile.Write(ref playing, false);
            frames.BeginSeek();
            InvokeCommand("loadfile", sourceAddress, "replace");
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfStopping();
            PollState(null);
            if (TryCompleteLoad(requestedGeneration))
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException("libmpv did not expose video metadata within 15 seconds.");
    }

    private bool TryCompleteLoad(long requestedGeneration)
    {
        lock (controlGate)
        {
            if (Volatile.Read(ref stopping) || Volatile.Read(ref disposed) ||
                !loadGate.IsLoaded(requestedGeneration))
            {
                return false;
            }

            VideoInfo current = Info;
            if (current.Width <= 0 || current.Height <= 0 || current.Duration <= TimeSpan.Zero)
            {
                return false;
            }

            Pause();
            return true;
        }
    }

    private static (VideoSourceKind Kind, string Address) CreateSourceRequest(string path)
    {
        bool hasHttpScheme = path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (hasHttpScheme)
        {
            if (!YouTubeUrlValidator.TryValidate(path, out Uri? uri, out string? error))
            {
                throw YouTubePlaybackException.InvalidUrl(error ?? "YouTube 주소가 올바르지 않습니다.");
            }

            return (VideoSourceKind.YouTubeUrl, uri!.AbsoluteUri);
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Video file was not found.", path);
        }

        return (VideoSourceKind.LocalFile, Path.GetFullPath(path));
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
            // 초기화가 끝난 뒤에 터진 실패는 생성자가 읽지 않는다. 그대로 두면 렌더 스레드만
            // 조용히 끝나고 타이머와 재생 상태는 살아 있어, 사용자에게는 화면이 멎은 것으로만
            // 보인다. 밖에서 알 수 있게 표시하고 알린다.
            if (renderReady.IsSet)
            {
                Volatile.Write(ref renderStopped, true);
                Volatile.Write(ref playing, false);
                RaiseRenderFailed(exception);
            }
        }
        finally
        {
            CleanupRenderContext();
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
            ProcessMpvEventsLocked();
            if (!loadGate.IsLoaded(loadGate.Generation))
            {
                return;
            }

            string? expectedPath = Volatile.Read(ref requestedPath);
            string? loadedPath = ReadStringProperty("path");
            if (IsLocalPathRequest() && expectedPath is not null && !PathsEqual(expectedPath, loadedPath))
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

    private bool IsLocalPathRequest()
        => (VideoSourceKind)Volatile.Read(ref requestedSourceKind) == VideoSourceKind.LocalFile;

    private void DrainMpvEventsLocked()
    {
        while (native.ReadEvent(mpvHandle).EventId is not MpvEventId.None)
        {
        }
    }

    private void ProcessMpvEventsLocked()
    {
        while (true)
        {
            MpvEvent current = native.ReadEvent(mpvHandle);
            if (current.EventId == MpvEventId.None)
            {
                return;
            }

            loadGate.Observe(current.EventId);
        }
    }

    /// <inheritdoc />
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
