using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Markup;

namespace EventFast;

public sealed class Localization : INotifyPropertyChanged
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["Search"] = "Search", ["Cancel"] = "Cancel", ["ExportExcel"] = "Export Excel",
        ["ExportCurrent"] = "Current results", ["ExportSelected"] = "Selected problem", ["ExportAll"] = "All events",
        ["IncludeXml"] = "Include XML", ["SearchTip"] = "Search keywords, Event ID, or mixed criteria (for example, disk 153)",
        ["Time"] = "Time:", ["Last1Hour"] = "Last hour", ["Last3Hours"] = "Last 3 hours", ["Last6Hours"] = "Last 6 hours",
        ["Last12Hours"] = "Last 12 hours", ["LastHours"] = "Last {0:N0} hours", ["Today"] = "Today", ["Last24Hours"] = "Last 24 hours",
        ["Last3Days"] = "Last 3 days", ["Last7Days"] = "Last 7 days", ["Last30Days"] = "Last 30 days",
        ["Custom"] = "Custom", ["AllTime"] = "All time", ["To"] = "to", ["Level"] = "Level:",
        ["CriticalOnly"] = "Critical only", ["ErrorsAndAbove"] = "Errors and above", ["WarningsAndAbove"] = "Warnings and above", ["All"] = "All",
        ["RestartAdmin"] = "Restart as administrator", ["Sort"] = "Sort:", ["Default"] = "Default", ["Latest"] = "Latest",
        ["Oldest"] = "Oldest", ["MostFrequent"] = "Most frequent", ["Language"] = "Language:",
        ["QuickAll"] = "All problems", ["QuickSystem"] = "System errors", ["QuickCrash"] = "Application crashes",
        ["QuickDisk"] = "Disk / SSD / NVMe", ["QuickNtfs"] = "NTFS / file system", ["QuickUsb"] = "USB / USB-C / UCSI",
        ["QuickDevice"] = "Device / Kernel-PnP", ["QuickDriver"] = "Drivers", ["QuickWhea"] = "Hardware / WHEA",
        ["QuickNetwork"] = "Network", ["QuickUpdate"] = "Windows Update", ["QuickPower"] = "Power / unexpected shutdown",
        ["ActiveFilters"] = "Active filters: {0}", ["FilterKeyword"] = "Keyword: {0}", ["FilterProvider"] = "Provider: {0}",
        ["FilterChannels"] = "Channels: {0}", ["FilterSort"] = "Sort: {0}",
        ["SearchPossibleCauses"] = "Search possible causes", ["EventId"] = "Event ID", ["ColumnSeverity"] = "Severity", ["ColumnProblem"] = "Problem",
        ["ColumnCount"] = "Count", ["ColumnSource"] = "Source", ["ColumnLastSeen"] = "Last seen",
        ["ProblemDetails"] = "Problem details", ["GroupedEvents"] = "Group Events", ["ColumnTime"] = "Time",
        ["EventContent"] = "Event content", ["CopyProblem"] = "Copy problem", ["CopyFull"] = "Copy full content", ["CopyXml"] = "Copy XML",
        ["ParsedXml"] = "Parsed XML", ["ParseXmlFailed"] = "Could not parse XML: {0}",
        ["LevelCritical"] = "Critical", ["LevelError"] = "Error", ["LevelWarning"] = "Warning",
        ["LevelInformation"] = "Information", ["LevelVerbose"] = "Verbose", ["LevelUnknown"] = "Unknown",
        ["ProblemDiskRetry"] = "Disk I/O retry", ["ProblemDiskError"] = "Disk I/O error",
        ["ProblemUnexpectedShutdown"] = "Unexpected shutdown / power failure", ["ProblemAppCrash"] = "Application crash",
        ["ProblemFallback"] = "{0} + Event ID {1}",
        ["ProblemStorageError"] = "Disk / storage I/O error", ["ProblemStorageTimeout"] = "Storage controller timeout",
        ["ProblemStorageEvent"] = "Storage event", ["ProblemAppFailure"] = "Application failure",
        ["ProblemNtfsCorruption"] = "NTFS file-system corruption", ["ProblemNtfsEvent"] = "NTFS file-system event",
        ["ProblemDotNetFailure"] = ".NET Runtime failure", ["ProblemWerReport"] = "Windows Error Reporting event",
        ["ProblemWheaEvent"] = "WHEA hardware event (Event ID {0})", ["ProblemDriverLoadFailure"] = "Device / driver load failure",
        ["ProblemDeviceRemoval"] = "Device removal blocked", ["ProblemDeviceStartFailure"] = "Device start failure",
        ["ProblemUpdateSuccess"] = "Windows Update installation succeeded", ["ProblemUpdateFailure"] = "Windows Update failure",
        ["ProblemUpdateEvent"] = "Windows Update event", ["ProblemDnsTimeout"] = "DNS resolution timeout", ["ProblemNetworkEvent"] = "Network event", ["ProblemUsbEvent"] = "USB / UCSI event",
        ["SelectChannel"] = "Select at least one channel.", ["Querying"] = "Querying…", ["QueryingNamed"] = "Querying “{0}”…",
        ["InvalidCustomDates"] = "Select a valid custom date range.", ["FirstBatch"] = "Showing first {0:N0} events · Querying in background…",
        ["QuerySummary"] = "Scanned {0:N0} · Matched {1:N0} · Critical {2:N0} · Errors {3:N0} · Warnings {4:N0} · Grouped into {5:N0}",
        ["SkippedChannels"] = " · Skipped {0} channel(s): {1}", ["QueryCancelled"] = "Query cancelled.",
        ["DropEvtx"] = "Drop an .evtx event log file.", ["AdminCancelled"] = "Administrator restart cancelled.",
        ["SelectProblem"] = "Select a problem first.", ["ExcelFilter"] = "Excel workbook (*.xlsx)|*.xlsx",
        ["Exporting"] = "Exporting Excel…", ["Exported"] = "Exported {0}", ["ExportCancelled"] = "Export cancelled.",
        ["DiskFull"] = "Not enough disk space. Excel export failed.", ["ExportFailed"] = "Export failed: {0}",
        ["ExcelProblemSheet"] = "Problem summary", ["ExcelEventsSheet"] = "Complete events",
        ["ExcelMessage"] = "Message", ["ExcelXml"] = "XML", ["ExcelRowLimit"] = "The event count exceeds the Excel worksheet limit.",
        ["GroupedEventsCount"] = "Group Events ({0:N0})", ["BrowserFailed"] = "Could not open the browser: {0}",
        ["CauseSearchSuffix"] = "Windows possible causes fix", ["LoadingDetails"] = "Loading the complete event message…",
        ["LoadDetailsFailed"] = "Could not load the complete event: {0}", ["SummaryHeading"] = "[Problem summary]",
        ["Occurrences"] = "Occurrences", ["FirstSeen"] = "First seen", ["LastSeen"] = "Last seen",
        ["EventInfoHeading"] = "[Event information]", ["EventMessageHeading"] = "[Event message]",
        ["Count"] = "Count", ["Provider"] = "Provider", ["Channel"] = "Channel", ["Computer"] = "Computer", ["RecordId"] = "Record ID",
        ["LanguageChanged"] = "Language changed.",
        ["HoursTooLarge"] = "--hours is too large.", ["EventIdRange"] = "--event-id must be between 1 and 65535.",
        ["LevelRange"] = "--level must be between 0 and 3.", ["ChannelRange"] = "--channel supports only System or Application.",
        ["InvalidSort"] = "Invalid --sort value.", ["InvalidQuick"] = "Invalid --quick value.",
        ["UnsupportedArgument"] = "Unsupported startup argument: {0}", ["EvtxNotFound"] = "EVTX file not found: {0}",
        ["FromToTogether"] = "--from and --to must be used together.", ["FromAfterTo"] = "--from cannot be later than --to.",
        ["ConflictingTime"] = "Time-range arguments cannot be combined.", ["PositiveInteger"] = "{0} requires a positive integer.",
        ["DateFormat"] = "{0} must use yyyy-MM-dd format.", ["MissingValue"] = "{0} is missing a value.",
        ["ProviderQuotes"] = "Provider cannot contain both single and double quotes.",
        ["ResultLimit"] = "The query returned more than {0:N0} events. Shorten the time range or add filters.",
        ["EmptyEventXml"] = "Event XML is empty.", ["WindowsX64Only"] = "The EventFast native renderer supports Windows x64 only.",
        ["InvalidSystemBuffer"] = "Invalid event System buffer: {0} fields / {1} bytes.",
        ["InvalidSystemType"] = "Invalid event System field {0}: expected type {1}, actual {2}.",
        ["UnsupportedValueType"] = "Unsupported value type: {0}.", ["InvalidEventDataBuffer"] = "Invalid EventData buffer: {0} fields / {1} bytes.",
        ["AccessDenied"] = "Access denied while reading the {0} event log.", ["MissingChannel"] = "This computer does not have the {0} event channel.",
        ["ReadFailed"] = "Failed to read the {0} event log."
    };

    private static readonly IReadOnlyDictionary<string, string> Chinese = new Dictionary<string, string>
    {
        ["Search"] = "搜尋", ["Cancel"] = "取消", ["ExportExcel"] = "匯出 Excel",
        ["ExportCurrent"] = "目前結果", ["ExportSelected"] = "選取問題", ["ExportAll"] = "全部事件",
        ["IncludeXml"] = "含 XML", ["SearchTip"] = "搜尋關鍵字、Event ID，或混合條件（例如 disk 153）",
        ["Time"] = "時間：", ["Last1Hour"] = "最近 1 小時", ["Last3Hours"] = "最近 3 小時", ["Last6Hours"] = "最近 6 小時",
        ["Last12Hours"] = "最近 12 小時", ["LastHours"] = "最近 {0:N0} 小時", ["Today"] = "今天", ["Last24Hours"] = "最近 24 小時",
        ["Last3Days"] = "最近 3 天", ["Last7Days"] = "最近 7 天", ["Last30Days"] = "最近 30 天",
        ["Custom"] = "自訂", ["AllTime"] = "全部時間", ["To"] = "至", ["Level"] = "等級：",
        ["CriticalOnly"] = "只看嚴重", ["ErrorsAndAbove"] = "錯誤以上", ["WarningsAndAbove"] = "警告以上", ["All"] = "全部",
        ["RestartAdmin"] = "以系統管理員重新啟動", ["Sort"] = "排序：", ["Default"] = "預設", ["Latest"] = "最新",
        ["Oldest"] = "最舊", ["MostFrequent"] = "最頻繁", ["Language"] = "語言：",
        ["QuickAll"] = "全部問題", ["QuickSystem"] = "系統錯誤", ["QuickCrash"] = "程式崩潰",
        ["QuickDisk"] = "磁碟 / SSD / NVMe", ["QuickNtfs"] = "NTFS / 檔案系統", ["QuickUsb"] = "USB / USB-C / UCSI",
        ["QuickDevice"] = "裝置 / Kernel-PnP", ["QuickDriver"] = "驅動程式", ["QuickWhea"] = "硬體 / WHEA",
        ["QuickNetwork"] = "網路", ["QuickUpdate"] = "Windows Update", ["QuickPower"] = "電源 / 異常關機",
        ["ActiveFilters"] = "啟用篩選：{0}", ["FilterKeyword"] = "關鍵字：{0}", ["FilterProvider"] = "Provider：{0}",
        ["FilterChannels"] = "Channel：{0}", ["FilterSort"] = "排序：{0}",
        ["SearchPossibleCauses"] = "使用搜尋引擎查詢可能原因", ["EventId"] = "Event ID", ["ColumnSeverity"] = "等級", ["ColumnProblem"] = "問題",
        ["ColumnCount"] = "次數", ["ColumnSource"] = "來源", ["ColumnLastSeen"] = "最後發生",
        ["ProblemDetails"] = "問題詳細資料", ["GroupedEvents"] = "群組事件", ["ColumnTime"] = "時間",
        ["EventContent"] = "事件內容", ["CopyProblem"] = "複製問題", ["CopyFull"] = "複製完整內容", ["CopyXml"] = "複製 XML",
        ["ParsedXml"] = "解析 XML", ["ParseXmlFailed"] = "無法解析 XML：{0}",
        ["LevelCritical"] = "嚴重", ["LevelError"] = "錯誤", ["LevelWarning"] = "警告",
        ["LevelInformation"] = "資訊", ["LevelVerbose"] = "詳細", ["LevelUnknown"] = "未知",
        ["ProblemDiskRetry"] = "磁碟 I/O 重試", ["ProblemDiskError"] = "磁碟 I/O 發生錯誤",
        ["ProblemUnexpectedShutdown"] = "非正常關機 / 電源異常", ["ProblemAppCrash"] = "應用程式崩潰",
        ["ProblemFallback"] = "{0} + Event ID {1}",
        ["ProblemStorageError"] = "磁碟／儲存裝置 I/O 錯誤", ["ProblemStorageTimeout"] = "儲存控制器逾時",
        ["ProblemStorageEvent"] = "儲存裝置事件", ["ProblemAppFailure"] = "應用程式失敗",
        ["ProblemNtfsCorruption"] = "NTFS 檔案系統損毀", ["ProblemNtfsEvent"] = "NTFS 檔案系統事件",
        ["ProblemDotNetFailure"] = ".NET Runtime 失敗", ["ProblemWerReport"] = "Windows 錯誤報告事件",
        ["ProblemWheaEvent"] = "WHEA 硬體事件（Event ID {0}）", ["ProblemDriverLoadFailure"] = "裝置／驅動程式載入失敗",
        ["ProblemDeviceRemoval"] = "裝置移除遭阻擋", ["ProblemDeviceStartFailure"] = "裝置啟動失敗",
        ["ProblemUpdateSuccess"] = "Windows Update 安裝成功", ["ProblemUpdateFailure"] = "Windows Update 失敗",
        ["ProblemUpdateEvent"] = "Windows Update 事件", ["ProblemDnsTimeout"] = "DNS 解析逾時", ["ProblemNetworkEvent"] = "網路事件", ["ProblemUsbEvent"] = "USB／UCSI 事件",
        ["SelectChannel"] = "請至少選擇一個 Channel。", ["Querying"] = "查詢中…", ["QueryingNamed"] = "查詢「{0}」…",
        ["InvalidCustomDates"] = "請選擇有效的自訂起訖日期。", ["FirstBatch"] = "已顯示第一批 {0:N0} 筆 · 背景查詢中…",
        ["QuerySummary"] = "掃描 {0:N0} 筆 · 符合 {1:N0} 筆 · 嚴重 {2:N0} · 錯誤 {3:N0} · 警告 {4:N0} · 合併 {5:N0} 類",
        ["SkippedChannels"] = " · 略過 {0} 個 Channel：{1}", ["QueryCancelled"] = "查詢已取消。",
        ["DropEvtx"] = "請拖入 .evtx 事件記錄檔。", ["AdminCancelled"] = "已取消以系統管理員身分重新啟動。",
        ["SelectProblem"] = "請先選取一個問題。", ["ExcelFilter"] = "Excel 活頁簿 (*.xlsx)|*.xlsx",
        ["Exporting"] = "正在匯出 Excel…", ["Exported"] = "已匯出 {0}", ["ExportCancelled"] = "匯出已取消。",
        ["DiskFull"] = "磁碟空間不足，Excel 匯出失敗。", ["ExportFailed"] = "匯出失敗：{0}",
        ["ExcelProblemSheet"] = "問題摘要", ["ExcelEventsSheet"] = "完整事件",
        ["ExcelMessage"] = "訊息", ["ExcelXml"] = "XML", ["ExcelRowLimit"] = "事件數量超過單一 Excel 工作表上限。",
        ["GroupedEventsCount"] = "群組事件 ({0:N0})", ["BrowserFailed"] = "無法開啟瀏覽器：{0}",
        ["CauseSearchSuffix"] = "Windows 可能原因 修正", ["LoadingDetails"] = "正在載入完整事件訊息…",
        ["LoadDetailsFailed"] = "無法載入完整事件：{0}", ["SummaryHeading"] = "【問題摘要】",
        ["Occurrences"] = "發生次數", ["FirstSeen"] = "首次發生", ["LastSeen"] = "最後發生",
        ["EventInfoHeading"] = "【事件資訊】", ["EventMessageHeading"] = "【事件訊息】",
        ["Count"] = "次數", ["Provider"] = "Provider", ["Channel"] = "Channel", ["Computer"] = "Computer", ["RecordId"] = "Record ID",
        ["LanguageChanged"] = "語言已變更。",
        ["HoursTooLarge"] = "--hours 數值過大。", ["EventIdRange"] = "--event-id 必須介於 1 到 65535。",
        ["LevelRange"] = "--level 必須介於 0 到 3。", ["ChannelRange"] = "--channel 僅支援 System 或 Application。",
        ["InvalidSort"] = "--sort 值無效。", ["InvalidQuick"] = "--quick 值無效。",
        ["UnsupportedArgument"] = "不支援的啟動參數：{0}", ["EvtxNotFound"] = "找不到 EVTX 檔案：{0}",
        ["FromToTogether"] = "--from 與 --to 必須同時使用。", ["FromAfterTo"] = "--from 不可晚於 --to。",
        ["ConflictingTime"] = "時間範圍參數不可同時使用。", ["PositiveInteger"] = "{0} 必須接正整數。",
        ["DateFormat"] = "{0} 必須使用 yyyy-MM-dd 格式。", ["MissingValue"] = "{0} 缺少值。",
        ["ProviderQuotes"] = "Provider 不可同時包含單引號與雙引號。",
        ["ResultLimit"] = "查詢結果超過 {0:N0} 筆，請縮短時間或增加篩選條件。",
        ["EmptyEventXml"] = "事件 XML 為空。", ["WindowsX64Only"] = "EventFast Native renderer 僅支援 Windows x64。",
        ["InvalidSystemBuffer"] = "事件 System 欄位 buffer 無效：{0} 欄／{1} bytes。",
        ["InvalidSystemType"] = "事件 System 欄位 {0} 類型錯誤：預期 {1}，實際 {2}。",
        ["UnsupportedValueType"] = "不支援的數值類型：{0}。", ["InvalidEventDataBuffer"] = "事件 EventData buffer 無效：{0} 欄／{1} bytes。",
        ["AccessDenied"] = "權限不足，無法讀取 {0} 事件記錄。", ["MissingChannel"] = "這台電腦沒有 {0} 事件通道。",
        ["ReadFailed"] = "讀取 {0} 事件記錄失敗。"
    };

    public static Localization Instance { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public string CurrentLanguage { get; private set; } = "en";
    public string this[string key] => Dictionary.TryGetValue(key, out var value) ? value : key;
    internal static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EventFast", "settings.json");
    private IReadOnlyDictionary<string, string> Dictionary => CurrentLanguage == "zh-TW" ? Chinese : English;

    private Localization() { }

    internal static void Initialize() => Instance.ChangeLanguage(LoadLanguage(SettingsPath), persist: false);
    internal static void SetLanguage(string language) => Instance.ChangeLanguage(language, persist: true);
    internal static void UseLanguage(string language) => Instance.ChangeLanguage(language, persist: false);
    internal static string Text(string key) => Instance[key];
    internal static string Text(string key, string language) =>
        (Normalize(language) == "zh-TW" ? Chinese : English).TryGetValue(key, out var value) ? value : key;
    internal static string Format(string key, params object?[] args) => FormatForLanguage(key, Instance.CurrentLanguage, args);
    internal static string FormatForLanguage(string key, string language, params object?[] args) =>
        string.Format(CultureInfo.GetCultureInfo(Normalize(language)), Text(key, language), args);
    internal static bool ResourcesMatch => English.Keys.Order().SequenceEqual(Chinese.Keys.Order());
    internal static string Level(string level) => Text(level switch
    {
        "Critical" => "LevelCritical", "Error" => "LevelError", "Warning" => "LevelWarning",
        "Information" => "LevelInformation", "Verbose" => "LevelVerbose", _ => "LevelUnknown"
    });
    internal static string Level(string level, string language) => Text(level switch
    {
        "Critical" => "LevelCritical", "Error" => "LevelError", "Warning" => "LevelWarning",
        "Information" => "LevelInformation", "Verbose" => "LevelVerbose", _ => "LevelUnknown"
    }, language);

    internal static string LoadLanguage(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return Normalize(document.RootElement.GetProperty("Language").GetString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return "en";
        }
    }

    internal static void SaveLanguage(string path, string language)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { Language = Normalize(language) }));
    }

    private void ChangeLanguage(string language, bool persist)
    {
        CurrentLanguage = Normalize(language);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(CurrentLanguage);
        if (persist)
        {
            try { SaveLanguage(SettingsPath, CurrentLanguage); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private static string Normalize(string? language) => language == "zh-TW" ? "zh-TW" : "en";
}

[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{key}]") { Source = Localization.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
