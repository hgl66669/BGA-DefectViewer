using System.Collections.ObjectModel;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.ViewModels;

/// <summary>
/// Settings Tab VM — 只負責顯示檔案狀態清單。
/// 路徑/Part#/Lot# 仍由 MainViewModel 持有（頂部工具列保留）。
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private ObservableCollection<FilePathConfig> _fileStatuses = new();
    public ObservableCollection<FilePathConfig> FileStatuses
    {
        get => _fileStatuses;
        set => SetProperty(ref _fileStatuses, value);
    }

    /// <summary>
    /// 當 Lot 選定後，由 MainViewModel 呼叫此方法更新檔案狀態清單。
    /// </summary>
    public void UpdateFileStatuses(string athSysPath, string partNo, string lotNo)
    {
        var statuses = new ObservableCollection<FilePathConfig>();

        var resultDir = FileLocator.GetResultDir(athSysPath, partNo, lotNo);

        // Master.csv（必要）
        var masterCheck = FileLocator.FindMasterCsv(athSysPath, partNo);
        statuses.Add(new FilePathConfig
        {
            Label = "Master.csv",
            IsRequired = true,
            Found = masterCheck.Found,
            DisplayPath = masterCheck.Found ? masterCheck.ActualPath! : masterCheck.ExpectedPath
        });

        // Summary.csv（必要）
        var summaryCheck = FileLocator.FindSummaryCsv(athSysPath, partNo, lotNo);
        statuses.Add(new FilePathConfig
        {
            Label = "Summary.csv",
            IsRequired = true,
            Found = summaryCheck.Found,
            DisplayPath = summaryCheck.Found ? summaryCheck.ActualPath! : summaryCheck.ExpectedPath
        });

        // .afa 目錄（必要，顯示筆數）
        int afaCount = FileLocator.CountAfaFiles(resultDir);
        statuses.Add(new FilePathConfig
        {
            Label = ".afa 目錄",
            IsRequired = true,
            Found = afaCount > 0,
            DisplayPath = resultDir,
            Count = afaCount
        });

        // .map 目錄（必要，顯示筆數）
        int mapCount = FileLocator.CountMapFiles(resultDir);
        statuses.Add(new FilePathConfig
        {
            Label = ".map 目錄",
            IsRequired = true,
            Found = mapCount > 0,
            DisplayPath = resultDir,
            Count = mapCount
        });

        // Map.csv（選配）
        var mapCsvCheck = FileLocator.FindMapCsv(athSysPath, lotNo);
        statuses.Add(new FilePathConfig
        {
            Label = "Map.csv",
            IsRequired = false,
            Found = mapCsvCheck.Found,
            DisplayPath = mapCsvCheck.Found ? mapCsvCheck.ActualPath! : mapCsvCheck.ExpectedPath
        });

        FileStatuses = statuses;
    }

    /// <summary>清除狀態（切換 Lot 前）</summary>
    public void Clear()
    {
        FileStatuses = new ObservableCollection<FilePathConfig>();
    }
}
