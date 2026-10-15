// Used only by GenerateToolCatalog.ps1 (Add-Type). Not compiled into VisionCore.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

public class HelpRow
{
    public string Name;
    public string Desc;
    public List<HelpRow> Children = new List<HelpRow>();
}

public static class HelpTable
{
    static readonly Regex Tag = new Regex(@"<(/?)(table|tr|td|th)\b[^>]*>", RegexOptions.IgnoreCase);
    static readonly Regex Popup = new Regex(@"(?s)<span class=""MCTextPopupBody[^""]*"">\s*<span class=""MCTextPopupArrow"">.*?</span>.*?</span>");

    public static string Text(string html)
    {
        string s = Popup.Replace(html, " ");
        s = Regex.Replace(s, "<[^>]+>", " ");
        s = s.Replace("&#160;", " ").Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    public static string Title(string html)
    {
        Match m = Regex.Match(html, @"(?s)<h1[^>]*>(.*?)</h1>");
        return m.Success ? Text(m.Groups[1].Value) : null;
    }

    // Returns the rows of the Inputs table. Nested tables in the description cell become Children.
    public static List<HelpRow> ParseInputs(string html)
    {
        html = Popup.Replace(html, " ");
        int a = html.IndexOf(">Inputs<", StringComparison.Ordinal);
        if (a < 0) return null;
        int t = html.IndexOf("<table", a, StringComparison.OrdinalIgnoreCase);
        int stop = html.IndexOf(">Outputs<", a, StringComparison.Ordinal);
        if (t < 0 || (stop > 0 && t > stop)) return null;
        int end;
        return ParseTable(html, t, out end);
    }

    static List<HelpRow> ParseTable(string html, int start, out int end)
    {
        var rows = new List<HelpRow>();
        int pos = html.IndexOf('>', start) + 1;
        HelpRow row = null;
        int cellIndex = -1;
        bool inCell = false;
        var cellText = new StringBuilder();
        while (true)
        {
            Match m = Tag.Match(html, pos);
            if (!m.Success) { end = html.Length; return rows; }
            bool close = m.Groups[1].Value == "/";
            string name = m.Groups[2].Value.ToLowerInvariant();
            if (inCell) cellText.Append(html, pos, m.Index - pos);
            if (name == "table" && !close)
            {
                int nestedEnd;
                var nested = ParseTable(html, m.Index, out nestedEnd);
                if (row != null && cellIndex >= 1) row.Children.AddRange(nested);
                pos = nestedEnd;
                continue;
            }
            if (name == "table" && close) { end = m.Index + m.Length; return rows; }
            // Header rows (Parameter/Description, nested column titles) are not arguments.
            if (name == "tr" && !close) { row = m.Value.IndexOf("Header", StringComparison.OrdinalIgnoreCase) >= 0 ? null : new HelpRow(); cellIndex = -1; }
            else if (name == "tr" && close) { if (row != null && !string.IsNullOrEmpty(row.Name)) rows.Add(row); row = null; }
            else if ((name == "td" || name == "th") && !close) { cellIndex++; inCell = true; cellText.Clear(); }
            else if ((name == "td" || name == "th") && close)
            {
                string txt = Text(cellText.ToString());
                if (row != null)
                {
                    if (cellIndex == 0) row.Name = txt;
                    else if (cellIndex == 1) row.Desc = txt;
                }
                inCell = false;
            }
            pos = m.Index + m.Length;
        }
    }
}
