using YttStudio.Video;

namespace YttStudio.Video.Tests;

public sealed class YouTubeUrlValidatorTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("http://youtu.be/dQw4w9WgXcQ?t=12")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ")]
    public void TryValidateAcceptsSupportedYouTubeForms(string value)
    {
        Assert.True(YouTubeUrlValidator.TryValidate(value, out Uri? uri, out string? error));
        Assert.NotNull(uri);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("https://example.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("ftp://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=short")]
    [InlineData("https://www.youtube.com/channel/dQw4w9WgXcQ")]
    [InlineData("https://youtube.com.evil.example/watch?v=dQw4w9WgXcQ")]
    public void TryValidateRejectsUnsupportedOrMalformedForms(string value)
    {
        Assert.False(YouTubeUrlValidator.TryValidate(value, out Uri? uri, out string? error));
        Assert.Null(uri);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryGetVideoIdReturnsTheCanonicalVideoId()
    {
        Uri uri = YouTubeUrlValidator.Validate("https://www.youtube.com/watch?v=dQw4w9WgXcQ");

        Assert.True(YouTubeUrlValidator.TryGetVideoId(uri, out string? videoId));
        Assert.Equal("dQw4w9WgXcQ", videoId);
    }

    [Fact]
    public void ValidateThrowsTypedInvalidUrlException()
    {
        YouTubePlaybackException exception = Assert.Throws<YouTubePlaybackException>(
            () => YouTubeUrlValidator.Validate("https://example.com/video"));

        Assert.Equal(YouTubePlaybackFailureKind.InvalidUrl, exception.Kind);
    }
}
