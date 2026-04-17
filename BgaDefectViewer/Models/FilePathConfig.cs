using BgaDefectViewer.Helpers;

namespace BgaDefectViewer.Models;

/// <summary>Settings Tab 用：描述單一檔案/目錄的狀態</summary>
public class FilePathConfig : ViewModelBase
{
    private string _label = "";
    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    /// <summary>true = 必要（●），false = 選配（○）</summary>
    private bool _isRequired;
    public bool IsRequired
    {
        get => _isRequired;
        set => SetProperty(ref _isRequired, value);
    }

    private bool _found;
    public bool Found
    {
        get => _found;
        set
        {
            if (SetProperty(ref _found, value))
                OnPropertyChanged(nameof(StatusSymbol));
        }
    }

    private string _displayPath = "";
    public string DisplayPath
    {
        get => _displayPath;
        set => SetProperty(ref _displayPath, value);
    }

    /// <summary>檔案數量（用於 .afa / .map 目錄）；0 = 不顯示</summary>
    private int _count;
    public int Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
                OnPropertyChanged(nameof(CountText));
        }
    }

    public string RequiredSymbol => IsRequired ? "●" : "○";
    public string StatusSymbol => Found ? "✓" : "✗";
    public string CountText => Count > 0 ? $"{Count} 筆" : "";
}
