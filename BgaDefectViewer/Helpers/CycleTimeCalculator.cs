using BgaDefectViewer.Models;

namespace BgaDefectViewer.Helpers;

/// <summary>
/// <see cref="CycleTimeCalculator.Calculate"/> 的結果。
/// </summary>
/// <param name="AverageSeconds">平均 Δt（秒）；沒有合法樣本時為 <c>null</c>。</param>
/// <param name="SampleCount">通過 gap 閾值並計入平均的 Δt 樣本數。</param>
public readonly record struct CycleTimeResult(double? AverageSeconds, int SampleCount)
{
    public static readonly CycleTimeResult Empty = new(null, 0);
    public bool HasValue => AverageSeconds.HasValue;
}

/// <summary>
/// 計算 Stage=1（首次檢驗）的平均 Cycle Time（秒）。
/// 邏輯仿 <c>BM_3300SI_Buyofftest.xlsx</c> Sheet3 S73 的 array formula：
/// <code>
///   next_t = AGGREGATE(15,6, Q where O=1 AND Q&gt;current_Q, 1)   // 下一個 Stage=1 時間
///   ct     = (next_t - current_Q) * 86400                          // 間隔秒數
///   IF ct &gt; 300 → 排除（視為休息/換批中斷）
///   結果   = AVERAGE(剩餘 ct)
/// </code>
/// 排除 &gt; 300s 是為了濾掉操作員休息、午餐、換批之類的非生產間隔。
/// </summary>
public static class CycleTimeCalculator
{
    /// <summary>
    /// 預設視為「非連續生產」的間隔上限（秒）。
    /// 設為 120 秒：實務上單片基板週期約 30~60 秒，120 秒已能涵蓋偶發的等待動作，
    /// 但能濾除午餐／換 magazine／換批等明顯的中斷。
    /// </summary>
    public const int DefaultMaxGapSeconds = 120;

    /// <summary>
    /// 從 <paramref name="rows"/> 中取出符合條件的 row 並計算平均 Cycle Time（秒）。
    /// </summary>
    /// <param name="rows">所有候選 row（通常為 LotMonitor 的 _allRows 或 TopN slice）。</param>
    /// <param name="stage1Only">
    /// <c>true</c>（預設）= 只計 Stage=1 的 row，反映實際生產節拍；
    /// <c>false</c> = 全部 row 都納入，反映「兩次任意檢驗動作」的平均間隔（含修補後重檢）。
    /// </param>
    /// <param name="maxGapSeconds">
    /// 視為「非連續生產」的間隔上限（秒）；超過此值的 Δt 不計入平均，用以排除休息／換批／跨日。
    /// </param>
    /// <returns>包含平均秒數與「實際計入平均的 Δt 樣本數」的結果。</returns>
    public static CycleTimeResult Calculate(
        IEnumerable<SummaryRow> rows,
        bool stage1Only = true,
        int maxGapSeconds = DefaultMaxGapSeconds)
    {
        // 1. 篩選 row 並解析時間
        IEnumerable<SummaryRow> filtered = stage1Only ? rows.Where(r => r.Stage == 1) : rows;
        var times = filtered
            .Select(r => RowDateParser.TryParse(r.DateTime, out var d) ? (DateTime?)d : null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

        if (times.Count < 2) return CycleTimeResult.Empty;

        // 2. 依時間升序排序
        times.Sort();

        // 3. 取連續兩兩之間的秒差，過濾掉 > maxGap 的間隔
        double sum = 0;
        int count = 0;
        for (int i = 1; i < times.Count; i++)
        {
            double delta = (times[i] - times[i - 1]).TotalSeconds;
            if (delta <= 0) continue;                        // 同一秒或時序錯亂 → 跳過
            if (delta > maxGapSeconds) continue;             // 大間隔（休息／換批）→ 不計
            sum += delta;
            count++;
        }

        return count > 0
            ? new CycleTimeResult(sum / count, count)
            : CycleTimeResult.Empty;
    }

    /// <summary>
    /// 格式化為 <c>"31.91s"</c> 風格的字串；無資料時回傳 <c>"—"</c>。
    /// </summary>
    public static string Format(double? seconds) =>
        seconds.HasValue ? $"{seconds.Value:0.00}s" : "—";
}
