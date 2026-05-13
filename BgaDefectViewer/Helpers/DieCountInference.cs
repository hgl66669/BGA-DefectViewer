using BgaDefectViewer.Models;

namespace BgaDefectViewer.Helpers;

/// <summary>
/// 從 SummaryRow 推導「每片基板的 die 數」(DIE/Sub)，用於 DieBase yield 計算。
/// 規則來源：BM_3300SI_Buyofftest.xlsx K77 公式 — 找「所有 8 項缺陷皆為 0 且 GDie&gt;0」
/// 的 row 之最大 GDie 值；若不存在則回傳 0（UI 顯示「請手動輸入」）。
/// </summary>
public static class DieCountInference
{
    public static int InferDieCountPerSubstrate(IEnumerable<SummaryRow> rows)
    {
        return rows
            .Where(r => r.Miss == 0
                     && r.Shift == 0
                     && r.SD == 0
                     && r.LD == 0
                     && r.ETC == 0
                     && r.Bridge == 0
                     && r.Extra == 0
                     && r.EO == 0
                     && r.GDie > 0)
            .Select(r => r.GDie)
            .DefaultIfEmpty(0)
            .Max();
    }
}
