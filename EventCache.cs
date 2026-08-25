namespace EventFast;

internal sealed class EventCache
{
    private readonly Dictionary<string, Entry> _entries = [];
    private readonly long _limit = Math.Clamp(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes * 8 / 100, 256L << 20, 1L << 30);
    private readonly object _gate = new();
    private long _size;

    internal IReadOnlyList<EventRow> GetOrAdd(string key, Func<IReadOnlyList<EventRow>> factory, bool bypassCache = false)
    {
        lock (_gate)
        {
            if (!bypassCache && _entries.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.Created < TimeSpan.FromMinutes(2))
                return cached.Rows;
        }

        var rows = factory();
        var entry = new Entry(rows, rows.Sum(Estimate), DateTime.UtcNow);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var previous))
                _size -= previous.Size;
            _entries[key] = entry;
            _size += entry.Size;
            // ponytail: clear-all eviction is enough for a two-channel cache; use LRU only when more channels land.
            if (_size > _limit)
            {
                _entries.Clear();
                _size = 0;
            }
        }
        return rows;
    }

    private static long Estimate(EventRow row) =>
        128L + (row.Provider.Length + row.Channel.Length + row.Computer.Length + row.Details.Length + row.Xml.Length) * sizeof(char);

    internal static void SelfTest()
    {
        var cache = new EventCache();
        var calls = 0;
        IReadOnlyList<EventRow> Factory()
        {
            calls++;
            return [];
        }
        cache.GetOrAdd("x", Factory);
        cache.GetOrAdd("x", Factory);
        cache.GetOrAdd("x", Factory, bypassCache: true);
        if (calls != 2)
            throw new InvalidOperationException("Event cache self-test failed.");
    }

    private sealed record Entry(IReadOnlyList<EventRow> Rows, long Size, DateTime Created);
}
