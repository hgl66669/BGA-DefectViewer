using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Parsers;

public static class SummaryCsvParser
{
    /// <summary>
    /// Parses all LOT sections in the file and returns the most recent one (last with data).
    /// A single .summary.csv can accumulate multiple inspection runs over time; we want
    /// the latest run, not the first one.
    /// </summary>
    public static LotSession ParseFirstLot(string filePath)
    {
        // Try multiple encodings to handle BOM and different file sources
        string[] allLines;
        try
        {
            allLines = File.ReadAllLines(filePath, Encoding.UTF8);
        }
        catch
        {
            allLines = File.ReadAllLines(filePath, Encoding.Default);
        }

        // Strip BOM if present on first line
        if (allLines.Length > 0 && allLines[0].Length > 0 && allLines[0][0] == '\uFEFF')
            allLines[0] = allLines[0].Substring(1);

        bool hasLotStart = allLines.Any(l => l.TrimStart().StartsWith("LOT Start"));

        // Collect all LOT sections; each LOT Start begins a new session
        var sessions = new List<LotSession>();
        LotSession? current = null;
        bool inSummary = false;
        int rowIndex = 0;

        // Files with no LOT Start markers are treated as a single lot
        if (!hasLotStart)
        {
            current = new LotSession();
            sessions.Add(current);
        }

        foreach (var rawLine in allLines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // --- LOT Start: begin a new section (don't stop at second one) ---
            if (line.StartsWith("LOT Start"))
            {
                current = new LotSession();
                sessions.Add(current);
                inSummary = false;
                rowIndex = 0;
                var parts = line.Split(',');
                if (parts.Length >= 2)
                    current.LotName = parts[1].Trim();
                continue;
            }

            if (current == null) continue;

            // --- Skip header row ---
            if (line.StartsWith("Name,") || line.StartsWith("Name\t"))
                continue;

            // --- LOT Summary End: close summary section, continue to next LOT ---
            if (line.StartsWith("LOT Summary End"))
            {
                inSummary = false;
                continue;
            }

            // --- LOT Summary section start ---
            if (line.StartsWith("LOT Summary"))
            {
                inSummary = true;
                current.SubstrateCount = ExtractSubstrateCount(line);
                continue;
            }

            if (!inSummary)
            {
                var row = ParseDataRow(line, rowIndex);
                if (row != null)
                {
                    current.Rows.Add(row);
                    rowIndex++;
                }
            }
            else
            {
                ParseSummaryLine(current.Summary, line);
            }
        }

        // Return the last section that has any data rows (most recent inspection run)
        var session = sessions.LastOrDefault(s => s.Rows.Count > 0)
                      ?? sessions.LastOrDefault()
                      ?? new LotSession();

        // If LotName was not set (no LOT Start marker), derive from file name
        if (string.IsNullOrEmpty(session.LotName))
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (fileName.EndsWith(".summary"))
                fileName = fileName.Substring(0, fileName.Length - ".summary".Length);
            session.LotName = fileName;
        }

        // If SubstrateCount was not set, count unique substrate IDs
        if (session.SubstrateCount == 0 && session.Rows.Count > 0)
            session.SubstrateCount = session.Rows.Select(r => r.SubstrateId).Distinct().Count();

        // If CSV had no LOT Summary section, calculate it from individual rows
        if (session.Summary.Lines.Count == 0 && session.Rows.Count > 0)
            session.Summary = LotSummaryCalculator.Calculate(session.Rows);

        return session;
    }

    private static SummaryRow? ParseDataRow(string line, int rowIndex)
    {
        var parts = line.Split(',');

        // Need at least: Name + some numeric data (minimum ~14 fields to be useful)
        if (parts.Length < 14) return null;

        // Sanity check: second field should be numeric (OK count)
        if (!int.TryParse(parts[1].Trim(), out _)) return null;

        return new SummaryRow
        {
            Name = parts[0].Trim(),
            OK = TryParseInt(parts[1]),
            Miss = TryParseInt(parts[2]),
            Shift = TryParseInt(parts[3]),
            SD = TryParseInt(parts[4]),
            LD = TryParseInt(parts[5]),
            ETC = TryParseInt(parts[6]),
            Bridge = TryParseInt(parts[7]),
            Extra = TryParseInt(parts[8]),
            EO = TryParseInt(parts[9]),
            NGDie = SafeGet(parts, 10, TryParseInt),
            GDie = SafeGet(parts, 11, TryParseInt),
            PPM = SafeGet(parts, 12, TryParseDouble),
            Judge = parts.Length > 13 ? parts[13].Trim() : "",
            Stage = SafeGet(parts, 14, TryParseInt),
            YieldPercent = SafeGet(parts, 15, TryParseDouble),
            DateTime = parts.Length > 16 ? parts[16].Trim() : "",
            RowIndex = rowIndex
        };
    }

    private static T SafeGet<T>(string[] parts, int index, Func<string, T> parser)
    {
        if (index < parts.Length)
            return parser(parts[index]);
        return default!;
    }

    private static void ParseSummaryLine(LotSummary summary, string line)
    {
        var parts = line.Split(',');
        if (parts.Length < 2) return;

        var summaryLine = new LotSummaryLine
        {
            Label        = parts[0].Trim(),
            OK           = parts.Length > 1  ? parts[1].Trim()  : "",
            Miss         = parts.Length > 2  ? parts[2].Trim()  : "",
            Shift        = parts.Length > 3  ? parts[3].Trim()  : "",
            SD           = parts.Length > 4  ? parts[4].Trim()  : "",
            LD           = parts.Length > 5  ? parts[5].Trim()  : "",
            ETC          = parts.Length > 6  ? parts[6].Trim()  : "",
            Bridge       = parts.Length > 7  ? parts[7].Trim()  : "",
            Extra        = parts.Length > 8  ? parts[8].Trim()  : "",
            EO           = parts.Length > 9  ? parts[9].Trim()  : "",
            NGDie        = parts.Length > 10 ? parts[10].Trim() : "",
            GDie         = parts.Length > 11 ? parts[11].Trim() : "",
            PPM          = parts.Length > 12 ? parts[12].Trim() : "",
            Judge        = parts.Length > 13 ? parts[13].Trim() : "",
            Stage        = parts.Length > 14 ? parts[14].Trim() : "",
            YieldPercent = parts.Length > 15 ? parts[15].Trim() : "",
            DateTime     = parts.Length > 16 ? parts[16].Trim() : "",
        };

        summary.Lines.Add(summaryLine);
    }

    private static int ExtractSubstrateCount(string lotSummaryLine)
    {
        var match = Regex.Match(lotSummaryLine, @"SubstrateCount=(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private static int TryParseInt(string s)
    {
        int.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out int v);
        return v;
    }

    private static double TryParseDouble(string s)
    {
        double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v);
        return v;
    }
}
