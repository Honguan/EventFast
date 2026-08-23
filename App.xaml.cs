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
            WindowsEventReader.Read("System", EventQuery.BuildXPath(new(0, null, null)), CancellationToken.None, 1);
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }
}
