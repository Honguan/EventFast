using System.Globalization;

namespace EventFast;

internal sealed record QueryCriteria(
    int MaximumLevel,
    TimeSpan Period,
    int? EventId,
    string? Keyword,
    IReadOnlyList<string>? Providers = null,
    IReadOnlyList<int>? EventIds = null,
    DateTime? From = null,
    DateTime? To = null);

internal sealed record QuickQuery(string Name, string[] Providers, int[] EventIds, string[]? Channels = null);

internal static class EventQuery
{
    internal static readonly IReadOnlyDictionary<string, QuickQuery> QuickQueries = new Dictionary<string, QuickQuery>
    {
        ["all"] = new("全部問題", [], []),
        ["system"] = new("系統錯誤", [], [], ["System"]),
        ["crash"] = new("程式崩潰", ["Application Error", ".NET Runtime", "Windows Error Reporting"], [1000, 1001, 1026], ["Application"]),
        ["disk"] = new("磁碟 / SSD / NVMe", ["disk", "storahci", "stornvme", "storport", "Ntfs", "Microsoft-Windows-Ntfs", "volmgr", "volsnap", "partmgr", "Microsoft-Windows-Kernel-Storage"], [7, 11, 51, 55, 98, 129, 140, 153, 157], ["System"]),
        ["ntfs"] = new("NTFS / 檔案系統", ["Ntfs", "Microsoft-Windows-Ntfs"], [55, 98, 140], ["System"]),
        ["usb"] = new("USB / USB-C", ["USBHUB", "USBXHCI", "Microsoft-Windows-USB-USBHUB3", "Microsoft-Windows-USB-USBXHCI", "UCSI", "UcmUcsiCx", "Microsoft-Windows-DriverFrameworks-UserMode"], [10110, 10111], ["System", "Microsoft-Windows-DriverFrameworks-UserMode/Operational"]),
        ["device"] = new("裝置", ["Microsoft-Windows-Kernel-PnP", "Kernel-PnP"], [219, 225, 411], ["System", "Microsoft-Windows-Kernel-PnP/Configuration"]),
        ["driver"] = new("驅動程式", ["Microsoft-Windows-Kernel-PnP", "Service Control Manager"], [219, 7000, 7001, 7026], ["System"]),
        ["whea"] = new("硬體 / WHEA", ["Microsoft-Windows-WHEA-Logger", "WHEA-Logger"], [1, 17, 18, 19, 20, 46, 47], ["System"]),
        ["network"] = new("網路", ["Tcpip", "Microsoft-Windows-DNS-Client", "Microsoft-Windows-NetworkProfile"], [4201, 1014, 10000, 10001], ["System", "Microsoft-Windows-WLAN-AutoConfig/Operational"]),
        ["update"] = new("Windows Update", ["Microsoft-Windows-WindowsUpdateClient"], [19, 20, 25, 31, 34], ["System", "Microsoft-Windows-WindowsUpdateClient/Operational"]),
        ["power"] = new("電源 / 異常關機", ["Microsoft-Windows-Kernel-Power", "EventLog"], [41, 6008], ["System"])
    };

    internal static QueryCriteria Parse(string search, int maximumLevel, TimeSpan period)
    {
        var tokens = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var numbers = tokens.Where(token => int.TryParse(token, out _)).Select(int.Parse).ToArray();
        var words = tokens.Where(token => !int.TryParse(token, out _)).ToArray();
        return new(maximumLevel, period, numbers.Length > 0 ? numbers[0] : null,
            words.Length == 0 ? null : string.Join(' ', words));
    }

    internal static QueryCriteria FromQuick(QuickQuery query, int maximumLevel, TimeSpan period) =>
        new(maximumLevel, period, null, null, query.Providers, query.EventIds);

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

        var quick = (criteria.EventIds ?? []).Select(id => $"EventID={id}")
            .Concat((criteria.Providers ?? []).Select(provider => $"Provider[@Name={Quote(provider)}]")).ToArray();
        if (quick.Length > 0)
            conditions.Add($"({string.Join(" or ", quick)})");

        return conditions.Count == 0 ? "*" : $"*[System[{string.Join(" and ", conditions)}]]";
    }

    internal static bool Matches(EventRow row, QueryCriteria criteria)
    {
        if (criteria.EventId is { } eventId && row.EventId != eventId)
            return false;

        if (criteria.Keyword is { } keyword && keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word =>
                !row.Provider.Contains(word, StringComparison.OrdinalIgnoreCase) &&
                !row.Details.Contains(word, StringComparison.OrdinalIgnoreCase) &&
                !ProblemClassifier.Classify(row).Contains(word, StringComparison.OrdinalIgnoreCase)))
            return false;

        if ((criteria.Providers?.Count ?? 0) + (criteria.EventIds?.Count ?? 0) == 0)
            return true;

        return (criteria.EventIds?.Contains(row.EventId) ?? false) ||
               (criteria.Providers?.Contains(row.Provider, StringComparer.OrdinalIgnoreCase) ?? false);
    }

    private static string Quote(string value)
    {
        if (!value.Contains('\''))
            return $"'{value}'";
        if (!value.Contains('"'))
            return $"\"{value}\"";
        throw new ArgumentException("Provider 不可同時包含單引號與雙引號。", nameof(value));
    }

    private static string Utc(DateTime value) => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);

    internal static void SelfTest()
    {
        var mixed = Parse("disk 153", 3, TimeSpan.FromHours(24));
        var xpath = BuildXPath(mixed);
        if (mixed.EventId != 153 || mixed.Keyword != "disk" || !xpath.Contains("EventID=153") || !xpath.Contains("86400000"))
            throw new InvalidOperationException("Mixed query self-test failed.");

        var quick = FromQuick(QuickQueries["power"], 2, TimeSpan.FromHours(1));
        if (!BuildXPath(quick).Contains("EventID=41") || !Matches(new(DateTime.Now, "錯誤", 41, "x", "System", 1, "", "", ""), quick))
            throw new InvalidOperationException("Quick query self-test failed.");

        var custom = mixed with { From = new DateTime(2026, 1, 1), To = new DateTime(2026, 1, 2) };
        if (!BuildXPath(custom).Contains($"@SystemTime >= '{Utc(custom.From!.Value)}'"))
            throw new InvalidOperationException("Custom time query self-test failed.");
    }
}
