namespace BgaDefectViewer.Models;

/// <summary>Part/Lot 下拉清單排序方式。</summary>
public enum FolderSortMode
{
    NameAsc,
    NameDesc,
    TimeNewest,
    TimeOldest,
}

public class FolderSortOption
{
    public FolderSortMode Mode { get; init; }
    public string Display { get; init; } = "";
}
