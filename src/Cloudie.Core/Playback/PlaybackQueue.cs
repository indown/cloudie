using Cloudie.Core.Netease.Models;

namespace Cloudie.Core.Playback;

public enum RepeatMode { Off, All, One }

/// <summary>
/// The play queue with shuffle and repeat, kept free of any player so it can be tested:
/// <see cref="Items"/> is the order the user sees, shuffle reorders a copy and remembers the
/// original order to restore it.
/// </summary>
public sealed class PlaybackQueue
{
    private readonly List<Track> _ordered = [];
    private readonly List<Track> _items = [];
    private readonly Random _random;

    public PlaybackQueue(Random? random = null) => _random = random ?? Random.Shared;

    /// <summary>A snapshot; the queue itself changes in place.</summary>
    public IReadOnlyList<Track> Items => _items.ToArray();
    public int CurrentIndex { get; private set; } = -1;
    public Track? Current => CurrentIndex >= 0 && CurrentIndex < _items.Count ? _items[CurrentIndex] : null;
    public bool IsShuffled { get; private set; }
    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;
    public int Count => _items.Count;

    /// <summary>The user's original order regardless of shuffle; with <see cref="PlayOrder"/> it is enough to persist the queue.</summary>
    public IReadOnlyList<Track> OrderedItems => _ordered.ToArray();

    /// <summary>For each entry of <see cref="Items"/>, its index in <see cref="OrderedItems"/>.</summary>
    public IReadOnlyList<int> PlayOrder => _items.Select(track => _ordered.IndexOf(track)).ToArray();

    /// <summary>
    /// Puts back a persisted queue without reshuffling. <paramref name="playOrder"/> indexes into
    /// <paramref name="ordered"/>; when it is missing or inconsistent the queue comes back unshuffled.
    /// </summary>
    public void Restore(IReadOnlyList<Track> ordered, IReadOnlyList<int>? playOrder, int currentIndex, bool shuffled)
    {
        _ordered.Clear();
        _ordered.AddRange(ordered);
        _items.Clear();
        var validOrder = shuffled && playOrder is not null && playOrder.Count == ordered.Count
                         && playOrder.All(index => index >= 0 && index < ordered.Count) && playOrder.Distinct().Count() == ordered.Count;
        _items.AddRange(validOrder ? playOrder!.Select(index => ordered[index]) : ordered);
        IsShuffled = validOrder;
        CurrentIndex = _items.Count == 0 ? -1 : Math.Clamp(currentIndex, 0, _items.Count - 1);
    }

    /// <summary>Replaces the queue and starts at <paramref name="startIndex"/> (the current song stays first in shuffle).</summary>
    public void Replace(IEnumerable<Track> tracks, int startIndex = 0)
    {
        _ordered.Clear();
        _ordered.AddRange(tracks);
        _items.Clear();
        _items.AddRange(_ordered);
        CurrentIndex = _items.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _items.Count - 1);
        if (IsShuffled) Reshuffle();
    }

    public void SetShuffle(bool shuffled)
    {
        if (shuffled == IsShuffled) return;
        IsShuffled = shuffled;
        if (shuffled)
        {
            Reshuffle();
        }
        else
        {
            var current = Current;
            _items.Clear();
            _items.AddRange(_ordered);
            CurrentIndex = current is null ? -1 : _items.FindIndex(t => t.Id == current.Id);
        }
    }

    /// <summary>Shuffles everything except the current song, which stays at the front.</summary>
    private void Reshuffle()
    {
        var current = Current;
        var rest = _ordered.Where(t => current is null || t.Id != current.Id).ToList();
        for (var i = rest.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (rest[i], rest[j]) = (rest[j], rest[i]);
        }
        _items.Clear();
        if (current is not null) _items.Add(current);
        _items.AddRange(rest);
        CurrentIndex = current is null ? (_items.Count > 0 ? 0 : -1) : 0;
    }

    public void InsertNext(Track track)
    {
        var at = CurrentIndex < 0 ? 0 : CurrentIndex + 1;
        _items.Insert(at, track);
        var orderedAt = Current is null ? 0 : _ordered.FindIndex(t => t.Id == Current.Id) + 1;
        _ordered.Insert(Math.Clamp(orderedAt, 0, _ordered.Count), track);
        if (CurrentIndex < 0) CurrentIndex = 0;
    }

    public void Append(IEnumerable<Track> tracks)
    {
        var list = tracks.ToList();
        _items.AddRange(list);
        _ordered.AddRange(list);
        if (CurrentIndex < 0 && _items.Count > 0) CurrentIndex = 0;
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count) return false;
        var track = _items[index];
        _items.RemoveAt(index);
        _ordered.Remove(track);
        if (index < CurrentIndex) CurrentIndex--;
        else if (index == CurrentIndex && CurrentIndex >= _items.Count) CurrentIndex = _items.Count - 1;
        return true;
    }

    public bool MoveTo(int index)
    {
        if (index < 0 || index >= _items.Count) return false;
        CurrentIndex = index;
        return true;
    }

    /// <summary>The index to play after the current song ends, or -1 when the queue is finished.</summary>
    public int NextIndex(bool manual = false)
    {
        if (_items.Count == 0) return -1;
        if (RepeatMode == RepeatMode.One && !manual) return CurrentIndex;
        var next = CurrentIndex + 1;
        if (next < _items.Count) return next;
        return RepeatMode == RepeatMode.Off && !manual ? -1 : 0;
    }

    /// <summary>The song that would play after the current one, without moving; null when the queue would end.</summary>
    public Track? PeekNext(bool manual = false)
    {
        var next = NextIndex(manual);
        return next >= 0 ? _items[next] : null;
    }

    public int PreviousIndex()
    {
        if (_items.Count == 0) return -1;
        var previous = CurrentIndex - 1;
        return previous >= 0 ? previous : _items.Count - 1;
    }

    public bool AdvanceToNext(bool manual = false)
    {
        var next = NextIndex(manual);
        if (next < 0) return false;
        CurrentIndex = next;
        return true;
    }

    public bool GoToPrevious()
    {
        var previous = PreviousIndex();
        if (previous < 0) return false;
        CurrentIndex = previous;
        return true;
    }
}
