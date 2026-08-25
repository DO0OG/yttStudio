namespace YttStudio.App.Preview;

/// <summary>
/// Describes the outcome of preparing or reverting an external-player preview.
/// </summary>
public enum ExternalPreviewStatus
{
    /// <summary>The script and subtitle file are available for external setup.</summary>
    Ready,

    /// <summary>The optional preview cannot be used in the current environment.</summary>
    Unavailable,

    /// <summary>The request was invalid or a required local file was not found.</summary>
    Failed,

    /// <summary>No local preview configuration is retained by the adapter.</summary>
    Reverted,
}

/// <summary>
/// A side-effect-free result from an external preview operation.
/// </summary>
public sealed record ExternalPreviewResult(
    ExternalPreviewStatus Status,
    string Message,
    string? ScriptPath = null,
    string? SubtitlePath = null)
{
    /// <summary>Gets whether setup completed and the optional preview is usable.</summary>
    public bool IsSuccess => Status == ExternalPreviewStatus.Ready;

    /// <summary>Gets whether the result represents a usable preview setup.</summary>
    public bool IsAvailable => Status == ExternalPreviewStatus.Ready;
}

/// <summary>
/// Human-readable setup and recovery guidance for an external preview tool.
/// </summary>
public sealed record ExternalPreviewGuidance(
    string Setup,
    string Revert,
    string Download,
    string FiddlerAlternative);

/// <summary>
/// Optional integration boundary for previewing the edited subtitles in an external player.
///
/// Implementations must not mutate editing state, export state, browser proxy settings, or
/// certificate stores. Preview is deliberately independent from editing and export.
/// </summary>
public interface IExternalPlayerPreview
{
    /// <summary>Gets the setup and recovery guidance shown to the user.</summary>
    ExternalPreviewGuidance Guidance { get; }

    /// <summary>
    /// Validates the local inputs and locates the optional preview script.
    /// This method does not start mitmproxy or alter proxy/certificate settings.
    /// </summary>
    /// <param name="subtitleFilePath">Path to the subtitle file to serve.</param>
    ExternalPreviewResult Prepare(string subtitleFilePath);

    /// <summary>
    /// Clears adapter-local state and returns instructions for manually reverting browser setup.
    /// No browser, proxy, or certificate state is changed by this call.
    /// </summary>
    ExternalPreviewResult Revert();
}
