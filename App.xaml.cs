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

        var eventFile = e.Args.FirstOrDefault(argument => File.Exists(argument) && Path.GetExtension(argument).Equals(".evtx", StringComparison.OrdinalIgnoreCase));
        new MainWindow(eventFile).Show();
    }
}
