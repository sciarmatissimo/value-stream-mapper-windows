// RFC-4180-style CSV parser with delimiter sniffing (comma, semicolon,
// tab), quoted fields, escaped quotes, embedded newlines, BOM and CRLF.

using System.Text;

namespace VsmCore.Import;

public static class CsvTable
{
    public static List<List<string>> Parse(string text)
    {
        if (text.StartsWith('﻿')) text = text[1..];
        var delimiter = SniffDelimiter(text);

        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        int i = 0;

        void EndField() { row.Add(field.ToString()); field.Clear(); }
        void EndRow() { EndField(); rows.Add(row); row = new List<string>(); }

        while (i < text.Length)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                field.Append(c); i++; continue;
            }
            if (c == '"' && field.Length == 0) { inQuotes = true; i++; continue; }
            if (c == delimiter) { EndField(); i++; continue; }
            if (c == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; EndRow(); i++; continue; }
            if (c == '\n') { EndRow(); i++; continue; }
            field.Append(c); i++;
        }
        if (field.Length > 0 || row.Count > 0) EndRow();
        return rows;
    }

    public static char SniffDelimiter(string text)
    {
        int comma = 0, semi = 0, tab = 0, lines = 0;
        bool inQuotes = false;
        foreach (var c in text)
        {
            if (c == '"') inQuotes = !inQuotes;
            if (inQuotes) continue;
            if (c == '\n' && ++lines >= 20) break;
            if (c == ',') comma++; else if (c == ';') semi++; else if (c == '\t') tab++;
        }
        if (semi > comma && semi >= tab) return ';';
        if (tab > comma && tab > semi) return '\t';
        return ',';
    }
}
