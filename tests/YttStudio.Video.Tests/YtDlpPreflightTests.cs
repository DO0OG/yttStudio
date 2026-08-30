using YttStudio.Video;

namespace YttStudio.Video.Tests;

public sealed class YtDlpPreflightTests
{
    [Fact]
    public async Task ProbeRejectsInvalidUrlBeforeLookingForTool()
    {
        bool lookedUp = false;
        YtDlpPreflight preflight = Create(
            new YtDlpProcessResult(0, "{}", string.Empty, false),
            () =>
            {
                lookedUp = true;
                return ("yt-dlp", "found");
            });

        YouTubePreflightResult result = await preflight.ProbeAsync(
            "https://example.com/video",
            TestContext.Current.CancellationToken);

        Assert.False(result.IsPlayable);
        Assert.Equal(YouTubePlaybackFailureKind.InvalidUrl, result.FailureKind);
        Assert.False(lookedUp);
    }

    [Fact]
    public async Task ProbeReportsMissingTool()
    {
        YtDlpPreflight preflight = Create(
            new YtDlpProcessResult(0, "{}", string.Empty, false),
            static () => (null, "yt-dlp를 찾지 못했습니다."));

        YouTubePreflightResult result = await preflight.ProbeAsync(
            ValidUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(YouTubePlaybackFailureKind.YtDlpMissing, result.FailureKind);
    }

    [Fact]
    public async Task ProbeReturnsMetadataWithoutDownloading()
    {
        YtDlpPreflight preflight = Create(new YtDlpProcessResult(
            0,
            "{\"title\":\"테스트\",\"duration\":12.5,\"is_live\":false,\"age_limit\":0,\"availability\":\"public\"}",
            string.Empty,
            false));

        YouTubePreflightResult result = await preflight.ProbeAsync(
            ValidUrl,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsPlayable);
        Assert.Equal(YouTubePlaybackFailureKind.None, result.FailureKind);
        Assert.Equal("테스트", result.Title);
        Assert.Equal(TimeSpan.FromSeconds(12.5), result.Duration);
    }

    [Theory]
    [InlineData("{\"is_live\":true}", YouTubeUnplayableReason.Live)]
    [InlineData("{\"age_limit\":18}", YouTubeUnplayableReason.AgeRestricted)]
    [InlineData("{\"availability\":\"private\"}", YouTubeUnplayableReason.Private)]
    [InlineData("{\"availability\":\"geo_restricted\"}", YouTubeUnplayableReason.RegionBlocked)]
    public async Task ProbeClassifiesMetadataPlaybackRestrictions(
        string json,
        YouTubeUnplayableReason expectedReason)
    {
        YtDlpPreflight preflight = Create(new YtDlpProcessResult(0, json, string.Empty, false));

        YouTubePreflightResult result = await preflight.ProbeAsync(
            ValidUrl,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsPlayable);
        Assert.Equal(YouTubePlaybackFailureKind.Unplayable, result.FailureKind);
        Assert.Equal(expectedReason, result.UnplayableReason);
    }

    [Theory]
    [InlineData("ERROR: Private video")]
    [InlineData("ERROR: Sign in to confirm your age")]
    [InlineData("ERROR: This video is not available in your country")]
    [InlineData("ERROR: This live event has ended")]
    public async Task ProbeClassifiesStableRestrictionMessages(string stderr)
    {
        YtDlpPreflight preflight = Create(new YtDlpProcessResult(1, string.Empty, stderr, false));

        YouTubePreflightResult result = await preflight.ProbeAsync(
            ValidUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(YouTubePlaybackFailureKind.Unplayable, result.FailureKind);
    }

    [Theory]
    [InlineData("ERROR: <urlopen error [Errno -3] Temporary failure in name resolution>")]
    [InlineData("ERROR: HTTPSConnectionPool timed out")]
    [InlineData("ERROR: HTTP Error 503: Service Unavailable")]
    public async Task ProbeSeparatesNetworkFailures(string stderr)
    {
        YtDlpPreflight preflight = Create(new YtDlpProcessResult(1, string.Empty, stderr, false));

        YouTubePreflightResult result = await preflight.ProbeAsync(
            ValidUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(YouTubePlaybackFailureKind.NetworkFailure, result.FailureKind);
    }

    [Theory]
    [InlineData("ERROR: unable to download video data: HTTP Error 403: Forbidden")]
    [InlineData("ERROR: Sign in to confirm you're not a bot")]
    [InlineData("ERROR: HTTP Error 429: Too Many Requests")]
    public async Task ProbeSeparatesYouTubeAccessDenialFromNetworkFailure(string stderr)
    {
        YtDlpPreflight preflight = Create(new YtDlpProcessResult(1, string.Empty, stderr, false));

        YouTubePreflightResult result = await preflight.ProbeAsync(
            ValidUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal("AccessDenied", result.FailureKind.ToString());
        Assert.NotEqual(YouTubePlaybackFailureKind.NetworkFailure, result.FailureKind);
    }

    [Fact]
    public async Task ProbeReportsTimeoutAsTypedFailure()
    {
        YtDlpPreflight preflight = Create(new YtDlpProcessResult(-1, string.Empty, string.Empty, true));

        YouTubePreflightResult result = await preflight.ProbeAsync(
            ValidUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(YouTubePlaybackFailureKind.Timeout, result.FailureKind);
    }

    [Fact]
    public async Task EnsurePlayableThrowsTheResultClassification()
    {
        YtDlpPreflight preflight = Create(new YtDlpProcessResult(1, string.Empty, "ERROR: Private video", false));

        YouTubePlaybackException exception = await Assert.ThrowsAsync<YouTubePlaybackException>(
            () => preflight.EnsurePlayableAsync(ValidUrl, TestContext.Current.CancellationToken));

        Assert.Equal(YouTubePlaybackFailureKind.Unplayable, exception.Kind);
        Assert.Equal(YouTubeUnplayableReason.Private, exception.Reason);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "xUnit",
        "xUnit1051",
        Justification = "호출자가 전달한 취소 토큰을 검증하는 시험이다.")]
    public async Task ProbeRethrowsCallerCancellation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        YtDlpPreflight preflight = Create(new YtDlpProcessResult(0, "{}", string.Empty, false));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => preflight.ProbeAsync(ValidUrl, cancellation.Token));
    }

    private const string ValidUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    private static YtDlpPreflight Create(
        YtDlpProcessResult result,
        Func<(string? Path, string Diagnostic)>? locator = null)
        => new(
            locator ?? (static () => ("yt-dlp", "found")),
            new StubRunner(result),
            TimeSpan.FromSeconds(1));

    private sealed class StubRunner(YtDlpProcessResult result) : IYtDlpProcessRunner
    {
        public Task<YtDlpProcessResult> RunAsync(
            string executablePath,
            Uri uri,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }
}
