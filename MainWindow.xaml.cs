using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace EventFast;

internal sealed record ParsedXmlNode(string Header, IReadOnlyList<ParsedXmlNode> Children, bool IsExpanded = false);

public partial class MainWindow : Window, IDisposable
{
    private CancellationTokenSource? _operationCancellation;
    private readonly EventCache _cache = new();
    private IReadOnlyList<ProblemGroup> _groups = [];
    private IReadOnlyList<EventRow> _rows = [];
    private IReadOnlyList<EventRow> _allRows = [];
    private string? _eventFile;
    private readonly string? _providerFilter;
    private string? _quickQuery;
    private string _selectedXml = "";
    private EventRow? _selectedRow;
    private string? _selectedMessage;
    private int _detailsLoadVersion;

    internal MainWindow(StartupOptions? options = null)
    {
        InitializeComponent();
        LanguageBox.SelectedItem = LanguageBox.Items.OfType<ComboBoxItem>()
            .First(item => item.Tag.ToString() == Localization.Instance.CurrentLanguage);
        FromDate.SelectedDate = DateTime.Today.AddDays(-1);
        ToDate.SelectedDate = DateTime.Today;
        options ??= new();
        _eventFile = options.EventFile;
        _providerFilter = options.Provider;
        _quickQuery = options.Quick;
        if (options.From is { } from && options.To is { } to)
        {
            FromDate.SelectedDate = from;
            ToDate.SelectedDate = to;
            TimeBox.SelectedItem = TimeBox.Items.OfType<ComboBoxItem>().First(item => item.Tag.ToString() == "custom");
        }
        else if (options.AllTime)
            TimeBox.SelectedItem = TimeBox.Items.OfType<ComboBoxItem>().First(item => item.Tag.ToString() == "all");
        else if (options.Today)
            TimeBox.SelectedIndex = 4;
        else if (options.Hours is { } hours)
        {
            var tag = hours.ToString(CultureInfo.InvariantCulture);
            var item = TimeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(candidate => candidate.Tag.ToString() == tag);
            if (item is null)
            {
                item = new ComboBoxItem { Tag = tag, Content = Localization.Format("LastHours", hours) };
                TimeBox.Items.Insert(TimeBox.Items.Count - 1, item);
            }
            TimeBox.SelectedItem = item;
        }
        if (options.MaximumLevel is { } maximumLevel)
            LevelBox.SelectedItem = LevelBox.Items.OfType<ComboBoxItem>().First(item => item.Tag.ToString() == maximumLevel.ToString(CultureInfo.InvariantCulture));
        if (options.Channels is { Count: > 0 } channels)
        {
            SystemBox.IsChecked = channels.Contains("System", StringComparer.OrdinalIgnoreCase);
            ApplicationBox.IsChecked = channels.Contains("Application", StringComparer.OrdinalIgnoreCase);
        }
        if (options.Sort is { } sort)
            SortBox.SelectedItem = SortBox.Items.OfType<ComboBoxItem>().First(item => item.Tag.ToString() == sort);
        SearchBox.Text = string.Join(' ', new[] { options.Query, options.EventId?.ToString(CultureInfo.InvariantCulture) }.OfType<string>());
        if (_eventFile is not null)
        {
            Title = $"EventFast — {Path.GetFileName(_eventFile)}";
            SystemBox.IsEnabled = ApplicationBox.IsEnabled = false;
            if (!options.HasTimeRange)
                TimeBox.SelectedIndex = TimeBox.Items.Count - 1;
        }
        if (options.AutoRun)
            Loaded += (_, _) => Dispatcher.BeginInvoke(() => _ = RunQueryAsync(_quickQuery is null ? null : EventQuery.QuickQueries[_quickQuery]));
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        _quickQuery = null;
        await RunQueryAsync(null);
    }

    private async void Quick_Click(object sender, RoutedEventArgs e)
    {
        _quickQuery = (string)((Button)sender).Tag;
        await RunQueryAsync(EventQuery.QuickQueries[_quickQuery]);
    }

    private async Task RunQueryAsync(QuickQuery? quick, bool refresh = false)
    {
        var selectedChannels = new[]
        {
            SystemBox.IsChecked == true ? "System" : null,
            ApplicationBox.IsChecked == true ? "Application" : null
        }.OfType<string>().ToArray();
        var channels = _eventFile is not null ? [_eventFile] : quick?.Channels is { Length: > 0 } ? quick.Channels : selectedChannels;

        if (channels.Length == 0)
        {
            StatusText.Text = Localization.Text("SelectChannel");
            return;
        }

        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        var operation = _operationCancellation = new CancellationTokenSource();
        var token = operation.Token;
        SearchButton.IsEnabled = false;
        ExportButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        StatusText.Text = quick is null ? Localization.Text("Querying") : Localization.Format("QueryingNamed", Localization.Text(quick.Name));

        try
        {
            var maximumLevel = int.Parse(((ComboBoxItem)LevelBox.SelectedItem).Tag.ToString()!, CultureInfo.InvariantCulture);
            var period = SelectedPeriod();
            var criteria = quick is null
                ? EventQuery.Parse(SearchBox.Text, maximumLevel, period, _providerFilter)
                : EventQuery.FromQuick(quick, maximumLevel, period);
            if (((ComboBoxItem)TimeBox.SelectedItem).Tag.ToString() == "custom")
            {
                if (FromDate.SelectedDate is not { } from || ToDate.SelectedDate is not { } to || from > to)
                    throw new InvalidOperationException(Localization.Text("InvalidCustomDates"));
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
                    StatusText.Text = Localization.Format("FirstBatch", previewRows.Count);
                });
            }
            var tasks = channels.Select(channel => Task.Run(() =>
            {
                try
                {
                    var key = $"{channel}\n{xpath}";
                    var cacheKey = criteria.Keyword is null ? key : $"{key}\nmessages";
                    var rows = _cache.GetOrAdd(cacheKey,
                        () => WindowsEventReader.Read(channel, xpath, token, firstBatch: ShowFirstBatch,
                            filePath: _eventFile is not null, failIfTruncated: true, includeMessage: criteria.Keyword is not null), refresh);
                    return (Rows: rows, Error: (string?)null, RequiresAdmin: false);
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
            token.ThrowIfCancellationRequested();
            _rows = rows;
            _allRows = allRows;
            _groups = groups;
            ApplySort();
            ExportButton.IsEnabled = groups.Count > 0;
            var errors = results.Count(result => result.Error is not null);
            var firstError = results.Select(result => result.Error).FirstOrDefault(error => error is not null);
            AdminButton.Visibility = results.Any(result => result.RequiresAdmin) ? Visibility.Visible : Visibility.Collapsed;
            StatusText.ToolTip = string.Join(Environment.NewLine, results.Select(result => result.Error).OfType<string>());
            StatusText.Text = Localization.Format("QuerySummary", allRows.Length, rows.Length,
                                  rows.Count(row => row.Level == "Critical"), rows.Count(row => row.Level == "Error"),
                                  rows.Count(row => row.Level == "Warning"), groups.Count) +
                              (errors > 0 ? Localization.Format("SkippedChannels", errors, firstError) : "");
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_operationCancellation, operation))
                StatusText.Text = Localization.Text("QueryCancelled");
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_operationCancellation, operation))
                StatusText.Text = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, operation))
            {
                _operationCancellation = null;
                operation.Dispose();
                SearchButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                ExportButton.IsEnabled = _groups.Count > 0;
            }
        }
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files ||
            !Path.GetExtension(files[0]).Equals(".evtx", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = Localization.Text("DropEvtx");
            return;
        }

        _eventFile = Path.GetFullPath(files[0]);
        _quickQuery = null;
        Title = $"EventFast — {Path.GetFileName(_eventFile)}";
        SystemBox.IsEnabled = ApplicationBox.IsEnabled = false;
        TimeBox.SelectedIndex = TimeBox.Items.Count - 1;
        await RunQueryAsync(null);
    }

    private void Admin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var startInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            foreach (var argument in CurrentStartupOptions().ToArguments())
                startInfo.ArgumentList.Add(argument);
            Process.Start(startInfo);
            Close();
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            StatusText.Text = Localization.Text("AdminCancelled");
        }
    }

    internal StartupOptions CurrentStartupOptions()
    {
        var time = ((ComboBoxItem)TimeBox.SelectedItem).Tag.ToString()!;
        var hours = int.TryParse(time, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedHours) ? parsedHours : (int?)null;
        return new(
            EventFile: _eventFile,
            Today: time == "today",
            Query: string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text,
            Provider: _providerFilter,
            Hours: hours,
            AllTime: time == "all",
            From: time == "custom" ? FromDate.SelectedDate : null,
            To: time == "custom" ? ToDate.SelectedDate : null,
            MaximumLevel: int.Parse(((ComboBoxItem)LevelBox.SelectedItem).Tag.ToString()!, CultureInfo.InvariantCulture),
            Channels: _eventFile is null
                ? new[] { SystemBox.IsChecked == true ? "System" : null, ApplicationBox.IsChecked == true ? "Application" : null }.OfType<string>().ToArray()
                : null,
            Sort: ((ComboBoxItem)SortBox.SelectedItem).Tag.ToString(),
            Quick: _quickQuery);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (((ComboBoxItem)ExportScopeBox.SelectedItem).Tag.ToString() == "selected" && EventsGrid.SelectedItem is not ProblemGroup)
        {
            StatusText.Text = Localization.Text("SelectProblem");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = Localization.Text("ExcelFilter"),
            FileName = $"EventFast-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx",
            DefaultExt = ".xlsx"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        ExportButton.IsEnabled = false;
        SearchButton.IsEnabled = false;
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        var operation = _operationCancellation = new CancellationTokenSource();
        CancelButton.IsEnabled = true;
        StatusText.Text = Localization.Text("Exporting");
        try
        {
            var scope = ((ComboBoxItem)ExportScopeBox.SelectedItem).Tag.ToString();
            IReadOnlyList<EventRow> rows = scope switch
            {
                "all" => _allRows,
                "selected" when EventsGrid.SelectedItem is ProblemGroup group => group.Events,
                "selected" => throw new InvalidOperationException(Localization.Text("SelectProblem")),
                _ => _rows
            };
            var groups = scope == "current" ? _groups : ProblemGrouping.Group(rows);
            var includeXml = IncludeXmlBox.IsChecked == true;
            await Task.Run(() =>
            {
                using var formatter = WindowsEventReader.CreateMessageFormatter();
                XlsxExporter.Export(dialog.FileName, groups, rows, includeXml, row => formatter.ReadContent(row, includeXml), operation.Token);
            }, operation.Token);
            StatusText.Text = Localization.Format("Exported", Path.GetFileName(dialog.FileName));
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_operationCancellation, operation))
                StatusText.Text = Localization.Text("ExportCancelled");
        }
        catch (IOException exception) when ((exception.HResult & 0xffff) == 112)
        {
            if (ReferenceEquals(_operationCancellation, operation))
                StatusText.Text = Localization.Text("DiskFull");
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_operationCancellation, operation))
                StatusText.Text = Localization.Format("ExportFailed", exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, operation))
            {
                _operationCancellation = null;
                operation.Dispose();
                SearchButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                ExportButton.IsEnabled = _groups.Count > 0;
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operationCancellation?.Cancel();

    private void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (EventsGrid is null || SortBox.SelectedItem is not ComboBoxItem item)
            return;

        ApplySort();
    }

    private void ApplySort()
    {
        if (EventsGrid is null || SortBox.SelectedItem is not ComboBoxItem item)
            return;

        EventsGrid.ItemsSource = (item.Tag.ToString() switch
        {
            "latest" => _groups.OrderByDescending(group => group.LastSeen),
            "oldest" => _groups.OrderBy(group => group.FirstSeen),
            "frequent" => _groups.OrderByDescending(group => group.Count),
            "eventId" => _groups.OrderBy(group => group.EventId),
            "provider" => _groups.OrderBy(group => group.Provider),
            _ => _groups.AsEnumerable()
        }).ToArray();
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
            await RunQueryAsync(_quickQuery is null ? null : EventQuery.QuickQueries[_quickQuery], refresh: true);
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E && ExportButton.IsEnabled)
        {
            e.Handled = true;
            Export_Click(ExportButton, new RoutedEventArgs());
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A && EventsGrid.IsKeyboardFocusWithin)
        {
            EventsGrid.SelectAll();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A && OccurrencesGrid.IsKeyboardFocusWithin)
        {
            OccurrencesGrid.SelectAll();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C &&
                 EventsGrid.IsKeyboardFocusWithin && EventsGrid.SelectedItems.Count > 0)
        {
            Clipboard.SetText(string.Join($"{Environment.NewLine}{Environment.NewLine}",
                EventsGrid.SelectedItems.OfType<ProblemGroup>().Select(FormatSummary)));
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && OccurrencesGrid.IsKeyboardFocusWithin && OccurrencesGrid.SelectedItem is EventRow occurrence)
        {
            e.Handled = true;
            await LoadSelectedDetailsAsync(occurrence);
        }
        else if (e.Key == Key.Enter && EventsGrid.SelectedItem is ProblemGroup)
        {
            e.Handled = true;
            await LoadSelectedDetailsAsync();
        }
        else if (e.Key == Key.Escape)
        {
            if (_operationCancellation is not null)
            {
                _operationCancellation.Cancel();
                e.Handled = true;
                return;
            }
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
            "all" => TimeSpan.Zero,
            _ => TimeSpan.FromHours(double.Parse(tag, CultureInfo.InvariantCulture))
        };
    }

    private void Time_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CustomTimePanel is not null && TimeBox.SelectedItem is ComboBoxItem item)
            CustomTimePanel.Visibility = item.Tag.ToString() == "custom" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is not ComboBoxItem item || item.Tag.ToString() == Localization.Instance.CurrentLanguage)
            return;

        Localization.SetLanguage(item.Tag.ToString()!);
        _groups = ProblemGrouping.Group(_rows);
        EventsGrid.SelectedItem = null;
        ApplySort();
        StatusText.Text = Localization.Text("LanguageChanged");
    }

    private void EventsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _detailsLoadVersion++;
        DetailsBox.Text = "";
        _selectedXml = "";
        _selectedRow = null;
        _selectedMessage = null;
        ParsedXmlStatus.Text = "";
        ParsedXmlTree.ItemsSource = null;
        if (EventsGrid.SelectedItem is ProblemGroup group)
        {
            OccurrencesGrid.ItemsSource = group.Events;
            OccurrencesTab.Header = Localization.Format("GroupedEventsCount", group.Count);
            DetailsTabs.SelectedIndex = 0;
        }
        else
        {
            OccurrencesGrid.ItemsSource = null;
            OccurrencesTab.Header = Localization.Text("GroupedEvents");
        }
    }

    private async void EventsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => await LoadSelectedDetailsAsync();

    private void EventsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(EventsGrid, source) is DataGridRow row)
            EventsGrid.SelectedItem = row.Item;
        else
            EventsGrid.SelectedItem = null;
    }

    private void EventsGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e) =>
        e.Handled = EventsGrid.SelectedItem is not ProblemGroup;

    private void SearchProblemOnline_Click(object sender, RoutedEventArgs e)
    {
        if (EventsGrid.SelectedItem is not ProblemGroup group)
            return;

        OpenSearch(BuildProblemSearchUri(group));
    }

    private void SearchSelectedEventOnline_Click(object sender, RoutedEventArgs e)
    {
        if (EventsGrid.SelectedItem is not ProblemGroup group)
            return;

        var row = _selectedRow ?? OccurrencesGrid.SelectedItem as EventRow ?? group.Events[^1];
        OpenSearch(BuildEventSearchUri(row, ReferenceEquals(row, _selectedRow) ? _selectedMessage : row.Message ?? row.Details));
    }

    private void OpenSearch(Uri uri)
    {
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception exception) { StatusText.Text = Localization.Format("BrowserFailed", exception.Message); }
    }

    internal static Uri BuildProblemSearchUri(ProblemGroup group) =>
        BuildEventSearchUri(group.Events[^1], $"{group.Problem} {group.Events[^1].Message ?? group.Events[^1].Details}");

    internal static Uri BuildEventSearchUri(EventRow row, string? message)
    {
        var context = string.Join(' ', (message ?? "").Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        // ponytail: search context is capped at 180 characters; widen it only if search quality proves insufficient.
        if (context.Length > 180)
            context = $"{context[..180]}…";
        var query = string.Join(' ', new[]
        {
            row.Provider, $"Event ID {row.EventId}", row.DisplayLevel,
            string.IsNullOrWhiteSpace(context) ? null : context, Localization.Text("CauseSearchSuffix")
        }.OfType<string>());
        return new Uri($"https://www.google.com/search?q={Uri.EscapeDataString(query)}");
    }

    private async void OccurrencesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OccurrencesGrid.SelectedItem is EventRow row)
            await LoadSelectedDetailsAsync(row);
    }

    private async Task LoadSelectedDetailsAsync(EventRow? selectedRow = null)
    {
        if (EventsGrid.SelectedItem is not ProblemGroup group)
            return;

        var row = selectedRow ?? group.Events[^1];
        var request = ++_detailsLoadVersion;
        _selectedRow = row;
        _selectedMessage = row.Message ?? row.Details;
        DetailsTabs.SelectedIndex = 1;
        DetailsBox.Text = Localization.Text("LoadingDetails");
        ParsedXmlStatus.Text = "";
        ParsedXmlTree.ItemsSource = null;
        (string Message, string Xml) content;
        try
        {
            content = await Task.Run(() =>
            {
                using var formatter = WindowsEventReader.CreateMessageFormatter();
                return formatter.ReadContent(row, includeXml: true);
            });
        }
        catch (Exception exception)
        {
            if (request == _detailsLoadVersion && ReferenceEquals(EventsGrid.SelectedItem, group))
                DetailsBox.Text = $"{FormatSummary(group)}{Environment.NewLine}{Environment.NewLine}{Localization.Format("LoadDetailsFailed", exception.Message)}";
            return;
        }
        if (request != _detailsLoadVersion || !ReferenceEquals(EventsGrid.SelectedItem, group))
            return;
        _selectedMessage = content.Message;
        _selectedXml = content.Xml;
        DetailsBox.Text = FormatDetails(group, row, content.Message, content.Xml);
        UpdateParsedXml(content.Xml);
    }

    private void UpdateParsedXml(string xml)
    {
        try
        {
            ParsedXmlTree.ItemsSource = ParseXmlTree(xml);
            ParsedXmlStatus.Text = "";
        }
        catch (Exception exception) when (exception is XmlException or InvalidDataException)
        {
            ParsedXmlTree.ItemsSource = null;
            ParsedXmlStatus.Text = Localization.Format("ParseXmlFailed", exception.Message);
        }
    }

    internal static IReadOnlyList<ParsedXmlNode> ParseXmlTree(string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new InvalidDataException(Localization.Text("EmptyEventXml"));
        return [ParseElement(root, true)];
    }

    private static ParsedXmlNode ParseElement(XElement element, bool expanded = false)
    {
        var children = element.Attributes().Select(attribute =>
                new ParsedXmlNode($"@{attribute.Name.LocalName} = {DisplayXmlValue(attribute.Value)}", []))
            .Concat(element.Elements().Select(child => ParseElement(child)))
            .Concat(element.Nodes().OfType<XText>().Where(text => element.HasElements && !string.IsNullOrWhiteSpace(text.Value))
                .Select(text => new ParsedXmlNode($"#text = {DisplayXmlValue(text.Value)}", [])))
            .ToArray();
        var value = !element.HasElements && !string.IsNullOrWhiteSpace(element.Value)
            ? $" = {DisplayXmlValue(element.Value)}"
            : "";
        return new ParsedXmlNode($"{element.Name.LocalName}{value}", children, expanded);
    }

    private static string DisplayXmlValue(string value)
    {
        var display = string.Join(' ', value.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        // ponytail: tree labels stop at 500 characters; raw XML remains the full-value inspector.
        return display.Length <= 500 ? display : $"{display[..500]}…";
    }

    internal static string FormatDetails(ProblemGroup group, EventRow row, string message, string xml)
    {
        var line = Environment.NewLine;
        return
            $"{Localization.Text("SummaryHeading")}{line}" +
            $"{group.Problem}{line}" +
            $"{Localization.Text("Occurrences")}: {group.Count:N0}{line}" +
            $"{Localization.Text("FirstSeen")}: {group.FirstSeen:yyyy-MM-dd HH:mm:ss}{line}" +
            $"{Localization.Text("LastSeen")}: {group.LastSeen:yyyy-MM-dd HH:mm:ss}{line}{line}" +
            $"{Localization.Text("EventInfoHeading")}{line}" +
            $"{Localization.Text("ColumnTime")}: {row.Time:yyyy-MM-dd HH:mm:ss}{line}" +
            $"{Localization.Text("ColumnSeverity")}: {row.DisplayLevel}{line}" +
            $"Event ID: {row.EventId}{line}" +
            $"{Localization.Text("Provider")}: {row.Provider}{line}" +
            $"{Localization.Text("Channel")}: {row.Channel}{line}" +
            $"{Localization.Text("Computer")}: {row.Computer}{line}" +
            $"{Localization.Text("RecordId")}: {row.RecordId}{line}{line}" +
            $"{Localization.Text("EventMessageHeading")}{line}{message.Trim()}{line}{line}" +
            $"{Localization.Text("RawXmlHeading")}{line}{xml.Trim()}";
    }

    private void CopyProblem_Click(object sender, RoutedEventArgs e)
    {
        if (EventsGrid.SelectedItem is ProblemGroup group)
            Clipboard.SetText(FormatSummary(group));
    }

    internal static string FormatSummary(ProblemGroup group) =>
        $"{group.Problem}{Environment.NewLine}" +
        $"Event ID: {group.EventId}{Environment.NewLine}" +
        $"Provider: {group.Provider}{Environment.NewLine}" +
        $"{Localization.Text("Count")}: {group.Count:N0}{Environment.NewLine}" +
        $"{Localization.Text("FirstSeen")}: {group.FirstSeen:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
        $"{Localization.Text("LastSeen")}: {group.LastSeen:yyyy-MM-dd HH:mm:ss}";

    private void CopyFull_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(DetailsBox.Text))
            Clipboard.SetText(DetailsBox.Text);
    }

    private void CopyXml_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_selectedXml))
            Clipboard.SetText(_selectedXml);
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        GC.SuppressFinalize(this);
    }
}
