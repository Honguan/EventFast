namespace EventFast;

internal sealed record QueryCriteria(int MaximumLevel, int? EventId, string? Provider);

internal static class EventQuery
{
    internal static QueryCriteria Parse(string search, int maximumLevel)
    {
        search = search.Trim();
        return int.TryParse(search, out var id)
            ? new(maximumLevel, id, null)
            : new(maximumLevel, null, string.IsNullOrWhiteSpace(search) ? null : search);
    }

    internal static string BuildXPath(QueryCriteria criteria)
    {
        var conditions = new List<string>
        {
            "TimeCreated[timediff(@SystemTime) <= 86400000]"
        };

        if (criteria.MaximumLevel > 0)
            conditions.Add($"Level > 0 and Level <= {criteria.MaximumLevel}");
        if (criteria.EventId is int id)
            conditions.Add($"EventID={id}");
        if (criteria.Provider is { } provider)
            conditions.Add($"Provider[@Name={Quote(provider)}]");

        return $"*[System[{string.Join(" and ", conditions)}]]";
    }

    private static string Quote(string value)
    {
        if (!value.Contains('\''))
            return $"'{value}'";
        if (!value.Contains('"'))
            return $"\"{value}\"";
        throw new ArgumentException("Provider 不可同時包含單引號與雙引號。", nameof(value));
    }

    internal static void SelfTest()
    {
        var byId = BuildXPath(Parse("153", 3));
        if (!byId.Contains("EventID=153") || !byId.Contains("Level > 0 and Level <= 3"))
            throw new InvalidOperationException("Event ID query self-test failed.");

        var byProvider = BuildXPath(Parse("Application Error", 0));
        if (!byProvider.Contains("Provider[@Name='Application Error']") || byProvider.Contains("Level >"))
            throw new InvalidOperationException("Provider query self-test failed.");
    }
}
