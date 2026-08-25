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

    internal static string Classify(string provider, int eventId)
    {
        var key = ClassificationKey(provider, eventId);
        return key is null ? Localization.Format("ProblemFallback", provider, eventId) : Localization.Format(key, eventId);
    }

    internal static string SearchText(EventRow row)
    {
        var key = ClassificationKey(row.Provider, row.EventId);
        return key is null
            ? $"{Localization.FormatForLanguage("ProblemFallback", "en", row.Provider, row.EventId)} " +
              Localization.FormatForLanguage("ProblemFallback", "zh-TW", row.Provider, row.EventId)
            : $"{Localization.FormatForLanguage(key, "en", row.EventId)} {Localization.FormatForLanguage(key, "zh-TW", row.EventId)}";
    }

    private static string? ClassificationKey(string provider, int eventId) =>
        provider switch
        {
            var p when p.Equals("disk", StringComparison.OrdinalIgnoreCase) && eventId == 153 => "ProblemDiskRetry",
            var p when p.Equals("disk", StringComparison.OrdinalIgnoreCase) && eventId == 51 => "ProblemDiskError",
            var p when p.Equals("disk", StringComparison.OrdinalIgnoreCase) && eventId is 7 or 11 or 157 => "ProblemStorageError",
            var p when (p.Equals("storahci", StringComparison.OrdinalIgnoreCase) || p.Equals("stornvme", StringComparison.OrdinalIgnoreCase) || p.Equals("storport", StringComparison.OrdinalIgnoreCase)) && eventId == 129 => "ProblemStorageTimeout",
            var p when (p.Equals("Ntfs", StringComparison.OrdinalIgnoreCase) || p.Equals("Microsoft-Windows-Ntfs", StringComparison.OrdinalIgnoreCase)) && eventId == 55 => "ProblemNtfsCorruption",
            var p when (p.Equals("Ntfs", StringComparison.OrdinalIgnoreCase) || p.Equals("Microsoft-Windows-Ntfs", StringComparison.OrdinalIgnoreCase)) && eventId is 98 or 140 => "ProblemNtfsEvent",
            var p when EventQuery.MatchesQuick("disk", p, eventId) => "ProblemStorageEvent",
            var p when p.EndsWith("Kernel-Power", StringComparison.OrdinalIgnoreCase) && eventId == 41 => "ProblemUnexpectedShutdown",
            var p when p.Equals("EventLog", StringComparison.OrdinalIgnoreCase) && eventId == 6008 => "ProblemUnexpectedShutdown",
            var p when p.Equals("Application Error", StringComparison.OrdinalIgnoreCase) && eventId == 1000 => "ProblemAppCrash",
            var p when p.Equals("Application Error", StringComparison.OrdinalIgnoreCase) && eventId == 1001 => "ProblemAppFailure",
            var p when p.Equals(".NET Runtime", StringComparison.OrdinalIgnoreCase) && eventId == 1026 => "ProblemDotNetFailure",
            var p when p.Equals("Windows Error Reporting", StringComparison.OrdinalIgnoreCase) && eventId == 1001 => "ProblemWerReport",
            var p when EventQuery.MatchesQuick("whea", p, eventId) => "ProblemWheaEvent",
            var p when (p.Equals("Microsoft-Windows-Kernel-PnP", StringComparison.OrdinalIgnoreCase) || p.Equals("Kernel-PnP", StringComparison.OrdinalIgnoreCase)) && eventId == 219 => "ProblemDriverLoadFailure",
            var p when (p.Equals("Microsoft-Windows-Kernel-PnP", StringComparison.OrdinalIgnoreCase) || p.Equals("Kernel-PnP", StringComparison.OrdinalIgnoreCase)) && eventId == 225 => "ProblemDeviceRemoval",
            var p when (p.Equals("Microsoft-Windows-Kernel-PnP", StringComparison.OrdinalIgnoreCase) || p.Equals("Kernel-PnP", StringComparison.OrdinalIgnoreCase)) && eventId == 411 => "ProblemDeviceStartFailure",
            var p when p.Equals("Service Control Manager", StringComparison.OrdinalIgnoreCase) && eventId is 7000 or 7001 or 7026 => "ProblemDriverLoadFailure",
            var p when p.Equals("Microsoft-Windows-WindowsUpdateClient", StringComparison.OrdinalIgnoreCase) && eventId == 19 => "ProblemUpdateSuccess",
            var p when p.Equals("Microsoft-Windows-WindowsUpdateClient", StringComparison.OrdinalIgnoreCase) && eventId is 20 or 25 or 31 => "ProblemUpdateFailure",
            var p when p.Equals("Microsoft-Windows-WindowsUpdateClient", StringComparison.OrdinalIgnoreCase) && eventId == 34 => "ProblemUpdateEvent",
            var p when p.Equals("Microsoft-Windows-DNS-Client", StringComparison.OrdinalIgnoreCase) && eventId == 1014 => "ProblemDnsTimeout",
            var p when p.Equals("Tcpip", StringComparison.OrdinalIgnoreCase) && eventId == 4201 => "ProblemNetworkEvent",
            var p when EventQuery.MatchesQuick("network", p, eventId) => "ProblemNetworkEvent",
            var p when EventQuery.MatchesQuick("usb", p, eventId) => "ProblemUsbEvent",
            _ => null
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
            .OrderByDescending(group => group.Events.Max(row => SeverityRank(row.Level)))
            .ThenByDescending(group => group.Count)
            .ThenByDescending(group => group.LastSeen)
            .ToArray();
    }

    internal static void SelfTest()
    {
        var now = DateTime.Now;
        var rows = new[]
        {
            new EventRow(now.AddMinutes(-2), "Error", 41, "Microsoft-Windows-Kernel-Power", "System", 1, "PC", "Unexpected 123", ""),
            new EventRow(now, "Critical", 41, "Microsoft-Windows-Kernel-Power", "System", 2, "PC", "Unexpected 123", ""),
            new EventRow(now, "Error", 1000, "Application Error", "Application", 3, "PC", "App failed", "")
        };

        var groups = Group(rows);
        if (groups.Count != 2 || groups[0].Problem != Localization.Text("ProblemUnexpectedShutdown") || groups[0].Count != 2 || groups[0].FirstSeen != now.AddMinutes(-2))
            throw new InvalidOperationException("Problem grouping self-test failed.");
    }

    private static string DetailsKey(EventRow row)
    {
        var codes = Regex.Matches(row.Details, @"(?i)\b0x[\da-f]+\b").Select(match => match.Value)
            .Concat(Regex.Matches(row.Details, @"(?i)\b(?:error|status|code|hresult)\s*[:=]?\s*(?<value>\d+)")
                .Select(match => match.Groups["value"].Value))
            .Select(code => code.ToUpperInvariant()).Distinct().Order().ToArray();
        if (row.Provider.Equals("Microsoft-Windows-WindowsUpdateClient", StringComparison.OrdinalIgnoreCase))
            return string.Join('|', codes);

        var value = Regex.Replace(row.Details.Trim(), @"\s+", " ");
        value = Regex.Replace(value, @"(?i)\b[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}\b", "<guid>");
        value = Regex.Replace(value, @"\b\d{4}[-/]\d{1,2}[-/]\d{1,2}(?:[ T]\d{1,2}:\d{2}(?::\d{2}(?:\.\d+)?)?)?\b", "<time>");
        value = Regex.Replace(value, @"(?i)\b(process|thread|record|sequence)\s*(?:id)?\s*[:=]?\s*\d+\b", "$1 <n>");
        value = Regex.Replace(value, @"(?i)(?:[A-Z]:\\[^\r\n]*?\\(?:Temp|Tmp)\\|/tmp/)\S+", "<temp>");
        value = Regex.Replace(value, @"(?i)0x[\da-f]+", "<code>");
        value = Regex.Replace(value, @"\b\d+\b", "<n>");
        return $"{value}|{string.Join('|', codes)}";
    }

    private static string SeverityLabel(string level) => level switch
    {
        "Critical" => Localization.Level("Critical"),
        "Error" => Localization.Level("Error"),
        "Warning" => Localization.Level("Warning"),
        "Information" => Localization.Level("Information"),
        "Verbose" => Localization.Level("Verbose"),
        _ => Localization.Level("Unknown")
    };

    private static int SeverityRank(string level) => level switch
    {
        "Critical" => 5,
        "Error" => 4,
        "Warning" => 3,
        "Information" => 2,
        "Verbose" => 1,
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
