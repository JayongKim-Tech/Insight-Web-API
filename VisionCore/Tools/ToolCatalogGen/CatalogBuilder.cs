// Used only by GenerateToolCatalog.ps1 (Add-Type). Not compiled into VisionCore.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

public class CatalogArg
{
    public string name;
    public string group;
    public string kind;      // ref | number | enum
    public double? min;
    public double? max;
    public string def;
    public List<string[]> options;
    public string desc;
}

public static class CatalogBuilder
{
    static readonly Regex EnumRe = new Regex(@"^(-?\d+)\s*=\s*(.+)$");
    static readonly Regex RangeRe = new Regex(@"^(-?[\d.]+)\s+to\s+(-?[\d.]+)");
    static readonly Regex NumRe = new Regex(@"^(-?[\d.]+)(\s*\(default\))?$");
    // Sub-field of a composite argument: "X", "X 0", "Width (default = 320)", "Angle Start", "Color: Blob"
    static readonly Regex SubRe = new Regex(@"^[A-Za-z][A-Za-z0-9 .:/_-]{0,40}?(\s*\(default\s*=?\s*[^)]*\))?$", RegexOptions.IgnoreCase);
    static readonly Regex SubDefault = new Regex(@"\s*\(default\s*=?\s*([^)]*)\)\s*$", RegexOptions.IgnoreCase);
    static readonly Regex DescRange = new Regex(@"\((-?[\d.]+)\s+to\s+(-?[\d.]+)");
    static readonly Regex DescDefault = new Regex(@"(?i)default(?:\s+setting)?\s*(?:=|is)\s*(-?[\d.]+)");
    static readonly Regex DescList = new Regex(@"\(([A-Z][A-Za-z]*(?:,\s*[A-Z][A-Za-z]*)+)\)");
    static readonly Regex DefaultMark = new Regex(@"\s*\(default\)", RegexOptions.IgnoreCase);

    public static List<CatalogArg> Build(List<HelpRow> rows)
    {
        var args = new List<CatalogArg>();
        foreach (HelpRow r in rows)
        {
            if (r.Desc == "Description") continue;
            if (r.Name.StartsWith("Note", StringComparison.OrdinalIgnoreCase)) continue;
            // Value-table rows at top level ("0", "22", "100 to 107" under Event's Trigger) explain the previous argument
            if (Regex.IsMatch(r.Name, @"^-?\d+(\s+to\s+-?\d+)?$")) continue;

            // Nested tables sometimes use an unmarked first row as column title (e.g. Status: "Status Value" with blank cells).
            if (r.Children.Any(c => !string.IsNullOrEmpty(c.Desc)))
                r.Children.RemoveAll(c => string.IsNullOrEmpty(c.Desc));

            var subs = r.Children.Where(IsSubField).ToList();
            var enums = r.Children.Where(c => EnumRe.IsMatch(c.Name)).ToList();

            // Composite parameter (Fixture, Region, Line, Point, Find Tolerances, ...): one argument per sub-field.
            // Needs 2+ real sub-fields; tables that only explain a value (device lists, formulas, path examples) are not composites.
            if (subs.Count >= 2 && enums.Count == 0 && !NotComposite.Contains(r.Name))
            {
                foreach (HelpRow s in subs)
                {
                    var sub = new CatalogArg { group = r.Name, kind = "number", desc = Short(s.Desc) };
                    Match sd = SubDefault.Match(s.Name);
                    string name = sd.Success ? s.Name.Substring(0, sd.Index) : s.Name;
                    if (sd.Success && Num(sd.Groups[1].Value.Trim()) != null) sub.def = sd.Groups[1].Value.Trim();
                    // "X 0" / "Y 1" (line endpoints) → "X0" / "Y1"
                    sub.name = Regex.Replace(name.Trim(), @"^([XY])\s+(\d)$", "$1$2");
                    Match rm = DescRange.Match(s.Desc ?? "");
                    if (rm.Success) { sub.min = Num(rm.Groups[1].Value); sub.max = Num(rm.Groups[2].Value); }
                    if (sub.def == null) { Match dm = DescDefault.Match(s.Desc ?? ""); if (dm.Success) sub.def = dm.Groups[1].Value; }
                    args.Add(sub);
                }
                continue;
            }
            Match list = r.Children.Count == 0 && (r.Name == "Fixture" || r.Name == "Region") ? DescList.Match(r.Desc ?? "") : Match.Empty;
            if (list.Success)
            {
                foreach (string f in list.Groups[1].Value.Split(','))
                    args.Add(new CatalogArg { name = f.Trim(), group = r.Name, kind = "number" });
                continue;
            }

            var a = new CatalogArg { name = r.Name, desc = Short(r.Desc) };
            string d = r.Desc ?? "";
            if (r.Name == "Image" || Regex.IsMatch(d, @"(?i)^(This parameter (must reference|uses Cell Reference)|Reference to a cell)"))
                a.kind = "ref";
            else if (enums.Count > 0)
            {
                a.kind = "enum";
                a.options = new List<string[]>();
                foreach (HelpRow e in enums)
                {
                    Match m = EnumRe.Match(e.Name);
                    string label = m.Groups[2].Value.Trim();
                    if (DefaultMark.IsMatch(label)) a.def = m.Groups[1].Value;
                    a.options.Add(new[] { m.Groups[1].Value, DefaultMark.Replace(label, "").Trim() });
                }
            }
            else
            {
                a.kind = "number";
                foreach (HelpRow c in r.Children)
                {
                    Match rm = RangeRe.Match(c.Name);
                    if (rm.Success)
                    {
                        double? lo = Num(rm.Groups[1].Value), hi = Num(rm.Groups[2].Value);
                        if (a.min == null || lo < a.min) a.min = lo;
                        if (a.max == null || hi > a.max) a.max = hi;
                        continue;
                    }
                    Match nm = NumRe.Match(c.Name);
                    if (nm.Success)
                    {
                        // "0" next to "1 to 4096" widens the range
                        double? n = Num(nm.Groups[1].Value);
                        if (a.min == null || n < a.min) a.min = n;
                        if (a.max == null || n > a.max) a.max = n;
                        if (nm.Groups[2].Success) a.def = nm.Groups[1].Value;
                    }
                    Match dm = DescDefault.Match(c.Name);
                    if (dm.Success) a.def = dm.Groups[1].Value;
                }
                if (a.min == null)
                {
                    Match rm = DescRange.Match(d);
                    if (rm.Success) { a.min = Num(rm.Groups[1].Value); a.max = Num(rm.Groups[2].Value); }
                }
                if (a.def == null)
                {
                    Match dm = DescDefault.Match(d);
                    if (dm.Success) a.def = dm.Groups[1].Value;
                }
            }
            args.Add(a);
        }
        return args;
    }

    // Sub-field row: short field-like name ("X", "Y 0", "Width (default = 320)", "Angle Start")
    // with a real description sentence (not "1 byte", "Any device", "X:/Subdir" or a score formula).
    static bool IsSubField(HelpRow c)
    {
        if (string.IsNullOrEmpty(c.Name) || EnumRe.IsMatch(c.Name) || !SubRe.IsMatch(c.Name)) return false;
        string name = SubDefault.Replace(c.Name, "").Trim();
        if (name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length > 4) return false;
        if (Regex.IsMatch(name, @"(?i)\bseries\b|\bis (greater|less)\b|In-Sight")) return false;   // per-device notes, score formulas
        string d = (c.Desc ?? "").Trim();
        if (d.Length == 0 || Regex.IsMatch(d, @"\bScore\s*=|=\s*\[")) return false;
        return true;
    }

    // Parameters whose help table lists example values (storage targets, path styles, data types) - one argument, not a composite.
    static readonly HashSet<string> NotComposite = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Storage", "File Path", "File Name", "Format", "Start Bit", "Number of Bits"
    };

    static double? Num(string s)
    {
        double v;
        return double.TryParse(s.TrimEnd('.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : (double?)null;
    }

    // Full description for the help panel (capped so the catalog stays small).
    static string Short(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        return s.Length > 800 ? s.Substring(0, 800) + "..." : s;
    }
}
