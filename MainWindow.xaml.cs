using System.Windows;
using System.Windows.Controls;

namespace EventFast;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _queryCancellation;

    public MainWindow() => InitializeComponent();

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        var channels = new[]
        {
            SystemBox.IsChecked == true ? "System" : null,
            ApplicationBox.IsChecked == true ? "Application" : null
        }.OfType<string>().ToArray();

        if (channels.Length == 0)
        {
            StatusText.Text = "請至少選擇一個 Channel。";
            return;
        }

        _queryCancellation?.Cancel();
        _queryCancellation?.Dispose();
        _queryCancellation = new CancellationTokenSource();
        var token = _queryCancellation.Token;
        SearchButton.IsEnabled = false;
        StatusText.Text = "查詢中…";

        try
        {
            var maximumLevel = int.Parse(((ComboBoxItem)LevelBox.SelectedItem).Tag.ToString()!);
            var xpath = EventQuery.BuildXPath(EventQuery.Parse(SearchBox.Text, maximumLevel));
            var tasks = channels.Select(channel => Task.Run(() => WindowsEventReader.Read(channel, xpath, token), token));
            var rows = (await Task.WhenAll(tasks)).SelectMany(result => result).OrderByDescending(row => row.Time).ToList();
            EventsGrid.ItemsSource = rows;
            StatusText.Text = $"找到 {rows.Count:N0} 筆事件";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "查詢已取消。";
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private void EventsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DetailsBox.Text = EventsGrid.SelectedItem is EventRow row
            ? $"{row.Time:G}\n{row.Level} · Event {row.EventId} · {row.Provider}\n{row.Channel} · {row.Computer} · Record {row.RecordId}\n\n{row.Details}\n\n{row.Xml}"
            : "";
    }

    protected override void OnClosed(EventArgs e)
    {
        _queryCancellation?.Cancel();
        _queryCancellation?.Dispose();
        base.OnClosed(e);
    }
}
