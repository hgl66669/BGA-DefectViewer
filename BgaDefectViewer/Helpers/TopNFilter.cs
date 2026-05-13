using System.Globalization;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Helpers;

/// <summary>
/// 從 <see cref="SummaryRow"/> 集合中取出「最新 N 個基板」並還原其所有 Stage rows。
/// 排序依據 = 基板 Stage=1 row 的 Date/Time（遞減）；無法解析時 fallback 到 RowIndex。
/// </summary>
public static class TopNFilter
{
    /// <summary>
    /// 依「首次檢驗 (最小 Stage) 的 Date/Time 由新到舊」取最新 N 個基板，
    /// 然後把那 N 個基板的所有 Stage rows 一併帶出。
    /// </summary>
    /// <param name="rows">原始 row 集合（可包含多 Stage）。</param>
    /// <param name="n">要保留的基板數量，呼叫端應先 clamp 至 [1, distinctCount]。</param>
    public static List<SummaryRow> SelectLatestNSubstrates(IEnumerable<SummaryRow> rows, int n)
    {
        if (n <= 0) return new List<SummaryRow>();

        var rowList = rows as IList<SummaryRow> ?? rows.ToList();

        // 1. 每個基板挑 Stage 最小的 row 當「首次檢驗 row」
        var firstByName = rowList
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(r => r.Stage).ThenBy(r => r.RowIndex).First())
            .ToList();

        // 2. 依 (Date/Time desc, RowIndex desc) 排序
        var sorted = firstByName
            .OrderByDescending(r => TryParseRowDate(r.DateTime, out var d) ? d : DateTime.MinValue)
            .ThenByDescending(r => r.RowIndex)
            .Take(n)
            .Select(r => r.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 3. 回傳這些基板的所有 Stage rows，保留原順序
        return rowList.Where(r => sorted.Contains(r.Name)).ToList();
    }

    /// <summary>
    /// 接受 <c>yyyy/MM/dd HH:mm:ss</c>, <c>yyyy/MM/dd HH:mm</c>,
    /// <c>yyyy/M/d HH:mm:ss</c>, <c>yyyy/M/d HH:mm</c> 等格式
    /// （與 <see cref="Parsers.SummaryCsvParser"/> 內部解析規則一致）。
    /// </summary>
    private static bool TryParseRowDate(string raw, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim();
        string[] formats = { "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd HH:mm", "yyyy/M/d HH:mm:ss", "yyyy/M/d HH:mm" };
        return DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
