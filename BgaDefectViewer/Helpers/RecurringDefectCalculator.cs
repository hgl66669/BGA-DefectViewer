using BgaDefectViewer.Models;

namespace BgaDefectViewer.Helpers;

/// <summary>
/// Computes lot-level recurring defect data in two phases:
///   Phase 1 (fast)  — die-level counts from already-loaded SubstrateMap data.
///   Phase 2 (async) — ball-level counts enriched from AfaFile data.
/// Only INSPECTION=1 (Pre Repair) data is used.
/// </summary>
public static class RecurringDefectCalculator
{
    // ── Phase 1 ───────────────────────────────────────────────────────────

    /// <summary>
    /// Calculate die-level recurring counts from .map files (already loaded).
    /// Returns null when no INSP=1 data is available.
    /// </summary>
    public static RecurringDefectData? CalculateFromMaps(
        IList<SubstrateMap> substrateMaps, string lotName)
    {
        // Collect INSP=1 from every substrate
        var inspections = substrateMaps
            .Select(s => s.Inspections.FirstOrDefault(i => i.InspectionNumber == 1))
            .Where(i => i != null && i.Rows > 0 && i.Cols > 0)
            .ToList();

        if (inspections.Count == 0) return null;

        int rows = inspections[0]!.Rows;
        int cols = inspections[0]!.Cols;

        var data = new RecurringDefectData
        {
            LotName = lotName,
            SubstrateCount = substrateMaps.Count,
            Rows = rows,
            Cols = cols,
            Dies = new RecurringDieInfo[rows, cols]
        };

        // Initialise die info
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                data.Dies[r, c] = new RecurringDieInfo { Row = r, Col = c };

        // Accumulate per-defect-type counts per die position
        foreach (var insp in inspections)
        {
            if (insp!.Rows != rows || insp.Cols != cols) continue;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    char ch = insp.DieGrid[r, c];
                    if (ch == 'G' || ch == '1') continue;
                    char upper = char.ToUpper(ch);
                    var counts = data.Dies[r, c].DefectCounts;
                    counts[upper] = counts.GetValueOrDefault(upper) + 1;
                }
        }

        return data;
    }

    // ── Phase 2 ───────────────────────────────────────────────────────────

    /// <summary>
    /// Enrich existing RecurringDefectData with ball-level counts from AFA files.
    /// Safe to call from a background thread; does not touch the UI.
    /// </summary>
    public static void EnrichWithAfaData(
        RecurringDefectData data, IList<AfaFile> afaFiles)
    {
        int rows = data.Rows;
        int cols = data.Cols;

        // Key: (row, col, ballId) → (count, defectCode → occurrences)
        var ballCounts = new Dictionary<(int, int, int), BallAccumulator>();

        foreach (var afa in afaFiles)
        {
            // Use INSP=1 only
            var insp1Results = afa.Inspections
                .Where(i => i.InspectionNumber == 1)
                .ToList();

            foreach (var result in insp1Results)
            {
                // Parse DieRow/DieCol strings: "1R" → 0, "2C" → 1 (0-based)
                if (!TryParseIndex(result.DieRow, 'R', out int dieRow)) dieRow = 0;
                if (!TryParseIndex(result.DieCol, 'C', out int dieCol)) dieCol = 0;

                if (dieRow >= rows || dieCol >= cols) continue;

                foreach (var ball in result.Defects)
                {
                    if (ball.BallId == -1) continue; // skip Extra balls

                    var key = (dieRow, dieCol, ball.BallId);
                    if (!ballCounts.TryGetValue(key, out var acc))
                    {
                        acc = new BallAccumulator { X = ball.X, Y = ball.Y, Diameter = ball.Diameter };
                        ballCounts[key] = acc;
                    }
                    acc.Count++;
                    acc.DefectCodeCounts[ball.DefectCode] =
                        acc.DefectCodeCounts.GetValueOrDefault(ball.DefectCode) + 1;
                }
            }
        }

        // Build the RecurringBallInfo lists and attach to die infos
        // Group by (row, col)
        var byDie = ballCounts
            .GroupBy(kv => (kv.Key.Item1, kv.Key.Item2))
            .ToDictionary(g => g.Key, g => g.ToList());

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var dieKey = (r, c);
                if (!byDie.TryGetValue(dieKey, out var entries)) continue;

                var balls = entries.Select(kv =>
                {
                    var acc = kv.Value;
                    int dominantCode = acc.DefectCodeCounts.MaxBy(p => p.Value).Key;
                    return new RecurringBallInfo
                    {
                        BallId          = kv.Key.Item3,
                        X               = acc.X,
                        Y               = acc.Y,
                        Diameter        = acc.Diameter,
                        Count           = acc.Count,
                        Judge           = DefectTypes.GetName(dominantCode),
                        DefectCodeCounts = new Dictionary<int, int>(acc.DefectCodeCounts)
                    };
                }).OrderByDescending(b => b.Count).ToList();

                data.Dies[r, c].Balls = balls;
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Parse "1R" / "2C" strings to 0-based index.</summary>
    private static bool TryParseIndex(string s, char suffix, out int index)
    {
        index = 0;
        if (string.IsNullOrEmpty(s)) return false;
        var numPart = s.TrimEnd(suffix, ' ');
        if (int.TryParse(numPart, out int val))
        {
            index = val - 1; // convert 1-based to 0-based
            return true;
        }
        return false;
    }

    private class BallAccumulator
    {
        public double X;
        public double Y;
        public double Diameter;
        public int Count;
        public Dictionary<int, int> DefectCodeCounts = new();
    }
}
