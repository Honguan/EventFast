using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EventFast;

internal static class XlsxExporter
{
    private const string Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    internal static void Export(string path, IReadOnlyList<ProblemGroup> groups, IEnumerable<EventRow> events,
        bool includeXml = false, Func<EventRow, string>? messageFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var archive = new ZipArchive(File.Create(temporaryPath), ZipArchiveMode.Create))
            {
                WriteText(archive, "[Content_Types].xml", ContentTypes);
                WriteText(archive, "_rels/.rels", PackageRelationships);
                WriteText(archive, "xl/workbook.xml", Workbook);
                WriteText(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships);
                WriteText(archive, "xl/styles.xml", Styles);
                WriteSheet(archive, "xl/worksheets/sheet1.xml",
                    ["Severity", "Problem", "Count", "Event ID", "Provider", "First Seen", "Last Seen", "Channel"],
                    groups.Select(group => new object?[] { group.Severity, group.Problem, group.Count, group.EventId, group.Provider, group.FirstSeen, group.LastSeen, group.Channel }));
                WriteSheet(archive, "xl/worksheets/sheet2.xml",
                    includeXml
                        ? ["Time", "Level", "Event ID", "Provider", "Channel", "Record ID", "Computer", "Message", "XML"]
                        : ["Time", "Level", "Event ID", "Provider", "Channel", "Record ID", "Computer", "Message"],
                    events.Select(row => includeXml
                        ? new object?[] { row.Time, row.Level, row.EventId, row.Provider, row.Channel, row.RecordId, row.Computer, (messageFactory ?? DefaultMessage)(row), row.Xml }
                        : [row.Time, row.Level, row.EventId, row.Provider, row.Channel, row.RecordId, row.Computer, (messageFactory ?? DefaultMessage)(row)]));
            }
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string DefaultMessage(EventRow row) => row.Details;

    private static void WriteSheet(ZipArchive archive, string name, string[] headers, IEnumerable<object?[]> rows)
    {
        using var writer = XmlWriter.Create(archive.CreateEntry(name, CompressionLevel.Fastest).Open(), new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = true });
        writer.WriteStartDocument();
        writer.WriteStartElement("worksheet", Spreadsheet);
        writer.WriteStartElement("sheetData", Spreadsheet);
        WriteRow(writer, 1, headers);
        var rowNumber = 2;
        foreach (var row in rows)
        {
            if (rowNumber > 1_048_576)
                throw new InvalidOperationException("事件數量超過單一 Excel 工作表上限。 ");
            WriteRow(writer, rowNumber++, row);
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteRow(XmlWriter writer, int rowNumber, IEnumerable<object?> values)
    {
        writer.WriteStartElement("row", Spreadsheet);
        writer.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));
        var column = 0;
        foreach (var value in values)
        {
            writer.WriteStartElement("c", Spreadsheet);
            writer.WriteAttributeString("r", $"{(char)('A' + column++)}{rowNumber}");
            if (value is byte or short or int or long or float or double or decimal)
            {
                writer.WriteElementString("v", Spreadsheet, Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteAttributeString("t", "inlineStr");
                writer.WriteStartElement("is", Spreadsheet);
                writer.WriteElementString("t", Spreadsheet, Clean(value is DateTime time
                    ? time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    : value?.ToString() ?? ""));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static string Clean(string value) => new(value.Where(character => character is '\t' or '\n' or '\r' || character >= ' ').Take(32_767).ToArray());

    private static void WriteText(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Fastest).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    internal static void SelfTest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"EventFast-{Guid.NewGuid():N}.xlsx");
        try
        {
            var row = new EventRow(DateTime.Now, "錯誤", 41, "Microsoft-Windows-Kernel-Power", "System", 1, "PC", "測試", "<Event />");
            Export(path, ProblemGrouping.Group([row]), [row]);
            using var archive = ZipFile.OpenRead(path);
            var sheet = archive.GetEntry("xl/worksheets/sheet1.xml");
            if (archive.GetEntry("xl/workbook.xml") is null || archive.GetEntry("xl/worksheets/sheet2.xml") is null ||
                sheet is null || !XDocument.Load(sheet.Open()).ToString().Contains("非正常關機"))
                throw new InvalidOperationException("XLSX export self-test failed.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private const string ContentTypes = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
        </Types>
        """;

    private const string PackageRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private const string Workbook = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets><sheet name="問題摘要" sheetId="1" r:id="rId1"/><sheet name="完整事件" sheetId="2" r:id="rId2"/></sheets>
        </workbook>
        """;

    private const string WorkbookRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
          <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private const string Styles = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts>
          <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
          <borders count="1"><border/></borders>
          <cellStyleXfs count="1"><xf/></cellStyleXfs><cellXfs count="1"><xf xfId="0"/></cellXfs>
        </styleSheet>
        """;
}
