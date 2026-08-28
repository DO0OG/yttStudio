using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace YttStudio.Video;

/// <summary>libmpv 소프트웨어 렌더 컨텍스트를 만들고 프레임을 받아 온다.</summary>
/// <remarks>
/// 이 안의 함수들은 전용 렌더 스레드에서만 돈다. mpv_render_* 는 그 스레드에서만 부를 수
/// 있고, 콜백은 신호만 남긴다.
/// </remarks>
public sealed partial class MpvVideoSource
{
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
}
