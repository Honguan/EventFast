using System.Text.RegularExpressions;

namespace EventFast;

internal sealed record ProblemGroup(
    string Severity,
    string Problem,
    int Count,
    int EventId,
    string Provider,
    DateTime FirstSeen,
    DateTime LastSeen,
    string Channel,
    IReadOnlyList<EventRow> Events);

internal static class ProblemClassifier
{
    internal static string Classify(EventRow row) => Classify(row.Provider, row.EventId);

    internal static string Classify(string provider, int eventId) =>
        provider switch
        {
            var p when p.Equals("disk", StringComparison.OrdinalIgnoreCase) && eventId == 153 => "磁碟 I/O 重試",
            var p when p.Equals("disk", StringComparison.OrdinalIgnoreCase) && eventId == 51 => "磁碟 I/O 發生錯誤",
            var p when p.EndsWith("Kernel-Power", StringComparison.OrdinalIgnoreCase) && eventId == 41 => "非正常關機 / 電源異常",
            var p when p.Equals("Application Error", StringComparison.OrdinalIgnoreCase) && eventId == 1000 => "應用程式崩潰",
            var p when p.Contains("WHEA-Logger", StringComparison.OrdinalIgnoreCase) => "WHEA 硬體錯誤事件",
            _ => $"{provider} + Event ID {eventId}"
        };
}

internal static class ProblemGrouping
{
    internal static IReadOnlyList<ProblemGroup> Group(IEnumerable<EventRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows
            .GroupBy(row => (row.Provider, row.EventId, DetailsKey(row)), StringTupleComparer.Instance)
            .Select(group =>
            {
                var events = group.OrderBy(row => row.Time).ThenBy(row => row.RecordId).ToArray();
                var severity = events.OrderByDescending(row => SeverityRank(row.Level)).First().Level;
                var channels = events.Select(row => row.Channel).Where(channel => !string.IsNullOrWhiteSpace(channel)).Distinct(StringComparer.OrdinalIgnoreCase);
                return new ProblemGroup(
                    SeverityLabel(severity),
                    ProblemClassifier.Classify(events[0]),
                    events.Length,
                    events[0].EventId,
                    events[0].Provider,
                    events[0].Time,
                    events[^1].Time,
                    string.Join(", ", channels),
                    events);
            })
            .OrderByDescending(group => SeverityRank(group.Severity))
            .ThenByDescending(group => group.Count)
            .ThenByDescending(group => group.LastSeen)
            .ToArray();
    }

    internal static void SelfTest()
    {
        var now = DateTime.Now;
        var rows = new[]
        {
            new EventRow(now.AddMinutes(-2), "錯誤", 41, "Microsoft-Windows-Kernel-Power", "System", 1, "PC", "Unexpected 123", ""),
            new EventRow(now, "嚴重", 41, "Microsoft-Windows-Kernel-Power", "System", 2, "PC", "Unexpected 456", ""),
            new EventRow(now, "錯誤", 1000, "Application Error", "Application", 3, "PC", "App failed", "")
        };

        var groups = Group(rows);
        if (groups.Count != 2 || groups[0].Problem != "非正常關機 / 電源異常" || groups[0].Count != 2 || groups[0].FirstSeen != now.AddMinutes(-2))
            throw new InvalidOperationException("Problem grouping self-test failed.");
    }

    private static string DetailsKey(EventRow row)
    {
        var firstLine = row.Details
            .Split('\r', '\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0) ?? "";
        return Regex.Replace(Regex.Replace(firstLine, @"\d+", "#"), @"\s+", " ");
    }

    private static string SeverityLabel(string level) => level switch
    {
        "嚴重" => "嚴重",
        "錯誤" => "錯誤",
        "警告" => "警告",
        "資訊" => "資訊",
        "詳細" => "詳細",
        _ => "未知"
    };

    private static int SeverityRank(string level) => level switch
    {
        "嚴重" => 5,
        "錯誤" => 4,
        "警告" => 3,
        "資訊" => 2,
        "詳細" => 1,
        _ => 0
    };

    private sealed class StringTupleComparer : IEqualityComparer<(string Provider, int EventId, string Details)>
    {
        internal static readonly StringTupleComparer Instance = new();

        public bool Equals((string Provider, int EventId, string Details) x, (string Provider, int EventId, string Details) y) =>
            x.EventId == y.EventId &&
            string.Equals(x.Provider, y.Provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Details, y.Details, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Provider, int EventId, string Details) value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Provider),
                value.EventId,
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Details));
    }
}
