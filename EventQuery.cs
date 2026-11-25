using System.Globalization;

namespace EventFast;

internal sealed record QueryCriteria(
    int MaximumLevel,
    TimeSpan Period,
    IReadOnlyList<int>? EventIds,
    string? Keyword,
    IReadOnlyList<string>? Providers = null,
    IReadOnlyList<QuickRule>? Rules = null,
    DateTime? From = null,
    DateTime? To = null);

// Problem is the localization key ProblemClassifier shows for events this rule matches.
internal sealed record QuickRule(string Provider, int[] EventIds, string Problem);
internal sealed record QuickQuery(string Name, QuickRule[] Rules, string[]? Channels = null);

internal static class EventQuery
{
    // Declared before QuickQueries, which reads it during static initialization.
    private static readonly QuickRule[] NtfsRules =
    [
        new("Ntfs", [55], "ProblemNtfsCorruption"), new("Ntfs", [98, 140], "ProblemNtfsEvent"),
        new("Microsoft-Windows-Ntfs", [55], "ProblemNtfsCorruption"), new("Microsoft-Windows-Ntfs", [98, 140], "ProblemNtfsEvent")
    ];

    private static QuickRule[] PnpRules(string provider) =>
        [new(provider, [219], "ProblemDriverLoadFailure"), new(provider, [225], "ProblemDeviceRemoval"), new(provider, [411], "ProblemDeviceStartFailure")];

    internal static readonly IReadOnlyDictionary<string, QuickQuery> QuickQueries = new Dictionary<string, QuickQuery>
    {
        ["all"] = new("QuickAll", []),
        ["system"] = new("QuickSystem", [], ["System"]),
        ["crash"] = new("QuickCrash", [new("Application Error", [1000], "ProblemAppCrash"), new("Application Error", [1001], "ProblemAppFailure"), new(".NET Runtime", [1026], "ProblemDotNetFailure"), new("Windows Error Reporting", [1001], "ProblemWerReport")], ["Application"]),
        ["disk"] = new("QuickDisk", [new("disk", [153], "ProblemDiskRetry"), new("disk", [51], "ProblemDiskError"), new("disk", [7, 11, 157], "ProblemStorageError"), new("storahci", [129], "ProblemStorageTimeout"), new("stornvme", [129], "ProblemStorageTimeout"), new("storport", [129], "ProblemStorageTimeout"), .. NtfsRules, new("volmgr", [], "ProblemStorageEvent"), new("volsnap", [], "ProblemStorageEvent"), new("partmgr", [], "ProblemStorageEvent"), new("Microsoft-Windows-Kernel-Storage", [], "ProblemStorageEvent")], ["System"]),
        ["ntfs"] = new("QuickNtfs", NtfsRules, ["System"]),
        ["usb"] = new("QuickUsb", [new("USBHUB", [], "ProblemUsbEvent"), new("USBXHCI", [], "ProblemUsbEvent"), new("Microsoft-Windows-USB-USBHUB3", [], "ProblemUsbEvent"), new("Microsoft-Windows-USB-USBXHCI", [], "ProblemUsbEvent"), new("UCSI", [], "ProblemUsbEvent"), new("UcmUcsiCx", [], "ProblemUsbEvent"), new("Microsoft-Windows-DriverFrameworks-UserMode", [10110, 10111], "ProblemUsbEvent")], ["System", "Microsoft-Windows-DriverFrameworks-UserMode/Operational"]),
        ["device"] = new("QuickDevice", [.. PnpRules("Microsoft-Windows-Kernel-PnP"), .. PnpRules("Kernel-PnP")], ["System", "Microsoft-Windows-Kernel-PnP/Configuration"]),
        ["driver"] = new("QuickDriver", [new("Microsoft-Windows-Kernel-PnP", [219], "ProblemDriverLoadFailure"), new("Service Control Manager", [7000, 7001, 7026], "ProblemDriverLoadFailure")], ["System"]),
        ["whea"] = new("QuickWhea", [new("Microsoft-Windows-WHEA-Logger", [1, 17, 18, 19, 20, 46, 47], "ProblemWheaEvent"), new("WHEA-Logger", [1, 17, 18, 19, 20, 46, 47], "ProblemWheaEvent")], ["System"]),
        ["network"] = new("QuickNetwork", [new("Tcpip", [4201], "ProblemNetworkEvent"), new("Microsoft-Windows-DNS-Client", [1014], "ProblemDnsTimeout"), new("Microsoft-Windows-NetworkProfile", [10000, 10001], "ProblemNetworkEvent"), new("Microsoft-Windows-WLAN-AutoConfig", [], "ProblemNetworkEvent")], ["System", "Microsoft-Windows-WLAN-AutoConfig/Operational"]),
        ["update"] = new("QuickUpdate", [new("Microsoft-Windows-WindowsUpdateClient", [19], "ProblemUpdateSuccess"), new("Microsoft-Windows-WindowsUpdateClient", [20, 25, 31], "ProblemUpdateFailure"), new("Microsoft-Windows-WindowsUpdateClient", [34], "ProblemUpdateEvent")], ["System", "Microsoft-Windows-WindowsUpdateClient/Operational"]),
        ["power"] = new("QuickPower", [new("Microsoft-Windows-Kernel-Power", [41], "ProblemUnexpectedShutdown"), new("Kernel-Power", [41], "ProblemUnexpectedShutdown"), new("EventLog", [6008], "ProblemUnexpectedShutdown")], ["System"])
    };

    // Every quick rule, used to name problems; no two rules for the same provider and Event ID name different problems.
    internal static readonly QuickRule[] ProblemRules = QuickQueries.Values.SelectMany(query => query.Rules).ToArray();

    internal static QueryCriteria Parse(string search, int maximumLevel, TimeSpan period, string? provider = null)
    {
        var tokens = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var numbers = tokens.Where(token => int.TryParse(token, out _)).Select(int.Parse).ToArray();
        var words = tokens.Where(token => !int.TryParse(token, out _)).ToArray();
        return new(maximumLevel, period, numbers.Length > 0 ? numbers.Distinct().ToArray() : null,
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
        if (criteria.EventIds is { Count: > 0 } ids)
            // The event log rejects a flat chain of more than 23 "or" terms, so long ID lists are nested in groups of 20.
            conditions.Add($"({string.Join(" or ", ids.Chunk(20).Select(chunk => $"({string.Join(" or ", chunk.Select(id => $"EventID={id}"))})"))})");

        if (criteria.Providers is { Count: > 0 })
            conditions.Add($"({string.Join(" or ", criteria.Providers.Select(provider => $"Provider[@Name={Quote(provider)}]"))})");
        if (criteria.Rules is { Count: > 0 })
            conditions.Add($"({string.Join(" or ", criteria.Rules.Select(RuleXPath))})");

        return conditions.Count == 0 ? "*" : $"*[System[{string.Join(" and ", conditions)}]]";
    }

    internal static bool Matches(EventRow row, QueryCriteria criteria)
    {
        if (criteria.EventIds is { Count: > 0 } eventIds && !eventIds.Contains(row.EventId))
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

    internal static bool RuleMatches(string provider, int eventId, QuickRule rule) =>
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
        if (mixed.EventIds is not [153] || mixed.Keyword != "disk" || !xpath.Contains("EventID=153") || !xpath.Contains("86400000"))
            throw new InvalidOperationException("Mixed query self-test failed.");

        var quick = FromQuick(QuickQueries["power"], 2, TimeSpan.FromHours(1));
        if (!BuildXPath(quick).Contains("EventID=41") || !Matches(new(DateTime.Now, "Error", 41, "Microsoft-Windows-Kernel-Power", "System", 1, "", "", ""), quick))
            throw new InvalidOperationException("Quick query self-test failed.");

        var custom = mixed with { From = new DateTime(2026, 1, 1), To = new DateTime(2026, 1, 2) };
        if (!BuildXPath(custom).Contains($"@SystemTime >= '{Utc(custom.From!.Value)}'"))
            throw new InvalidOperationException("Custom time query self-test failed.");
    }
}
