using System.Collections.ObjectModel;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.ViewModels;

/// <summary>Die 格子的最小資料單元（用於 DieGridControl ItemTemplate）</summary>
public class DieCell : ViewModelBase
{
    public char Letter { get; }
    public int Row { get; }
    public int Col { get; }
    public string Tooltip { get; }
    public bool IsHeader { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public DieCell(char letter, int row, int col, string tooltip, bool isHeader = false)
    {
        Letter = letter; Row = row; Col = col; Tooltip = tooltip; IsHeader = isHeader;
    }

    /// <summary>顯示文字：表頭格顯示行列號，資料格顯示大寫字母</summary>
    public string DisplayText => IsHeader
        ? (Row < 0 && Col < 0 ? "V"
           : Col < 0 ? $"{Row + 1:D2}"
           : $"{Col + 1:D2}")
        : char.ToUpper(Letter).ToString();
}

/// <summary>Substrate Map Tab VM — Die 色塊格子 + 基板清單</summary>
public class SubstrateMapViewModel : ViewModelBase
{
    // ── 事件 ──────────────────────────────────────────────────────────
    /// <summary>使用者雙擊某片基板（單 Die 模式 → 切到 Substrate Viewer）</summary>
    public event Action<SubstrateMap>? SubstrateDoubleClicked;
    /// <summary>使用者雙擊多 Die 基板中的某個 Die（→ 切到 Substrate Viewer，並過濾至該 Die）</summary>
    public event Action<SubstrateMap, int, int>? DieDoubleClicked;
    /// <summary>使用者單擊某片基板（→ 通知 Lot Monitor 同步選擇）</summary>
    public event Action<string>? SubstrateSelected;

    // ── 基板清單 ──────────────────────────────────────────────────────
    private ObservableCollection<SubstrateMap> _substrateMaps = new();
    public ObservableCollection<SubstrateMap> SubstrateMaps
    {
        get => _substrateMaps;
        set => SetProperty(ref _substrateMaps, value);
    }

    private SubstrateMap? _selectedSubstrateMap;
    public SubstrateMap? SelectedSubstrateMap
    {
        get => _selectedSubstrateMap;
        set
        {
            if (SetProperty(ref _selectedSubstrateMap, value))
                OnSelectedSubstrateChanged();
        }
    }

    // ── INSPECTION 下拉 ───────────────────────────────────────────────
    private ObservableCollection<int> _inspectionNumbers = new();
    public ObservableCollection<int> InspectionNumbers
    {
        get => _inspectionNumbers;
        set => SetProperty(ref _inspectionNumbers, value);
    }

    private int _selectedInspection;
    public int SelectedInspection
    {
        get => _selectedInspection;
        set
        {
            if (SetProperty(ref _selectedInspection, value))
                RebuildDieGrid();
        }
    }

    // ── Die Grid ──────────────────────────────────────────────────────
    private ObservableCollection<DieCell> _currentDieGrid = new();
    public ObservableCollection<DieCell> CurrentDieGrid
    {
        get => _currentDieGrid;
        set => SetProperty(ref _currentDieGrid, value);
    }

    private int _gridColumns = 1;
    public int GridColumns
    {
        get => _gridColumns;
        set => SetProperty(ref _gridColumns, value);
    }

    private DieCell? _selectedDieCell;
    public DieCell? SelectedDieCell
    {
        get => _selectedDieCell;
        private set
        {
            if (_selectedDieCell != null) _selectedDieCell.IsSelected = false;
            _selectedDieCell = value;
            if (_selectedDieCell != null) _selectedDieCell.IsSelected = true;
        }
    }

    // ── 基板統計文字 ──────────────────────────────────────────────────
    private string _substrateStats = "";
    public string SubstrateStats
    {
        get => _substrateStats;
        set => SetProperty(ref _substrateStats, value);
    }

    // ── 公開方法 ──────────────────────────────────────────────────────

    /// <summary>載入批次所有 .map 資料</summary>
    public void Load(IEnumerable<SubstrateMap> maps)
    {
        _selectedInspection = 0; // 切換批號時重置，新批預設從 INSP 1 開始

        // Natural sort: "kensa-1, kensa-2, …, kensa-10" instead of alphabetical
        SubstrateMaps = new ObservableCollection<SubstrateMap>(
            maps.OrderBy(m =>
                {
                    var match = System.Text.RegularExpressions.Regex.Match(m.SubstrateId, @"^(.*?)(\d+)$");
                    return match.Success ? match.Groups[1].Value : m.SubstrateId;
                })
                .ThenBy(m =>
                {
                    var match = System.Text.RegularExpressions.Regex.Match(m.SubstrateId, @"^(.*?)(\d+)$");
                    return match.Success && long.TryParse(match.Groups[2].Value, out long n) ? n : 0L;
                }));

        SelectedSubstrateMap = SubstrateMaps.FirstOrDefault();
    }

    /// <summary>由 Lot Monitor 單擊觸發，高亮對應片</summary>
    public void HighlightSubstrate(string substrateId)
    {
        var target = SubstrateMaps.FirstOrDefault(m =>
            m.SubstrateId.EndsWith(substrateId, StringComparison.OrdinalIgnoreCase)
            || substrateId.EndsWith(m.SubstrateId, StringComparison.OrdinalIgnoreCase)
            || m.SubstrateId == substrateId);

        if (target != null)
            SelectedSubstrateMap = target;
    }

    /// <summary>由 View code-behind 呼叫（ListBox 選擇變更）</summary>
    public void OnSubstrateSelected(SubstrateMap? map)
    {
        if (map == null) return;
        SelectedSubstrateMap = map;
        SubstrateSelected?.Invoke(map.SubstrateId);
    }

    /// <summary>由 View code-behind 呼叫（雙擊整片基板，單 Die 模式）</summary>
    public void OnSubstrateDoubleClick(SubstrateMap? map)
    {
        if (map == null) return;
        SubstrateDoubleClicked?.Invoke(map);
    }

    /// <summary>由 View code-behind 呼叫（雙擊多 Die 格子中的特定 Die）</summary>
    public void OnDieDoubleClick(SubstrateMap? map, DieCell cell)
    {
        if (map == null) return;
        if (cell.Row < 0 || cell.Col < 0) return; // 表頭格不觸發
        DieDoubleClicked?.Invoke(map, cell.Row, cell.Col);
    }

    /// <summary>由 View code-behind 呼叫（單擊 Die 格子 → 選取高亮）</summary>
    public void OnDieCellClicked(DieCell cell)
    {
        if (cell.IsHeader) return;
        SelectedDieCell = cell;
    }

    // ── 內部邏輯 ──────────────────────────────────────────────────────

    private void OnSelectedSubstrateChanged()
    {
        var map = _selectedSubstrateMap;
        if (map == null)
        {
            InspectionNumbers = new ObservableCollection<int>();
            CurrentDieGrid = new ObservableCollection<DieCell>();
            SubstrateStats = "";
            return;
        }

        // 更新 INSPECTION 下拉，盡量保留目前已選的 Inspection 號碼
        InspectionNumbers = new ObservableCollection<int>(
            map.Inspections.Select(i => i.InspectionNumber));

        int target;
        if (InspectionNumbers.Contains(_selectedInspection))
            target = _selectedInspection;
        else
        {
            // 找最接近但不超過目前選擇的 Inspection；若無則取最後一輪
            var lower = InspectionNumbers.Where(n => n < _selectedInspection).ToList();
            target = lower.Count > 0 ? lower[lower.Count - 1] : InspectionNumbers.FirstOrDefault();
        }

        if (_selectedInspection != target)
            SelectedInspection = target;  // setter 會呼叫 RebuildDieGrid
        else
            RebuildDieGrid();  // 相同值時 setter 不觸發，需手動重建
    }

    private void RebuildDieGrid()
    {
        _selectedDieCell = null; // old cell references are stale after rebuild
        var map = _selectedSubstrateMap;
        if (map == null) return;

        // Sync DisplayInspectionNumber on all list items so their tile/stats update
        foreach (var m in SubstrateMaps)
            m.DisplayInspectionNumber = SelectedInspection;

        var insp = map.Inspections.FirstOrDefault(i => i.InspectionNumber == SelectedInspection)
                   ?? map.LastInspection;
        if (insp == null)
        {
            CurrentDieGrid = new ObservableCollection<DieCell>();
            SubstrateStats = "";
            return;
        }

        // 更新統計文字
        SubstrateStats =
            $"OK={insp.OK}  MISS={insp.Miss}  BRIDGE={insp.Bridge}  ETC={insp.ETC}  PPM={insp.PPM:F1}";

        int rows = insp.Rows;
        int cols = insp.Cols > 0 ? insp.Cols : 1;

        // +1 欄給列標題
        GridColumns = cols + 1;

        // 展平 Die Grid（row-major），含表頭
        var cells = new List<DieCell>();

        // 首行：角格 "V" + 欄標題 "01","02"...
        cells.Add(new DieCell('0', -1, -1, "", isHeader: true));
        for (int c = 0; c < cols; c++)
            cells.Add(new DieCell('0', -1, c, "", isHeader: true));

        // 資料行：列標題 + 資料格
        for (int r = 0; r < rows; r++)
        {
            cells.Add(new DieCell('0', r, -1, "", isHeader: true));
            for (int c = 0; c < cols; c++)
            {
                char letter = insp.DieGrid[r, c];
                cells.Add(new DieCell(letter, r, c, BuildTooltip(letter, r, c)));
            }
        }
        CurrentDieGrid = new ObservableCollection<DieCell>(cells);
    }

    private static string BuildTooltip(char letter, int row, int col)
    {
        var info = Helpers.DieJudge.GetInfo(letter);
        return $"[{row + 1},{col + 1}] {info.DefectName}";
    }
}
