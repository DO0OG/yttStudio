namespace YttStudio.App.Preview;

/// <summary>
/// 실제 브라우저에서 자막을 확인할 때 쓰는 선택적 mitmproxy 스크립트를 제공한다.
///
/// 어댑터는 복사된 스크립트 에셋을 찾고 자막 경로를 검증할 뿐이다.
/// mitmproxy 를 번들하거나 프로세스를 띄우거나 루트 인증서를 설치하거나 브라우저 프록시
/// 설정을 바꾸지 않는다. 따라서 프리뷰 실패가 편집이나 내보내기에 영향을 주지 않는다.
/// </summary>
public sealed class MitmproxyPreviewAdapter : IExternalPlayerPreview
{
    /// <summary>
    /// 실행 파일 옆에 복사되는 스크립트 에셋의 상대 경로다.
    /// </summary>
    public const string ScriptAssetRelativePath = "Preview/Assets/mitmproxy_script.py";

    /// <summary>
    /// 스크립트가 자막 파일 경로를 읽는 환경변수다.
    /// </summary>
    public const string SubtitleEnvironmentVariable = "YTTSTUDIO_SUBTITLE_FILE";

    /// <summary>
    /// 에셋이 제공하는 두 mitmproxy 훅의 이름이다.
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

    /// <summary><see cref="Prepare"/> 가 마지막으로 돌려준 스크립트 경로를 가져온다.</summary>
    public string? ScriptPath => preparedScriptPath;

    /// <summary><see cref="Prepare"/> 가 마지막으로 돌려준 자막 경로를 가져온다.</summary>
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
