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
/// Lot# ComboBox 的單一條目。<see cref="Id"/> 為實際 lot 識別字串（含 <c>__merged__</c> 與
/// <c>__day__</c> 前綴形式），<see cref="DisplayName"/> 透過 <see cref="FileLocator.FormatLotForDisplay"/>
/// 轉換為人讀友善字串並附加基板數。
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

    public string DisplayName
    {
        get
        {
            var name = FileLocator.FormatLotForDisplay(Id);
            return _substrateCount.HasValue ? $"{name}  ({_substrateCount} 片)" : name;
        }
    }
}
