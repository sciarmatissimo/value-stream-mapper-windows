// Minimal .xlsx reader: ZIP container via System.IO.Compression, then the
// workbook sheet list, relationships, shared strings and each sheet's grid.

using System.IO.Compression;
using System.Xml;

namespace VsmCore.Import;

public sealed class XlsxReaderException : Exception
{
    public XlsxReaderException(string message) : base(message) { }
}

public static class XlsxReader
{
    public static bool IsZip(byte[] data) =>
        data.Length > 4 && data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04;

    public static List<(string Name, List<List<string>> Rows)> Sheets(byte[] data)
    {
        if (!IsZip(data)) throw new XlsxReaderException("The file is not an Excel (.xlsx) workbook.");
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        var workbook = Entry(zip, "xl/workbook.xml") ?? throw new XlsxReaderException("The workbook is missing its workbook part.");
        var rels = ParseRelationships(Entry(zip, "xl/_rels/workbook.xml.rels") ?? throw new XlsxReaderException("The workbook is missing its relationships part."));
        var shared = Entry(zip, "xl/sharedStrings.xml") is byte[] sst ? ParseSharedStrings(sst) : new List<string>();

        var result = new List<(string, List<List<string>>)>();
        foreach (var (name, relId) in ParseWorkbook(workbook))
        {
            if (!rels.TryGetValue(relId, out var target)) continue;
            var path = target.StartsWith('/') ? target[1..] : "xl/" + target;
            if (Entry(zip, path) is not byte[] xml) continue;
            result.Add((name, ParseSheet(xml, shared)));
        }
        if (result.Count == 0) throw new XlsxReaderException("The workbook is missing its worksheet part.");
        return result;
    }

    public static byte[]? Entry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return null;
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static Dictionary<string, string> ParseRelationships(byte[] xml)
    {
        var map = new Dictionary<string, string>();
        using var r = Reader(xml);
        while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship")
            {
                var id = r.GetAttribute("Id"); var target = r.GetAttribute("Target");
                if (id != null && target != null) map[id] = target;
            }
        return map;
    }

    private static List<(string Name, string RelId)> ParseWorkbook(byte[] xml)
    {
        var sheets = new List<(string, string)>();
        using var r = Reader(xml);
        while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "sheet")
            {
                string relId = "";
                for (int i = 0; i < r.AttributeCount; i++)
                {
                    r.MoveToAttribute(i);
                    if (r.LocalName == "id") relId = r.Value;
                }
                r.MoveToElement();
                sheets.Add((r.GetAttribute("name") ?? "", relId));
            }
        return sheets;
    }

    private static List<string> ParseSharedStrings(byte[] xml)
    {
        var strings = new List<string>();
        using var r = Reader(xml);
        var current = new System.Text.StringBuilder();
        bool inItem = false, inPhonetic = false, inText = false;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                if (r.LocalName == "si") { inItem = true; current.Clear(); }
                else if (r.LocalName == "rPh") inPhonetic = true;
                else if (r.LocalName == "t") inText = inItem && !inPhonetic;
                if (r.IsEmptyElement && r.LocalName == "si") { strings.Add(""); inItem = false; }
            }
            else if (r.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
            {
                if (inText) current.Append(r.Value);
            }
            else if (r.NodeType == XmlNodeType.EndElement)
            {
                if (r.LocalName == "t") inText = false;
                else if (r.LocalName == "rPh") inPhonetic = false;
                else if (r.LocalName == "si") { strings.Add(current.ToString()); inItem = false; }
            }
        }
        return strings;
    }

    private static List<List<string>> ParseSheet(byte[] xml, List<string> shared)
    {
        var rows = new List<List<string>>();
        using var r = Reader(xml);
        Dictionary<int, string>? currentRow = null;
        int col = 0; string type = ""; var value = new System.Text.StringBuilder();
        bool inValue = false, inInline = false;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                switch (r.LocalName)
                {
                    case "row": currentRow = new Dictionary<int, string>(); break;
                    case "c":
                        col = ColumnIndex(r.GetAttribute("r") ?? "");
                        type = r.GetAttribute("t") ?? "";
                        value.Clear();
                        if (r.IsEmptyElement && currentRow != null) currentRow[col] = "";
                        break;
                    case "v": inValue = !r.IsEmptyElement; break;
                    case "t": inInline = type == "inlineStr" && !r.IsEmptyElement; break;
                }
            }
            else if (r.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace)
            {
                if (inValue || inInline) value.Append(r.Value);
            }
            else if (r.NodeType == XmlNodeType.EndElement)
            {
                switch (r.LocalName)
                {
                    case "v": inValue = false; break;
                    case "t": inInline = false; break;
                    case "c":
                        var v = value.ToString();
                        if (type == "s" && int.TryParse(v, out var idx) && idx >= 0 && idx < shared.Count) v = shared[idx];
                        if (currentRow != null) currentRow[col] = v;
                        break;
                    case "row":
                        if (currentRow != null)
                        {
                            var width = currentRow.Count == 0 ? 0 : currentRow.Keys.Max() + 1;
                            var row = Enumerable.Repeat("", width).ToList();
                            foreach (var (c, s) in currentRow) row[c] = s;
                            rows.Add(row);
                            currentRow = null;
                        }
                        break;
                }
            }
        }
        return rows;
    }

    /// <summary>"B12" → 1, "AA3" → 26</summary>
    public static int ColumnIndex(string cellRef)
    {
        int index = 0;
        foreach (var ch in cellRef)
        {
            if (ch < 'A' || ch > 'Z') break;
            index = index * 26 + (ch - 'A' + 1);
        }
        return Math.Max(index - 1, 0);
    }

    private static XmlReader Reader(byte[] xml) =>
        XmlReader.Create(new MemoryStream(xml), new XmlReaderSettings { IgnoreComments = true, DtdProcessing = DtdProcessing.Ignore });
}
