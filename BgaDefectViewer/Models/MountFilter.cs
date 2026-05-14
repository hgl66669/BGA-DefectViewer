using BgaDefectViewer.Helpers;

namespace BgaDefectViewer.Models;

/// <summary>植球機特殊統計模式。<c>Off</c> 表示不啟用過濾。</summary>
public enum MountMode
{
    Off,
    /// <summary>雙植球機：依時間順序輪流分配給 Mount 1 / Mount 2。</summary>
    Dual,
    /// <summary>三植球機：依時間順序輪流分配給 Mount 1 / Mount 2 / Mount 3。</summary>
    Triple,
}

/// <summary>
/// Lot Monitor 的「特殊統計規則」狀態。
/// <para>
/// 分配邏輯：把所有基板依「首次檢驗 (Stage=1) 的 Date/Time」排序，再以 round-robin 輪流配給
/// Mount 1 / 2 (/ 3)。這樣即使基板名稱不規則（沒有流水號、隨機編號），也能反映實際生產時的
/// 植球站分配順序。
/// </para>
/// 使用流程：
/// <list type="number">
/// <item>設定 <see cref="Mode"/> 與 <see cref="Mount1Enabled"/>/<see cref="Mount2Enabled"/>/<see cref="Mount3Enabled"/>。</item>
/// <item>呼叫 <see cref="RebuildAssignment"/> 傳入該批次的所有 row，計算 mount 索引快取。</item>
/// <item>呼叫 <see cref="IsAccepted"/> 過濾 row。</item>
/// </list>
/// </summary>
public class MountFilter
{
    public MountMode Mode { get; set; } = MountMode.Off;
    public bool Mount1Enabled { get; set; } = true;
    public bool Mount2Enabled { get; set; } = true;
    public bool Mount3Enabled { get; set; } = true;

    /// <summary>基板名 → mount 索引 (1-based)。<see cref="RebuildAssignment"/> 後填入。</summary>
    private readonly Dictionary<string, int> _substrateToMount =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 根據傳入的 row 重建「基板 → mount」映射。
    /// <para>排序鍵：每個基板取其代表 row（優先 Stage 最小者，再以 Date/Time 升冪、RowIndex 升冪 tiebreak），
    /// 然後把這些代表 row 依 Date/Time → RowIndex 升冪排序，依 mode 的 divisor (2 或 3) round-robin 分配。</para>
    /// <c>Mode == Off</c> 時清空映射。
    /// </summary>
    public void RebuildAssignment(IEnumerable<SummaryRow> rows)
    {
        _substrateToMount.Clear();
        if (Mode == MountMode.Off) return;

        var firstByName = rows
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var rep = g
                    .OrderBy(r => r.Stage)
                    .ThenBy(r => RowDateParser.TryParse(r.DateTime, out var d) ? d : DateTime.MaxValue)
                    .ThenBy(r => r.RowIndex)
                    .First();
                return new
                {
                    Name = g.Key,
                    Time = RowDateParser.TryParse(rep.DateTime, out var dt) ? dt : DateTime.MaxValue,
                    Idx  = rep.RowIndex,
                };
            })
            .OrderBy(x => x.Time)
            .ThenBy(x => x.Idx)
            .ToList();

        int divisor = Mode == MountMode.Dual ? 2 : 3;
        for (int i = 0; i < firstByName.Count; i++)
            _substrateToMount[firstByName[i].Name] = (i % divisor) + 1;
    }

    /// <summary>該基板名稱在當前過濾下是否應顯示。</summary>
    public bool IsAccepted(string substrateName)
    {
        if (Mode == MountMode.Off) return true;
        if (!_substrateToMount.TryGetValue(substrateName, out var idx)) return true;
        return idx switch
        {
            1 => Mount1Enabled,
            2 => Mount2Enabled,
            3 => Mount3Enabled,
            _ => true,
        };
    }

    /// <summary>查詢某基板被分配到哪個 Mount (1-based)；未分配回傳 <c>null</c>。</summary>
    public int? GetMountIndex(string substrateName)
        => _substrateToMount.TryGetValue(substrateName, out var idx) ? idx : null;

    /// <summary>UI 上「啟用中過濾」的簡短描述，無啟用時為空字串。</summary>
    public string Describe()
    {
        if (Mode == MountMode.Off) return "";
        var enabled = new List<string>();
        if (Mount1Enabled) enabled.Add("M1");
        if (Mount2Enabled) enabled.Add("M2");
        if (Mode == MountMode.Triple && Mount3Enabled) enabled.Add("M3");
        string modeLabel = Mode == MountMode.Dual ? "雙植球" : "三植球";
        return enabled.Count == 0
            ? $"{modeLabel}: 全部排除"
            : $"{modeLabel}: {string.Join("+", enabled)}";
    }

    public MountFilter Clone() => new()
    {
        Mode = Mode,
        Mount1Enabled = Mount1Enabled,
        Mount2Enabled = Mount2Enabled,
        Mount3Enabled = Mount3Enabled,
    };
}
