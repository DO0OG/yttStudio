namespace YttStudio.App.Preview;

/// <summary>
/// 외부 플레이어 프리뷰를 준비하거나 되돌린 결과를 기술한다.
/// </summary>
public enum ExternalPreviewStatus
{
    /// <summary>외부 설정에 쓸 스크립트와 자막 파일이 준비되었다.</summary>
    Ready,

    /// <summary>현재 환경에서는 선택적 프리뷰를 쓸 수 없다.</summary>
    Unavailable,

    /// <summary>요청이 잘못되었거나 필요한 로컬 파일을 찾지 못했다.</summary>
    Failed,

    /// <summary>어댑터는 로컬 프리뷰 설정을 보관하지 않는다.</summary>
    Reverted,
}

/// <summary>
/// 외부 프리뷰 작업의 부작용 없는 결과다.
/// </summary>
public sealed record ExternalPreviewResult(
    ExternalPreviewStatus Status,
    string Message,
    string? ScriptPath = null,
    string? SubtitlePath = null)
{
    /// <summary>설정이 끝나 선택적 프리뷰를 쓸 수 있는지 가져온다.</summary>
    public bool IsSuccess => Status == ExternalPreviewStatus.Ready;

    /// <summary>결과가 사용 가능한 프리뷰 설정을 뜻하는지 가져온다.</summary>
    public bool IsAvailable => Status == ExternalPreviewStatus.Ready;
}

/// <summary>
/// 외부 프리뷰 도구의 설정과 복구 안내를 사람이 읽을 수 있는 형태로 담는다.
/// </summary>
public sealed record ExternalPreviewGuidance(
    string Setup,
    string Revert,
    string Download,
    string FiddlerAlternative);

/// <summary>
/// 편집한 자막을 외부 플레이어에서 확인하기 위한 선택적 통합 경계다.
///
/// 구현은 편집 상태나 내보내기 상태나 브라우저 프록시 설정이나
/// 인증서 저장소를 바꾸지 않는다. 프리뷰는 편집과 내보내기에서 의도적으로 독립적이다.
/// </summary>
public interface IExternalPlayerPreview
{
    /// <summary>사용자에게 보여줄 설정과 복구 안내를 가져온다.</summary>
    ExternalPreviewGuidance Guidance { get; }

    /// <summary>
    /// 로컬 입력을 검증하고 선택적 프리뷰 스크립트를 찾는다.
    /// 이 메서드는 mitmproxy 를 실행하거나 프록시와 인증서 설정을 바꾸지 않는다.
    /// </summary>
    /// <param name="subtitleFilePath">제공할 자막 파일 경로다.</param>
    ExternalPreviewResult Prepare(string subtitleFilePath);

    /// <summary>
    /// 어댑터 로컬 상태를 지우고 브라우저 설정을 수동으로 되돌리는 방법을 알려준다.
    /// 이 호출은 브라우저나 프록시나 인증서 상태를 바꾸지 않는다.
    /// </summary>
    ExternalPreviewResult Revert();
}
