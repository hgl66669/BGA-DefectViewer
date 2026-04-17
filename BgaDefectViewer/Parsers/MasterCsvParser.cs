using System.Globalization;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Parsers;

public static class MasterCsvParser
{
    public static MasterBall[] Parse(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var balls = new MasterBall[lines.Length];

        for (int i = 0; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 3) continue;
            balls[i] = new MasterBall
            {
                Id = i + 1,
                X = double.Parse(parts[0], CultureInfo.InvariantCulture),
                Y = double.Parse(parts[1], CultureInfo.InvariantCulture),
                Diameter = double.Parse(parts[2], CultureInfo.InvariantCulture)
            };
        }
        return balls;
    }

    public static (double minX, double maxX, double minY, double maxY) GetBounds(MasterBall[] balls)
    {
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
