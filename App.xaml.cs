using System.IO;
using System.Globalization;
using System.Windows;

namespace EventFast;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Localization.Initialize();

        if (e.Args.Contains("--self-test"))
        {
            EventQuery.SelfTest();
            ProblemGrouping.SelfTest();
            XlsxExporter.SelfTest();
            EventCache.SelfTest();
            var rows = WindowsEventReader.Read("System", EventQuery.BuildXPath(new(0, TimeSpan.FromHours(24), null, null)), CancellationToken.None, 1);
            if (rows.Count > 0)
                WindowsEventReader.ReadMessage(rows[0]);
            WindowsEventReader.Read("System", EventQuery.BuildXPath(EventQuery.FromQuick(EventQuery.QuickQueries["power"], 3, TimeSpan.FromDays(7))), CancellationToken.None, 1);
            Shutdown();
            return;
        }

        try
        {
            new MainWindow(StartupOptions.Parse(e.Args)).Show();
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(exception.Message, "EventFast", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(2);
        }
    }
}

internal sealed record StartupOptions(
    string? EventFile = null,
    bool Today = false,
    int? Hours = null,
    int? EventId = null,
    string? Query = null,
    string? Provider = null,
    bool AllTime = false,
    DateTime? From = null,
    DateTime? To = null,
    int? MaximumLevel = null,
    IReadOnlyList<string>? Channels = null,
    string? Sort = null,
    string? Quick = null)
{
    internal bool AutoRun => EventFile is not null || Today || Hours is not null || EventId is not null || Query is not null || Provider is not null ||
                             AllTime || From is not null || MaximumLevel is not null || Channels is not null || Sort is not null || Quick is not null;

    internal bool HasTimeRange => Today || Hours is not null || AllTime || From is not null;

    internal static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? eventFile = null;
        string? query = null;
        string? provider = null;
        int? hours = null;
        int? eventId = null;
        int? maximumLevel = null;
        DateTime? from = null;
        DateTime? to = null;
        var channels = new List<string>();
        string? sort = null;
        string? quick = null;
        var today = false;
        var allTime = false;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--today":
                    today = true;
                    break;
                case "--all-time":
                    allTime = true;
                    break;
                case "--hours":
                    hours = PositiveInteger(args, ref index, "--hours");
                    if (hours > TimeSpan.MaxValue.TotalHours)
                        throw new ArgumentException(Localization.Text("HoursTooLarge"));
                    break;
                case "--event-id":
                    eventId = PositiveInteger(args, ref index, "--event-id");
                    if (eventId > ushort.MaxValue)
                        throw new ArgumentException(Localization.Text("EventIdRange"));
                    break;
                case "--query":
                    query = Value(args, ref index, "--query");
                    break;
                case "--provider":
                    provider = Value(args, ref index, "--provider");
                    break;
                case "--from":
                    from = Date(args, ref index, "--from");
                    break;
                case "--to":
                    to = Date(args, ref index, "--to");
                    break;
                case "--level":
                    if (!int.TryParse(Value(args, ref index, "--level"), NumberStyles.None, CultureInfo.InvariantCulture, out var level) || level is < 0 or > 3)
                        throw new ArgumentException(Localization.Text("LevelRange"));
                    maximumLevel = level;
                    break;
                case "--channel":
                    var channel = Value(args, ref index, "--channel");
                    if (channel is not "System" and not "Application")
                        throw new ArgumentException(Localization.Text("ChannelRange"));
                    if (!channels.Contains(channel, StringComparer.OrdinalIgnoreCase))
                        channels.Add(channel);
                    break;
                case "--sort":
                    sort = Value(args, ref index, "--sort");
                    if (sort is not ("default" or "latest" or "oldest" or "frequent" or "eventId" or "provider"))
                        throw new ArgumentException(Localization.Text("InvalidSort"));
                    break;
                case "--quick":
                    quick = Value(args, ref index, "--quick");
                    if (!EventQuery.QuickQueries.ContainsKey(quick))
                        throw new ArgumentException(Localization.Text("InvalidQuick"));
                    break;
                default:
                    if (args[index].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException(Localization.Format("UnsupportedArgument", args[index]));
                    if (!File.Exists(args[index]) || !Path.GetExtension(args[index]).Equals(".evtx", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException(Localization.Format("EvtxNotFound", args[index]));
                    eventFile = Path.GetFullPath(args[index]);
                    break;
            }
        }

        if ((from is null) != (to is null))
            throw new ArgumentException(Localization.Text("FromToTogether"));
        if (from is { } start && to is { } end && start > end)
            throw new ArgumentException(Localization.Text("FromAfterTo"));
        if ((today ? 1 : 0) + (hours is not null ? 1 : 0) + (allTime ? 1 : 0) + (from is not null ? 1 : 0) > 1)
            throw new ArgumentException(Localization.Text("ConflictingTime"));
        return new(eventFile, today, hours, eventId, query, provider, allTime, from, to, maximumLevel,
            channels.Count == 0 ? null : channels, sort, quick);
    }

    internal IReadOnlyList<string> ToArguments()
    {
        var args = new List<string>();
        if (EventFile is not null)
            args.Add(EventFile);
        AddFlag(args, Today, "--today");
        AddFlag(args, AllTime, "--all-time");
        AddValue(args, "--hours", Hours);
        AddValue(args, "--event-id", EventId);
        AddValue(args, "--query", Query);
        AddValue(args, "--provider", Provider);
        AddValue(args, "--from", From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddValue(args, "--to", To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddValue(args, "--level", MaximumLevel);
        foreach (var channel in Channels ?? [])
            AddValue(args, "--channel", channel);
        AddValue(args, "--sort", Sort);
        AddValue(args, "--quick", Quick);
        return args;
    }

    private static int PositiveInteger(IReadOnlyList<string> args, ref int index, string option)
    {
        var value = Value(args, ref index, option);
        if (!int.TryParse(value, out var number) || number <= 0)
            throw new ArgumentException(Localization.Format("PositiveInteger", option));
        return number;
    }

    private static DateTime Date(IReadOnlyList<string> args, ref int index, string option)
    {
        if (!DateTime.TryParseExact(Value(args, ref index, option), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            throw new ArgumentException(Localization.Format("DateFormat", option));
        return date;
    }

    private static void AddFlag(List<string> args, bool value, string option)
    {
        if (value)
            args.Add(option);
    }

    private static void AddValue(List<string> args, string option, object? value)
    {
        if (value is null)
            return;
        args.Add(option);
        args.Add(Convert.ToString(value, CultureInfo.InvariantCulture)!);
    }

    private static string Value(IReadOnlyList<string> args, ref int index, string option)
    {
        if (++index >= args.Count || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException(Localization.Format("MissingValue", option));
        return args[index];
    }
}
