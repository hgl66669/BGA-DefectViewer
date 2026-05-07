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

        var partDir  = FileLocator.GetPartResultsDir(athSysPath, partNo);
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

        // Summary.csv（必要）— virtual day lot 解析路徑為 partDir/.summary.csv
        var summaryCheck = FileLocator.FindSummaryCsv(athSysPath, partNo, lotNo);
        statuses.Add(new FilePathConfig
        {
            Label = "Summary.csv",
            IsRequired = true,
            Found = summaryCheck.Found,
            DisplayPath = summaryCheck.Found ? summaryCheck.ActualPath! : summaryCheck.ExpectedPath
        });

        // .afa 計數（必要）—— 包含時間戳子資料夾
        int afaCount = CountAfaForLot(athSysPath, partNo, lotNo, resultDir);
        statuses.Add(new FilePathConfig
        {
            Label = ".afa 目錄",
            IsRequired = true,
            Found = afaCount > 0,
            DisplayPath = resultDir,
            Count = afaCount
        });

        // .map 計數（必要）—— 包含時間戳子資料夾
        int mapCount = CountMapForLot(athSysPath, partNo, lotNo, resultDir);
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

        // 新格式：Part 級 .summary.csv（選配，存在即代表這個 Part 有用過新格式）
        var rollingPath = Path.Combine(partDir, FileLocator.RollingSummaryName);
        bool hasRolling = File.Exists(rollingPath);
        statuses.Add(new FilePathConfig
        {
            Label = "Part 級 .summary.csv (新格式)",
            IsRequired = false,
            Found = hasRolling,
            DisplayPath = rollingPath
        });

        // 新格式：時間戳基板資料夾數量（純資訊）
        int tsFolderCount = FileLocator.CountTimestampFolders(partDir);
        statuses.Add(new FilePathConfig
        {
            Label = "新格式基板資料夾",
            IsRequired = false,
            Found = tsFolderCount > 0,
            DisplayPath = partDir,
            Count = tsFolderCount
        });

        FileStatuses = statuses;
    }

    /// <summary>
    /// Counts `.afa` files for the resolved lot, taking new-format timestamp folders into
    /// account. Falls back to the legacy flat scan when no timestamp folders exist.
    /// </summary>
    private static int CountAfaForLot(string athSysPath, string partNo, string lotNo, string legacyResultDir)
    {
        var folders = FileLocator.ResolveLotFolders(athSysPath, partNo, lotNo);
        if (folders.Count == 0) return FileLocator.CountAfaFiles(legacyResultDir);
        int total = 0;
        foreach (var f in folders)
            total += Directory.Exists(f)
                ? Directory.GetFiles(f, "*.afa").Count(p => !FileLocator.IsIgnoredFile(p))
                : 0;
        return total;
    }

    private static int CountMapForLot(string athSysPath, string partNo, string lotNo, string legacyResultDir)
    {
        var folders = FileLocator.ResolveLotFolders(athSysPath, partNo, lotNo);
        if (folders.Count == 0) return FileLocator.CountMapFiles(legacyResultDir);
        int total = 0;
        foreach (var f in folders)
            total += Directory.Exists(f)
                ? Directory.GetFiles(f, "*.map").Count(p => !FileLocator.IsIgnoredFile(p))
                : 0;
        return total;
    }

    /// <summary>清除狀態（切換 Lot 前）</summary>
    public void Clear()
    {
        FileStatuses = new ObservableCollection<FilePathConfig>();
    }
}
