using BgaDefectViewer.Models;

namespace BgaDefectViewer.Helpers;

/// <summary>
/// Calculates the 5-row LOT Summary (1st Insp., Repaired, PPM rows, Repaired[%])
/// from individual SummaryRow data when the CSV has no built-in LOT Summary section.
/// </summary>
public static class LotSummaryCalculator
{
    // ── Public API ───────────────────────────────────────────────────────

    /// <summary>
    /// 既有 API：以預設選項計算（Yield = GDie / (GDie + NGDie) × 100）。
    /// </summary>
    public static LotSummary Calculate(List<SummaryRow> rows)
        => Calculate(rows, LotSummaryOptions.Default());

    /// <summary>
    /// 新 overload：可指定 Yield 模式（Default / DieBase）、是否計入 ETC、以及 DIE/Sub。
    /// 維持 5-row 輸出結構；只有 DieBase 模式會覆寫「1st Insp.」與「Repaired」兩行的 <c>YieldPercent</c>。
    /// </summary>
    public static LotSummary Calculate(List<SummaryRow> rows, LotSummaryOptions options)
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
        summary.Lines.Add(BuildAgg("1st Insp.", stage1, options));
        summary.Lines.Add(BuildAgg("Repaired", repaired, options));
        summary.Lines.Add(BuildPpm("1st Insp.(PPM)", stage1));
        summary.Lines.Add(BuildPpm("Repaired(PPM)", repaired));
        summary.Lines.Add(BuildRepairPct(stage1OfRepaired, repaired));

        // DieBase 模式：覆寫前兩行的 YieldPercent
        if (options.Mode == YieldMode.DieBase)
        {
            summary.Lines[0].YieldPercent = ComputeDieBaseYield(stage1, options).ToString("F3");
            summary.Lines[1].YieldPercent = ComputeDieBaseYield(repaired, options).ToString("F3");
        }

        return summary;
    }

    /// <summary>
    /// True iff the row has <b>at least one ETC-class defect</b>
    /// (ETC / E.O. / LD / SD / Bridge &gt; 0) AND <b>no hard defect</b>
    /// (Miss / Shift / Extra all zero). CountETC=false reclassifies such rows as good
    /// (their NGDie is moved into GDie).
    /// <para>
    /// Note: a "fully clean" row (all 8 defects = 0) is <b>not</b> considered ETC-class-only,
    /// even if it happens to have an anomalous NGDie &gt; 0 — those rows' NGDie keeps counting
    /// normally because there's no ETC-class defect to attribute it to.
    /// </para>
    /// </summary>
    public static bool IsETCClassOnly(SummaryRow r) =>
        r.Miss == 0 && r.Shift == 0 && r.Extra == 0
        && (r.ETC > 0 || r.EO > 0 || r.LD > 0 || r.SD > 0 || r.Bridge > 0);

    /// <summary>
    /// True when toggling CountETC would actually change the aggregate for this row —
    /// i.e. the row has ETC-class-only defects AND a non-zero NGDie. Used to decide whether
    /// the LOT Summary indicator should flip to "calculated" (amber).
    /// </summary>
    public static bool WouldCountETCAffect(SummaryRow r) =>
        r.NGDie > 0 && IsETCClassOnly(r);

    // ── DieBase Yield ────────────────────────────────────────────────────

    /// <summary>
    /// 仿 Excel L77 array formula 的精神（DIE base yield）：
    /// <c>Yield = (1 - Σ NGDie(selected) / (n × D)) × 100</c>
    /// 其中 <c>n</c> = 傳入 row 數（不會因 CountETC 而改變），
    /// <c>D</c> = <see cref="LotSummaryOptions.DieBaseDieCount"/>。
    /// CountETC=false 時，分子排除 ETC 類 (ETC/E.O./LD/SD/Bridge) only 的 row，
    /// 也就是僅 Miss/Shift/Extra 中至少一項 &gt; 0 的 row 才計入。
    /// 若 <c>n×D ≤ 0</c> 直接回傳 0；結果 clamp 到 [0, 100]。
    /// </summary>
    private static double ComputeDieBaseYield(List<SummaryRow> rows, LotSummaryOptions opt)
    {
        int n = rows.Count;
        int d = opt.DieBaseDieCount;
        if (n <= 0 || d <= 0) return 0.0;

        IEnumerable<SummaryRow> selected = opt.CountETC
            ? rows
            : rows.Where(r => !IsETCClassOnly(r));

        int sumK = selected.Sum(r => r.NGDie);
        double yield = (1.0 - sumK / (double)(n * d)) * 100.0;
        return Math.Clamp(yield, 0.0, 100.0);
    }

    // ── Aggregate row (counts + PPM + Yield) ─────────────────────────────

    private static LotSummaryLine BuildAgg(string label, List<SummaryRow> rows, LotSummaryOptions opt)
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

        int ng, gd;
        if (!opt.CountETC)
        {
            // CountETC=false：把「無 Miss/Shift/Extra」、僅含 ETC 類缺陷 (ETC/E.O./LD/SD/Bridge) 的
            // row 重新分類為良品 — NGDie 計入 GDie，NGDie 計數歸零。總 die 數保持不變。
            // 若批次內沒有此類 row（含 NGDie>0），結果與 CountETC=true 完全相同。
            int etcClassNg = rows.Where(IsETCClassOnly).Sum(r => r.NGDie);
            ng = rows.Sum(r => r.NGDie) - etcClassNg;
            gd = rows.Sum(r => r.GDie) + etcClassNg;
        }
        else
        {
            ng = rows.Sum(r => r.NGDie);
            gd = rows.Sum(r => r.GDie);
        }

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
