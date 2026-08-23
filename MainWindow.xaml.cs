using System.ComponentModel;
using System.Diagnostics;
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
    private IReadOnlyList<EventRow> _allRows = [];
    private string? _eventFile;

    public MainWindow(string? eventFile = null)
    {
        InitializeComponent();
        FromDate.SelectedDate = DateTime.Today.AddDays(-1);
        ToDate.SelectedDate = DateTime.Today;
        _eventFile = eventFile;
        if (_eventFile is not null)
        {
            Title = $"EventFast — {Path.GetFileName(_eventFile)}";
            SystemBox.IsEnabled = ApplicationBox.IsEnabled = false;
            Loaded += async (_, _) => await RunQueryAsync(null);
        }
    }

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
        var channels = _eventFile is not null ? [_eventFile] : quick?.Channels is { Length: > 0 } ? quick.Channels : selectedChannels;

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
            if (((ComboBoxItem)TimeBox.SelectedItem).Tag.ToString() == "custom")
            {
                if (FromDate.SelectedDate is not { } from || ToDate.SelectedDate is not { } to || from > to)
                    throw new InvalidOperationException("請選擇有效的自訂起訖日期。");
                criteria = criteria with { From = from, To = to.AddDays(1).AddTicks(-1) };
            }
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
                        () => WindowsEventReader.Read(channel, xpath, token, firstBatch: ShowFirstBatch, filePath: _eventFile is not null)), Error: (string?)null, RequiresAdmin: false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return (Rows: (IReadOnlyList<EventRow>)[], Error: exception.Message, RequiresAdmin: exception is UnauthorizedAccessException);
                }
            }, token));
            var results = await Task.WhenAll(tasks);
            var allRows = results.SelectMany(result => result.Rows).ToArray();
            var rows = allRows.Where(row => EventQuery.Matches(row, criteria)).ToArray();
            token.ThrowIfCancellationRequested();
            var groups = ProblemGrouping.Group(rows);
            _rows = rows;
            _allRows = allRows;
            _groups = groups;
            EventsGrid.ItemsSource = groups;
            ExportButton.IsEnabled = groups.Count > 0;
            var errors = results.Count(result => result.Error is not null);
            AdminButton.Visibility = results.Any(result => result.RequiresAdmin) ? Visibility.Visible : Visibility.Collapsed;
            StatusText.ToolTip = string.Join(Environment.NewLine, results.Select(result => result.Error).OfType<string>());
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

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files ||
            !Path.GetExtension(files[0]).Equals(".evtx", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "請拖入 .evtx 事件記錄檔。";
            return;
        }

        _eventFile = Path.GetFullPath(files[0]);
        Title = $"EventFast — {Path.GetFileName(_eventFile)}";
        SystemBox.IsEnabled = ApplicationBox.IsEnabled = false;
        await RunQueryAsync(null);
    }

    private void Admin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            StatusText.Text = "已取消以系統管理員身分重新啟動。";
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (((ComboBoxItem)ExportScopeBox.SelectedItem).Tag.ToString() == "selected" && EventsGrid.SelectedItem is not ProblemGroup)
        {
            StatusText.Text = "請先選取一個問題。";
            return;
        }

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
            var scope = ((ComboBoxItem)ExportScopeBox.SelectedItem).Tag.ToString();
            IReadOnlyList<EventRow> rows = scope switch
            {
                "all" => _allRows,
                "selected" when EventsGrid.SelectedItem is ProblemGroup group => group.Events,
                "selected" => throw new InvalidOperationException("請先選取一個問題。"),
                _ => _rows
            };
            var groups = scope == "current" ? _groups : ProblemGrouping.Group(rows);
            var includeXml = IncludeXmlBox.IsChecked == true;
            await Task.Run(() =>
            {
                using var formatter = WindowsEventReader.CreateMessageFormatter();
                XlsxExporter.Export(dialog.FileName, groups, rows, includeXml, formatter.Format);
            });
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
        return tag switch
        {
            "today" => DateTime.Now - DateTime.Today,
            "custom" => TimeSpan.FromDays(1),
            _ => TimeSpan.FromHours(double.Parse(tag))
        };
    }

    private void Time_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CustomTimePanel is not null && TimeBox.SelectedItem is ComboBoxItem item)
            CustomTimePanel.Visibility = item.Tag.ToString() == "custom" ? Visibility.Visible : Visibility.Collapsed;
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

    private void CopyProblem_Click(object sender, RoutedEventArgs e)
    {
        if (EventsGrid.SelectedItem is ProblemGroup group)
            Clipboard.SetText($"{group.Problem} · Event {group.EventId} · {group.Count:N0} 次 · {group.Provider}");
    }

    private void CopyFull_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(DetailsBox.Text))
            Clipboard.SetText(DetailsBox.Text);
    }

    private void CopyXml_Click(object sender, RoutedEventArgs e)
    {
        if (EventsGrid.SelectedItem is ProblemGroup group)
            Clipboard.SetText(group.Events[^1].Xml);
    }

    protected override void OnClosed(EventArgs e)
    {
        _queryCancellation?.Cancel();
        _queryCancellation?.Dispose();
        base.OnClosed(e);
    }
}
