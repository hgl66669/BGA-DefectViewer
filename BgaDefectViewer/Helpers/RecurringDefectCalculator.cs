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

    // Extra balls have BallId == -1 (no Master correspondence), so cross-substrate
    // recurring can't be keyed by BallId. Instead we cluster them within each die
    // by XY proximity: defects within this tolerance (mm) at the same die position
    // across different substrates are treated as the same recurring position.
    private const double ExtraClusterToleranceMm = 0.15;

    // Synthetic BallId code for Extra clusters: 4 = Extra in DefectTypes.
    private const int ExtraDefectCode = 4;

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

        // Extras collected per die for cross-substrate clustering after the loop.
        var extrasByDie = new Dictionary<(int, int), List<DefectBall>>();

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
                    if (ball.BallId == -1)
                    {
                        // Extra: defer to clustering pass (no Master BallId to group by).
                        if (!extrasByDie.TryGetValue((dieRow, dieCol), out var list))
                        {
                            list = new List<DefectBall>();
                            extrasByDie[(dieRow, dieCol)] = list;
                        }
                        list.Add(ball);
                        continue;
                    }

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

        // Cluster Extras per die and inject each cluster as a synthetic ball.
        // Synthetic BallIds start at -1000 and decrement so they don't collide
        // with the existing -1 sentinel and remain visually identifiable in the
        // detail table.
        int nextExtraId = -1000;
        foreach (var ((dr, dc), extras) in extrasByDie)
        {
            foreach (var cluster in ClusterExtras(extras, ExtraClusterToleranceMm))
            {
                var acc = new BallAccumulator
                {
                    X = cluster.AverageX,
                    Y = cluster.AverageY,
                    Diameter = cluster.AverageDiameter,
                    Count = cluster.Count,
                };
                acc.DefectCodeCounts[ExtraDefectCode] = cluster.Count;
                ballCounts[(dr, dc, nextExtraId--)] = acc;
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
                        BallId             = kv.Key.Item3,
                        X                  = acc.X,
                        Y                  = acc.Y,
                        Diameter           = acc.Diameter,
                        Count              = acc.Count,
                        Judge              = DefectTypes.GetName(dominantCode),
                        DominantDefectCode = dominantCode,
                        DefectCodeCounts   = new Dictionary<int, int>(acc.DefectCodeCounts)
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

    /// <summary>
    /// Greedy proximity-based clustering of Extra defects. Each incoming point
    /// joins the nearest existing cluster within <paramref name="tolerance"/>
    /// (centroid distance), otherwise starts a new cluster. Order-dependent but
    /// adequate for the typical handful of Extras per die.
    /// </summary>
    private static List<ExtraCluster> ClusterExtras(List<DefectBall> extras, double tolerance)
    {
        var clusters = new List<ExtraCluster>();
        double t2 = tolerance * tolerance;
        foreach (var ex in extras)
        {
            ExtraCluster? best = null;
            double bestD2 = t2;
            foreach (var c in clusters)
            {
                double dx = ex.X - c.AverageX;
                double dy = ex.Y - c.AverageY;
                double d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; best = c; }
            }
            if (best == null)
            {
                var nc = new ExtraCluster();
                nc.Add(ex);
                clusters.Add(nc);
            }
            else
            {
                best.Add(ex);
            }
        }
        return clusters;
    }

    private class ExtraCluster
    {
        private double _sumX, _sumY, _sumDia;
        public int Count { get; private set; }

        public double AverageX => Count > 0 ? _sumX / Count : 0;
        public double AverageY => Count > 0 ? _sumY / Count : 0;
        public double AverageDiameter => Count > 0 ? _sumDia / Count : 0;

        public void Add(DefectBall b)
        {
            _sumX += b.X;
            _sumY += b.Y;
            _sumDia += b.Diameter;
            Count++;
        }
    }
}
