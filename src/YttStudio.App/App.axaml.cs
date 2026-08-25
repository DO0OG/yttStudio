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

            // SPEC §12: a snapshot left on disk means the previous run did not shut down
            // cleanly. Offer recovery once the window exists to host a dialog.
            MainWindowViewModel viewModel = mainViewModel;
            window.Opened += async (_, _) => await viewModel.OfferCrashRecoveryAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
