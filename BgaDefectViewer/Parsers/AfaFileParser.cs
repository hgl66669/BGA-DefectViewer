using System.Globalization;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Parsers;

public static class AfaFileParser
{
    public static AfaFile Parse(string filePath)
    {
        var afa = new AfaFile();
        int currentInspNumber = 0;
        InspectionResult? currentInsp = null;

        foreach (var line in File.ReadAllLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("StartTime;"))
                afa.StartTime = line.Substring("StartTime;".Length).Trim();
            else if (line.StartsWith("Mapfile;"))
                afa.MapFile = line.Substring("Mapfile;".Length).Trim();
            else if (line.StartsWith("Recipe;"))
                afa.Recipe = line.Substring("Recipe;".Length).Trim();
            else if (line.StartsWith("LotNo;"))
                afa.LotNo = line.Substring("LotNo;".Length).Trim();
            else if (line.StartsWith("WaferID;"))
                afa.WaferId = line.Substring("WaferID;".Length).Trim();
            else if (line.StartsWith("BallName;"))
                afa.BallName = line.Substring("BallName;".Length).Trim();
            else if (line.StartsWith("BallDiameter;"))
                afa.BallDiameter = ParseDouble(line.Substring("BallDiameter;".Length).Trim());
            else if (line.StartsWith("Balls;"))
                afa.Balls = ParseInt(line.Substring("Balls;".Length).Trim());
            else if (line.StartsWith("TotalBalls;"))
                afa.TotalBalls = ParseInt(line.Substring("TotalBalls;".Length).Trim());
            else if (line.StartsWith("INSPECTION="))
            {
                // Track inspection number; each Data; line below creates its own InspectionResult
                currentInspNumber = ParseInt(line.Substring("INSPECTION=".Length).Trim());
                currentInsp = null;
            }
            else if (line.StartsWith("Data;"))
            {
                // Each Data; line represents one defective die → create a new InspectionResult
                var parts = line.Substring("Data;".Length).Split(',');
                if (parts.Length >= 5)
                {
                    currentInsp = new InspectionResult
                    {
                        InspectionNumber = currentInspNumber,
                        DieIndex  = ParseInt(parts[0]),
                        DieCol    = parts[1].Trim(),
                        DieRow    = parts[2].Trim(),
                        WorstCode = ParseInt(parts[3]),
                        WorstName = parts[4].Trim()
                    };
                    afa.Inspections.Add(currentInsp);
                }
            }
            else if (line.StartsWith("BallData;") && currentInsp != null)
            {
                var parts = line.Substring("BallData;".Length).Split(',');
                if (parts.Length >= 5)
                {
                    currentInsp.Defects.Add(new DefectBall
                    {
                        BallId     = ParseInt(parts[0]),
                        DefectCode = ParseInt(parts[1]),
                        X          = ParseDouble(parts[2]),
                        Y          = ParseDouble(parts[3]),
                        Diameter   = ParseDouble(parts[4]),
                        Unknown    = parts.Length > 5 ? ParseInt(parts[5]) : 0
                    });
                }
            }
            else if (line.StartsWith("EndTime;"))
                afa.EndTime = line.Substring("EndTime;".Length).Trim();
            else if (line.StartsWith("EndInspection;"))
                afa.DurationSeconds = ParseDouble(line.Substring("EndInspection;".Length).Trim());
        }

        return afa;
    }

    private static int ParseInt(string s)
    {
        int.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out int v);
        return v;
    }

    private static double ParseDouble(string s)
    {
        double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v);
        return v;
    }
}
