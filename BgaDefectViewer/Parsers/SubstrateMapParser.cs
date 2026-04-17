using System.Text.RegularExpressions;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Parsers;

/// <summary>解析 {SubstrateID}.map 檔案（規格第 6 節）</summary>
public static class SubstrateMapParser
{
    public static SubstrateMap Parse(string filePath)
    {
        var lines = File.ReadAllLines(filePath)
            .Select(l => l.TrimEnd('\r'))
            .ToList();

        var map = new SubstrateMap();
        MapInspection? current = null;
        List<string>? gridLines = null;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("INSPECTION="))
            {
                // 儲存前一輪的 Die Grid
                if (current != null && gridLines != null)
                    FinalizeGrid(current, gridLines);

                current = new MapInspection
                {
                    InspectionNumber = int.Parse(line.Split('=')[1])
                };
                gridLines = new List<string>();
                map.Inspections.Add(current);
            }
            else if (current != null && gridLines != null
                     && gridLines.Count == 0
                     && line.Contains("OK="))
            {
                // 統計行: "{基板ID} OK=... MISS=... ..."
                int firstSpace = line.IndexOf(' ');
                if (firstSpace > 0)
                    map.SubstrateId = line.Substring(0, firstSpace);
                ParseStats(line.Substring(firstSpace + 1), current);
            }
            else if (current != null && gridLines != null
                     && !line.StartsWith("INSPECTION"))
            {
                // Die Grid 行
                gridLines.Add(line);
            }
        }

        // 儲存最後一輪
        if (current != null && gridLines != null)
            FinalizeGrid(current, gridLines);

        return map;
    }

    private static void FinalizeGrid(MapInspection insp, List<string> gridLines)
    {
        if (gridLines.Count == 0) return;
        insp.Rows = gridLines.Count;
        insp.Cols = gridLines[0].Length;
        insp.DieGrid = new char[insp.Rows, insp.Cols];
        for (int r = 0; r < insp.Rows; r++)
            for (int c = 0; c < insp.Cols; c++)
                insp.DieGrid[r, c] = c < gridLines[r].Length
                    ? gridLines[r][c] : '1';
    }

    private static void ParseStats(string stats, MapInspection insp)
    {
        // 特殊處理 E.O.（包含句點）：先 Replace "." 統一後再 match
        var pairs = Regex.Matches(stats, @"([\w.]+?)=([\d.]+)");
        foreach (Match m in pairs)
        {
            var key = m.Groups[1].Value.ToUpper().Replace(".", "");
            var val = m.Groups[2].Value;

            switch (key)
            {
                case "OK":     insp.OK = int.Parse(val); break;
                case "MISS":   insp.Miss = int.Parse(val); break;
                case "SHIFT":  insp.Shift = int.Parse(val); break;
                case "SD":     insp.SD = int.Parse(val); break;
                case "LD":     insp.LD = int.Parse(val); break;
                case "ETC":    insp.ETC = int.Parse(val); break;
                case "BRIDGE": insp.Bridge = int.Parse(val); break;
                case "EXTRA":  insp.Extra = int.Parse(val); break;
                case "EO":     insp.EO = int.Parse(val); break;
                case "GD":     insp.GDie = int.Parse(val); break;
                case "NGD":    insp.NGDie = int.Parse(val); break;
                case "PPM":
                    if (double.TryParse(val, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double ppm))
                        insp.PPM = ppm;
                    break;
            }
        }
    }
}
