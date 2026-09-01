namespace YttStudio.App;

public sealed partial class MainWindowViewModel
{
    /// <summary>큐 편집기에서 표시할 자막 줄 수의 전역 상한이다.</summary>
    public int MaxSubtitleLines
    {
        get => preferences.MaxSubtitleLines;
        set
        {
            int normalized = AppPreferences.NormalizeSubtitleLines(value);
            if (preferences.MaxSubtitleLines == normalized)
            {
                return;
            }

            preferences.MaxSubtitleLines = normalized;
            SavePreferences();
            OnPropertyChanged();
        }
    }
}
