namespace BgaDefectViewer.Simulation.Models;

/// <summary>Full inspection output for one Generate cycle. Mirrors the
/// shape of KBGA's <c>summary.csv</c> — code counts + per-pad list — so
/// future defect judges can extend without changing this contract.</summary>
public class InspectionFrame
{
    public required PadInspectionResult[] PadResults { get; init; }
    public required IReadOnlyDictionary<DefectCode, int> Counts { get; init; }

    public int TotalPads => PadResults.Length;

    /// <summary>One-line summary in KBGA's "OK=84 Miss=162 ..." style.
    /// Only emits non-zero codes (plus OK) to stay compact.</summary>
    public string SummaryText
    {
        get
        {
            var parts = new List<string>(8) { $"OK={Counts.GetValueOrDefault(DefectCode.OK)}" };
            foreach (var code in s_summaryOrder)
            {
                int n = Counts.GetValueOrDefault(code);
                if (n > 0) parts.Add($"{ShortName(code)}={n}");
            }
            return string.Join("  ", parts);
        }
    }

    private static readonly DefectCode[] s_summaryOrder =
    [
        DefectCode.Miss, DefectCode.Shift, DefectCode.SD, DefectCode.LD,
        DefectCode.ETC, DefectCode.Bridge, DefectCode.Extra, DefectCode.EO,
        DefectCode.Failure,
    ];

    private static string ShortName(DefectCode c) => c switch
    {
        DefectCode.Miss => "Miss",
        DefectCode.Shift => "Shift",
        DefectCode.SD => "SD",
        DefectCode.LD => "LD",
        DefectCode.ETC => "ETC",
        DefectCode.Bridge => "Bridge",
        DefectCode.Extra => "Extra",
        DefectCode.EO => "E.O.",
        DefectCode.Failure => "Fail",
        _ => c.ToString(),
    };
}
