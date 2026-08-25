using Avalonia;
using Serilog;

namespace YttStudio.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YttStudio",
            "logs");
        Directory.CreateDirectory(logDirectory);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(logDirectory, "yttstudio-.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "YttStudio terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
