using System.Diagnostics;
using EventFast;

var cases = new[]
{
    ("System 24h warning+", "System", new QueryCriteria(3, TimeSpan.FromHours(24), null, null)),
    ("System 30d Event 51", "System", new QueryCriteria(0, TimeSpan.FromDays(30), 51, null)),
    ("System 30d Event 153", "System", new QueryCriteria(0, TimeSpan.FromDays(30), 153, null)),
    ("Application 30d Event 1000", "Application", new QueryCriteria(0, TimeSpan.FromDays(30), 1000, null)),
    ("System 30d disk providers", "System", EventQuery.FromQuick(EventQuery.QuickQueries["disk"], 0, TimeSpan.FromDays(30)))
};

Console.WriteLine("Case\tEvents\tFirst batch ms\tTotal ms\tCPU ms\tPeak RAM MB\tEvents/second");
foreach (var benchmark in cases)
{
    var process = Process.GetCurrentProcess();
    var cpu = process.TotalProcessorTime;
    var stopwatch = Stopwatch.StartNew();
    double firstBatch = 0;
    var rows = WindowsEventReader.Read(benchmark.Item2, EventQuery.BuildXPath(benchmark.Item3), CancellationToken.None,
        firstBatch: _ => firstBatch = stopwatch.Elapsed.TotalMilliseconds);
    stopwatch.Stop();
    process.Refresh();
    Console.WriteLine($"{benchmark.Item1}\t{rows.Count}\t{firstBatch:F1}\t{stopwatch.Elapsed.TotalMilliseconds:F1}\t" +
        $"{(process.TotalProcessorTime - cpu).TotalMilliseconds:F1}\t{process.PeakWorkingSet64 / 1048576d:F1}\t" +
        $"{rows.Count / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001):F0}");
}

var keywordCriteria = EventQuery.Parse("disk", 3, TimeSpan.FromHours(24));
var keywordWatch = Stopwatch.StartNew();
var keywordRows = WindowsEventReader.Read("System", EventQuery.BuildXPath(keywordCriteria), CancellationToken.None, includeMessage: true)
    .Where(row => EventQuery.Matches(row, keywordCriteria)).ToArray();
keywordWatch.Stop();
Console.WriteLine($"System 24h keyword disk\t{keywordRows.Length}\t-\t{keywordWatch.Elapsed.TotalMilliseconds:F1}\t-\t-\t" +
                  $"{keywordRows.Length / Math.Max(keywordWatch.Elapsed.TotalSeconds, 0.001):F0}");

var cache = new EventCache();
var cacheCriteria = new QueryCriteria(3, TimeSpan.FromHours(24), null, null);
var cacheKey = EventQuery.BuildXPath(cacheCriteria);
var cacheWatch = Stopwatch.StartNew();
cache.GetOrAdd(cacheKey, () => WindowsEventReader.Read("System", cacheKey, CancellationToken.None));
var cold = cacheWatch.Elapsed.TotalMilliseconds;
cacheWatch.Restart();
cache.GetOrAdd(cacheKey, () => throw new InvalidOperationException("Warm cache miss."));
cacheWatch.Stop();
Console.WriteLine($"System 24h cache cold/warm\t-\t-\t{cold:F1}/{cacheWatch.Elapsed.TotalMilliseconds:F3}\t-\t-\t-");

if (args.Contains("--large"))
{
    Console.WriteLine("Synthetic events\tGroup ms\tGroups\tExport ms\tExport/s\tXLSX MB\tManaged RAM MB");
    foreach (var count in new[] { 10_000, 100_000, 500_000, 1_000_000 })
    {
        var rows = Enumerable.Range(0, count).Select(index => new EventRow(
            DateTime.Now.AddSeconds(-index), index % 20 == 0 ? "嚴重" : index % 3 == 0 ? "警告" : "錯誤",
            1000 + index % 50, $"Provider-{index % 10}", "System", index, "PC", $"Synthetic event {index}", "<Event />")).ToArray();
        var stopwatch = Stopwatch.StartNew();
        var groups = ProblemGrouping.Group(rows);
        stopwatch.Stop();
        var groupTime = stopwatch.Elapsed.TotalMilliseconds;
        var path = Path.Combine(Path.GetTempPath(), $"EventFast-Benchmark-{count}.xlsx");
        try
        {
            stopwatch.Restart();
            XlsxExporter.Export(path, groups, rows);
            stopwatch.Stop();
            Console.WriteLine($"{count}\t{groupTime:F1}\t{groups.Count}\t{stopwatch.Elapsed.TotalMilliseconds:F1}\t" +
                $"{count / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001):F0}\t{new FileInfo(path).Length / 1048576d:F1}\t" +
                $"{GC.GetTotalMemory(false) / 1048576d:F1}");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
