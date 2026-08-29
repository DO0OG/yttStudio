namespace YttStudio.App.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void EveryKeyHasAllThreeLanguages()
    {
        // 제공하는 세 언어가 모두 채워져야 한다. 빈 항목은
        // 빈 라벨로 조용히 렌더되므로 여기서 단언으로 잡는다.
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

    [Theory]
    [InlineData(AppLanguage.Korean, "주소로 열기")]
    [InlineData(AppLanguage.English, "Open by Address")]
    [InlineData(AppLanguage.Japanese, "アドレスで開く")]
    public void OpenVideoUrlIsLocalized(AppLanguage language, string expected)
    {
        Localizer localizer = new() { Language = language };

        Assert.Equal(expected, localizer["OpenVideoUrl"]);
    }

    [Theory]
    [InlineData(AppLanguage.Korean, "주소로 열기", "YouTube 주소", "열기", "취소")]
    [InlineData(AppLanguage.English, "Open by Address", "YouTube address", "Open", "Cancel")]
    [InlineData(AppLanguage.Japanese, "アドレスで開く", "YouTube アドレス", "開く", "キャンセル")]
    public void OpenVideoUrlDialogLabelsAreLocalized(
        AppLanguage language,
        string title,
        string prompt,
        string openLabel,
        string cancelLabel)
    {
        Localizer localizer = new() { Language = language };

        Assert.Equal(title, localizer["OpenVideoUrlTitle"]);
        Assert.Equal(prompt, localizer["OpenVideoUrlPrompt"]);
        Assert.Equal(openLabel, localizer["OpenVideoUrlConfirm"]);
        Assert.Equal(cancelLabel, localizer["Cancel"]);
    }

    [Fact]
    public void UnknownKeyReturnsTheKeyItself()
    {
        // 누락된 항목은 조용히 비는 대신 UI 에 드러나야 한다.
        Localizer localizer = new();

        Assert.Equal("NoSuchKey", localizer["NoSuchKey"]);
    }

    [Fact]
    public void ChangingLanguageRaisesIndexerNotification()
    {
        // 인덱서 바인딩은 소유자가 "Item[]" 을 무효화할 때만 갱신된다.
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
