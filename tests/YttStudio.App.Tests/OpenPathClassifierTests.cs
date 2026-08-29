namespace YttStudio.App.Tests;

public sealed class OpenPathClassifierTests
{
    [Theory]
    [InlineData("caption.ytt", OpenPathKind.Subtitle)]
    [InlineData("caption.SRV3", OpenPathKind.Subtitle)]
    [InlineData("caption.ass", OpenPathKind.Subtitle)]
    [InlineData("clip.mp4", OpenPathKind.Video)]
    [InlineData("clip.MKV", OpenPathKind.Video)]
    [InlineData("clip.webm", OpenPathKind.Video)]
    [InlineData("clip.mov", OpenPathKind.Video)]
    [InlineData("clip.avi", OpenPathKind.Video)]
    [InlineData("clip.m4v", OpenPathKind.Video)]
    [InlineData("project.yttproj", OpenPathKind.Project)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", OpenPathKind.VideoUrl)]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", OpenPathKind.VideoUrl)]
    public void ClassifiesSupportedExtensions(string path, OpenPathKind expected)
    {
        Assert.Equal(expected, OpenPathClassifier.Classify(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("caption.srt")]
    [InlineData("clip.mpg")]
    public void RejectsUnsupportedExtensions(string? path)
    {
        Assert.Equal(OpenPathKind.Unsupported, OpenPathClassifier.Classify(path));
        Assert.False(OpenPathClassifier.IsDropSupported(path));
    }

    [Theory]
    [InlineData("caption.ytt")]
    [InlineData("caption.ass")]
    [InlineData("clip.webm")]
    public void AcceptsSubtitleAndVideoForDrop(string path)
    {
        Assert.True(OpenPathClassifier.IsDropSupported(path));
    }
}
