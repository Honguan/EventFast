using System.IO.Compression;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
    ("Startup arguments", TestStartupArguments),
    ("Formatted message search", TestFormattedMessageSearch),
    ("XLSX export mapping", TestExport),
    ("XLSX locked-file safety", TestLockedExport),
    ("XLSX mid-write failure safety", TestInterruptedExport),
    ("Unicode and long values", TestUnicode)
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

if (args.Contains("--integration"))
{
    foreach (var channel in new[] { "System", "Application", "Setup" })
    {
        var firstBatchSeen = false;
        var rows = WindowsEventReader.Read(channel,
            EventQuery.BuildXPath(new(0, TimeSpan.FromDays(30), null, null)), CancellationToken.None, 1,
            batch => firstBatchSeen = batch.Count > 0);
        Assert(rows.Count == 0 || firstBatchSeen);
        if (rows.Count > 0)
        {
            WindowsEventReader.ReadMessage(rows[0]);
            Assert(WindowsEventReader.ReadXml(rows[0]).Contains("<Event", StringComparison.Ordinal));
        }
        Console.WriteLine($"PASS Native {channel} ({rows.Count} sampled)");
    }

    WindowsEventReader.Read("System", EventQuery.BuildXPath(new(3, TimeSpan.Zero, null, null,
        From: DateTime.Today.AddDays(-7), To: DateTime.Now)), CancellationToken.None, 1);
    Console.WriteLine("PASS Native custom time range");

    var messageRows = WindowsEventReader.Read("System", EventQuery.BuildXPath(new(0, TimeSpan.FromDays(7), null, null)), CancellationToken.None, 10);
    using (var formatter = WindowsEventReader.CreateMessageFormatter())
        foreach (var row in messageRows)
            formatter.Format(row);
    Console.WriteLine($"PASS Cached publisher message formatting ({messageRows.Count} sampled)");
    WithPath(path =>
    {
        using var formatter = WindowsEventReader.CreateMessageFormatter();
        XlsxExporter.Export(path, ProblemGrouping.Group(messageRows), messageRows, true, formatter.Format, WindowsEventReader.ReadXml);
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
}

static void TestFilters()
{
    var row = Row(1000, "Application Error", "NVIDIA module crash");
    Assert(EventQuery.Matches(row, EventQuery.Parse("NVIDIA crash 1000", 3, TimeSpan.FromDays(1))));
    Assert(!EventQuery.Matches(row, EventQuery.Parse("Realtek 1000", 3, TimeSpan.FromDays(1))));
}

static void TestGrouping()
{
    var now = DateTime.Now;
    var groups = ProblemGrouping.Group([
        Row(153, "disk", "Retry sector 123", now.AddMinutes(-2), "警告"),
        Row(153, "disk", "Retry sector 456", now, "錯誤"),
        Row(1000, "Application Error", "App failed", now, "錯誤")
    ]);
    Assert(groups.Count == 2 && groups[0].Problem == "磁碟 I/O 重試" && groups[0].Count == 2 && groups[0].Severity == "錯誤");
}

static void TestStartupArguments()
{
    var options = StartupOptions.Parse(["--hours", "24", "--event-id", "51", "--query", "disk"]);
    Assert(options.Hours == 24 && options.EventId == 51 && options.Query == "disk" && options.AutoRun);
    AssertThrows<ArgumentException>(() => StartupOptions.Parse(["--today", "--hours", "1"]));
    AssertThrows<ArgumentException>(() => StartupOptions.Parse(["--event-id", "65536"]));
    AssertThrows<ArgumentException>(() => StartupOptions.Parse(["--query", "--today"]));
    AssertThrows<ArgumentException>(() => StartupOptions.Parse(["--unknown"]));
}

static void TestFormattedMessageSearch()
{
    var row = Row(1000, "Application Error", "payload without product name");
    var enriched = MainWindow.AddMessages([row], _ => "NVIDIA process crashed")[0];
    Assert(EventQuery.Matches(enriched, EventQuery.Parse("NVIDIA crash", 3, TimeSpan.FromDays(1))));
}

static void TestExport()
{
    WithPath(path =>
    {
        var row = Row(41, "Microsoft-Windows-Kernel-Power", "Unexpected shutdown");
        XlsxExporter.Export(path, ProblemGrouping.Group([row]), [row], includeXml: true);
        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet2.xml")!.Open());
        var xml = reader.ReadToEnd();
        Assert(xml.Contains("Message") && xml.Contains("XML") && xml.Contains("Unexpected shutdown"));
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

static void TestInterruptedExport()
{
    WithPath(path =>
    {
        File.WriteAllText(path, "original");
        var temporaryPattern = $"{Path.GetFileName(path)}.*.tmp";
        AssertThrows<IOException>(() => XlsxExporter.Export(path, [], FailDuringExport()));
        Assert(File.ReadAllText(path) == "original");
        Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, temporaryPattern).Any());
    });

    static IEnumerable<EventRow> FailDuringExport()
    {
        yield return Row(1, "Provider", "first row");
        throw new IOException("Simulated disk-full write failure.");
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
            var window = new MainWindow(new(null, false, 24, null, null));
            window.Show();
            window.Hide();
            Assert(((ComboBoxItem)((ComboBox)window.FindName("TimeBox")).SelectedItem).Tag.ToString() == "24");
            var stopwatch = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) =>
            {
                var status = ((TextBlock)window.FindName("StatusText")).Text;
                var searchEnabled = ((Button)window.FindName("SearchButton")).IsEnabled;
                if (searchEnabled && !status.Contains("查詢中", StringComparison.Ordinal))
                {
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
