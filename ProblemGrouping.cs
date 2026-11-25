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
        EventQuery.ProblemRules.FirstOrDefault(rule => EventQuery.RuleMatches(provider, eventId, rule))?.Problem;
}

internal static partial class ProblemGrouping
{
    // Labels are the only language-dependent part of a group, so a language switch rebuilds them without regrouping.
    internal static ProblemGroup Relabel(ProblemGroup group) => group with
    {
        Severity = Localization.Level(group.Events.MaxBy(row => SeverityRank(row.Level))!.Level),
        Problem = ProblemClassifier.Classify(group.Events[0])
    };

    internal static ProblemGroup[] RelabelAll(IEnumerable<ProblemGroup> groups) => DefaultOrder(groups.Select(Relabel));

    private static ProblemGroup[] DefaultOrder(IEnumerable<ProblemGroup> groups) => groups
        .OrderByDescending(group => group.Events.Max(row => SeverityRank(row.Level)))
        .ThenByDescending(group => group.Count)
        .ThenByDescending(group => group.LastSeen)
        .ToArray();

    internal static IReadOnlyList<ProblemGroup> Group(IEnumerable<EventRow> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        cancellationToken.ThrowIfCancellationRequested();

        var groups = DefaultOrder(rows
            .GroupBy(row =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return (row.Provider, row.EventId, DetailsKey(row));
            }, StringTupleComparer.Instance)
            .Select(group =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var events = group.OrderBy(row => row.Time).ThenBy(row => row.RecordId).ToArray();
                var severity = events.MaxBy(row => SeverityRank(row.Level))!.Level;
                var channels = events.Select(row => row.Channel).Where(channel => !string.IsNullOrWhiteSpace(channel)).Distinct(StringComparer.OrdinalIgnoreCase);
                return new ProblemGroup(
                    Localization.Level(severity),
                    ProblemClassifier.Classify(events[0]),
                    events.Length,
                    events[0].EventId,
                    events[0].Provider,
                    events[0].Time,
                    events[^1].Time,
                    string.Join(", ", channels),
                    events);
            }));
        cancellationToken.ThrowIfCancellationRequested();
        return groups;
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
        var codes = HexCodeRegex().Matches(row.Details).Select(match => match.Value)
            .Concat(NamedCodeRegex().Matches(row.Details).Select(match => match.Groups["value"].Value))
            .Select(code => code.ToUpperInvariant()).Distinct().Order().ToArray();
        if (row.Provider.Equals("Microsoft-Windows-WindowsUpdateClient", StringComparison.OrdinalIgnoreCase))
            return string.Join('|', codes);

        var value = WhitespaceRegex().Replace(row.Details.Trim(), " ");
        value = GuidRegex().Replace(value, "<guid>");
        value = TimestampRegex().Replace(value, "<time>");
        value = IdentifierRegex().Replace(value, "$1 <n>");
        value = TempPathRegex().Replace(value, "<temp>");
        value = AnyHexRegex().Replace(value, "<code>");
        value = NumberRegex().Replace(value, "<n>");
        return $"{value}|{string.Join('|', codes)}";
    }

    [GeneratedRegex(@"(?i)\b0x[\da-f]+\b")]
    private static partial Regex HexCodeRegex();
    [GeneratedRegex(@"(?i)\b(?:error|status|code|hresult)\s*[:=]?\s*(?<value>\d+)")]
    private static partial Regex NamedCodeRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
    [GeneratedRegex(@"(?i)\b[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}\b")]
    private static partial Regex GuidRegex();
    [GeneratedRegex(@"\b\d{4}[-/]\d{1,2}[-/]\d{1,2}(?:[ T]\d{1,2}:\d{2}(?::\d{2}(?:\.\d+)?)?)?\b")]
    private static partial Regex TimestampRegex();
    [GeneratedRegex(@"(?i)\b(process|thread|record|sequence)\s*(?:id)?\s*[:=]?\s*\d+\b")]
    private static partial Regex IdentifierRegex();
    [GeneratedRegex(@"(?i)(?:[A-Z]:\\[^\r\n]*?\\(?:Temp|Tmp)\\|/tmp/)\S+")]
    private static partial Regex TempPathRegex();
    [GeneratedRegex(@"(?i)0x[\da-f]+")]
    private static partial Regex AnyHexRegex();
    [GeneratedRegex(@"\b\d+\b")]
    private static partial Regex NumberRegex();

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
