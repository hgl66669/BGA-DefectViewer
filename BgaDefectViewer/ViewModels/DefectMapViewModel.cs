using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.ViewModels;

public class DefectMapViewModel : ViewModelBase
{
    private bool _isAvailable;
    public bool IsAvailable
    {
        get => _isAvailable;
        set => SetProperty(ref _isAvailable, value);
    }

    private bool _isCalculated;
    /// <summary>true = calculated from .map files; false = read from map.csv</summary>
    public bool IsCalculated
    {
        get => _isCalculated;
        set => SetProperty(ref _isCalculated, value);
    }

    private DieMapData? _mapData;
    public DieMapData? MapData
    {
        get => _mapData;
        set => SetProperty(ref _mapData, value);
    }

    public string LotName => _mapData?.LotName ?? "";
    public string RecipeName => _mapData?.RecipeName ?? "";

    public void Load(DieMapData? data)
    {
        MapData = data;
        IsAvailable = data != null;
        IsCalculated = data?.IsCalculated ?? false;
        OnPropertyChanged(nameof(LotName));
        OnPropertyChanged(nameof(RecipeName));
    }
}
