namespace BgaDefectViewer.Models;

/// <summary>Part/Lot 下拉清單排序方式。</summary>
public enum FolderSortMode
{
    NameAsc,
    NameDesc,
    TimeNewest,
    TimeOldest,
    /// <summary>依筆數遞減（Part = 批數、Lot = 基板數）；尚未載入計數的項目排至最後。</summary>
    CountDesc,
    /// <summary>依筆數遞增；尚未載入計數的項目排至最後。</summary>
    CountAsc,
}

public class FolderSortOption
{
    public FolderSortMode Mode { get; init; }
    public string Display { get; init; } = "";
}
