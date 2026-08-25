using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
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
    string? LogFilePath = null,
    string? Message = null)
{
    public string DisplayLevel => Localization.Level(Level);
}

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
    private const int EvtRenderContextUser = 2;
    private const int EvtFormatMessageEvent = 1;
    private const int BatchSize = 256;

    internal static IReadOnlyList<EventRow> Read(string channel, string xpath, CancellationToken cancellationToken,
        int maximumRows = 1_000_000, Action<IReadOnlyList<EventRow>>? firstBatch = null, bool filePath = false,
        bool failIfTruncated = false, bool includeMessage = false)
    {
        using var query = EvtQuery(IntPtr.Zero, channel, xpath, (filePath ? EvtQueryFilePath : EvtQueryChannelPath) | EvtQueryReverseDirection);
        if (query.IsInvalid)
            throw NativeError(channel);

        using var renderer = new SystemRenderer();
        using var userRenderer = new UserRenderer();
        using var messageFormatter = includeMessage ? new MessageFormatter() : null;
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
                    cancellationToken.ThrowIfCancellationRequested();
                    var handle = new EventHandle(handles[index]);
                    ownedHandles[index] = handle;
                    handles[index] = IntPtr.Zero;
                    var row = renderer.Render(handle, filePath ? channel : null);
                    var details = userRenderer.TryRender(handle);
                    row = details is null ? AddDetails(row, RenderXml(handle)) : row with { Details = details };
                    batch[index] = messageFormatter is null ? row : AddMessage(row, messageFormatter.Format(handle, row));
                }

                if (firstBatch is not null)
                {
                    firstBatch(batch);
                    firstBatch = null;
                }

                rows.AddRange(batch);
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
        new(Localization.Format("ResultLimit", maximumRows));

    internal static string ReadMessage(EventRow row)
    {
        using var formatter = new MessageFormatter();
        return formatter.Format(row);
    }

    internal static MessageFormatter CreateMessageFormatter() => new();

    internal static string ReadXml(EventRow row)
    {
        if (row.Xml.Length > 0)
            return row.Xml;

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

        internal string Format(EventRow row) => ReadContent(row, includeXml: false).Message;

        internal (string Message, string Xml) ReadContent(EventRow row, bool includeXml)
        {
            if (row.Message is not null && (!includeXml || row.Xml.Length > 0))
                return (row.Message, row.Xml);

            var xpath = $"*[System[EventRecordID={row.RecordId}]]";
            using var query = EvtQuery(IntPtr.Zero, row.LogFilePath ?? row.Channel, xpath, row.LogFilePath is null ? EvtQueryChannelPath : EvtQueryFilePath);
            if (query.IsInvalid)
                return (row.Message ?? row.Details, row.Xml);

            var events = new IntPtr[1];
            if (!EvtNext(query, 1, events, 0, 0, out var returned) || returned == 0)
                return (row.Message ?? row.Details, row.Xml);

            using var handle = new EventHandle(events[0]);
            return (row.Message ?? Format(handle, row), includeXml && row.Xml.Length == 0 ? RenderXml(handle) : row.Xml);
        }

        internal string Format(EventHandle handle, EventRow row)
        {
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
        var root = XDocument.Parse(xml).Root ?? throw new InvalidDataException(Localization.Text("EmptyEventXml"));
        var details = string.Join(Environment.NewLine,
            root.Descendants().Where(element => !element.HasElements &&
                    element.Ancestors().Any(ancestor => ancestor.Name.LocalName is "EventData" or "UserData"))
                .Select(element => element.Value).Where(value => !string.IsNullOrWhiteSpace(value)));
        return row with { Details = details };
    }

    private static EventRow AddMessage(EventRow row, string message) =>
        string.IsNullOrWhiteSpace(message)
            ? row
            : row with
            {
                Message = message,
                Details = row.Details.Contains(message, StringComparison.Ordinal) ? row.Details :
                    string.IsNullOrWhiteSpace(row.Details) ? message : $"{row.Details}{Environment.NewLine}{message}"
            };

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
                throw new PlatformNotSupportedException(Localization.Text("WindowsX64Only"));
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
                throw new InvalidDataException(Localization.Format("InvalidSystemBuffer", count, used));

            var time = Value(8, TypeFileTime);
            var level = Number(4, TypeByte);
            return new EventRow(
                time.Type == TypeNull ? DateTime.MinValue : DateTime.FromFileTimeUtc(checked((long)time.UInt64)).ToLocalTime(),
                level switch { 1 => "Critical", 2 => "Error", 3 => "Warning", 4 => "Information", 5 => "Verbose", _ => "Unknown" },
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
                throw new InvalidDataException(Localization.Format("InvalidSystemType", index, expectedType, type));
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
                _ => throw new InvalidOperationException(Localization.Format("UnsupportedValueType", expectedType))
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

    private sealed class UserRenderer : IDisposable
    {
        private const int TypeMask = 0x7f;
        private const int TypeArray = 0x80;
        private const int TypeNull = 0;
        private const int TypeString = 1;
        private const int TypeAnsiString = 2;
        private const int TypeSByte = 3;
        private const int TypeByte = 4;
        private const int TypeInt16 = 5;
        private const int TypeUInt16 = 6;
        private const int TypeInt32 = 7;
        private const int TypeUInt32 = 8;
        private const int TypeInt64 = 9;
        private const int TypeUInt64 = 10;
        private const int TypeSingle = 11;
        private const int TypeDouble = 12;
        private const int TypeBoolean = 13;
        private const int TypeBinary = 14;
        private const int TypeGuid = 15;
        private const int TypeSizeT = 16;
        private const int TypeFileTime = 17;
        private const int TypeSystemTime = 18;
        private const int TypeSid = 19;
        private const int TypeHexInt32 = 20;
        private const int TypeHexInt64 = 21;
        private const int TypeXml = 35;

        private readonly EventHandle _context;
        private IntPtr _buffer;
        private int _capacity;

        internal UserRenderer()
        {
            _context = EvtCreateRenderContext(0, IntPtr.Zero, EvtRenderContextUser);
            if (_context.IsInvalid)
                throw NativeError("user render context");
        }

        internal string? TryRender(EventHandle handle)
        {
            if (!EvtRender(_context, handle, EvtRenderEventValues, _capacity, _buffer, out var used, out var count))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ErrorInsufficientBuffer || used <= 0)
                    throw NativeError("event data", error);
                Resize(used);
                if (!EvtRender(_context, handle, EvtRenderEventValues, _capacity, _buffer, out used, out count))
                    throw NativeError("event data");
            }

            if (count == 0)
                return "";
            if (used < checked(count * 16))
                throw new InvalidDataException(Localization.Format("InvalidEventDataBuffer", count, used));

            var values = new List<string>(count);
            for (var index = 0; index < count; index++)
            {
                var value = Marshal.PtrToStructure<EvtVariant>(IntPtr.Add(_buffer, index * 16));
                if ((value.Type & TypeArray) != 0)
                    // ponytail: rare array variants keep the exact XML fallback; add array conversion only if profiling shows it matters.
                    return null;
                var text = Text(value, value.Type & TypeMask);
                if (text is null)
                    return null;
                if (!string.IsNullOrWhiteSpace(text))
                    values.Add(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
            }
            return string.Join(Environment.NewLine, values);
        }

        private static string? Text(EvtVariant value, int type)
        {
            try
            {
                return type switch
                {
                    TypeNull => "",
                    TypeString or TypeXml => Marshal.PtrToStringUni(value.Pointer) ?? "",
                    TypeAnsiString => Marshal.PtrToStringAnsi(value.Pointer) ?? "",
                    TypeSByte => value.SByte.ToString(CultureInfo.InvariantCulture),
                    TypeByte => value.Byte.ToString(CultureInfo.InvariantCulture),
                    TypeInt16 => value.Int16.ToString(CultureInfo.InvariantCulture),
                    TypeUInt16 => value.UInt16.ToString(CultureInfo.InvariantCulture),
                    TypeInt32 => value.Int32.ToString(CultureInfo.InvariantCulture),
                    TypeUInt32 => value.UInt32.ToString(CultureInfo.InvariantCulture),
                    TypeInt64 => value.Int64.ToString(CultureInfo.InvariantCulture),
                    TypeUInt64 or TypeSizeT => value.UInt64.ToString(CultureInfo.InvariantCulture),
                    TypeSingle => value.Single.ToString(CultureInfo.InvariantCulture),
                    TypeDouble => value.Double.ToString(CultureInfo.InvariantCulture),
                    TypeBoolean => value.Int32 == 0 ? "false" : "true",
                    TypeBinary => Binary(value),
                    TypeGuid => value.Pointer == IntPtr.Zero ? "" : Marshal.PtrToStructure<Guid>(value.Pointer).ToString("B"),
                    TypeFileTime => DateTime.FromFileTimeUtc(checked((long)value.UInt64)).ToString("O", CultureInfo.InvariantCulture),
                    TypeSystemTime => SystemTimeText(value.Pointer),
                    TypeSid => value.Pointer == IntPtr.Zero ? "" : new SecurityIdentifier(value.Pointer).Value,
                    TypeHexInt32 => $"0x{value.UInt32:x}",
                    TypeHexInt64 => $"0x{value.UInt64:x}",
                    _ => null
                };
            }
            catch (Exception exception) when (exception is ArgumentException or OverflowException)
            {
                return null;
            }
        }

        private static string Binary(EvtVariant value)
        {
            if (value.Pointer == IntPtr.Zero || value.Count == 0)
                return "";
            var bytes = new byte[value.Count];
            Marshal.Copy(value.Pointer, bytes, 0, bytes.Length);
            return Convert.ToHexString(bytes);
        }

        private static string SystemTimeText(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero)
                return "";
            var value = Marshal.PtrToStructure<SystemTime>(pointer);
            return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Milliseconds,
                DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
        }

        private void Resize(int size)
        {
            _buffer = _buffer == IntPtr.Zero ? Marshal.AllocHGlobal(size) : Marshal.ReAllocHGlobal(_buffer, size);
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
        [FieldOffset(0)] internal sbyte SByte;
        [FieldOffset(0)] internal byte Byte;
        [FieldOffset(0)] internal short Int16;
        [FieldOffset(0)] internal ushort UInt16;
        [FieldOffset(0)] internal int Int32;
        [FieldOffset(0)] internal uint UInt32;
        [FieldOffset(0)] internal long Int64;
        [FieldOffset(0)] internal ulong UInt64;
        [FieldOffset(0)] internal float Single;
        [FieldOffset(0)] internal double Double;
        [FieldOffset(8)] internal int Count;
        [FieldOffset(12)] internal int Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        internal ushort Year;
        internal ushort Month;
        internal ushort DayOfWeek;
        internal ushort Day;
        internal ushort Hour;
        internal ushort Minute;
        internal ushort Second;
        internal ushort Milliseconds;
    }

    private static Exception NativeError(string target, int? code = null)
    {
        var error = code ?? Marshal.GetLastWin32Error();
        return error switch
        {
            5 => new UnauthorizedAccessException(Localization.Format("AccessDenied", target)),
            15007 => new InvalidOperationException(Localization.Format("MissingChannel", target)),
            _ => new Win32Exception(error, Localization.Format("ReadFailed", target))
        };
    }

    internal sealed class EventHandle : SafeHandleZeroOrMinusOneIsInvalid
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
