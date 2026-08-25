using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using YttStudio.Core;

namespace YttStudio.App;

/// <summary>지원되는 가라오케 효과 모드 하나의 사용자 표시 이름을 제공한다.</summary>
public sealed record KaraokeTypeOption(KaraokeType Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>도메인 모델을 바꾸지 않고 가라오케 섹션 하나를 편집기 뷰에 노출한다.</summary>
public sealed class KaraokeSectionViewModel : INotifyPropertyChanged
{
    private readonly MainWindowViewModel owner;
    private readonly TimeSpan? karaokeOffset;

    internal KaraokeSectionViewModel(
        MainWindowViewModel owner,
        Guid cueId,
        int index,
        string text,
        TimeSpan? karaokeOffset)
    {
        this.owner = owner;
        CueId = cueId;
        Index = index;
        Text = text;
        this.karaokeOffset = karaokeOffset;
        SplitCommand = new DelegateCommand(
            () => owner.SplitKaraokeSection(CueId, Index, SplitOffset),
            () => CanSplit);
        RemoveCommand = new DelegateCommand(
            () => owner.RemoveKaraokeSection(CueId, Index),
            () => CanRemove);
        MergeNextCommand = new DelegateCommand(
            () => owner.MergeKaraokeSections(CueId, Index),
            () => CanMergeNext);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid CueId { get; }

    public int Index { get; }

    public string Text { get; }

    public string OffsetMillisecondsText
    {
        get => karaokeOffset is TimeSpan offset
            ? offset.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)
            : string.Empty;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double current) ||
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out current))
            {
                owner.SetKaraokeOffset(CueId, Index, current);
            }
        }
    }

    public string OffsetDisplay => karaokeOffset is TimeSpan offset
        ? $"{offset.TotalMilliseconds:0.###} ms"
        : "미지정";

    public bool HasOffset => karaokeOffset.HasValue;

    public bool CanSplit => SplitOffset > 0;

    public bool CanRemove => owner.KaraokeSectionCount(CueId) > 1;

    public bool CanMergeNext => owner.KaraokeSectionCount(CueId) > Index + 1;

    public ICommand SplitCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand MergeNextCommand { get; }

    private int SplitOffset
    {
        get
        {
            int[] boundaries = StringInfo.ParseCombiningCharacters(Text);
            return boundaries.Length > 1 ? boundaries[boundaries.Length / 2] : 0;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
