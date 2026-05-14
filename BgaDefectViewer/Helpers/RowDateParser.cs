using System.Globalization;

namespace BgaDefectViewer.Helpers;

/// <summary>
/// 解析 <c>SummaryRow.DateTime</c> 欄位的共用 helper。
/// <para>
/// 已知 KBGA 各版本輸出兩種 separator：
/// <list type="bullet">
/// <item>空白：<c>2026/04/13 01:06:17</c></item>
/// <item>連字號：<c>2026/04/13-01:06:17</c></item>
/// </list>
/// 此 helper 接受兩種，並額外允許省略秒數的縮寫格式。
/// </para>
/// </summary>
public static class RowDateParser
{
    private static readonly string[] Formats =
    {
        // 空白分隔（既有格式）
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/MM/dd HH:mm",
        "yyyy/M/d HH:mm:ss",
        "yyyy/M/d HH:mm",
        // 連字號分隔（KBGA 部分版本輸出）
        "yyyy/MM/dd-HH:mm:ss",
        "yyyy/MM/dd-HH:mm",
        "yyyy/M/d-HH:mm:ss",
        "yyyy/M/d-HH:mm",
    };

    /// <summary>
    /// 解析 <see cref="Models.SummaryRow.DateTime"/> 字串。先嘗試已知精確格式，
    /// 失敗後回退到 <see cref="System.DateTime.TryParse(string, IFormatProvider, DateTimeStyles, out DateTime)"/>。
    /// </summary>
    public static bool TryParse(string? raw, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim();
        return DateTime.TryParseExact(s, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
