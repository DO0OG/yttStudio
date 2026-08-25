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
        }

        base.OnFrameworkInitializationCompleted();
    }
}
