using YttStudio.Video;

namespace YttStudio.Video.Tests;

public sealed class YtDlpLocatorTests
{
    [Fact]
    public void TryFindCandidatesReturnsFirstExistingCandidate()
    {
        bool found = YtDlpLocator.TryFindCandidatesForTest(
            ["app/yt-dlp.exe", "path/yt-dlp.exe", "other/yt-dlp.exe"],
            candidate => candidate == "path/yt-dlp.exe",
            out string? executablePath,
            out string diagnostic);

        Assert.True(found);
        Assert.Equal("path/yt-dlp.exe", executablePath);
        Assert.Contains("path/yt-dlp.exe", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void TryFindCandidatesReportsAllProbesWhenMissing()
    {
        bool found = YtDlpLocator.TryFindCandidatesForTest(
            ["app/yt-dlp", "path/yt-dlp"],
            static _ => false,
            out string? executablePath,
            out string diagnostic);

        Assert.False(found);
        Assert.Null(executablePath);
        Assert.Contains("app/yt-dlp", diagnostic, StringComparison.Ordinal);
        Assert.Contains("path/yt-dlp", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void TryFindCandidatesDeduplicatesProbes()
    {
        int calls = 0;
        YtDlpLocator.TryFindCandidatesForTest(
            ["same", "same"],
            _ =>
            {
                calls++;
                return false;
            },
            out _,
            out _);

        Assert.Equal(1, calls);
    }
}
