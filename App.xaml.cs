using System.IO;
using System.Windows;

namespace EventFast;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

internal sealed record StartupOptions(string? EventFile, bool Today, int? Hours, int? EventId, string? Query, string? Provider)
{
    internal bool AutoRun => EventFile is not null || Today || Hours is not null || EventId is not null || Query is not null || Provider is not null;

    internal static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? eventFile = null;
        string? query = null;
        string? provider = null;
        int? hours = null;
        int? eventId = null;
        var today = false;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--today":
                    today = true;
                    break;
                case "--hours":
                    hours = PositiveInteger(args, ref index, "--hours");
                    if (hours > TimeSpan.MaxValue.TotalHours)
                        throw new ArgumentException("--hours 數值過大。");
                    break;
                case "--event-id":
                    eventId = PositiveInteger(args, ref index, "--event-id");
                    if (eventId > ushort.MaxValue)
                        throw new ArgumentException("--event-id 必須介於 1 到 65535。");
                    break;
                case "--query":
                    query = Value(args, ref index, "--query");
                    break;
                case "--provider":
                    provider = Value(args, ref index, "--provider");
                    break;
                default:
                    if (args[index].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"不支援的啟動參數：{args[index]}");
                    if (!File.Exists(args[index]) || !Path.GetExtension(args[index]).Equals(".evtx", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException($"找不到 EVTX 檔案：{args[index]}");
                    eventFile = Path.GetFullPath(args[index]);
                    break;
            }
        }

        if (today && hours is not null)
            throw new ArgumentException("--today 與 --hours 不可同時使用。");
        return new(eventFile, today, hours, eventId, query, provider);
    }

    private static int PositiveInteger(IReadOnlyList<string> args, ref int index, string option)
    {
        var value = Value(args, ref index, option);
        if (!int.TryParse(value, out var number) || number <= 0)
            throw new ArgumentException($"{option} 必須接正整數。");
        return number;
    }

    private static string Value(IReadOnlyList<string> args, ref int index, string option)
    {
        if (++index >= args.Count || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{option} 缺少值。");
        return args[index];
    }
}
