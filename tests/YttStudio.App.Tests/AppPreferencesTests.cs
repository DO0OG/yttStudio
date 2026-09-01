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

    [Fact]
    public void SubtitleLineLimitDefaultsToThreeAndClampsAssignments()
    {
        AppPreferences preferences = new();

        Assert.Equal(5, preferences.MaxSubtitleLines);

        preferences.MaxSubtitleLines = 0;
        Assert.Equal(1, preferences.MaxSubtitleLines);

        preferences.MaxSubtitleLines = 99;
        Assert.Equal(10, preferences.MaxSubtitleLines);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    public void SubtitleLineLimitClampsLegacyPreferenceJson(int stored, int expected)
    {
        string path = CreateTemporaryPath();
        try
        {
            File.WriteAllText(path, $"{{\"MaxSubtitleLines\":{stored}}}");

            AppPreferences restored = new PreferencesStore(path).Load();

            Assert.Equal(expected, restored.MaxSubtitleLines);
        }
        finally
        {
            DeleteTemporaryPath(path);
        }
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
