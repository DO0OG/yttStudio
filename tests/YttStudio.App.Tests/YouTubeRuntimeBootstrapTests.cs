using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;

namespace YttStudio.App.Tests;

public sealed class YouTubeRuntimeBootstrapTests
{
    [Fact]
    public void ManagedDenoInstallerPinsSupportedRuntime()
    {
        Type? installerType = typeof(MainWindowViewModel).Assembly.GetType("YttStudio.App.DenoAutoInstaller");

        Assert.NotNull(installerType);
        FieldInfo? pinnedVersion = installerType!.GetField(
            "PinnedVersion",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo? ensureAvailable = installerType.GetMethod(
            "EnsureAvailableAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(pinnedVersion);
        Assert.Equal("2.9.6", pinnedVersion!.GetRawConstantValue());
        Assert.NotNull(ensureAvailable);
    }

    [Fact]
    public void MainWindowOwnsManagedDenoInstallerForYouTubeBootstrap()
    {
        FieldInfo? field = typeof(MainWindowViewModel).GetField(
            "YouTubeDenoInstaller",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.Equal("DenoAutoInstaller", field!.FieldType.Name);
    }

    [Fact]
    public async Task ManagedDenoDownloadReleasesArchiveBeforeHashVerification()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            return;
        }

        Type installerType = typeof(MainWindowViewModel).Assembly.GetType("YttStudio.App.DenoAutoInstaller")!;
        object asset = installerType
            .GetMethod("GetCurrentAsset", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, null)!;
        long assetLength = (long)asset.GetType().GetProperty("AssetLength")!.GetValue(asset)!;

        string workspace = Path.Combine(Path.GetTempPath(), $"ytt-deno-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            using HttpClient httpClient = new(new FixedLengthPayloadHandler(assetLength));
            object installer = Activator.CreateInstance(
                installerType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: [httpClient, workspace],
                culture: null)!;

            Task download = (Task)installerType
                .GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(installer, [asset, Path.Combine(workspace, "deno.zip"), CancellationToken.None])!;

            // 다운로드 핸들이 닫히지 않으면 해시 검증 단계에서 파일 잠금 IOException이 먼저 발생한다.
            InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(() => download);
            Assert.Contains("SHA-256", failure.Message);
        }
        finally
        {
            try
            {
                Directory.Delete(workspace, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManagedYtDlpAcceptsExistingBinaryOnlyWhenJsRuntimesIsSupported(bool advertisesOption)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Type installerType = typeof(MainWindowViewModel).Assembly.GetType("YttStudio.App.YtDlpAutoInstaller")!;
        MethodInfo probe = installerType.GetMethod(
            "SupportsRequiredOptionsAsync",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        string workspace = Path.Combine(Path.GetTempPath(), $"ytt-ytdlp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            string stub = Path.Combine(workspace, "yt-dlp.cmd");
            string helpLine = advertisesOption
                ? "    --js-runtimes NAME[:PATH]  JavaScript runtimes to use"
                : "    --no-playlist              Download only the video";
            await File.WriteAllLinesAsync(
                stub,
                ["@echo off", $"echo {helpLine}", "exit /b 0"],
                TestContext.Current.CancellationToken);

            Task<bool> supported = (Task<bool>)probe.Invoke(null, [stub, CancellationToken.None])!;
            Assert.Equal(advertisesOption, await supported);
        }
        finally
        {
            try
            {
                Directory.Delete(workspace, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class FixedLengthPayloadHandler(long length) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StreamContent(new ZeroStream(length)),
            };
            response.Content.Headers.ContentLength = length;
            return Task.FromResult(response);
        }
    }

    private sealed class ZeroStream(long length) : Stream
    {
        private long position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int available = (int)Math.Min(count, length - position);
            if (available <= 0)
            {
                return 0;
            }

            Array.Clear(buffer, offset, available);
            position += available;
            return available;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
