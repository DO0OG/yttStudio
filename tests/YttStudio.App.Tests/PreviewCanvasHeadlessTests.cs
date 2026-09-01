using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using YttStudio.App;

[assembly: AvaloniaTestApplication(typeof(YttStudio.App.Tests.HeadlessTestAppBuilder))]

namespace YttStudio.App.Tests;

public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<HeadlessTestApplication>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            });
}

public sealed class HeadlessTestApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}

public sealed class PreviewCanvasHeadlessTests
{
    [AvaloniaFact]
    public void ShownPreviewCanvasReceivesHitAtEmptyCoordinate()
    {
        PreviewCanvas canvas = new() { Width = 320, Height = 180 };
        Window window = new()
        {
            Width = 320,
            Height = 180,
            Content = canvas,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            _ = window.CaptureRenderedFrame();

            Point? windowPoint = canvas.TranslatePoint(new Point(12, 12), window);
            Assert.NotNull(windowPoint);
            IInputElement? hit = window.InputHitTest(windowPoint.Value);

            Assert.Same(canvas, hit);
        }
        finally
        {
            window.Close();
        }
    }
}
