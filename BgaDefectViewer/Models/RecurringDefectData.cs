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
    /// <summary>Per-defect-code substrate counts: code → how many substrates had that defect here.</summary>
    public Dictionary<int, int> DefectCodeCounts { get; set; } = new();

    public string DisplayX => X.ToString("F3");
    public string DisplayY => Y.ToString("F3");

    /// <summary>Sum of substrate counts for the given defect codes (used for filtered display).</summary>
    public int GetCountForCodes(ISet<int> codes)
        => codes.Count == 0 ? Count
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

    /// <summary>Filtered die count: sum of counts for the selected defect type chars.</summary>
    public int GetCountForChars(ISet<char> chars)
        => chars.Count == 0 ? DieCount
           : DefectCounts.Where(kv => chars.Contains(kv.Key)).Sum(kv => kv.Value);
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
