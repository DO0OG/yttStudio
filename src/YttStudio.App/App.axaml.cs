using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace YttStudio.App;

public partial class App : Application
{
    private MainWindowViewModel? mainViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new();
            mainViewModel = new MainWindowViewModel(new FileDialogService(window));
            window.DataContext = mainViewModel;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => mainViewModel.Dispose();

            // 디스크에 남은 스냅샷은 직전 실행이 정상 종료하지 않았다는 뜻이다.
            // 대화상자를 띄울 창이 준비되면 복구를 제안한다.
            MainWindowViewModel viewModel = mainViewModel;
            window.Opened += async (_, _) => await viewModel.OfferCrashRecoveryAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
