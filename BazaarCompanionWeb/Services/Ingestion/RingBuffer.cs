namespace BazaarCompanionWeb.Services.Ingestion;

/// <summary>
/// Fixed-capacity circular buffer. Single-writer / many-reader safe via lock on writes,
/// snapshot copy on reads. Older entries are overwritten when capacity is reached.
/// </summary>
public sealed class RingBuffer<T>
{
    private readonly T[] _buffer;
    private readonly int _capacity;
    private readonly Lock _lock = new();
    private int _head;
    private int _count;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _buffer = new T[capacity];
    }

    public int Capacity => _capacity;

    public int Count
    {
        get
        {
            lock (_lock) return _count;
        }
    }

    public void Add(T item)
    {
        lock (_lock)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity) _count++;
        }
    }

    /// <summary>
    /// Returns a snapshot of buffer contents in insertion order (oldest first).
    /// </summary>
    public IReadOnlyList<T> Snapshot()
    {
        lock (_lock)
        {
            if (_count == 0) return [];
            var result = new T[_count];
            var start = (_head - _count + _capacity) % _capacity;
            for (var i = 0; i < _count; i++)
                result[i] = _buffer[(start + i) % _capacity];
            return result;
        }
    }

    /// <summary>
    /// Returns items added at or after <paramref name="cutoff"/> using a selector
    /// to extract the timestamp. Caller-supplied selector avoids forcing a timestamp interface.
    /// </summary>
    public IReadOnlyList<T> SnapshotSince(DateTime cutoff, Func<T, DateTime> timestampSelector)
    {
        var snap = Snapshot();
        var result = new List<T>(snap.Count);
        foreach (var item in snap)
        {
            if (timestampSelector(item) >= cutoff) result.Add(item);
        }
        return result;
    }
}
