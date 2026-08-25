using System.ComponentModel;
using System.Runtime.CompilerServices;
using YttStudio.Core;
using YttStudio.Core.Editing;

namespace YttStudio.App;

public sealed record CanvasCueItem(
    Guid Id,
    CanvasRect Bounds,
    CanvasPoint Anchor,
    AnchorPoint AnchorKind,
    bool Selected);

public sealed record CanvasMovePreview(double DeltaX, double DeltaY, IReadOnlyList<SnapGuide> Guides);

public sealed record StyleOption(Guid Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class CueRowViewModel : INotifyPropertyChanged
{
    private readonly MainWindowViewModel owner;

    public CueRowViewModel(MainWindowViewModel owner, Guid id, int number)
    {
        this.owner = owner;
        Id = id;
        Number = number;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid Id { get; }
    public int Number { get; }

    public double StartMilliseconds
    {
        get => Cue.Start.TotalMilliseconds;
        set => UpdateTiming(value, EndMilliseconds, Track);
    }

    public double EndMilliseconds
    {
        get => Cue.End.TotalMilliseconds;
        set => UpdateTiming(StartMilliseconds, value, Track);
    }

    public double DurationMilliseconds => EndMilliseconds - StartMilliseconds;

    public int Track
    {
        get => Cue.Track;
        set => UpdateTiming(StartMilliseconds, EndMilliseconds, value);
    }

    /// <summary>큐에 적용된 스타일 이름이다. 식별자 대신 사람이 읽는 이름을 보여준다.</summary>
    public string Style => owner.StyleNameOf(Cue.StyleId);

    public string Text
    {
        get => string.Concat(Cue.Sections.Select(section => section.Text));
        set
        {
            owner.UpdateCueText(Id, value ?? string.Empty);
            NotifyAll();
        }
    }

    private Cue Cue => owner.GetCue(Id) ?? throw new InvalidOperationException("Cue no longer exists.");

    private void UpdateTiming(double start, double end, int track)
    {
        owner.UpdateCueTiming(Id, start, end, track);
        NotifyAll();
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(StartMilliseconds));
        OnPropertyChanged(nameof(EndMilliseconds));
        OnPropertyChanged(nameof(DurationMilliseconds));
        OnPropertyChanged(nameof(Track));
        OnPropertyChanged(nameof(Style));
        OnPropertyChanged(nameof(Text));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
