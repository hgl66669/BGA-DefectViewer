using BgaDefectViewer.Helpers;

namespace BgaDefectViewer.Models;

/// <summary>
/// 「載入更多」哨兵相關常數。Part#/Lot#/合併對話框三處列表共用同一套標記，
/// 讓 ComboBox / ListBox 在資料量超過 page size 時於最後一筆顯示可點擊的「載入更多」項目。
/// </summary>
public static class LoadMoreSentinel
{
    /// <summary>哨兵的識別字串。Part# 的 <see cref="PartListItem.Name"/>、Lot# 的
    /// <see cref="LotListItem.Id"/>、合併對話框的 LotPick.LotId 皆使用此值，
    /// setter / SelectionChanged 以此判斷是否為哨兵點擊。</summary>
    public const string Id = "__loadmore__";

    /// <summary>哨兵的分組鍵（給 Lot# / 合併對話框分組視圖用）。XAML HeaderTemplate 對此值
    /// 做 DataTrigger 隱藏，使哨兵不出現自成一格的灰字日期標題。</summary>
    public const string GroupKey = "__loadmore_group__";
}

/// <summary>
/// Part# ComboBox 的單一條目。<see cref="Name"/> 為實際料號（給 SelectedValue 用），
/// <see cref="DisplayName"/> 為顯示文字（料號 + 批數）。<see cref="LotCount"/> 初始為 <c>null</c>，
/// 由背景 Task 載完後 push 進來。
/// </summary>
public class PartListItem : ViewModelBase
{
    public string Name { get; init; } = "";

    /// <summary>True 時此項目為「載入更多」哨兵，<see cref="DisplayName"/> 顯示提示文字而非實際料號。</summary>
    public bool IsLoadMore { get; init; }

    /// <summary>哨兵剩餘可載入筆數，顯示於提示文字。</summary>
    public int RemainingCount { get; init; }

    private int? _lotCount;
    /// <summary>該料號下的 Lot# 筆數。尚未計算完成時為 <c>null</c>。</summary>
    public int? LotCount
    {
        get => _lotCount;
        set
        {
            if (SetProperty(ref _lotCount, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>ComboBox 顯示的字串：<c>"2231658  (12 批)"</c>，counts 未就緒時僅顯示料號。
    /// 哨兵項目顯示 <c>"▼ 載入更多 (還有 N 筆)"</c>。</summary>
    public string DisplayName => IsLoadMore
        ? $"▼ 載入更多 (還有 {RemainingCount} 筆)"
        : _lotCount.HasValue ? $"{Name}  ({_lotCount} 批)" : Name;
}

/// <summary>
/// Lot# ComboBox 的單一條目。<see cref="Id"/> 為實際 lot 識別字串（含 <c>__merged__</c>、
/// <c>__day__</c>、<c>__umkf__</c> 前綴形式），<see cref="DisplayName"/> 透過
/// <see cref="FileLocator.FormatLotForDisplay"/> 轉換為人讀友善字串並附加計數。
/// </summary>
public class LotListItem : ViewModelBase
{
    public string Id { get; init; } = "";

    /// <summary>True 時此項目為「載入更多」哨兵；DisplayName / DateGroupKey 改走特殊路徑。</summary>
    public bool IsLoadMore { get; init; }

    /// <summary>哨兵剩餘可載入筆數。</summary>
    public int RemainingCount { get; init; }

    private int? _substrateCount;
    /// <summary>該批內的基板數量。尚未計算完成時為 <c>null</c>（合併批可由建立時直接帶入）。</summary>
    public int? SubstrateCount
    {
        get => _substrateCount;
        set
        {
            if (SetProperty(ref _substrateCount, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    private int? _umkfChildCount;
    /// <summary>
    /// UMKF 母批底下實際合併的子批數量。<c>null</c> 代表此項目不是 UMKF 母批，DisplayName 走一般路徑。
    /// </summary>
    public int? UmkfChildCount
    {
        get => _umkfChildCount;
        set
        {
            if (SetProperty(ref _umkfChildCount, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    private bool _isSummaryOnly;
    /// <summary>
    /// True 表示該批僅有 <c>.summary.csv</c> 而缺少 <c>.afa</c>/<c>.map</c> 等逐片基板詳細檔。
    /// 此狀態會在 <see cref="DisplayName"/> 追加「(僅摘要)」小註記，提示使用者選此批將無法載入
    /// Substrate Viewer / Defect Map / Map 級資訊。
    /// <para>對 UMKF 母批：當且僅當底下所有子批皆為 summary-only 時為 true。</para>
    /// </summary>
    public bool IsSummaryOnly
    {
        get => _isSummaryOnly;
        set
        {
            if (SetProperty(ref _isSummaryOnly, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    private DateTime? _createdAt;
    /// <summary>
    /// 該批代表的日期：對 legacy／新格式 lot 取其 .summary.csv 第一個 row 的 Date/Time；
    /// 對虛擬日批 (<c>__day__</c>) 取其日期；對合併批 (<c>__merged__</c>) 取合併建立時間。
    /// <para>背景任務載入完成後從 folder mtime 升級成 CSV-based date，UI 透過 PropertyChanged
    /// 觸發 <see cref="DateGroupKey"/> 重算 + CollectionViewSource 的 live grouping 重新分組。</para>
    /// </summary>
    public DateTime? CreatedAt
    {
        get => _createdAt;
        set
        {
            if (SetProperty(ref _createdAt, value))
                OnPropertyChanged(nameof(DateGroupKey));
        }
    }

    /// <summary>Dropdown 分組鍵：<c>"2026/05/15"</c>；無時間時為 <c>"(未知日期)"</c>；
    /// 哨兵則為 <see cref="LoadMoreSentinel.GroupKey"/>，XAML 以 DataTrigger 隱藏該 header。</summary>
    public string DateGroupKey =>
        IsLoadMore ? LoadMoreSentinel.GroupKey :
        CreatedAt.HasValue ? CreatedAt.Value.ToString("yyyy/MM/dd") : "(未知日期)";

    public string DisplayName
    {
        get
        {
            if (IsLoadMore) return $"▼ 載入更多 (還有 {RemainingCount} 筆)";
            var name = FileLocator.FormatLotForDisplay(Id);
            string body;
            if (_umkfChildCount.HasValue)
            {
                body = _substrateCount.HasValue
                    ? $"{name}  ({_umkfChildCount} 子批 / {_substrateCount} 片)"
                    : $"{name}  ({_umkfChildCount} 子批)";
            }
            else
            {
                body = _substrateCount.HasValue ? $"{name}  ({_substrateCount} 片)" : name;
            }
            return _isSummaryOnly ? body + "  (僅摘要)" : body;
        }
    }
}
