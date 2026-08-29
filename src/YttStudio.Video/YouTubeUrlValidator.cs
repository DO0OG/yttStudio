namespace YttStudio.Video;

/// <summary>외부 네트워크 주소를 libmpv에 넘기기 전에 YouTube 주소인지 확인한다.</summary>
public static class YouTubeUrlValidator
{
    private static readonly HashSet<string> YouTubeHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "music.youtube.com",
        "youtu.be",
        "www.youtu.be",
        "youtube-nocookie.com",
        "www.youtube-nocookie.com",
    };

    /// <summary>입력이 재생 가능한 YouTube 영상 주소 형식인지 확인한다.</summary>
    public static bool IsValid(string? value)
        => TryValidate(value, out _, out _);

    /// <summary>검증된 주소와 영상 ID를 반환한다.</summary>
    public static bool TryValidate(string? value, out Uri? uri, out string? error)
    {
        uri = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "YouTube 주소가 비어 있습니다.";
            return false;
        }

        string trimmed = value.Trim();
        // YouTube 는 평문으로 서비스하지 않는다. 평문 주소를 그대로 외부 도구에
        // 넘기지 않도록 https 만 받는다.
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? candidate) ||
            !candidate.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            error = "https YouTube 주소가 필요합니다.";
            return false;
        }

        if (!candidate.IsDefaultPort || !string.IsNullOrEmpty(candidate.UserInfo))
        {
            error = "YouTube 주소의 포트 또는 사용자 정보가 올바르지 않습니다.";
            return false;
        }

        string host = candidate.Host.TrimEnd('.');
        bool hasVideoId;
        try
        {
            hasVideoId = YouTubeHosts.Contains(host) && TryReadVideoId(candidate, host, out _);
        }
        catch (UriFormatException)
        {
            hasVideoId = false;
        }

        if (!hasVideoId)
        {
            error = "YouTube 영상 주소가 아닙니다.";
            return false;
        }

        uri = candidate;
        return true;
    }

    /// <summary>검증에 실패하면 형식화된 주소 오류를 던진다.</summary>
    public static Uri Validate(string value)
    {
        if (TryValidate(value, out Uri? uri, out string? error))
        {
            return uri!;
        }

        throw YouTubePlaybackException.InvalidUrl(error ?? "YouTube 주소가 올바르지 않습니다.");
    }

    /// <summary>검증된 주소에서 YouTube 영상 ID를 읽는다.</summary>
    public static bool TryGetVideoId(Uri uri, out string? videoId)
    {
        ArgumentNullException.ThrowIfNull(uri);
        videoId = null;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal) || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        string host = uri.Host.TrimEnd('.');
        bool hasVideoId;
        string? value = null;
        try
        {
            hasVideoId = YouTubeHosts.Contains(host) && TryReadVideoId(uri, host, out value);
        }
        catch (UriFormatException)
        {
            hasVideoId = false;
            value = null;
        }

        if (!hasVideoId)
        {
            return false;
        }

        videoId = value;
        return true;
    }

    private static bool TryReadVideoId(Uri uri, string host, out string? videoId)
    {
        videoId = null;
        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? candidate = null;
        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("www.youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length == 1)
            {
                candidate = segments[0];
            }
        }
        else if (segments.Length == 2 &&
            segments[0] is "shorts" or "embed" or "live")
        {
            candidate = segments[1];
        }
        else if (segments.Length == 1 && segments[0].Equals("watch", StringComparison.OrdinalIgnoreCase))
        {
            candidate = ReadQueryValue(uri, "v");
        }

        if (!IsVideoId(candidate))
        {
            return false;
        }

        videoId = candidate;
        return true;
    }

    private static string? ReadQueryValue(Uri uri, string name)
    {
        string query = uri.Query.TrimStart('?');
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            string key = separator < 0 ? pair : pair[..separator];
            if (!Uri.UnescapeDataString(key).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            return Uri.UnescapeDataString(value.Replace('+', ' ')).Trim();
        }

        return null;
    }

    private static bool IsVideoId(string? value)
    {
        if (value is null || value.Length != 11)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
