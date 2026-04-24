using System.Globalization;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Parsers;

/// <summary>
/// Parses the INI-style `Master.dat` (or `{partNo}.dat`) sidecar that sits
/// next to the master CSV in `AthleteSYS/KbgaData/Master/`.
///
/// Only the alignment fields are consumed right now. The file also contains
/// pixel-space positions (AlignmentPoint1 / AlignmentPoint2 / AlignmentCenter)
/// but we prefer the `Mm` variants because they are already in the same
/// stage-coordinate system the simulator uses (mm, device origin at 0,0).
/// </summary>
public static class MasterDatParser
{
    /// <summary>
    /// Read the file. Returns null if the file is missing or unreadable —
    /// callers should treat that as "no alignment registered yet".
    /// </summary>
    public static MasterMetadata? Parse(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        string[] lines;
        try { lines = File.ReadAllLines(filePath); }
        catch { return null; }

        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("[")) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }

        var md = new MasterMetadata
        {
            AlignmentPoint1Mm = TryParsePair(kv, "AlignmentPoint1mm"),
            AlignmentPoint2Mm = TryParsePair(kv, "AlignmentPoint2mm"),
            AlignmentCenterMm = TryParsePair(kv, "AlignmentCenterMm"),
            SubstrateSize    = TryParseTriple(kv, "SubstrateSize"),
        };

        // If none of the fields are present, there is nothing useful to
        // return — callers can fall back to default grid-index alignment.
        if (md.AlignmentPoint1Mm == null &&
            md.AlignmentPoint2Mm == null &&
            md.AlignmentCenterMm == null &&
            md.SubstrateSize == null) return null;

        return md;
    }

    private static (double X, double Y, double Z)? TryParseTriple(
        Dictionary<string, string> kv, string key)
    {
        if (!kv.TryGetValue(key, out var val)) return null;
        var parts = val.Split(',');
        if (parts.Length < 3) return null;
        if (double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
            double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double y) &&
            double.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
        {
            return (x, y, z);
        }
        return null;
    }

    private static (double X, double Y)? TryParsePair(Dictionary<string, string> kv, string key)
    {
        if (!kv.TryGetValue(key, out var val)) return null;
        var parts = val.Split(',');
        if (parts.Length != 2) return null;
        if (double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
            double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
        {
            return (x, y);
        }
        return null;
    }
}
