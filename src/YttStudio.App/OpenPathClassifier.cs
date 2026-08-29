namespace YttStudio.App;

public enum OpenPathKind
{
    Unsupported,
    Project,
    Subtitle,
    Video,
    VideoUrl,
}

/// <summary>입력 경로를 기존 열기 작업으로 라우팅하기 위한 확장자 분류기.</summary>
public static class OpenPathClassifier
{
    public static OpenPathKind Classify(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return OpenPathKind.Unsupported;
        }

        if (YttStudio.Video.YouTubeUrlValidator.IsValid(path))
        {
            return OpenPathKind.VideoUrl;
        }

        string extension;
        try
        {
            extension = Path.GetExtension(path);
        }
        catch (ArgumentException)
        {
            return OpenPathKind.Unsupported;
        }

        return extension.ToLowerInvariant() switch
        {
            ".yttproj" => OpenPathKind.Project,
            ".ytt" or ".srv3" or ".ass" => OpenPathKind.Subtitle,
            ".mp4" or ".mkv" or ".webm" or ".mov" or ".avi" or ".m4v" => OpenPathKind.Video,
            _ => OpenPathKind.Unsupported,
        };
    }

    public static bool IsDropSupported(string? path)
        => Classify(path) is OpenPathKind.Subtitle or OpenPathKind.Video;
}
