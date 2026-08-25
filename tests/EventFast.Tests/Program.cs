using System.IO.Compression;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EventFast;

var tests = new (string Name, Action Run)[]
{
    ("Query parser", TestParser),
    ("XPath/time/level filters", TestXPath),
    ("Keyword and Event ID filters", TestFilters),
    ("Grouping/classifier/sorting", TestGrouping),
    ("Problem summary", TestProblemSummary),
    ("Event details layout", TestEventDetailsLayout),
    ("Problem search URL", TestProblemSearchUrl),
    ("Startup arguments", TestStartupArguments),
    ("Formatted message search", TestFormattedMessageSearch),
    ("XLSX export mapping", TestExport),
    ("XLSX locked-file safety", TestLockedExport),
    ("XLSX disk-full safety", TestDiskFullExport),
    ("XLSX cancellation safety", TestCancelledExport),
    ("Unicode and long values", TestUnicode)
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

if (args.Contains("--integration"))
{
    using (var cancellation = new CancellationTokenSource())
    {
        var firstBatchSeen = false;
        AssertThrows<OperationCanceledException>(() => WindowsEventReader.Read("System", "*", cancellation.Token,
            firstBatch: _ =>
            {
                firstBatchSeen = true;
                cancellation.Cancel();
            }, includeMessage: true));
        Assert(firstBatchSeen);
    }
    Console.WriteLine("PASS In-flight native query cancellation");

    foreach (var channel in new[] { "System", "Application", "Setup" })
    {
        var firstBatchSeen = false;
        IReadOnlyList<string>? firstBatchDetails = null;
        var rows = WindowsEventReader.Read(channel,
            EventQuery.BuildXPath(new(0, TimeSpan.FromDays(30), null, null)), CancellationToken.None, 25,
            batch =>
            {
                firstBatchSeen = batch.Count > 0 && batch.All(row => row.Xml.Length == 0);
                firstBatchDetails = batch.Select(row => row.Details).ToArray();
            });
        Assert(rows.Count == 0 || firstBatchSeen && firstBatchDetails!.SequenceEqual(rows.Select(row => row.Details)));
        if (rows.Count > 0)
        {
            var sample = rows[0];
            var byId = WindowsEventReader.Read(channel,
                EventQuery.BuildXPath(new(0, TimeSpan.FromDays(30), sample.EventId, null)), CancellationToken.None, 25);
            Assert(byId.Count > 0 && byId.All(row => row.EventId == sample.EventId));

            var byProvider = WindowsEventReader.Read(channel,
                EventQuery.BuildXPath(new(0, TimeSpan.FromDays(30), null, null, [sample.Provider])), CancellationToken.None, 25);
            Assert(byProvider.Count > 0 && byProvider.All(row => row.Provider.Equals(sample.Provider, StringComparison.OrdinalIgnoreCase)));

            var byTime = WindowsEventReader.Read(channel,
                EventQuery.BuildXPath(new(0, TimeSpan.Zero, null, null, From: sample.Time.AddTicks(-1), To: sample.Time.AddTicks(1))),
                CancellationToken.None, 25);
            Assert(byTime.Any(row => row.RecordId == sample.RecordId));

            var maximumLevel = sample.Level switch { "嚴重" => 1, "錯誤" => 2, "警告" => 3, "資訊" => 4, "詳細" => 5, _ => 0 };
            var byLevel = WindowsEventReader.Read(channel,
                EventQuery.BuildXPath(new(maximumLevel, TimeSpan.FromDays(30), null, null)), CancellationToken.None, 25);
            Assert(maximumLevel == 0 || byLevel.Count > 0 &&
                byLevel.All(row => LevelNumber(row.Level) is var level && level > 0 && level <= maximumLevel));

            var expectedMessage = WindowsEventReader.ReadMessage(sample);
            IReadOnlyList<EventRow>? messageBatch = null;
            var withMessages = WindowsEventReader.Read(channel, $"*[System[EventRecordID={sample.RecordId}]]", CancellationToken.None, 1,
                firstBatch: batch => messageBatch = batch, includeMessage: true);
            Assert(withMessages.Count == 1 && withMessages[0].RecordId == sample.RecordId &&
                   (string.IsNullOrWhiteSpace(expectedMessage) || withMessages[0].Details.Contains(expectedMessage, StringComparison.Ordinal)) &&
                   messageBatch!.Select(row => row.Details).SequenceEqual(withMessages.Select(row => row.Details)));
            foreach (var row in rows)
            {
                var xml = WindowsEventReader.ReadXml(row);
                Assert(xml.Contains("<Event", StringComparison.Ordinal));
                AssertSystemFields(row, xml);
                AssertEventData(row, xml);
            }
        }
        Console.WriteLine($"PASS Native {channel} filters (Event ID, Provider, Time, Level)");
        Console.WriteLine($"PASS Native {channel} ({rows.Count} sampled)");
    }

    WindowsEventReader.Read("System", EventQuery.BuildXPath(new(3, TimeSpan.Zero, null, null,
        From: DateTime.Today.AddDays(-7), To: DateTime.Now)), CancellationToken.None, 1);
    Console.WriteLine("PASS Native custom time range");

    AssertThrows<InvalidOperationException>(() =>
        WindowsEventReader.Read("System", "*", CancellationToken.None, 1, failIfTruncated: true));
    Console.WriteLine("PASS Native result-limit mapping");

    var messageRows = WindowsEventReader.Read("System", EventQuery.BuildXPath(new(0, TimeSpan.FromDays(7), null, null)), CancellationToken.None, 10);
    using (var formatter = WindowsEventReader.CreateMessageFormatter())
        foreach (var row in messageRows)
            formatter.Format(row);
    Console.WriteLine($"PASS Cached publisher message formatting ({messageRows.Count} sampled)");
    WithPath(path =>
    {
        using var formatter = WindowsEventReader.CreateMessageFormatter();
        XlsxExporter.Export(path, ProblemGrouping.Group(messageRows), messageRows, true, row => formatter.ReadContent(row, includeXml: true));
        Assert(new FileInfo(path).Length > 0);
    });
    Console.WriteLine("PASS Native Message/XML XLSX export");

    AssertThrows<InvalidOperationException>(() => WindowsEventReader.Read("EventFast-Missing-Channel", "*", CancellationToken.None));
    Console.WriteLine("PASS Missing channel error mapping");

    var systemFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "winevt", "Logs", "System.evtx");
    try
    {
        WindowsEventReader.Read(systemFile, "*", CancellationToken.None, 1, filePath: true);
        Console.WriteLine("PASS Direct EVTX access");
    }
    catch (UnauthorizedAccessException exception)
    {
        Assert(exception.Message.Contains("權限不足"));
        Console.WriteLine("PASS Non-admin EVTX permission mapping");
    }

    var exportedFile = Path.Combine(Path.GetTempPath(), $"EventFast-Export-{Guid.NewGuid():N}.evtx");
    try
    {
        var startInfo = new ProcessStartInfo("wevtutil.exe") { UseShellExecute = false };
        startInfo.ArgumentList.Add("epl");
        startInfo.ArgumentList.Add("System");
        startInfo.ArgumentList.Add(exportedFile);
        startInfo.ArgumentList.Add("/ow:true");
        using var export = Process.Start(startInfo) ?? throw new InvalidOperationException("Cannot start wevtutil.");
        export.WaitForExit();
        Assert(export.ExitCode == 0);
        var offline = WindowsEventReader.Read(exportedFile, "*", CancellationToken.None, 1, filePath: true);
        Assert(offline.Count == 0 || offline[0].LogFilePath == exportedFile);
        if (offline.Count > 0)
            WindowsEventReader.ReadMessage(offline[0]);
        Console.WriteLine($"PASS Offline EVTX ({offline.Count} sampled)");
    }
    finally { File.Delete(exportedFile); }

    WithEvtxFile([], path => AssertFails(() => WindowsEventReader.Read(path, "*", CancellationToken.None, filePath: true)));
    WithEvtxFile([1, 2, 3, 4], path => AssertFails(() => WindowsEventReader.Read(path, "*", CancellationToken.None, filePath: true)));
    Console.WriteLine("PASS Empty/corrupt EVTX errors");
}

if (args.Contains("--excel"))
    TestExcelOpen();

if (args.Contains("--leak"))
    TestNativeLeaks(null);

var soakIndex = Array.IndexOf(args, "--soak-minutes");
if (soakIndex >= 0)
{
    if (soakIndex + 1 >= args.Length || !int.TryParse(args[soakIndex + 1], out var minutes) || minutes <= 0)
        throw new ArgumentException("--soak-minutes requires a positive integer.");
    TestNativeLeaks(TimeSpan.FromMinutes(minutes));
}

if (args.Contains("--ui"))
    TestUiQueryCompletion();

var largeEvtxIndex = Array.IndexOf(args, "--large-evtx");
if (largeEvtxIndex >= 0 && largeEvtxIndex + 1 < args.Length)
{
    var stopwatch = Stopwatch.StartNew();
    double firstBatch = 0;
    var rows = WindowsEventReader.Read(args[largeEvtxIndex + 1], "*", CancellationToken.None,
        firstBatch: _ => firstBatch = stopwatch.Elapsed.TotalMilliseconds, filePath: true);
    stopwatch.Stop();
    Assert(rows.Count > 50_000 && rows.All(row => row.Xml.Length == 0));
    var querySeconds = stopwatch.Elapsed.TotalSeconds;
    stopwatch.Restart();
    var groups = ProblemGrouping.Group(rows);
    stopwatch.Stop();
    Console.WriteLine($"PASS Large EVTX ({rows.Count:N0} events, first batch {firstBatch:F1} ms, query {querySeconds:F2} s, " +
        $"group {stopwatch.Elapsed.TotalMilliseconds:F1} ms/{groups.Count:N0} groups, {GC.GetTotalMemory(false) / 1048576d:F1} MB managed)");
}

var actualDiskFullIndex = Array.IndexOf(args, "--actual-disk-full");
if (actualDiskFullIndex >= 0)
{
    if (actualDiskFullIndex + 1 >= args.Length)
        throw new ArgumentException("--actual-disk-full requires an output path.");
    TestActualDiskFull(args[actualDiskFullIndex + 1]);
}

return;

static void TestParser()
{
    var query = EventQuery.Parse("NVIDIA crash 1000", 3, TimeSpan.FromHours(12));
    Assert(query.EventId == 1000 && query.Keyword == "NVIDIA crash" && query.Period == TimeSpan.FromHours(12));
}

static void TestXPath()
{
    var xpath = EventQuery.BuildXPath(new(2, TimeSpan.FromHours(3), 51, null));
    Assert(xpath.Contains("10800000") && xpath.Contains("Level > 0 and Level <= 2") && xpath.Contains("EventID=51"));
    var custom = EventQuery.BuildXPath(new(3, TimeSpan.Zero, null, null, From: DateTime.Today.AddDays(-7), To: DateTime.Now));
    Assert(custom.Contains("@SystemTime >=") && custom.Contains("@SystemTime <="));
    Assert(EventQuery.BuildXPath(new(0, TimeSpan.Zero, null, null)) == "*");
}

static void TestFilters()
{
    var row = Row(1000, "Application Error", "NVIDIA module crash");
    Assert(EventQuery.Matches(row, EventQuery.Parse("NVIDIA crash 1000", 3, TimeSpan.FromDays(1))));
    Assert(!EventQuery.Matches(row, EventQuery.Parse("Realtek 1000", 3, TimeSpan.FromDays(1))));
    Assert(!EventQuery.Matches(row, EventQuery.Parse("NVIDIA crash 1001", 3, TimeSpan.FromDays(1))));
    Assert(EventQuery.Matches(Row(153, "disk", "retry"), EventQuery.Parse("磁碟", 3, TimeSpan.FromDays(1))));
    var provider = new QueryCriteria(0, TimeSpan.FromHours(1), null, null, ["disk"]);
    Assert(EventQuery.BuildXPath(provider).Contains("Provider[@Name='disk']") && EventQuery.Matches(Row(153, "disk", "retry"), provider));
    Assert(!EventQuery.Matches(Row(153, "storport", "retry"), provider));
}

static void TestGrouping()
{
    var now = DateTime.Now;
    var groups = ProblemGrouping.Group([
        Row(153, "disk", "Retry sector 123", now.AddMinutes(-2), "警告"),
        Row(153, "disk", "Retry sector 123", now, "錯誤"),
        Row(153, "disk", "Retry sector 456", now, "錯誤"),
        Row(1000, "Application Error", "App failed", now, "錯誤"),
        Row(2, "Provider", "Failure\nDevice 01234567-89ab-cdef-0123-456789abcdef status 0xabcdef12", now, "錯誤"),
        Row(2, "Provider", "Failure\nDevice fedcba98-7654-3210-fedc-ba9876543210 status 0x12345678", now, "錯誤")
    ]);
    Assert(groups.Count == 5 && groups.Any(group => group.Problem == "磁碟 I/O 重試" && group.Count == 2 && group.Severity == "錯誤") &&
           groups.Count(group => group.Provider == "disk") == 2 && groups.Count(group => group.Provider == "Provider") == 2);
}

static void TestStartupArguments()
{
    var options = StartupOptions.Parse(["--hours", "24", "--event-id", "51", "--provider", "disk", "--query", "retry"]);
    Assert(options.Hours == 24 && options.EventId == 51 && options.Provider == "disk" && options.Query == "retry" && options.AutoRun);
    var criteria = EventQuery.Parse($"{options.Query} {options.EventId}", 3, TimeSpan.FromHours(options.Hours!.Value), options.Provider);
    Assert(criteria.Keyword == "retry" && criteria.EventId == 51 && criteria.Providers!.SequenceEqual(["disk"]) &&
           EventQuery.BuildXPath(criteria).Contains("Provider[@Name='disk']") &&
           EventQuery.Matches(Row(51, "disk", "retry"), criteria) && !EventQuery.Matches(Row(51, "storport", "retry disk"), criteria));
    var state = new StartupOptions(Query: "重試 路徑 153", Provider: "Application Error",
        From: new DateTime(2026, 8, 1), To: new DateTime(2026, 8, 24), MaximumLevel: 2,
        Channels: ["System"], Sort: "provider", Quick: "disk");
    var restored = StartupOptions.Parse(state.ToArguments());
    Assert(restored.Query == state.Query && restored.Provider == state.Provider && restored.From == state.From && restored.To == state.To &&
           restored.MaximumLevel == 2 && restored.Channels!.SequenceEqual(["System"]) && restored.Sort == "provider" && restored.Quick == "disk" &&
           EventQuery.Parse(restored.Query!, 2, TimeSpan.Zero, restored.Provider).EventId == 153);
    var eventFile = Path.Combine(Path.GetTempPath(), $"事件 記錄-{Guid.NewGuid():N}.evtx");
    try
    {
        File.WriteAllBytes(eventFile, []);
        var fileState = StartupOptions.Parse(new StartupOptions(EventFile: eventFile, AllTime: true, Query: "錯誤 路徑").ToArguments());
        Assert(fileState.EventFile == Path.GetFullPath(eventFile) && fileState.AllTime && fileState.Query == "錯誤 路徑");
    }
    finally { File.Delete(eventFile); }
    AssertThrows<ArgumentException>(() => StartupOptions.Parse(["--today", "--hours", "1"]));
    AssertThrows<ArgumentException>(() => StartupOptions.Parse(["--from", "2026-08-01"]));
    AssertThrows<ArgumentException>(() => StartupOptions.Parse(["--level", "4"]));
    AssertThrows<ArgumentException>(() => StartupOptions.Parse(["--channel", "Security"]));
    AssertThrows<ArgumentException>(() => StartupOptions.Parse(["--quick", "missing"]));
    AssertThrows<ArgumentException>(() => StartupOptions.Parse(["--event-id", "65536"]));
    AssertThrows<ArgumentException>(() => StartupOptions.Parse(["--query", "--today"]));
    AssertThrows<ArgumentException>(() => StartupOptions.Parse(["--unknown"]));
}

static void TestProblemSummary()
{
    var group = ProblemGrouping.Group([Row(153, "disk", "Retry sector 1", new DateTime(2026, 8, 24, 8, 14, 0))])[0];
    var summary = MainWindow.FormatSummary(group);
    Assert(summary.Contains("Event ID: 153") && summary.Contains("Provider: disk") &&
           summary.Contains("First Seen: 2026-08-24 08:14:00") && summary.Contains("Last Seen: 2026-08-24 08:14:00"));
}

static void TestEventDetailsLayout()
{
    var row = Row(17, "Microsoft-Windows-WHEA-Logger", "payload", new DateTime(2026, 8, 24, 22, 45, 57), "警告");
    var group = ProblemGrouping.Group([row])[0];
    var details = MainWindow.FormatDetails(group, row, "發生已修正的硬體錯誤。", "<Event />");
    Assert(details.Contains("事件資訊") && details.Contains("時間：2026-08-24 22:45:57") &&
           details.Contains("Event ID：17") && details.Contains("事件訊息") && details.Contains("原始 XML"));
}

static void TestProblemSearchUrl()
{
    var row = Row(17, "A&B Provider", "payload");
    var uri = MainWindow.BuildProblemSearchUri(ProblemGrouping.Group([row])[0]);
    Assert(uri.Scheme == Uri.UriSchemeHttps && uri.Host == "www.google.com" && uri.Query.Contains("%26") &&
           Uri.UnescapeDataString(uri.Query).Contains("Event ID 17 A&B Provider Windows 可能原因"));
}

static void TestCancelledExport()
{
    WithPath(path =>
    {
        File.WriteAllText(path, "original");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        AssertThrows<OperationCanceledException>(() => XlsxExporter.Export(path, [], [], cancellationToken: cancellation.Token));
        Assert(File.ReadAllText(path) == "original");
        Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, $"{Path.GetFileName(path)}.*.tmp").Any());
    });
}

static void TestFormattedMessageSearch()
{
    var row = Row(1000, "Application Error", "payload without product name");
    var enriched = row with { Details = $"{row.Details}{Environment.NewLine}NVIDIA process crashed" };
    Assert(EventQuery.Matches(enriched, EventQuery.Parse("NVIDIA crash", 3, TimeSpan.FromDays(1))));
}

static void TestExport()
{
    WithPath(path =>
    {
        var row = Row(41, "Microsoft-Windows-Kernel-Power", "Unexpected shutdown");
        var contentCalls = 0;
        XlsxExporter.Export(path, ProblemGrouping.Group([row]), [row], includeXml: true, _ =>
        {
            contentCalls++;
            return (row.Details, row.Xml);
        });
        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet2.xml")!.Open());
        var xml = reader.ReadToEnd();
        Assert(contentCalls == 1 && xml.Contains("Message") && xml.Contains("XML") && xml.Contains("Unexpected shutdown"));
    });
}

static void TestLockedExport()
{
    WithPath(path =>
    {
        File.WriteAllText(path, "original");
        var failed = false;
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try { XlsxExporter.Export(path, [], []); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { failed = true; }
        }
        Assert(failed);
        Assert(File.ReadAllText(path) == "original");
    });
}

static void TestDiskFullExport()
{
    WithPath(path =>
    {
        File.WriteAllText(path, "original");
        var temporaryPattern = $"{Path.GetFileName(path)}.*.tmp";
        IOException? failure = null;
        try { XlsxExporter.Export(path, [], FailDuringExport()); }
        catch (IOException exception) { failure = exception; }
        Assert(failure is not null && (failure.HResult & 0xffff) == 112);
        Assert(File.ReadAllText(path) == "original");
        Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, temporaryPattern).Any());
    });

    static IEnumerable<EventRow> FailDuringExport()
    {
        yield return Row(1, "Provider", "first row");
        throw new IOException("磁碟空間不足，無法匯出。", unchecked((int)0x80070070));
    }
}

static void TestActualDiskFull(string path)
{
    File.WriteAllText(path, "original");
    var temporaryPattern = $"{Path.GetFileName(path)}.*.tmp";
    IOException? failure = null;
    try { XlsxExporter.Export(path, [], Rows()); }
    catch (IOException exception) { failure = exception; }
    Assert(failure is not null && (failure.HResult & 0xffff) == 112);
    Assert(File.ReadAllText(path) == "original");
    Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, temporaryPattern).Any());
    Console.WriteLine($"PASS Actual disk-full XLSX safety (HRESULT 0x{failure!.HResult:x8})");

    static IEnumerable<EventRow> Rows()
    {
        for (var index = 0; index < 1_000_000; index++)
            yield return Row(index % 65_536, $"Provider-{index % 100}",
                $"Disk-full validation row {index:x8} {unchecked((ulong)index * 2_654_435_761UL):x16}");
    }
}

static void TestUnicode()
{
    WithPath(path =>
    {
        var row = Row(1, "中文 Provider", new string('長', 40_000));
        XlsxExporter.Export(path, ProblemGrouping.Group([row]), [row]);
        Assert(new FileInfo(path).Length > 0);
    });
}

static void TestExcelOpen()
{
    var excelType = Type.GetTypeFromProgID("Excel.Application") ?? throw new InvalidOperationException("Microsoft Excel is not installed.");
    WithPath(path =>
    {
        var row = Row(41, "Microsoft-Windows-Kernel-Power", "Excel open test");
        XlsxExporter.Export(path, ProblemGrouping.Group([row]), [row]);
        dynamic? excel = null;
        dynamic? workbook = null;
        try
        {
            excel = Activator.CreateInstance(excelType)!;
            excel.Visible = false;
            excel.DisplayAlerts = false;
            workbook = excel.Workbooks.Open(path);
            Assert((int)workbook.Worksheets.Count == 2);
            Console.WriteLine("PASS Microsoft Excel open/readback");
        }
        finally
        {
            if (workbook is not null)
            {
                workbook.Close(false);
                Marshal.FinalReleaseComObject(workbook);
            }
            if (excel is not null)
            {
                excel.Quit();
                Marshal.FinalReleaseComObject(excel);
            }
        }
    });
}

static void TestNativeLeaks(TimeSpan? duration)
{
    var xpath = EventQuery.BuildXPath(new(0, TimeSpan.FromHours(24), null, null));
    for (var warmup = 0; warmup < 10; warmup++)
    {
        var rows = WindowsEventReader.Read("System", xpath, CancellationToken.None, 1);
        if (rows.Count > 0)
            WindowsEventReader.ReadMessage(rows[0]);
    }
    GC.Collect();
    GC.WaitForPendingFinalizers();
    var process = Process.GetCurrentProcess();
    process.Refresh();
    var handles = process.HandleCount;
    var memory = process.PrivateMemorySize64;
    var stopwatch = Stopwatch.StartNew();
    var iterations = 0;
    while (duration is { } soak ? stopwatch.Elapsed < soak : iterations < 500)
    {
        var rows = WindowsEventReader.Read("System", xpath, CancellationToken.None, 1);
        if (rows.Count > 0)
            WindowsEventReader.ReadMessage(rows[0]);
        iterations++;
    }
    GC.Collect();
    GC.WaitForPendingFinalizers();
    process.Refresh();
    var handleGrowth = process.HandleCount - handles;
    var memoryGrowth = process.PrivateMemorySize64 - memory;
    Assert(handleGrowth <= 10 && memoryGrowth < 32L * 1024 * 1024);
    Console.WriteLine($"PASS Native {(duration is null ? "leak loop" : "soak")} ({iterations:N0} runs/{stopwatch.Elapsed}, " +
                      $"handles {handleGrowth:+#;-#;0}, private memory {memoryGrowth / 1048576d:+0.0;-0.0;0.0} MB)");
}

static void TestUiQueryCompletion()
{
    Exception? failure = null;
    using var finished = new ManualResetEventSlim();
    var thread = new Thread(() =>
    {
        try
        {
            var app = new Application();
            var window = new MainWindow(new(null, false, 24, null, null, null));
            var eventsGrid = (DataGrid)window.FindName("EventsGrid");
            var occurrencesGrid = (DataGrid)window.FindName("OccurrencesGrid");
            var detailsBox = (TextBox)window.FindName("DetailsBox");
            var sampleRows = new[]
            {
                Row(153, "disk", "Retry sector 1"),
                Row(153, "disk", "Retry sector 1")
            };
            var sampleGroup = ProblemGrouping.Group(sampleRows)[0];
            eventsGrid.ItemsSource = new[] { sampleGroup };
            eventsGrid.SelectedItem = sampleGroup;
            Assert(occurrencesGrid.Items.Count == 2);
            Assert(((TabItem)window.FindName("OccurrencesTab")).Header.ToString()!.Contains("2"));
            ((ComboBox)window.FindName("SortBox")).SelectedIndex = 4;
            window.Show();
            window.Hide();
            Assert(detailsBox.Padding == new Thickness(12) && detailsBox.FontSize == 14 &&
                   TextBlock.GetLineHeight(detailsBox) == 22 && detailsBox.FontFamily.Source == "Microsoft JhengHei UI");
            Assert(((ComboBoxItem)((ComboBox)window.FindName("TimeBox")).SelectedItem).Tag.ToString() == "24");
            var restartState = StartupOptions.Parse(window.CurrentStartupOptions().ToArguments());
            Assert(restartState.Hours == 24 && restartState.MaximumLevel == 3 && restartState.Sort == "eventId" &&
                   restartState.Channels!.Order().SequenceEqual(new[] { "Application", "System" }));
            var stopwatch = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) =>
            {
                var status = ((TextBlock)window.FindName("StatusText")).Text;
                var searchEnabled = ((Button)window.FindName("SearchButton")).IsEnabled;
                if (searchEnabled && !status.Contains("查詢中", StringComparison.Ordinal))
                {
                    var eventIds = eventsGrid.Items.Cast<ProblemGroup>().Select(group => group.EventId).ToArray();
                    Assert(eventIds.SequenceEqual(eventIds.Order()));
                    timer.Stop();
                    window.Close();
                    app.Shutdown();
                    Console.WriteLine($"PASS UI query completion ({stopwatch.Elapsed.TotalMilliseconds:F0} ms, {status})");
                }
                else if (stopwatch.Elapsed > TimeSpan.FromSeconds(5))
                {
                    failure = new TimeoutException($"UI query did not complete: {status}");
                    timer.Stop();
                    window.Close();
                    app.Shutdown();
                }
            };
            timer.Start();
            app.Run();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            finished.Set();
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!finished.Wait(TimeSpan.FromSeconds(10)))
        throw new TimeoutException("UI test thread did not stop.");
    if (failure is not null)
        throw failure;
}

static EventRow Row(int id, string provider, string details, DateTime? time = null, string level = "錯誤") =>
    new(time ?? DateTime.Now, level, id, provider, "System", id, "PC", details, "<Event />");

static int LevelNumber(string level) => level switch
{
    "嚴重" => 1,
    "錯誤" => 2,
    "警告" => 3,
    "資訊" => 4,
    "詳細" => 5,
    _ => 0
};

static void AssertSystemFields(EventRow row, string xml)
{
    var root = XDocument.Parse(xml).Root!;
    XNamespace ns = root.Name.Namespace;
    var system = root.Element(ns + "System")!;
    Assert(row.EventId == (int?)system.Element(ns + "EventID"));
    Assert(row.Provider == (string?)system.Element(ns + "Provider")?.Attribute("Name"));
    Assert(row.Channel == (string?)system.Element(ns + "Channel"));
    Assert(row.RecordId == (long?)system.Element(ns + "EventRecordID"));
    Assert(row.Computer == (string?)system.Element(ns + "Computer"));
}

static void AssertEventData(EventRow row, string xml)
{
    var root = XDocument.Parse(xml).Root!;
    var expected = string.Join(Environment.NewLine,
        root.Descendants().Where(element => !element.HasElements &&
                element.Ancestors().Any(ancestor => ancestor.Name.LocalName is "EventData" or "UserData"))
            .Select(element => element.Value).Where(value => !string.IsNullOrWhiteSpace(value)));
    if (row.Details != expected)
        throw new InvalidOperationException($"EventData mismatch. Expected: {expected}\nActual: {row.Details}");
}

static void WithPath(Action<string> action)
{
    var path = Path.Combine(Path.GetTempPath(), $"EventFast-Test-{Guid.NewGuid():N}.xlsx");
    try { action(path); }
    finally { File.Delete(path); }
}

static void WithEvtxFile(byte[] content, Action<string> action)
{
    var path = Path.Combine(Path.GetTempPath(), $"EventFast-Test-{Guid.NewGuid():N}.evtx");
    try
    {
        File.WriteAllBytes(path, content);
        action(path);
    }
    finally { File.Delete(path); }
}

static void Assert(bool condition)
{
    if (!condition) throw new InvalidOperationException("Assertion failed.");
}

static void AssertThrows<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static void AssertFails(Action action)
{
    try { action(); }
    catch { return; }
    throw new InvalidOperationException("Expected operation to fail.");
}
