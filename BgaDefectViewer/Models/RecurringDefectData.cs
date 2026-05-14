namespace BgaDefectViewer.Models;

/// <summary>A single ball position with its recurring defect count across substrates.</summary>
public class RecurringBallInfo
{
    public int BallId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Diameter { get; set; }
    /// <summary>Total substrates where this ball was defective (any defect type).</summary>
    public int Count { get; set; }
    /// <summary>Most frequently observed defect type name across all substrates.</summary>
    public string Judge { get; set; } = "";
    /// <summary>
    /// Numeric defect code paired with <see cref="Judge"/>. Used by the detail
    /// grid to colour the Judge cell background (same palette as SubstrateViewer).
    /// Filter-aware: when the user filters by defect type, this reflects the
    /// dominant code AMONG SELECTED TYPES, not the absolute dominant.
    /// </summary>
    public int DominantDefectCode { get; set; }
    /// <summary>Per-defect-code substrate counts: code → how many substrates had that defect here.</summary>
    public Dictionary<int, int> DefectCodeCounts { get; set; } = new();

    public string DisplayX => X.ToString("F3");
    public string DisplayY => Y.ToString("F3");

    /// <summary>
    /// Sum of substrate counts for the given defect codes. An empty filter
    /// means "no defect type selected" → 0 (caller should render nothing),
    /// not the total count.
    /// </summary>
    public int GetCountForCodes(ISet<int> codes)
        => codes.Count == 0 ? 0
           : DefectCodeCounts.Where(kv => codes.Contains(kv.Key)).Sum(kv => kv.Value);
}

/// <summary>Per-die recurring defect info for one die position in the substrate grid.</summary>
public class RecurringDieInfo
{
    public int Row { get; set; }
    public int Col { get; set; }
    /// <summary>Per-defect-type die counts: uppercase die char → substrate count.</summary>
    public Dictionary<char, int> DefectCounts { get; set; } = new();
    /// <summary>Total defective substrates (all defect types).</summary>
    public int DieCount => DefectCounts.Values.Sum();
    /// <summary>Ball-level detail — populated after .afa files are loaded.</summary>
    public List<RecurringBallInfo> Balls { get; set; } = new();
    /// <summary>Max ball-level total count; falls back to DieCount when ball data is not yet available.</summary>
    public int MaxBallCount => Balls.Count > 0 ? Balls.Max(b => b.Count) : DieCount;

    /// <summary>
    /// Filtered die count: sum of counts for the selected defect type chars.
    /// An empty filter means "no defect type selected" → 0, so the cell is
    /// greyed out instead of showing the total.
    /// </summary>
    public int GetCountForChars(ISet<char> chars)
        => chars.Count == 0 ? 0
           : DefectCounts.Where(kv => chars.Contains(kv.Key)).Sum(kv => kv.Value);

    /// <summary>
    /// Max recurring count of any single ball position within this die, filtered
    /// to the selected defect codes. This is the proper "recurring" semantic —
    /// answers "how many substrates did the worst-repeating ball appear in?"
    ///
    /// Phase 2 (Balls populated): max over Balls of GetCountForCodes(codes).
    /// Phase 1 fallback (no ball data): max per-type substrate count under
    /// <paramref name="charsFallback"/>; this is an upper bound, but uses MAX
    /// rather than SUM so the colour/value is in the same conceptual range as
    /// the Phase 2 result (avoids a visible jump when ball data finishes loading).
    /// </summary>
    public int GetMaxBallCountForCodes(ISet<int> codes, ISet<char> charsFallback)
    {
        if (Balls.Count > 0)
        {
            if (codes.Count == 0) return 0;
            int max = 0;
            foreach (var b in Balls)
            {
                int c = b.GetCountForCodes(codes);
                if (c > max) max = c;
            }
            return max;
        }
        if (charsFallback.Count == 0) return 0;
        int dmax = 0;
        foreach (var kv in DefectCounts)
            if (charsFallback.Contains(kv.Key) && kv.Value > dmax) dmax = kv.Value;
        return dmax;
    }

    /// <summary>
    /// How many distinct ball positions in this die recurred at least
    /// <paramref name="minCount"/> times (filtered by selected defect codes).
    /// Answers "how many problem spots does this die have?" — high values
    /// indicate widespread defects, low values indicate localised hotspots.
    ///
    /// Returns 0 when no ball-level data is available (Phase 1 only) because
    /// per-position information doesn't exist in the .map data — the caller
    /// should rely on Max-mode display until Phase 2 lands.
    /// </summary>
    public int GetRecurringPositionCount(ISet<int> codes, int minCount)
    {
        if (Balls.Count == 0 || codes.Count == 0) return 0;
        int count = 0;
        foreach (var b in Balls)
        {
            if (b.GetCountForCodes(codes) >= minCount) count++;
        }
        return count;
    }
}

/// <summary>Lot-level recurring defect data computed from all substrates.</summary>
public class RecurringDefectData
{
    public string LotName { get; set; } = "";
    public int SubstrateCount { get; set; }
    public int Rows { get; set; }
    public int Cols { get; set; }
    /// <summary>[row, col] die info. Initialised in CalculateFromMaps.</summary>
    public RecurringDieInfo[,] Dies { get; set; } = new RecurringDieInfo[0, 0];
}
