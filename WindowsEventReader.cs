using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32.SafeHandles;

namespace EventFast;

internal sealed record EventRow(
    DateTime Time,
    string Level,
    int EventId,
    string Provider,
    string Channel,
    long RecordId,
    string Computer,
    string Details,
    string Xml);

internal static class WindowsEventReader
{
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorTimeout = 1460;
    private const int EvtQueryChannelPath = 0x1;
    private const int EvtQueryReverseDirection = 0x200;
    private const int EvtRenderEventXml = 1;
    private const int EvtFormatMessageEvent = 1;
    private const int BatchSize = 256;

    internal static IReadOnlyList<EventRow> Read(string channel, string xpath, CancellationToken cancellationToken, int maximumRows = 50_000)
    {
        using var query = EvtQuery(IntPtr.Zero, channel, xpath, EvtQueryChannelPath | EvtQueryReverseDirection);
        if (query.IsInvalid)
            throw NativeError(channel);

        var rows = new List<EventRow>();
        var handles = new IntPtr[BatchSize];

        // ponytail: v0.1 caps one channel at 50k rows; replace with paging after profiling real large logs.
        while (rows.Count < maximumRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!EvtNext(query, handles.Length, handles, 0, 0, out var returned))
            {
                var error = Marshal.GetLastWin32Error();
                if (error is ErrorNoMoreItems or ErrorTimeout)
                    break;
                throw NativeError(channel, error);
            }

            try
            {
                for (var index = 0; index < returned; index++)
                {
                    using var handle = new EventHandle(handles[index]);
                    handles[index] = IntPtr.Zero;
                    rows.Add(Parse(RenderXml(handle)));
                    if (rows.Count >= maximumRows)
                        break;
                }
            }
            finally
            {
                for (var index = 0; index < returned; index++)
                {
                    if (handles[index] != IntPtr.Zero)
                    {
                        EvtClose(handles[index]);
                        handles[index] = IntPtr.Zero;
                    }
                }
            }
        }

        return rows;
    }

    internal static string ReadMessage(EventRow row)
    {
        var xpath = $"*[System[EventRecordID={row.RecordId}]]";
        using var query = EvtQuery(IntPtr.Zero, row.Channel, xpath, EvtQueryChannelPath);
        if (query.IsInvalid)
            return row.Details;

        var events = new IntPtr[1];
        if (!EvtNext(query, 1, events, 0, 0, out var returned) || returned == 0)
            return row.Details;

        using var handle = new EventHandle(events[0]);
        using var metadata = EvtOpenPublisherMetadata(IntPtr.Zero, row.Provider, null, 0, 0);
        if (metadata.IsInvalid)
            return row.Details;

        EvtFormatMessage(metadata, handle, 0, 0, IntPtr.Zero, EvtFormatMessageEvent, 0, null, out var size);
        if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer || size == 0)
            return row.Details;

        var buffer = new StringBuilder(size);
        return EvtFormatMessage(metadata, handle, 0, 0, IntPtr.Zero, EvtFormatMessageEvent, size, buffer, out _)
            ? buffer.ToString()
            : row.Details;
    }

    private static string RenderXml(EventHandle handle)
    {
        EvtRender(IntPtr.Zero, handle, EvtRenderEventXml, 0, null, out var size, out _);
        var error = Marshal.GetLastWin32Error();
        if (error != ErrorInsufficientBuffer)
            throw NativeError("event", error);

        var buffer = new StringBuilder(size / sizeof(char));
        if (!EvtRender(IntPtr.Zero, handle, EvtRenderEventXml, size, buffer, out _, out _))
            throw NativeError("event");
        return buffer.ToString();
    }

    private static EventRow Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new InvalidDataException("事件 XML 為空。 ");
        XNamespace ns = root.Name.Namespace;
        var system = root.Element(ns + "System") ?? throw new InvalidDataException("事件缺少 System 資料。");
        var level = (int?)system.Element(ns + "Level") ?? 0;
        var details = string.Join(Environment.NewLine,
            root.Descendants().Where(element => element.Parent?.Name.LocalName is "EventData" or "UserData")
                .Select(element => element.Value).Where(value => !string.IsNullOrWhiteSpace(value)));

        return new(
            DateTime.TryParse((string?)system.Element(ns + "TimeCreated")?.Attribute("SystemTime"), out var time) ? time.ToLocalTime() : DateTime.MinValue,
            level switch { 1 => "嚴重", 2 => "錯誤", 3 => "警告", 4 => "資訊", 5 => "詳細", _ => "未知" },
            (int?)system.Element(ns + "EventID") ?? 0,
            (string?)system.Element(ns + "Provider")?.Attribute("Name") ?? "",
            (string?)system.Element(ns + "Channel") ?? "",
            (long?)system.Element(ns + "EventRecordID") ?? 0,
            (string?)system.Element(ns + "Computer") ?? "",
            details,
            xml);
    }

    private static Exception NativeError(string target, int? code = null)
    {
        var error = code ?? Marshal.GetLastWin32Error();
        return error switch
        {
            5 => new UnauthorizedAccessException($"權限不足，無法讀取 {target} 事件記錄。"),
            15007 => new InvalidOperationException($"這台電腦沒有 {target} 事件通道。"),
            _ => new Win32Exception(error, $"讀取 {target} 事件記錄失敗。")
        };
    }

    private sealed class EventHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal EventHandle() : base(true) { }
        internal EventHandle(IntPtr handle) : base(true) => SetHandle(handle);
        protected override bool ReleaseHandle() => EvtClose(handle);
    }

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern EventHandle EvtQuery(IntPtr session, string path, string query, int flags);

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern EventHandle EvtOpenPublisherMetadata(IntPtr session, string publisherId, string? logFilePath, int locale, int flags);

    [DllImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtNext(EventHandle resultSet, int eventArraySize, [Out] IntPtr[] events, int timeout, int flags, out int returned);

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtRender(IntPtr context, EventHandle fragment, int flags, int bufferSize, StringBuilder? buffer, out int bufferUsed, out int propertyCount);

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtFormatMessage(EventHandle publisherMetadata, EventHandle eventHandle, int messageId, int valueCount,
        IntPtr values, int flags, int bufferSize, StringBuilder? buffer, out int bufferUsed);

    [DllImport("wevtapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtClose(IntPtr handle);
}
