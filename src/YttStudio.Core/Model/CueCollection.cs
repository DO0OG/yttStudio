using System.Collections.Specialized;

namespace YttStudio.Core;

/// <summary>Stores cues by identity and maintains a start-time-sorted lookup index.</summary>
public sealed class CueCollection : IReadOnlyCollection<Cue>, INotifyCollectionChanged
{
    private readonly Dictionary<Guid, Cue> byId = [];
    private readonly Dictionary<Guid, long> insertionSequence = [];
    private readonly List<Cue> sortedByStart = [];
    private readonly HashSet<Guid> advanceActiveIds = [];
    private TimeSpan? advanceTime;
    private int advanceCursor;
    private long nextSequence;

    public int Count => byId.Count;
    public Cue? this[Guid id] => byId.GetValueOrDefault(id);

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>Returns cues active at the half-open interval Start &lt;= time &lt; End.</summary>
    public IReadOnlyList<Cue> GetActiveAt(TimeSpan time)
    {
        int upperBound = FindFirstStartingAfter(time);
        List<Cue> active = [];
        for (int index = upperBound - 1; index >= 0; index--)
        {
            Cue cue = sortedByStart[index];
            if (cue.End > time)
            {
                active.Add(cue);
            }
        }

        active.Sort(CompareRenderOrder);
        return active;
    }

    /// <summary>Advances the cached active set and reports cues that entered or exited.</summary>
    public ActiveSetDelta AdvanceTo(TimeSpan time)
    {
        if (advanceTime is null || time < advanceTime)
        {
            return ResetAdvanceState(time);
        }

        List<Cue> exited = [];
        foreach (Guid id in advanceActiveIds.ToArray())
        {
            Cue? cue = this[id];
            if (cue is null || cue.End <= time)
            {
                advanceActiveIds.Remove(id);
                if (cue is not null)
                {
                    exited.Add(cue);
                }
            }
        }

        List<Cue> entered = [];
        while (advanceCursor < sortedByStart.Count && sortedByStart[advanceCursor].Start <= time)
        {
            Cue cue = sortedByStart[advanceCursor++];
            if (cue.End > time && advanceActiveIds.Add(cue.Id))
            {
                entered.Add(cue);
            }
        }

        advanceTime = time;
        return CreateDelta(entered, exited);
    }

    internal void Add(Cue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        byId.Add(cue.Id, cue);
        if (!insertionSequence.ContainsKey(cue.Id))
        {
            insertionSequence.Add(cue.Id, nextSequence++);
        }
        sortedByStart.Insert(FindInsertionIndex(cue), cue);
        InvalidateAdvanceState();
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, cue));
    }

    internal void Remove(Guid id)
    {
        if (!byId.Remove(id, out Cue? cue))
        {
            return;
        }

        sortedByStart.Remove(cue);
        InvalidateAdvanceState();
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, cue));
    }

    internal void OnStartChanged(Cue cue)
    {
        if (!byId.ContainsKey(cue.Id))
        {
            throw new InvalidOperationException("The cue does not belong to this collection.");
        }

        sortedByStart.Remove(cue);
        sortedByStart.Insert(FindInsertionIndex(cue), cue);
        InvalidateAdvanceState();
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public IEnumerator<Cue> GetEnumerator() => sortedByStart.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    private ActiveSetDelta ResetAdvanceState(TimeSpan time)
    {
        List<Cue> previous = advanceActiveIds.Select(id => this[id]).OfType<Cue>().ToList();
        IReadOnlyList<Cue> current = GetActiveAt(time);
        HashSet<Guid> currentIds = current.Select(cue => cue.Id).ToHashSet();
        List<Cue> entered = current.Where(cue => !advanceActiveIds.Contains(cue.Id)).ToList();
        List<Cue> exited = previous.Where(cue => !currentIds.Contains(cue.Id)).ToList();

        advanceActiveIds.Clear();
        advanceActiveIds.UnionWith(currentIds);
        advanceCursor = FindFirstStartingAfter(time);
        advanceTime = time;
        return new ActiveSetDelta(entered, exited, current);
    }

    private ActiveSetDelta CreateDelta(List<Cue> entered, List<Cue> exited)
    {
        entered.Sort(CompareRenderOrder);
        exited.Sort(CompareRenderOrder);
        List<Cue> active = advanceActiveIds.Select(id => byId[id]).ToList();
        active.Sort(CompareRenderOrder);
        return new ActiveSetDelta(entered, exited, active);
    }

    private int FindInsertionIndex(Cue cue)
    {
        int low = 0;
        int high = sortedByStart.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (CompareStart(sortedByStart[middle], cue) <= 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private int FindFirstStartingAfter(TimeSpan time)
    {
        int low = 0;
        int high = sortedByStart.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (sortedByStart[middle].Start <= time)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private void InvalidateAdvanceState()
    {
        advanceTime = null;
        advanceCursor = 0;
    }

    private int CompareStart(Cue left, Cue right)
    {
        int result = left.Start.CompareTo(right.Start);
        return result != 0 ? result : insertionSequence[left.Id].CompareTo(insertionSequence[right.Id]);
    }

    private int CompareRenderOrder(Cue left, Cue right)
    {
        int result = left.ZOrder.CompareTo(right.ZOrder);
        return result != 0 ? result : CompareStart(left, right);
    }
}

/// <summary>Describes changes to the active cue set since the previous advance.</summary>
public sealed class ActiveSetDelta
{
    public ActiveSetDelta(IReadOnlyList<Cue> entered, IReadOnlyList<Cue> exited, IReadOnlyList<Cue> active)
    {
        Entered = entered;
        Exited = exited;
        Active = active;
    }

    public IReadOnlyList<Cue> Entered { get; }
    public IReadOnlyList<Cue> Exited { get; }
    public IReadOnlyList<Cue> Active { get; }
}
