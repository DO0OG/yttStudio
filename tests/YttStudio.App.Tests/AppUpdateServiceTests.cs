using System.Net;
using System.Text;
using YttStudio.App;

namespace YttStudio.App.Tests;

public sealed class AppUpdateServiceTests
{
    [Fact]
    public void SemanticVersionUsesSemVerPrereleasePrecedence()
    {
        string[] ordered =
        [
            "1.0.0-alpha",
            "1.0.0-alpha.1",
            "1.0.0-alpha.beta",
            "1.0.0-beta",
            "1.0.0-beta.2",
            "1.0.0-beta.11",
            "1.0.0-rc.1",
            "1.0.0",
        ];

        for (int index = 1; index < ordered.Length; index++)
        {
            Assert.True(
                SemanticVersion.Parse(ordered[index - 1]) < SemanticVersion.Parse(ordered[index]),
                $"{ordered[index - 1]} < {ordered[index]}");
        }
    }

    [Fact]
    public void SemanticVersionIgnoresBuildMetadataForPrecedence()
    {
        SemanticVersion first = SemanticVersion.Parse("v1.2.3+build.1");
        SemanticVersion second = SemanticVersion.Parse("1.2.3+build.2");

        Assert.Equal(first, second);
        Assert.Equal("1.2.3+build.1", first.ToString());
        Assert.False(SemanticVersion.TryParse("v1.2.03", out _));
        Assert.False(SemanticVersion.TryParseTag("1.2.3", out _));
        Assert.False(SemanticVersion.TryParseTag("v1.2.3-01", out _));
    }

    [Theory]
    [InlineData("win-x64", "yttStudio-v0.2.0-win-x64-setup.exe")]
    [InlineData("osx-arm64", "yttStudio-v0.2.0-osx-arm64.dmg")]
    [InlineData("linux-x64", "yttStudio-v0.2.0-linux-x86_64.AppImage")]
    public async Task CheckForUpdateSelectsThePreferredPlatformAsset(
        string runtimeIdentifier,
        string expectedAssetName)
    {
        string alternateAssetName = runtimeIdentifier switch
        {
            "win-x64" => "yttStudio-v0.2.0-win-x64.zip",
            "osx-arm64" => "yttStudio-v0.2.0-osx-arm64.tar.gz",
            _ => "yttStudio-v0.2.0-linux-x64.tar.gz",
        };
        RecordingHandler handler = new(_ => ReleaseResponse(
            "v0.2.0",
            AssetJson(alternateAssetName),
            AssetJson(expectedAssetName)));
        using HttpClient httpClient = new(handler);
        AppUpdateService service = new(httpClient, runtimeIdentifier);

        AppUpdateCheckResult result = await service.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsUpdateAvailable);
        Assert.NotNull(result.SelectedAsset);
        Assert.Equal(expectedAssetName, result.SelectedAsset!.Name);
        Assert.Equal("https://github.com/DO0OG/yttStudio/releases/download/v0.2.0/" + expectedAssetName,
            result.SelectedAsset.DownloadUri.ToString());
        AssertRequestHeaders(handler.Requests.Single());
    }

    [Theory]
    [InlineData("win-x64", "Portable", "yttStudio-v0.2.0-win-x64.zip")]
    [InlineData("osx-arm64", "TarGz", "yttStudio-v0.2.0-osx-arm64.tar.gz")]
    [InlineData("linux-x64", "TarGz", "yttStudio-v0.2.0-linux-x64.tar.gz")]
    public async Task CheckForUpdateSelectsTheAssetMatchingTheExecutionForm(
        string runtimeIdentifier,
        string executionFormName,
        string expectedAssetName)
    {
        RecordingHandler handler = new(_ => ReleaseResponse(
            "v0.2.0",
            AssetJson(expectedAssetName)));
        using HttpClient httpClient = new(handler);
        AppUpdateService service = new(
            httpClient,
            runtimeIdentifier,
            Enum.Parse<AppUpdateExecutionForm>(executionFormName));

        AppUpdateCheckResult result = await service.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(expectedAssetName, result.SelectedAsset?.Name);
    }

    [Fact]
    public async Task CheckForUpdateReturnsNoUpdateForTheCurrentVersion()
    {
        RecordingHandler handler = new(_ => ReleaseResponse("v0.1.0"));
        using HttpClient httpClient = new(handler);
        AppUpdateService service = new(httpClient, "win-x64");

        AppUpdateCheckResult result = await service.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.SelectedAsset);
        Assert.Equal("0.1.0", result.CurrentVersion);
        Assert.Equal("0.1.0", result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdateWrapsNetworkErrors()
    {
        HttpRequestException networkError = new("fake network failure");
        RecordingHandler handler = new(_ => throw networkError);
        using HttpClient httpClient = new(handler);
        AppUpdateService service = new(httpClient, "win-x64");

        AppUpdateException exception = await Assert.ThrowsAsync<AppUpdateException>(
            () => service.CheckForUpdateAsync(TestContext.Current.CancellationToken));

        Assert.Equal(AppUpdateErrorKind.Network, exception.Kind);
        Assert.Same(networkError, exception.InnerException);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData((HttpStatusCode)429)]
    public async Task CheckForUpdateReportsRateLimit(HttpStatusCode statusCode)
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(statusCode));
        using HttpClient httpClient = new(handler);
        AppUpdateService service = new(httpClient, "win-x64");

        AppUpdateException exception = await Assert.ThrowsAsync<AppUpdateException>(
            () => service.CheckForUpdateAsync(TestContext.Current.CancellationToken));

        Assert.Equal(AppUpdateErrorKind.RateLimited, exception.Kind);
    }

    [Fact]
    public async Task CheckForUpdateReportsMissingPlatformAsset()
    {
        RecordingHandler handler = new(_ => ReleaseResponse(
            "v0.2.0",
            AssetJson("yttStudio-v0.2.0-win-arm64.zip")));
        using HttpClient httpClient = new(handler);
        AppUpdateService service = new(httpClient, "win-x64");

        AppUpdateException exception = await Assert.ThrowsAsync<AppUpdateException>(
            () => service.CheckForUpdateAsync(TestContext.Current.CancellationToken));

        Assert.Equal(AppUpdateErrorKind.AssetNotFound, exception.Kind);
    }

    [Fact]
    public async Task CheckForUpdateReportsInvalidReleaseTag()
    {
        RecordingHandler handler = new(_ => ReleaseResponse("release-0.2.0"));
        using HttpClient httpClient = new(handler);
        AppUpdateService service = new(httpClient, "win-x64");

        AppUpdateException exception = await Assert.ThrowsAsync<AppUpdateException>(
            () => service.CheckForUpdateAsync(TestContext.Current.CancellationToken));

        Assert.Equal(AppUpdateErrorKind.InvalidMetadata, exception.Kind);
    }

    [Fact]
    public async Task CheckForUpdateReportsInvalidSelectedAssetMetadata()
    {
        RecordingHandler handler = new(_ => ReleaseResponse(
            "v0.2.0",
            "{\"name\":\"yttStudio-v0.2.0-win-x64-setup.exe\",\"browser_download_url\":\"not-a-url\"}"));
        using HttpClient httpClient = new(handler);
        AppUpdateService service = new(httpClient, "win-x64");

        AppUpdateException exception = await Assert.ThrowsAsync<AppUpdateException>(
            () => service.CheckForUpdateAsync(TestContext.Current.CancellationToken));

        Assert.Equal(AppUpdateErrorKind.InvalidMetadata, exception.Kind);
    }

    [Fact]
    public async Task DownloadStreamsToTheDestinationAndReportsProgress()
    {
        byte[] expected = Encoding.UTF8.GetBytes("yttStudio update payload");
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expected),
        });
        using HttpClient httpClient = new(handler);
        AppUpdateService service = new(httpClient, "win-x64");
        AppUpdateAsset asset = new(
            "yttStudio-v0.2.0-win-x64.zip",
            new Uri("https://github.com/DO0OG/yttStudio/releases/download/v0.2.0/update.zip"),
            expected.Length);
        string destinationDirectory = Path.Combine(Path.GetTempPath(), "yttStudio-update-tests", Guid.NewGuid().ToString("N"));
        RecordingProgress progress = new();

        try
        {
            Directory.CreateDirectory(destinationDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(destinationDirectory, asset.Name),
                "old payload",
                TestContext.Current.CancellationToken);

            string path = await service.DownloadAsync(
                asset,
                destinationDirectory,
                progress,
                TestContext.Current.CancellationToken);

            Assert.Equal(Path.Combine(destinationDirectory, asset.Name), path);
            Assert.Equal(expected, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.NotEmpty(progress.Values);
            Assert.Equal(0, progress.Values[0].BytesTransferred);
            Assert.Equal(expected.Length, progress.Values[^1].BytesTransferred);
            Assert.Equal(expected.Length, progress.Values[^1].TotalBytes);
            Assert.Equal(1, progress.Values[^1].Fraction);
            Assert.Single(Directory.GetFiles(destinationDirectory));
            AssertRequestHeaders(handler.Requests.Single());
        }
        finally
        {
            if (Directory.Exists(destinationDirectory))
            {
                Directory.Delete(destinationDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadRejectsContentLengthMismatchWithoutLeavingAnIncompleteFile()
    {
        byte[] payload = Encoding.UTF8.GetBytes("short payload");
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        };
        response.Content.Headers.ContentLength = payload.Length + 1;
        RecordingHandler handler = new(_ => response);
        using HttpClient httpClient = new(handler);
        AppUpdateService service = new(httpClient, "win-x64");
        AppUpdateAsset asset = new(
            "yttStudio-v0.2.0-win-x64.zip",
            new Uri("https://github.com/DO0OG/yttStudio/releases/download/v0.2.0/update.zip"),
            payload.Length);
        string destinationDirectory = CreateTemporaryDirectory();

        try
        {
            AppUpdateException exception = await Assert.ThrowsAsync<AppUpdateException>(
                () => service.DownloadAsync(
                    asset,
                    destinationDirectory,
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(AppUpdateErrorKind.DownloadFailed, exception.Kind);
            Assert.Empty(Directory.GetFiles(destinationDirectory));
        }
        finally
        {
            DeleteTemporaryDirectory(destinationDirectory);
        }
    }

    [Fact]
    public async Task DownloadRejectsBodyShorterThanAssetSizeWhenContentLengthIsMissing()
    {
        byte[] payload = Encoding.UTF8.GetBytes("short payload");
        using NonSeekableReadStream stream = new(payload);
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
        });
        using HttpClient httpClient = new(handler);
        AppUpdateService service = new(httpClient, "win-x64");
        AppUpdateAsset asset = new(
            "yttStudio-v0.2.0-win-x64.zip",
            new Uri("https://github.com/DO0OG/yttStudio/releases/download/v0.2.0/update.zip"),
            payload.Length + 1);
        string destinationDirectory = CreateTemporaryDirectory();

        try
        {
            AppUpdateException exception = await Assert.ThrowsAsync<AppUpdateException>(
                () => service.DownloadAsync(
                    asset,
                    destinationDirectory,
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(AppUpdateErrorKind.DownloadFailed, exception.Kind);
            Assert.Empty(Directory.GetFiles(destinationDirectory));
        }
        finally
        {
            DeleteTemporaryDirectory(destinationDirectory);
        }
    }

    [Fact]
    public async Task DownloadCancellationDeletesTheTemporaryFileAndLeavesNoFinalFile()
    {
        byte[] payload = Encoding.UTF8.GetBytes("cancelled payload");
        using CancellationAfterFirstReadStream stream = new(payload);
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
        });
        using HttpClient httpClient = new(handler);
        AppUpdateService service = new(httpClient, "win-x64");
        AppUpdateAsset asset = new(
            "yttStudio-v0.2.0-win-x64.zip",
            new Uri("https://github.com/DO0OG/yttStudio/releases/download/v0.2.0/update.zip"),
            null);
        string destinationDirectory = CreateTemporaryDirectory();
        using CancellationTokenSource cancellation = new();

        try
        {
            Task<string> download = service.DownloadAsync(
                asset,
                destinationDirectory,
                cancellationToken: cancellation.Token);
            await stream.FirstRead.Task.WaitAsync(TestContext.Current.CancellationToken);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await download);
            Assert.Empty(Directory.GetFiles(destinationDirectory));
        }
        finally
        {
            DeleteTemporaryDirectory(destinationDirectory);
        }
    }

    [Fact]
    public async Task DownloadReadFailureDeletesTheGeneratedTemporaryFile()
    {
        byte[] payload = Encoding.UTF8.GetBytes("payload before read failure");
        using ThrowingAfterFirstReadStream stream = new(payload);
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
        });
        using HttpClient httpClient = new(handler);
        AppUpdateService service = new(httpClient, "win-x64");
        AppUpdateAsset asset = new(
            "yttStudio-v0.2.0-win-x64.zip",
            new Uri("https://github.com/DO0OG/yttStudio/releases/download/v0.2.0/update.zip"),
            null);
        string destinationDirectory = CreateTemporaryDirectory();

        try
        {
            AppUpdateException exception = await Assert.ThrowsAsync<AppUpdateException>(
                () => service.DownloadAsync(
                    asset,
                    destinationDirectory,
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(AppUpdateErrorKind.DownloadFailed, exception.Kind);
            Assert.Empty(Directory.GetFiles(destinationDirectory));
        }
        finally
        {
            DeleteTemporaryDirectory(destinationDirectory);
        }
    }

    [Fact]
    public void UnsupportedRuntimeIdentifierIsRejected()
    {
        using HttpClient httpClient = new(new RecordingHandler(_ => ReleaseResponse("v0.2.0")));

        AppUpdateException exception = Assert.Throws<AppUpdateException>(
            () => new AppUpdateService(httpClient, "linux-arm64"));

        Assert.Equal(AppUpdateErrorKind.UnsupportedPlatform, exception.Kind);
    }

    private static HttpResponseMessage ReleaseResponse(string tagName, params string[] assets)
    {
        string assetJson = string.Join(',', assets);
        string json = $"{{\"tag_name\":\"{tagName}\",\"assets\":[{assetJson}]}}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static string AssetJson(string name)
        => $"{{\"name\":\"{name}\",\"browser_download_url\":\"https://github.com/DO0OG/yttStudio/releases/download/v0.2.0/{name}\",\"size\":12,\"content_type\":\"application/octet-stream\"}}";

    private static void AssertRequestHeaders(HttpRequestMessage request)
    {
        Assert.True(request.Headers.UserAgent.Count > 0);
        Assert.Contains("application/vnd.github+json", request.Headers.Accept.Select(value => value.MediaType));
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "yttStudio-update-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }

    private sealed class RecordingProgress : IProgress<AppUpdateProgress>
    {
        public List<AppUpdateProgress> Values { get; } = [];

        public void Report(AppUpdateProgress value) => Values.Add(value);
    }

    private class NonSeekableReadStream(byte[] data) : Stream
    {
        private int position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = Math.Min(count, data.Length - position);
            if (read <= 0)
            {
                return 0;
            }

            data.AsSpan(position, read).CopyTo(buffer.AsSpan(offset, read));
            position += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int read = Math.Min(buffer.Length, data.Length - position);
            if (read <= 0)
            {
                return new(0);
            }

            data.AsMemory(position, read).CopyTo(buffer);
            position += read;
            return new(read);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CancellationAfterFirstReadStream(byte[] data) : NonSeekableReadStream(data)
    {
        private bool firstRead = true;

        public TaskCompletionSource<bool> FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (firstRead)
            {
                firstRead = false;
                FirstRead.TrySetResult(true);
                return base.ReadAsync(buffer, cancellationToken);
            }

            return new(WaitForCancellationAsync(cancellationToken));
        }

        private static async Task<int> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class ThrowingAfterFirstReadStream(byte[] data) : NonSeekableReadStream(data)
    {
        private bool firstRead = true;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (firstRead)
            {
                firstRead = false;
                return base.ReadAsync(buffer, cancellationToken);
            }

            throw new IOException("fake read failure");
        }
    }
}
