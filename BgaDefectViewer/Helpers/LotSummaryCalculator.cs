using BgaDefectViewer.Models;

namespace BgaDefectViewer.Helpers;

/// <summary>
/// Calculates the 5-row LOT Summary (1st Insp., Repaired, PPM rows, Repaired[%])
/// from individual SummaryRow data when the CSV has no built-in LOT Summary section.
/// </summary>
public static class LotSummaryCalculator
{
    public static LotSummary Calculate(List<SummaryRow> rows)
    {
        // De-duplicate: for each (Name, Stage), keep the last row
        // (later CSV entries are more recent runs of the same stage)
        var deduped = new Dictionary<(string, int), SummaryRow>();
        foreach (var r in rows)
            deduped[(r.Name, r.Stage)] = r;
        var all = deduped.Values.ToList();

        // Stage=1 row (minimum stage) for each unique substrate
        var stage1 = all.GroupBy(r => r.Name)
                        .Select(g => g.OrderBy(r => r.Stage).First())
                        .ToList();

        // Substrates that have any Stage > 1 (went through repair)
        var repairedNames = all.Where(r => r.Stage > 1)
                               .Select(r => r.Name)
                               .ToHashSet();

        // Last-stage row for each repaired substrate
        var repaired = all.Where(r => repairedNames.Contains(r.Name))
                          .GroupBy(r => r.Name)
                          .Select(g => g.OrderByDescending(r => r.Stage).First())
                          .ToList();

        // Stage=1 rows restricted to repaired substrates only (for Repaired[%] comparison)
        var stage1OfRepaired = stage1.Where(r => repairedNames.Contains(r.Name)).ToList();

        var summary = new LotSummary { IsCalculated = true };
        summary.Lines.Add(BuildAgg("1st Insp.", stage1));
        summary.Lines.Add(BuildAgg("Repaired", repaired));
        summary.Lines.Add(BuildPpm("1st Insp.(PPM)", stage1));
        summary.Lines.Add(BuildPpm("Repaired(PPM)", repaired));
        summary.Lines.Add(BuildRepairPct(stage1OfRepaired, repaired));
        return summary;
    }

    // ── Aggregate row (counts + PPM + Yield) ─────────────────────────────

    private static LotSummaryLine BuildAgg(string label, List<SummaryRow> rows)
    {
        if (rows.Count == 0)
            return new LotSummaryLine { Label = label };

        int ok     = rows.Sum(r => r.OK);
        int miss   = rows.Sum(r => r.Miss);
        int shift  = rows.Sum(r => r.Shift);
        int sd     = rows.Sum(r => r.SD);
        int ld     = rows.Sum(r => r.LD);
        int etc    = rows.Sum(r => r.ETC);
        int bridge = rows.Sum(r => r.Bridge);
        int extra  = rows.Sum(r => r.Extra);
        int eo     = rows.Sum(r => r.EO);
        int ng     = rows.Sum(r => r.NGDie);
        int gd     = rows.Sum(r => r.GDie);

        int defects = miss + shift + sd + ld + etc + bridge + extra + eo;
        double ppm  = ok > 0 ? defects / (double)ok * 1e6 : 0.0;
        double yld  = (gd + ng) > 0
                        ? gd / (double)(gd + ng) * 100.0
                        : (defects == 0 ? 100.0 : 0.0);

        string dt = rows.Select(r => r.DateTime)
                        .Where(s => !string.IsNullOrEmpty(s))
                        .LastOrDefault() ?? "";

        return new LotSummaryLine
        {
            Label        = label,
            OK           = ok.ToString(),
            Miss         = miss.ToString(),
            Shift        = shift.ToString(),
            SD           = sd.ToString(),
            LD           = ld.ToString(),
            ETC          = etc.ToString(),
            Bridge       = bridge.ToString(),
            Extra        = extra.ToString(),
            EO           = eo.ToString(),
            NGDie        = ng.ToString(),
            GDie         = gd.ToString(),
            PPM          = ppm.ToString("F1"),
            YieldPercent = yld.ToString("F3"),
            DateTime     = dt
        };
    }

    // ── PPM-per-defect-type row ───────────────────────────────────────────

    private static LotSummaryLine BuildPpm(string label, List<SummaryRow> rows)
    {
        if (rows.Count == 0)
            return new LotSummaryLine { Label = label };

        int ok = rows.Sum(r => r.OK);
        if (ok == 0)
            return new LotSummaryLine { Label = label };

        double Ppm(int n) => n / (double)ok * 1e6;
        int defects = rows.Sum(r => r.Miss + r.Shift + r.SD + r.LD + r.ETC + r.Bridge + r.Extra + r.EO);

        return new LotSummaryLine
        {
            Label  = label,
            Miss   = Ppm(rows.Sum(r => r.Miss)).ToString("F1"),
            Shift  = Ppm(rows.Sum(r => r.Shift)).ToString("F1"),
            SD     = Ppm(rows.Sum(r => r.SD)).ToString("F1"),
            LD     = Ppm(rows.Sum(r => r.LD)).ToString("F1"),
            ETC    = Ppm(rows.Sum(r => r.ETC)).ToString("F1"),
            Bridge = Ppm(rows.Sum(r => r.Bridge)).ToString("F1"),
            Extra  = Ppm(rows.Sum(r => r.Extra)).ToString("F1"),
            EO     = Ppm(rows.Sum(r => r.EO)).ToString("F1"),
            PPM    = Ppm(defects).ToString("F1")
        };
    }

    // ── Repaired[%] row ──────────────────────────────────────────────────

    private static LotSummaryLine BuildRepairPct(List<SummaryRow> stage1Sub, List<SummaryRow> repaired)
    {
        var line = new LotSummaryLine { Label = "Repaired[%]" };

        if (repaired.Count == 0)
        {
            // No repairs → all dashes
            line.OK = line.Miss = line.Shift = line.SD = line.LD =
            line.ETC = line.Bridge = line.Extra = line.EO = "-";
            return line;
        }

        int s1Ok = stage1Sub.Sum(r => r.OK);
        line.OK     = s1Ok > 0
                        ? (repaired.Sum(r => r.OK) / (double)s1Ok * 100.0).ToString("F1")
                        : "-";

        line.Miss   = PctImprove(stage1Sub.Sum(r => r.Miss),   repaired.Sum(r => r.Miss));
        line.Shift  = PctImprove(stage1Sub.Sum(r => r.Shift),  repaired.Sum(r => r.Shift));
        line.SD     = PctImprove(stage1Sub.Sum(r => r.SD),     repaired.Sum(r => r.SD));
        line.LD     = PctImprove(stage1Sub.Sum(r => r.LD),     repaired.Sum(r => r.LD));
        line.ETC    = PctImprove(stage1Sub.Sum(r => r.ETC),    repaired.Sum(r => r.ETC));
        line.Bridge = PctImprove(stage1Sub.Sum(r => r.Bridge), repaired.Sum(r => r.Bridge));
        line.Extra  = PctImprove(stage1Sub.Sum(r => r.Extra),  repaired.Sum(r => r.Extra));
        line.EO     = PctImprove(stage1Sub.Sum(r => r.EO),     repaired.Sum(r => r.EO));

        return line;
    }

    /// <summary>
    /// Returns improvement percentage: (1 - repaired/original) × 100.
    /// Returns "-" if original is 0 or repair made things worse.
    /// </summary>
    private static string PctImprove(int original, int repaired) =>
        original <= 0 || repaired > original
            ? "-"
            : ((1.0 - repaired / (double)original) * 100.0).ToString("F1");
}
