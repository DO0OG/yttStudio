namespace YttStudio.App;

public sealed partial class MainWindowViewModel
{
    /// <summary>이미 준비된 영상 소스가 있거나 프로그램 내부 설치가 가능한지 확인한다.</summary>
    private bool CanOpenVideo()
        => videoSource is not null || MpvAutoInstaller.IsAutomaticInstallationSupported;
}
