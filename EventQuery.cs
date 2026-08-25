using System.Globalization;

namespace EventFast;

internal sealed record QueryCriteria(
    int MaximumLevel,
    TimeSpan Period,
    int? EventId,
    string? Keyword,
    IReadOnlyList<string>? Providers = null,
    IReadOnlyList<QuickRule>? Rules = null,
    DateTime? From = null,
    DateTime? To = null);

internal sealed record QuickRule(string Provider, int[] EventIds);
internal sealed record QuickQuery(string Name, QuickRule[] Rules, string[]? Channels = null);

internal static class EventQuery
{
    internal static readonly IReadOnlyDictionary<string, QuickQuery> QuickQueries = new Dictionary<string, QuickQuery>
    {
        ["all"] = new("QuickAll", []),
        ["system"] = new("QuickSystem", [], ["System"]),
        ["crash"] = new("QuickCrash", [new("Application Error", [1000, 1001]), new(".NET Runtime", [1026]), new("Windows Error Reporting", [1001])], ["Application"]),
        ["disk"] = new("QuickDisk", [new("disk", [7, 11, 51, 153, 157]), new("storahci", [129]), new("stornvme", [129]), new("storport", [129]), new("Ntfs", [55, 98, 140]), new("Microsoft-Windows-Ntfs", [55, 98, 140]), new("volmgr", []), new("volsnap", []), new("partmgr", []), new("Microsoft-Windows-Kernel-Storage", [])], ["System"]),
        ["ntfs"] = new("QuickNtfs", [new("Ntfs", [55, 98, 140]), new("Microsoft-Windows-Ntfs", [55, 98, 140])], ["System"]),
        ["usb"] = new("QuickUsb", [new("USBHUB", []), new("USBXHCI", []), new("Microsoft-Windows-USB-USBHUB3", []), new("Microsoft-Windows-USB-USBXHCI", []), new("UCSI", []), new("UcmUcsiCx", []), new("Microsoft-Windows-DriverFrameworks-UserMode", [10110, 10111])], ["System", "Microsoft-Windows-DriverFrameworks-UserMode/Operational"]),
        ["device"] = new("QuickDevice", [new("Microsoft-Windows-Kernel-PnP", [219, 225, 411]), new("Kernel-PnP", [219, 225, 411])], ["System", "Microsoft-Windows-Kernel-PnP/Configuration"]),
        ["driver"] = new("QuickDriver", [new("Microsoft-Windows-Kernel-PnP", [219]), new("Service Control Manager", [7000, 7001, 7026])], ["System"]),
        ["whea"] = new("QuickWhea", [new("Microsoft-Windows-WHEA-Logger", [1, 17, 18, 19, 20, 46, 47]), new("WHEA-Logger", [1, 17, 18, 19, 20, 46, 47])], ["System"]),
        ["network"] = new("QuickNetwork", [new("Tcpip", [4201]), new("Microsoft-Windows-DNS-Client", [1014]), new("Microsoft-Windows-NetworkProfile", [10000, 10001]), new("Microsoft-Windows-WLAN-AutoConfig", [])], ["System", "Microsoft-Windows-WLAN-AutoConfig/Operational"]),
        ["update"] = new("QuickUpdate", [new("Microsoft-Windows-WindowsUpdateClient", [19, 20, 25, 31, 34])], ["System", "Microsoft-Windows-WindowsUpdateClient/Operational"]),
        ["power"] = new("QuickPower", [new("Microsoft-Windows-Kernel-Power", [41]), new("EventLog", [6008])], ["System"])
    };

    internal static QueryCriteria Parse(string search, int maximumLevel, TimeSpan period, string? provider = null)
    {
        var tokens = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var numbers = tokens.Where(token => int.TryParse(token, out _)).Select(int.Parse).ToArray();
        var words = tokens.Where(token => !int.TryParse(token, out _)).ToArray();
        return new(maximumLevel, period, numbers.Length > 0 ? numbers[0] : null,
            words.Length == 0 ? null : string.Join(' ', words), provider is null ? null : [provider]);
    }

    internal static QueryCriteria FromQuick(QuickQuery query, int maximumLevel, TimeSpan period) =>
        new(maximumLevel, period, null, null, Rules: query.Rules);

    internal static string BuildXPath(QueryCriteria criteria)
    {
        var time = criteria.From is { } from && criteria.To is { } to
            ? $"TimeCreated[@SystemTime >= '{Utc(from)}' and @SystemTime <= '{Utc(to)}']"
            : criteria.Period > TimeSpan.Zero
                ? $"TimeCreated[timediff(@SystemTime) <= {(long)criteria.Period.TotalMilliseconds}]"
                : null;
        var conditions = new List<string>();
        if (time is not null)
            conditions.Add(time);

        if (criteria.MaximumLevel > 0)
            conditions.Add($"Level > 0 and Level <= {criteria.MaximumLevel}");
        if (criteria.EventId is int id)
            conditions.Add($"EventID={id}");

        if (criteria.Providers is { Count: > 0 })
            conditions.Add($"({string.Join(" or ", criteria.Providers.Select(provider => $"Provider[@Name={Quote(provider)}]"))})");
        if (criteria.Rules is { Count: > 0 })
            conditions.Add($"({string.Join(" or ", criteria.Rules.Select(RuleXPath))})");

        return conditions.Count == 0 ? "*" : $"*[System[{string.Join(" and ", conditions)}]]";
    }

    internal static bool Matches(EventRow row, QueryCriteria criteria)
    {
        if (criteria.EventId is { } eventId && row.EventId != eventId)
            return false;

        if (criteria.Keyword is { } keyword && keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word =>
                !row.Provider.Contains(word, StringComparison.OrdinalIgnoreCase) &&
                !row.Details.Contains(word, StringComparison.OrdinalIgnoreCase) &&
                !ProblemClassifier.SearchText(row).Contains(word, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (criteria.Providers is { Count: > 0 } && !criteria.Providers.Contains(row.Provider, StringComparer.OrdinalIgnoreCase))
            return false;

        return criteria.Rules is not { Count: > 0 } || criteria.Rules.Any(rule => RuleMatches(row, rule));
    }

    private static bool RuleMatches(EventRow row, QuickRule rule) =>
        RuleMatches(row.Provider, row.EventId, rule);

    internal static bool MatchesQuick(string name, string provider, int eventId) =>
        QuickQueries[name].Rules.Any(rule => RuleMatches(provider, eventId, rule));

    private static bool RuleMatches(string provider, int eventId, QuickRule rule) =>
        provider.Equals(rule.Provider, StringComparison.OrdinalIgnoreCase) &&
        (rule.EventIds.Length == 0 || rule.EventIds.Contains(eventId));

    private static string RuleXPath(QuickRule rule)
    {
        var provider = $"Provider[@Name={Quote(rule.Provider)}]";
        return rule.EventIds.Length == 0
            ? provider
            : $"({provider} and ({string.Join(" or ", rule.EventIds.Select(id => $"EventID={id}"))}))";
    }

    private static string Quote(string value)
    {
        if (!value.Contains('\''))
            return $"'{value}'";
        if (!value.Contains('"'))
            return $"\"{value}\"";
        throw new ArgumentException(Localization.Text("ProviderQuotes"), nameof(value));
    }

    private static string Utc(DateTime value) => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);

    internal static void SelfTest()
    {
        var mixed = Parse("disk 153", 3, TimeSpan.FromHours(24));
        var xpath = BuildXPath(mixed);
        if (mixed.EventId != 153 || mixed.Keyword != "disk" || !xpath.Contains("EventID=153") || !xpath.Contains("86400000"))
            throw new InvalidOperationException("Mixed query self-test failed.");

        var quick = FromQuick(QuickQueries["power"], 2, TimeSpan.FromHours(1));
        if (!BuildXPath(quick).Contains("EventID=41") || !Matches(new(DateTime.Now, "Error", 41, "Microsoft-Windows-Kernel-Power", "System", 1, "", "", ""), quick))
            throw new InvalidOperationException("Quick query self-test failed.");

        var custom = mixed with { From = new DateTime(2026, 1, 1), To = new DateTime(2026, 1, 2) };
        if (!BuildXPath(custom).Contains($"@SystemTime >= '{Utc(custom.From!.Value)}'"))
            throw new InvalidOperationException("Custom time query self-test failed.");
    }
}
