using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace EventFast;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _queryCancellation;
    private readonly EventCache _cache = new();
    private IReadOnlyList<ProblemGroup> _groups = [];
    private IReadOnlyList<EventRow> _rows = [];

    public MainWindow() => InitializeComponent();

    private async void Search_Click(object sender, RoutedEventArgs e) => await RunQueryAsync(null);

    private async void Quick_Click(object sender, RoutedEventArgs e) =>
        await RunQueryAsync(EventQuery.QuickQueries[(string)((Button)sender).Tag]);

    private async Task RunQueryAsync(QuickQuery? quick)
    {
        var selectedChannels = new[]
        {
            SystemBox.IsChecked == true ? "System" : null,
            ApplicationBox.IsChecked == true ? "Application" : null
        }.OfType<string>().ToArray();
        var channels = quick?.Channels is { Length: > 0 } ? quick.Channels : selectedChannels;

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
        StatusText.Text = quick is null ? "查詢中…" : $"查詢「{quick.Name}」…";

        try
        {
            var maximumLevel = int.Parse(((ComboBoxItem)LevelBox.SelectedItem).Tag.ToString()!);
            var period = SelectedPeriod();
            var criteria = quick is null
                ? EventQuery.Parse(SearchBox.Text, maximumLevel, period)
                : EventQuery.FromQuick(quick, maximumLevel, period);
            var xpath = EventQuery.BuildXPath(criteria);
            var previewRows = new List<EventRow>();
            void ShowFirstBatch(IReadOnlyList<EventRow> batch)
            {
                var filtered = batch.Where(row => EventQuery.Matches(row, criteria)).ToArray();
                Dispatcher.BeginInvoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;
                    previewRows.AddRange(filtered);
                    EventsGrid.ItemsSource = ProblemGrouping.Group(previewRows);
                    StatusText.Text = $"已顯示第一批 {previewRows.Count:N0} 筆 · 背景查詢中…";
                });
            }
            var tasks = channels.Select(channel => Task.Run(() =>
            {
                try
                {
                    return (Rows: _cache.GetOrAdd($"{channel}\n{xpath}",
                        () => WindowsEventReader.Read(channel, xpath, token, firstBatch: ShowFirstBatch)), Error: (string?)null);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return (Rows: (IReadOnlyList<EventRow>)[], Error: exception.Message);
                }
            }, token));
            var results = await Task.WhenAll(tasks);
            var rows = results.SelectMany(result => result.Rows)
                .Where(row => EventQuery.Matches(row, criteria)).ToArray();
            token.ThrowIfCancellationRequested();
            var groups = ProblemGrouping.Group(rows);
            _rows = rows;
            _groups = groups;
            EventsGrid.ItemsSource = groups;
            ExportButton.IsEnabled = groups.Count > 0;
            var errors = results.Count(result => result.Error is not null);
            var levels = $"嚴重 {rows.Count(row => row.Level == "嚴重"):N0} · 錯誤 {rows.Count(row => row.Level == "錯誤"):N0} · 警告 {rows.Count(row => row.Level == "警告"):N0}";
            StatusText.Text = $"符合 {rows.Length:N0} 筆 · {levels} · 合併 {groups.Count:N0} 類" + (errors > 0 ? $" · 略過 {errors} 個 Channel" : "");
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

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Excel 活頁簿 (*.xlsx)|*.xlsx",
            FileName = $"EventFast-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx",
            DefaultExt = ".xlsx"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        ExportButton.IsEnabled = false;
        StatusText.Text = "正在匯出 Excel…";
        try
        {
            // ponytail: publisher metadata is reopened per row; cache handles only if export profiling proves this is the bottleneck.
            await Task.Run(() => XlsxExporter.Export(dialog.FileName, _groups, _rows, messageFactory: WindowsEventReader.ReadMessage));
            StatusText.Text = $"已匯出 {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"匯出失敗：{exception.Message}";
        }
        finally
        {
            ExportButton.IsEnabled = _groups.Count > 0;
        }
    }

    private void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (EventsGrid is null || SortBox.SelectedItem is not ComboBoxItem item)
            return;

        IEnumerable<ProblemGroup> sorted = item.Tag.ToString() switch
        {
            "latest" => _groups.OrderByDescending(group => group.LastSeen),
            "frequent" => _groups.OrderByDescending(group => group.Count),
            "eventId" => _groups.OrderBy(group => group.EventId),
            "provider" => _groups.OrderBy(group => group.Provider),
            _ => _groups.AsEnumerable()
        };
        EventsGrid.ItemsSource = sorted.ToArray();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            e.Handled = true;
            await RunQueryAsync(null);
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E && ExportButton.IsEnabled)
        {
            e.Handled = true;
            Export_Click(ExportButton, new RoutedEventArgs());
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C && EventsGrid.SelectedItem is ProblemGroup group)
        {
            Clipboard.SetText($"{group.Problem} · Event {group.EventId} · {group.Count:N0} 次 · {group.Provider}");
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EventsGrid.SelectedItem = null;
            DetailsBox.Clear();
            e.Handled = true;
        }
    }

    private TimeSpan SelectedPeriod()
    {
        var tag = ((ComboBoxItem)TimeBox.SelectedItem).Tag.ToString()!;
        return tag == "today" ? DateTime.Now - DateTime.Today : TimeSpan.FromHours(double.Parse(tag));
    }

    private async void EventsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EventsGrid.SelectedItem is not ProblemGroup group)
        {
            DetailsBox.Text = "";
            return;
        }

        var row = group.Events[^1];
        DetailsBox.Text = "正在載入完整事件訊息…";
        var message = await Task.Run(() => WindowsEventReader.ReadMessage(row));
        if (!ReferenceEquals(EventsGrid.SelectedItem, group))
            return;
        DetailsBox.Text =
            $"{group.Problem}\n發生 {group.Count:N0} 次 · 首次 {group.FirstSeen:G} · 最後 {group.LastSeen:G}\n\n" +
            $"{row.Time:G}\n{row.Level} · Event {row.EventId} · {row.Provider}\n" +
            $"{row.Channel} · {row.Computer} · Record {row.RecordId}\n\n{message}\n\n{row.Xml}";
    }

    protected override void OnClosed(EventArgs e)
    {
        _queryCancellation?.Cancel();
        _queryCancellation?.Dispose();
        base.OnClosed(e);
    }
}
