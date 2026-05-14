using System.Globalization;
using System.IO;
using System.Text;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Helpers;

/// <summary>
/// 將 Lot Monitor 目前狀態（已套用合併批 / 特殊統計 / Yield 選項 / Cycle Time 等）
/// 輸出成一份 CSV 報表。
/// <para>
/// 資料區塊仿 KBGA 原始 <c>.summary.csv</c> 格式（<c>LOT Start ... LOT Summary End</c>），
/// 開頭加上 <c>#</c> 開頭的 metadata 註解列記錄當下的選項；註解列在我們自己的
/// <see cref="Parsers.SummaryCsvParser"/> 中會被當作非法 row 自動跳過，
/// 也不會影響其他標準 CSV 讀取工具。
/// </para>
/// </summary>
public static class LotReportExporter
{
    /// <summary>輸出報表所需的完整輸入資料；由 <c>LotMonitorViewModel.BuildExportInput()</c> 組裝。</summary>
    public sealed class ExportInput
    {
        public string LotDisplayName { get; set; } = "";
        public int SubstrateCount { get; set; }

        /// <summary>已套用 Mount filter 之後的 row 集合（與主 DataGrid 顯示一致）。</summary>
        public IReadOnlyList<SummaryRow> Rows { get; set; } = Array.Empty<SummaryRow>();

        /// <summary>整批 LOT Summary 五行（已依當前 Yield 選項計算）。</summary>
        public IReadOnlyList<LotSummaryLine> SummaryLines { get; set; } = Array.Empty<LotSummaryLine>();

        // ── Metadata (寫入 # 開頭註解列) ─────────────────────────────────
        public DateTime ExportedAt { get; set; } = DateTime.Now;
        public string YieldMode { get; set; } = "Default";
        public bool CountETC { get; set; } = true;
        public int DieBaseDieCountEffective { get; set; }
        public bool DieBaseDieCountIsManual { get; set; }

        public string MountFilterDescription { get; set; } = "";   // 空 = 未啟用

        public bool TopNEnabled { get; set; }
        public int TopN { get; set; }

        public bool CycleTimeStage1Only { get; set; } = true;
        public int CycleTimeMaxGapSeconds { get; set; }
        public CycleTimeResult CycleTimeOverall { get; set; }
        public CycleTimeResult CycleTimeTopN { get; set; }
    }

    /// <summary>把報表寫入 <paramref name="outputPath"/>（UTF-8 with BOM，方便 Excel 直接打開）。</summary>
    public static void WriteCsv(string outputPath, ExportInput input)
    {
        var sb = new StringBuilder();
        AppendMetadataHeader(sb, input);
        AppendDataSection(sb, input);
        AppendSummarySection(sb, input);
        File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    // ── Metadata 註解列 ─────────────────────────────────────────────────

    private static void AppendMetadataHeader(StringBuilder sb, ExportInput x)
    {
        sb.AppendLine("# BGA Defect Viewer Export");
        sb.AppendLine($"# Exported: {x.ExportedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"# Lot: {x.LotDisplayName}");
        sb.AppendLine($"# Substrates (filtered): {x.SubstrateCount}");

        string dieSrc = x.DieBaseDieCountIsManual ? "manual" : "auto";
        sb.AppendLine($"# Yield mode: {x.YieldMode} | CountETC: {(x.CountETC ? "✓" : "✗")} | DIE/Sub: {x.DieBaseDieCountEffective} ({dieSrc})");

        if (!string.IsNullOrEmpty(x.MountFilterDescription))
            sb.AppendLine($"# Mount filter: {x.MountFilterDescription}");
        else
            sb.AppendLine("# Mount filter: (off)");

        if (x.TopNEnabled)
            sb.AppendLine($"# Top-N (display only, not in this export): latest {x.TopN} substrates by Stage=1 time");

        string ctMode = x.CycleTimeStage1Only ? "Stage=1 only" : "all rows";
        sb.AppendLine(
            $"# Cycle Time (overall): {CycleTimeCalculator.Format(x.CycleTimeOverall.AverageSeconds)} " +
            $"({x.CycleTimeOverall.SampleCount} samples, gap <= {x.CycleTimeMaxGapSeconds}s, {ctMode})");

        if (x.TopNEnabled)
            sb.AppendLine(
                $"# Cycle Time (top N): {CycleTimeCalculator.Format(x.CycleTimeTopN.AverageSeconds)} " +
                $"({x.CycleTimeTopN.SampleCount} samples)");

        sb.AppendLine("#");
    }

    // ── 資料區塊（LOT Start … 行清單） ───────────────────────────────────

    private static void AppendDataSection(StringBuilder sb, ExportInput x)
    {
        sb.Append("LOT Start,").AppendLine(EscapeCsv(LotNameWithFilter(x)));
        sb.AppendLine("Name,OK,Miss,Shift,SD,LD,ETC,Bridge,Extra,E.O.,NGDie,GDie,PPM,Judge,Stage,Yield%,Date/Time");
        foreach (var r in x.Rows)
            AppendRow(sb, r);
    }

    private static void AppendRow(StringBuilder sb, SummaryRow r)
    {
        var inv = CultureInfo.InvariantCulture;
        sb.Append(EscapeCsv(r.Name)).Append(',');
        sb.Append(r.OK).Append(',');
        sb.Append(r.Miss).Append(',');
        sb.Append(r.Shift).Append(',');
        sb.Append(r.SD).Append(',');
        sb.Append(r.LD).Append(',');
        sb.Append(r.ETC).Append(',');
        sb.Append(r.Bridge).Append(',');
        sb.Append(r.Extra).Append(',');
        sb.Append(r.EO).Append(',');
        sb.Append(r.NGDie).Append(',');
        sb.Append(r.GDie).Append(',');
        sb.Append(r.PPM.ToString("F1", inv)).Append(',');
        sb.Append(EscapeCsv(r.Judge)).Append(',');
        sb.Append(r.Stage).Append(',');
        sb.Append(r.YieldPercent.ToString("F3", inv)).Append(',');
        sb.AppendLine(EscapeCsv(r.DateTime));
    }

    // ── LOT Summary 區塊（5 行 + LOT Summary End） ──────────────────────

    private static void AppendSummarySection(StringBuilder sb, ExportInput x)
    {
        sb.Append("LOT Summary,").Append(EscapeCsv(LotNameWithFilter(x)))
          .Append(",SubstrateCount=").AppendLine(x.SubstrateCount.ToString());

        foreach (var line in x.SummaryLines)
            AppendSummaryLine(sb, line);

        sb.AppendLine("LOT Summary End");
    }

    private static void AppendSummaryLine(StringBuilder sb, LotSummaryLine l)
    {
        sb.Append(EscapeCsv(l.Label)).Append(',');
        sb.Append(EscapeCsv(l.OK)).Append(',');
        sb.Append(EscapeCsv(l.Miss)).Append(',');
        sb.Append(EscapeCsv(l.Shift)).Append(',');
        sb.Append(EscapeCsv(l.SD)).Append(',');
        sb.Append(EscapeCsv(l.LD)).Append(',');
        sb.Append(EscapeCsv(l.ETC)).Append(',');
        sb.Append(EscapeCsv(l.Bridge)).Append(',');
        sb.Append(EscapeCsv(l.Extra)).Append(',');
        sb.Append(EscapeCsv(l.EO)).Append(',');
        sb.Append(EscapeCsv(l.NGDie)).Append(',');
        sb.Append(EscapeCsv(l.GDie)).Append(',');
        sb.Append(EscapeCsv(l.PPM)).Append(',');
        sb.Append(EscapeCsv(l.Judge)).Append(',');
        sb.Append(EscapeCsv(l.Stage)).Append(',');
        sb.Append(EscapeCsv(l.YieldPercent)).Append(',');
        sb.AppendLine(EscapeCsv(l.DateTime));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>合併 LotDisplayName 與 Mount filter 描述，避免下游讀取時混淆。</summary>
    private static string LotNameWithFilter(ExportInput x) =>
        string.IsNullOrEmpty(x.MountFilterDescription)
            ? x.LotDisplayName
            : $"{x.LotDisplayName} [{x.MountFilterDescription}]";

    /// <summary>CSV 欄位逸出：含逗號／雙引號／換行時用雙引號包起來，內部雙引號改成兩個。</summary>
    private static string EscapeCsv(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>把字串中的非法檔名字元換成 <c>_</c>，供建議檔名使用。</summary>
    public static string SanitizeForFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    /// <summary>建議檔名：<c>{lot}_export_{yyyyMMdd_HHmmss}.csv</c>，過長會截斷。</summary>
    public static string SuggestedFileName(string lotDisplayName)
    {
        var safe = SanitizeForFileName(lotDisplayName);
        if (safe.Length > 60) safe = safe.Substring(0, 60).TrimEnd();
        return $"{safe}_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
    }
}
