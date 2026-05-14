namespace BgaDefectViewer.Helpers;

/// <summary>
/// UMKF (PSPREP_/PSPUBL_) 料號專用的批號合併規則。
/// 一個 lot 名稱的第 6 碼 (index 5) 視為「序號變動位」；去除 <c>-R</c> 後綴後前 5 碼相同
/// 的 lot 視為同一系列，可合併為單一母批顯示。
/// <para>規則來源：CSV Merger Tool (Athlete-FA 客戶端) 的 BatchKey / FindMasterBatch 演算法。</para>
/// </summary>
public static class UmkfBatchMerger
{
    private const string PrepPrefix = "PSPREP_";
    private const string PublPrefix = "PSPUBL_";
    private const string RSuffix = "-R";

    /// <summary>條件 1：料號名稱以 PSPREP_ 或 PSPUBL_ 開頭。</summary>
    public static bool IsUmkfPartNumber(string? partName) =>
        !string.IsNullOrEmpty(partName) &&
        (partName.StartsWith(PrepPrefix, StringComparison.OrdinalIgnoreCase) ||
         partName.StartsWith(PublPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 條件 2：批號清單符合第 6 碼變動規律。判定為：
    /// 至少存在一個分組同時滿足 (a) 包含 ≥ 2 個 lot，(b) 組內至少有一個 lot 的第 6 碼為數字。
    /// 只要有任何「真的能合併」的群組就視為符合特殊規則。
    /// </summary>
    public static bool HasSpecialBatchPattern(IEnumerable<string> lotNames)
    {
        var groups = GroupLots(lotNames, includeRBatches: true);
        foreach (var g in groups.Values)
        {
            if (g.Count < 2) continue;
            foreach (var lot in g)
            {
                var trimmed = StripRSuffix(lot);
                if (trimmed.Length >= 6 && char.IsDigit(trimmed[5])) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 將所有 lot 依分組鍵歸類。回傳 Dictionary&lt;分組鍵, lot 名稱清單&gt;。
    /// 當 <paramref name="includeRBatches"/> 為 <c>false</c> 時，<c>-R</c> 結尾的 lot 直接排除。
    /// </summary>
    public static Dictionary<string, List<string>> GroupLots(
        IEnumerable<string> lotNames, bool includeRBatches = true)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in lotNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (!includeRBatches && name.EndsWith(RSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            var key = GetBatchKey(name);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<string>();
                groups[key] = list;
            }
            list.Add(name);
        }
        return groups;
    }

    /// <summary>
    /// 從一群歸屬同一分組的 lot 中挑出「母批」。
    /// 優先順序：
    /// 1. 無 <c>-R</c> 後綴且第 6 碼為數字
    /// 2. 有 <c>-R</c> 後綴但去除後第 6 碼為數字
    /// 3. 字典序最小（無 <c>-R</c> 者優先）
    /// </summary>
    public static string FindMasterBatch(IEnumerable<string> lotsInGroup)
    {
        var lots = lotsInGroup.ToList();
        if (lots.Count == 0) return "";

        foreach (var lot in lots)
        {
            if (lot.EndsWith(RSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            if (lot.Length >= 6 && char.IsDigit(lot[5])) return lot;
        }
        foreach (var lot in lots)
        {
            if (!lot.EndsWith(RSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            var trimmed = lot.Substring(0, lot.Length - RSuffix.Length);
            if (trimmed.Length >= 6 && char.IsDigit(trimmed[5])) return lot;
        }
        return lots
            .OrderBy(n => n.EndsWith(RSuffix, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    /// <summary>
    /// 產生分組鍵：去除 <c>-R</c> 後，將第 6 碼 (index 5) 置換為底線 <c>_</c>。
    /// 長度不足 6 的 lot 名稱直接以原字串為鍵。
    /// </summary>
    public static string GetBatchKey(string lotName)
    {
        var trimmed = StripRSuffix(lotName);
        if (trimmed.Length < 6) return trimmed;
        var chars = trimmed.ToCharArray();
        chars[5] = '_';
        return new string(chars);
    }

    private static string StripRSuffix(string name) =>
        name.EndsWith(RSuffix, StringComparison.OrdinalIgnoreCase)
            ? name.Substring(0, name.Length - RSuffix.Length)
            : name;
}
