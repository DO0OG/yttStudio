using System.Reflection;

namespace YttStudio.App.Tests;

public sealed class YouTubeRuntimeBootstrapTests
{
    [Fact]
    public void ManagedDenoInstallerPinsSupportedRuntime()
    {
        Type? installerType = typeof(MainWindowViewModel).Assembly.GetType("YttStudio.App.DenoAutoInstaller");

        Assert.NotNull(installerType);
        FieldInfo? pinnedVersion = installerType!.GetField(
            "PinnedVersion",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo? ensureAvailable = installerType.GetMethod(
            "EnsureAvailableAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(pinnedVersion);
        Assert.Equal("2.9.6", pinnedVersion!.GetRawConstantValue());
        Assert.NotNull(ensureAvailable);
    }

    [Fact]
    public void MainWindowOwnsManagedDenoInstallerForYouTubeBootstrap()
    {
        FieldInfo? field = typeof(MainWindowViewModel).GetField(
            "YouTubeDenoInstaller",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.Equal("DenoAutoInstaller", field!.FieldType.Name);
    }
}
