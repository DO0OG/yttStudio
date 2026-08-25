using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using YttStudio.Core;

namespace YttStudio.App;

/// <summary>Provides the user-facing name for one supported karaoke effect mode.</summary>
public sealed record KaraokeTypeOption(KaraokeType Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Exposes one karaoke section to the editor view without mutating the domain model.</summary>
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
