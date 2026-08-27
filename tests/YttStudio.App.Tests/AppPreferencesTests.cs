using YttStudio.App;
using YttStudio.Render;

namespace YttStudio.App.Tests;

public sealed class AppPreferencesTests
{
    [Fact]
    public void PreviewViewportModeRoundTripsThroughPreferencesStore()
    {
        string path = CreateTemporaryPath();
        try
        {
            PreferencesStore store = new(path);
            AppPreferences preferences = new()
            {
                PreviewViewportMode = PreviewViewportMode.YouTubeFullscreen,
            };

            Assert.True(store.TrySave(preferences, out string? error), error);

            AppPreferences restored = store.Load();
            Assert.Equal(PreviewViewportMode.YouTubeFullscreen, restored.PreviewViewportMode);
        }
        finally
        {
            DeleteTemporaryPath(path);
        }
    }

    [Fact]
    public void UnsupportedPreviewViewportModeFallsBackToVideoFrame()
    {
        string path = CreateTemporaryPath();
        try
        {
            File.WriteAllText(path, "{\"PreviewViewportMode\": 999}");

            AppPreferences restored = new PreferencesStore(path).Load();

            Assert.Equal(PreviewViewportMode.VideoFrame, restored.PreviewViewportMode);
        }
        finally
        {
            DeleteTemporaryPath(path);
        }
    }

    [Fact]
    public void MobilePortraitIsNotASelectableOrPersistedMode()
    {
        AppPreferences preferences = new()
        {
            PreviewViewportMode = PreviewViewportMode.MobilePortrait,
        };

        Assert.False(AppPreferences.IsSelectablePreviewViewportMode(
            PreviewViewportMode.MobilePortrait));
        Assert.Equal(PreviewViewportMode.VideoFrame, preferences.PreviewViewportMode);
    }

    private static string CreateTemporaryPath()
        => Path.Combine(Path.GetTempPath(), $"YttStudio-preferences-{Guid.NewGuid():N}.json");

    private static void DeleteTemporaryPath(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
