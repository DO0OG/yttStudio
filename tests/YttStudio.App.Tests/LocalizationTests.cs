namespace YttStudio.App.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void EveryKeyHasAllThreeLanguages()
    {
        // The three shipped languages must all be filled in. A blank entry would render as an
        // empty label rather than failing loudly, so it is asserted here instead.
        List<string> incomplete = [];
        foreach (string key in Localizer.Keys)
        {
            LocalizedText? text = Localizer.Find(key);
            if (text is null
                || string.IsNullOrWhiteSpace(text.Korean)
                || string.IsNullOrWhiteSpace(text.English)
                || string.IsNullOrWhiteSpace(text.Japanese))
            {
                incomplete.Add(key);
            }
        }

        Assert.Empty(incomplete);
    }

    [Theory]
    [InlineData(AppLanguage.Korean, "자막 열기")]
    [InlineData(AppLanguage.English, "Open Subtitle")]
    [InlineData(AppLanguage.Japanese, "字幕を開く")]
    public void IndexerReturnsActiveLanguage(AppLanguage language, string expected)
    {
        Localizer localizer = new() { Language = language };

        Assert.Equal(expected, localizer["OpenSubtitle"]);
    }

    [Fact]
    public void UnknownKeyReturnsTheKeyItself()
    {
        // A missing entry has to be visible in the UI, not silently blank.
        Localizer localizer = new();

        Assert.Equal("NoSuchKey", localizer["NoSuchKey"]);
    }

    [Fact]
    public void ChangingLanguageRaisesIndexerNotification()
    {
        // Indexer bindings only refresh when the owner invalidates "Item[]".
        Localizer localizer = new();
        List<string?> changed = [];
        localizer.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        localizer.Language = AppLanguage.Japanese;

        Assert.Contains("Item[]", changed);
    }

    [Fact]
    public void SettingTheSameLanguageDoesNotNotify()
    {
        Localizer localizer = new();
        int notifications = 0;
        localizer.PropertyChanged += (_, _) => notifications++;

        localizer.Language = AppLanguage.Korean;

        Assert.Equal(0, notifications);
    }
}
