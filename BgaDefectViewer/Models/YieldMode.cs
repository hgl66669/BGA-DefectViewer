namespace BgaDefectViewer.Models;

/// <summary>
/// 良率計算模式：
/// <list type="bullet">
/// <item><c>Default</c> — 沿用 KBGA result 的 <c>GDie / (GDie + NGDie)</c> 公式。</item>
/// <item><c>DieBase</c> — 仿 BM_3300SI_Buyofftest.xlsx L77 array formula：
///   <c>1 - (Σ NGDie of selected Stage=1 rows) / (n × DIE/Sub)</c>。</item>
/// </list>
/// </summary>
public enum YieldMode { Default, DieBase }

/// <summary>
/// <see cref="Helpers.LotSummaryCalculator.Calculate(System.Collections.Generic.List{SummaryRow}, LotSummaryOptions)"/>
/// 的選項物件。預設等同於既有行為。
/// </summary>
public class LotSummaryOptions
{
    public YieldMode Mode { get; set; } = YieldMode.Default;

    /// <summary>
    /// 是否將「只有 SD/LD/ETC/Bridge/E.O. 的 row」計入 DieBase Yield 分子。
    /// <list type="bullet">
    /// <item><c>true</c>  → 所有 Stage=1 row 的 NGDie 都計入。</item>
    /// <item><c>false</c> → 只有具備 Miss/Shift/Extra 的 row 才計入（Excel 中 N77 = ✗ 的行為）。</item>
    /// </list>
    /// 此選項僅在 <see cref="Mode"/> = <see cref="YieldMode.DieBase"/> 時生效。
    /// </summary>
    public bool CountETC { get; set; } = true;

    /// <summary>DIE/Sub — 每片基板的 die 數量。僅 DieBase 模式使用。</summary>
    public int DieBaseDieCount { get; set; } = 0;

    public static LotSummaryOptions Default() => new();
}
