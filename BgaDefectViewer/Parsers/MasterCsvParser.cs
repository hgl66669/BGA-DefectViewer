using System.Globalization;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Parsers;

public static class MasterCsvParser
{
    /// <summary>
    /// Parse a Master.csv into a tightly packed MasterBall[].
    ///
    /// Real KBGA master CSVs interleave section headers ("LOT Start",
    /// "LOT End", blank rows, etc.) with the numeric ball rows. Any line
    /// whose first three comma-separated fields are not all parseable as
    /// invariant-culture doubles is silently skipped — that covers
    /// headers, comments, trailing newlines, and the UTF-8 BOM. Ball IDs
    /// are 1-based and assigned in the order they appear in the file
    /// (skipped rows do not consume an ID).
    /// </summary>
    public static MasterBall[] Parse(string filePath)
    {
        using var fs = FileLocator.OpenSharedRead(filePath);
        using var sr = new StreamReader(fs);
        var balls = new List<MasterBall>();
        string? rawLine;
        while ((rawLine = sr.ReadLine()) != null)
        {
            // Strip UTF-8 BOM if present on the first line.
            var line = rawLine.TrimStart('﻿').Trim();
            if (line.Length == 0) continue;

            var parts = line.Split(',');
            if (parts.Length < 3) continue;

            // TryParse all three numeric fields — any failure (e.g. a
            // "LOT Start" header row) means this is not a ball row.
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)) continue;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double diameter)) continue;

            balls.Add(new MasterBall
            {
                Id = balls.Count + 1,
                X = x,
                Y = y,
                Diameter = diameter,
            });
        }
        return balls.ToArray();
    }

    public static (double minX, double maxX, double minY, double maxY) GetBounds(MasterBall[] balls)
    {
        if (balls.Length == 0) return (0, 0, 0, 0);

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        foreach (var b in balls)
        {
            if (b.X < minX) minX = b.X;
            if (b.X > maxX) maxX = b.X;
            if (b.Y < minY) minY = b.Y;
            if (b.Y > maxY) maxY = b.Y;
        }
        return (minX, maxX, minY, maxY);
    }
}
