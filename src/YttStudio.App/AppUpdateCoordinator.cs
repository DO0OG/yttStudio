namespace YttStudio.App;

/// <summary>ViewModel의 업데이트 흐름과 네트워크 서비스를 분리하는 내부 조정기다.</summary>
internal interface IAppUpdateCoordinator
{
    string RuntimeIdentifier { get; }

    AppUpdateExecutionForm ExecutionForm { get; }

    Task<AppUpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    Task<string> DownloadAsync(
        AppUpdateAsset asset,
        string destinationDirectory,
        IProgress<AppUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>기존 업데이트 서비스를 ViewModel 내부 조정기 계약으로 연결한다.</summary>
internal sealed class AppUpdateCoordinator(AppUpdateService service) : IAppUpdateCoordinator
{
    public string RuntimeIdentifier => service.RuntimeIdentifier;

    public AppUpdateExecutionForm ExecutionForm => service.ExecutionForm;

    public Task<AppUpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        => service.CheckForUpdateAsync(cancellationToken);

    public Task<string> DownloadAsync(
        AppUpdateAsset asset,
        string destinationDirectory,
        IProgress<AppUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => service.DownloadAsync(asset, destinationDirectory, progress, cancellationToken);
}
