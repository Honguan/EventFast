using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
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
    string Xml,
    string? LogFilePath = null);

internal static class WindowsEventReader
{
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorTimeout = 1460;
    private const int EvtQueryChannelPath = 0x1;
    private const int EvtQueryFilePath = 0x2;
    private const int EvtQueryReverseDirection = 0x200;
    private const int EvtRenderEventValues = 0;
    private const int EvtRenderEventXml = 1;
    private const int EvtRenderContextSystem = 1;
    private const int EvtFormatMessageEvent = 1;
    private const int BatchSize = 256;

    internal static IReadOnlyList<EventRow> Read(string channel, string xpath, CancellationToken cancellationToken,
        int maximumRows = 1_000_000, Action<IReadOnlyList<EventRow>>? firstBatch = null, bool filePath = false,
        bool failIfTruncated = false)
    {
        using var query = EvtQuery(IntPtr.Zero, channel, xpath, (filePath ? EvtQueryFilePath : EvtQueryChannelPath) | EvtQueryReverseDirection);
        if (query.IsInvalid)
            throw NativeError(channel);

        using var renderer = new SystemRenderer();
        var rows = new List<EventRow>();
        var handles = new IntPtr[BatchSize];
        var discardedRows = false;

        // ponytail: one million matches the documented UI ceiling; add disk-backed paging only beyond it.
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

            var count = Math.Min(returned, maximumRows - rows.Count);
            discardedRows |= returned > count;
            var ownedHandles = new EventHandle[count];
            try
            {
                var batch = new EventRow[count];
                for (var index = 0; index < count; index++)
                {
                    var handle = new EventHandle(handles[index]);
                    ownedHandles[index] = handle;
                    handles[index] = IntPtr.Zero;
                    batch[index] = renderer.Render(handle, filePath ? channel : null);
                }

                if (firstBatch is not null)
                {
                    firstBatch(batch);
                    firstBatch = null;
                }

                for (var index = 0; index < count; index++)
                    rows.Add(AddDetails(batch[index], RenderXml(ownedHandles[index])));
            }
            finally
            {
                foreach (var handle in ownedHandles)
                    handle?.Dispose();
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

        if (failIfTruncated && rows.Count == maximumRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (discardedRows)
                throw ResultLimit(maximumRows);
            if (EvtNext(query, handles.Length, handles, 0, 0, out var returned))
            {
                for (var index = 0; index < returned; index++)
                {
                    EvtClose(handles[index]);
                    handles[index] = IntPtr.Zero;
                }
                throw ResultLimit(maximumRows);
            }

            var error = Marshal.GetLastWin32Error();
            if (error is not ErrorNoMoreItems and not ErrorTimeout)
                throw NativeError(channel, error);
        }

        return rows;
    }

    private static InvalidOperationException ResultLimit(int maximumRows) =>
        new($"查詢結果超過 {maximumRows:N0} 筆，請縮短時間或增加篩選條件。");

    internal static string ReadMessage(EventRow row)
    {
        using var formatter = new MessageFormatter();
        return formatter.Format(row);
    }

    internal static MessageFormatter CreateMessageFormatter() => new();

    internal static string ReadXml(EventRow row)
    {
        var xpath = $"*[System[EventRecordID={row.RecordId}]]";
        using var query = EvtQuery(IntPtr.Zero, row.LogFilePath ?? row.Channel, xpath,
            row.LogFilePath is null ? EvtQueryChannelPath : EvtQueryFilePath);
        if (query.IsInvalid)
            return row.Xml;

        var events = new IntPtr[1];
        if (!EvtNext(query, 1, events, 0, 0, out var returned) || returned == 0)
            return row.Xml;

        using var handle = new EventHandle(events[0]);
        return RenderXml(handle);
    }

    internal sealed class MessageFormatter : IDisposable
    {
        private readonly Dictionary<(string Provider, string? File), EventHandle> _metadata = [];

        internal string Format(EventRow row)
        {
            var xpath = $"*[System[EventRecordID={row.RecordId}]]";
            using var query = EvtQuery(IntPtr.Zero, row.LogFilePath ?? row.Channel, xpath, row.LogFilePath is null ? EvtQueryChannelPath : EvtQueryFilePath);
            if (query.IsInvalid)
                return row.Details;

            var events = new IntPtr[1];
            if (!EvtNext(query, 1, events, 0, 0, out var returned) || returned == 0)
                return row.Details;

            using var handle = new EventHandle(events[0]);
            var key = (row.Provider, row.LogFilePath);
            if (!_metadata.TryGetValue(key, out var metadata))
            {
                metadata = EvtOpenPublisherMetadata(IntPtr.Zero, row.Provider, row.LogFilePath, 0, 0);
                if (!metadata.IsInvalid)
                    _metadata.Add(key, metadata);
            }
            if (metadata.IsInvalid)
            {
                metadata.Dispose();
                return row.Details;
            }

            EvtFormatMessage(metadata, handle, 0, 0, IntPtr.Zero, EvtFormatMessageEvent, 0, IntPtr.Zero, out var size);
            if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer || size == 0)
                return row.Details;

            var buffer = Marshal.AllocHGlobal(checked(size * sizeof(char)));
            try
            {
                return EvtFormatMessage(metadata, handle, 0, 0, IntPtr.Zero, EvtFormatMessageEvent, size, buffer, out _)
                    ? Marshal.PtrToStringUni(buffer) ?? row.Details
                    : row.Details;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Dispose()
        {
            foreach (var handle in _metadata.Values)
                handle.Dispose();
            _metadata.Clear();
        }
    }

    private static string RenderXml(EventHandle handle)
    {
        EvtRender(IntPtr.Zero, handle, EvtRenderEventXml, 0, IntPtr.Zero, out var size, out _);
        var error = Marshal.GetLastWin32Error();
        if (error != ErrorInsufficientBuffer)
            throw NativeError("event", error);

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!EvtRender(IntPtr.Zero, handle, EvtRenderEventXml, size, buffer, out _, out _))
                throw NativeError("event");
            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static EventRow AddDetails(EventRow row, string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new InvalidDataException("事件 XML 為空。 ");
        var details = string.Join(Environment.NewLine,
            root.Descendants().Where(element => element.Parent?.Name.LocalName is "EventData" or "UserData")
                .Select(element => element.Value).Where(value => !string.IsNullOrWhiteSpace(value)));
        return row with { Details = details };
    }

    private sealed class SystemRenderer : IDisposable
    {
        private const int PropertyCount = 18;
        private const int TypeMask = 0x7f;
        private const int TypeArray = 0x80;
        private const int TypeNull = 0;
        private const int TypeString = 1;
        private const int TypeByte = 4;
        private const int TypeUInt16 = 6;
        private const int TypeUInt64 = 10;
        private const int TypeFileTime = 17;

        private readonly EventHandle _context;
        private IntPtr _buffer;
        private int _capacity;

        internal SystemRenderer()
        {
            if (IntPtr.Size != 8 || Marshal.SizeOf<EvtVariant>() != 16)
                throw new PlatformNotSupportedException("EventFast Native renderer 僅支援 Windows x64。");
            _context = EvtCreateRenderContext(0, IntPtr.Zero, EvtRenderContextSystem);
            if (_context.IsInvalid)
                throw NativeError("system render context");
        }

        internal EventRow Render(EventHandle handle, string? logFilePath)
        {
            if (!EvtRender(_context, handle, EvtRenderEventValues, _capacity, _buffer, out var used, out var count))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ErrorInsufficientBuffer || used <= 0)
                    throw NativeError("event system fields", error);
                Resize(used);
                if (!EvtRender(_context, handle, EvtRenderEventValues, _capacity, _buffer, out used, out count))
                    throw NativeError("event system fields");
            }

            if (count < PropertyCount || used < checked(count * 16))
                throw new InvalidDataException($"事件 System 欄位 buffer 無效：{count} 欄／{used} bytes。");

            var time = Value(8, TypeFileTime);
            var level = Number(4, TypeByte);
            return new EventRow(
                time.Type == TypeNull ? DateTime.MinValue : DateTime.FromFileTimeUtc(checked((long)time.UInt64)).ToLocalTime(),
                level switch { 1 => "嚴重", 2 => "錯誤", 3 => "警告", 4 => "資訊", 5 => "詳細", _ => "未知" },
                checked((int)Number(2, TypeUInt16)),
                Text(0),
                Text(14),
                checked((long)Number(9, TypeUInt64)),
                Text(15),
                "",
                "",
                logFilePath);
        }

        private EvtVariant Value(int index, int expectedType)
        {
            var value = Marshal.PtrToStructure<EvtVariant>(IntPtr.Add(_buffer, index * 16));
            var type = value.Type & TypeMask;
            if ((value.Type & TypeArray) != 0 || type is not TypeNull && type != expectedType)
                throw new InvalidDataException($"事件 System 欄位 {index} 類型錯誤：預期 {expectedType}，實際 {type}。");
            value.Type = type;
            return value;
        }

        private ulong Number(int index, int expectedType)
        {
            var value = Value(index, expectedType);
            return value.Type == TypeNull ? 0 : expectedType switch
            {
                TypeByte => value.Byte,
                TypeUInt16 => value.UInt16,
                TypeUInt64 => value.UInt64,
                _ => throw new InvalidOperationException($"不支援的數值類型：{expectedType}。")
            };
        }

        private string Text(int index)
        {
            var value = Value(index, TypeString);
            return value.Type == TypeNull ? "" : Marshal.PtrToStringUni(value.Pointer) ?? "";
        }

        private void Resize(int size)
        {
            _buffer = _buffer == IntPtr.Zero
                ? Marshal.AllocHGlobal(size)
                : Marshal.ReAllocHGlobal(_buffer, size);
            _capacity = size;
        }

        public void Dispose()
        {
            _context.Dispose();
            if (_buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
                _buffer = IntPtr.Zero;
            }
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct EvtVariant
    {
        [FieldOffset(0)] internal IntPtr Pointer;
        [FieldOffset(0)] internal byte Byte;
        [FieldOffset(0)] internal ushort UInt16;
        [FieldOffset(0)] internal ulong UInt64;
        [FieldOffset(8)] internal int Count;
        [FieldOffset(12)] internal int Type;
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
    private static extern EventHandle EvtCreateRenderContext(int valuePathsCount, IntPtr valuePaths, int flags);

    [DllImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtNext(EventHandle resultSet, int eventArraySize, [Out] IntPtr[] events, int timeout, int flags, out int returned);

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtRender(IntPtr context, EventHandle fragment, int flags, int bufferSize, IntPtr buffer, out int bufferUsed, out int propertyCount);

    [DllImport("wevtapi.dll", EntryPoint = "EvtRender", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtRender(EventHandle context, EventHandle fragment, int flags, int bufferSize, IntPtr buffer, out int bufferUsed, out int propertyCount);

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtFormatMessage(EventHandle publisherMetadata, EventHandle eventHandle, int messageId, int valueCount,
        IntPtr values, int flags, int bufferSize, IntPtr buffer, out int bufferUsed);

    [DllImport("wevtapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtClose(IntPtr handle);
}
