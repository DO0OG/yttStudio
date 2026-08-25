namespace YttStudio.App.Preview;

/// <summary>
/// Provides the optional mitmproxy script used to preview subtitles in a real browser.
///
/// The adapter only discovers the copied script asset and validates the subtitle path. It does
/// not bundle mitmproxy, launch a process, install a root certificate, or mutate browser proxy
/// settings. A preview failure therefore cannot affect editing or export.
/// </summary>
public sealed class MitmproxyPreviewAdapter : IExternalPlayerPreview
{
    /// <summary>
    /// Relative path of the script asset copied beside the App executable.
    /// </summary>
    public const string ScriptAssetRelativePath = "Preview/Assets/mitmproxy_script.py";

    /// <summary>
    /// Environment variable read by the script for the subtitle file path.
    /// </summary>
    public const string SubtitleEnvironmentVariable = "YTTSTUDIO_SUBTITLE_FILE";

    /// <summary>
    /// Names of the two mitmproxy hooks supplied by the asset.
    /// </summary>
    public static IReadOnlyList<string> HookNames { get; } =
        ["ensure_subtitle_selector", "apply_custom_subtitles"];

    private string? preparedScriptPath;
    private string? preparedSubtitlePath;

    /// <inheritdoc />
    public ExternalPreviewGuidance Guidance { get; } = new(
        Setup:
            "1. Install mitmproxy separately from https://mitmproxy.org/. " +
            "2. Set YTTSTUDIO_SUBTITLE_FILE to the subtitle file path. " +
            "3. Run `mitmdump --listen-host 127.0.0.1 --listen-port 8080 -s \"Preview/Assets/mitmproxy_script.py\"`. " +
            "4. In the browser, manually set the HTTP(S) proxy to 127.0.0.1:8080 and trust the " +
            "mitmproxy certificate only if the browser asks for it.",
        Revert:
            "Stop mitmdump, restore the browser's previous proxy setting, and remove the " +
            "mitmproxy certificate only if you installed it. YttStudio does not change or remove " +
            "either setting automatically.",
        Download:
            "Download mitmproxy from the official site: https://mitmproxy.org/. " +
            "YttStudio ships only its small hook script; it does not bundle mitmproxy.",
        FiddlerAlternative:
            "On Windows, Fiddler Classic can be used instead of mitmproxy. Configure its proxy " +
            "and HTTPS certificate manually, then restore the previous settings when finished.");

    /// <summary>Gets the last script path returned by <see cref="Prepare"/>.</summary>
    public string? ScriptPath => preparedScriptPath;

    /// <summary>Gets the last subtitle path returned by <see cref="Prepare"/>.</summary>
    public string? SubtitlePath => preparedSubtitlePath;

    /// <inheritdoc />
    public ExternalPreviewResult Prepare(string subtitleFilePath)
    {
        if (string.IsNullOrWhiteSpace(subtitleFilePath))
        {
            return Failure("A subtitle file path is required.");
        }

        string fullSubtitlePath;
        try
        {
            fullSubtitlePath = Path.GetFullPath(subtitleFilePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Failure($"The subtitle file path is invalid: {exception.Message}");
        }

        if (!File.Exists(fullSubtitlePath))
        {
            return Failure($"The subtitle file was not found: {fullSubtitlePath}");
        }

        string? scriptPath = FindScriptAsset();
        if (scriptPath is null)
        {
            preparedScriptPath = null;
            preparedSubtitlePath = null;
            return new ExternalPreviewResult(
                ExternalPreviewStatus.Unavailable,
                "The optional external preview script is not available. Editing and export remain available.");
        }

        preparedScriptPath = scriptPath;
        preparedSubtitlePath = fullSubtitlePath;
        return new ExternalPreviewResult(
            ExternalPreviewStatus.Ready,
            "The optional preview inputs are ready. Follow the setup guidance to start mitmdump manually.",
            scriptPath,
            fullSubtitlePath);
    }

    /// <inheritdoc />
    public ExternalPreviewResult Revert()
    {
        preparedScriptPath = null;
        preparedSubtitlePath = null;
        return new ExternalPreviewResult(
            ExternalPreviewStatus.Reverted,
            "Adapter state was cleared. Stop mitmdump and restore browser proxy/certificate settings manually.");
    }

    private ExternalPreviewResult Failure(string message)
    {
        preparedScriptPath = null;
        preparedSubtitlePath = null;
        return new ExternalPreviewResult(ExternalPreviewStatus.Failed, message);
    }

    private static string? FindScriptAsset()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Preview", "Assets", "mitmproxy_script.py"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "mitmproxy_script.py"),
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }
}
