using BgaDefectViewer.Helpers;

namespace BgaDefectViewer.Models;

/// <summary>
/// Part# ComboBox 的單一條目。<see cref="Name"/> 為實際料號（給 SelectedValue 用），
/// <see cref="DisplayName"/> 為顯示文字（料號 + 批數）。<see cref="LotCount"/> 初始為 <c>null</c>，
/// 由背景 Task 載完後 push 進來。
/// </summary>
public class PartListItem : ViewModelBase
{
    public string Name { get; init; } = "";

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

    /// <summary>ComboBox 顯示的字串：<c>"2231658  (12 批)"</c>，counts 未就緒時僅顯示料號。</summary>
    public string DisplayName => _lotCount.HasValue ? $"{Name}  ({_lotCount} 批)" : Name;
}

/// <summary>
/// Lot# ComboBox 的單一條目。<see cref="Id"/> 為實際 lot 識別字串（含 <c>__merged__</c>、
/// <c>__day__</c>、<c>__umkf__</c> 前綴形式），<see cref="DisplayName"/> 透過
/// <see cref="FileLocator.FormatLotForDisplay"/> 轉換為人讀友善字串並附加計數。
/// </summary>
public class LotListItem : ViewModelBase
{
    public string Id { get; init; } = "";

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

    /// <summary>Dropdown 分組鍵：<c>"2026/05/15"</c>；無時間時為 <c>"(未知日期)"</c>。</summary>
    public string DateGroupKey =>
        CreatedAt.HasValue ? CreatedAt.Value.ToString("yyyy/MM/dd") : "(未知日期)";

    public string DisplayName
    {
        get
        {
            var name = FileLocator.FormatLotForDisplay(Id);
            if (_umkfChildCount.HasValue)
            {
                return _substrateCount.HasValue
                    ? $"{name}  ({_umkfChildCount} 子批 / {_substrateCount} 片)"
                    : $"{name}  ({_umkfChildCount} 子批)";
            }
            return _substrateCount.HasValue ? $"{name}  ({_substrateCount} 片)" : name;
        }
    }
}
