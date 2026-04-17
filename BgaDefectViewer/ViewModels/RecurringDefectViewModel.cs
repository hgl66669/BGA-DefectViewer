using System.Collections.ObjectModel;
using BgaDefectViewer.Controls;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;
using BgaDefectViewer.Parsers;

namespace BgaDefectViewer.ViewModels;

// ── Defect-type checkbox item ─────────────────────────────────────────────────

/// <summary>One checkbox entry in the "欠陥種類" filter row.</summary>
public class DefectTypeFilter : ViewModelBase
{
    /// <summary>Uppercase die-chart character (e.g. 'M', 'S', 'B').</summary>
    public char Letter { get; }
    public string Name { get; }
    /// <summary>Corresponding ball-level DefectCodes (e.g. M→{2}, D→{21,22}).</summary>
    public IReadOnlyList<int> DefectCodes { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public DefectTypeFilter(char letter, string name, int[] defectCodes, bool defaultSelected)
    {
        Letter = letter; Name = name; DefectCodes = defectCodes; _isSelected = defaultSelected;
    }
}

// ── Die grid cell ─────────────────────────────────────────────────────────────

/// <summary>A single cell in the recurring-defect die grid.</summary>
public class RecurringDieCell : ViewModelBase
{
    public int Row { get; }
    public int Col { get; }
    public bool IsHeader { get; }

    private int _displayCount;
    /// <summary>
    /// Effective count (filtered by defect type + min-count threshold).
    /// 0 means the cell is greyed out. Drives colour and text.
    /// </summary>
    public int DisplayCount
    {
        get => _displayCount;
        set
        {
            if (SetProperty(ref _displayCount, value))
                OnPropertyChanged(nameof(DisplayText));
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Header cells show row/col numbers; data cells show DisplayCount (or blank when 0).</summary>
    public string DisplayText => IsHeader
        ? (Row < 0 && Col < 0 ? "V"
           : Col < 0 ? $"{Row + 1:D2}"
           : $"{Col + 1:D2}")
        : (_displayCount > 0 ? _displayCount.ToString() : "");

    public RecurringDieCell(int row, int col, bool isHeader)
    {
        Row = row; Col = col; IsHeader = isHeader;
    }
}

// ── Main ViewModel ────────────────────────────────────────────────────────────

/// <summary>ViewModel for the Recurring Defects tab.</summary>
public class RecurringDefectViewModel : ViewModelBase
{
    private RecurringDefectData? _data;
    private MasterBall[]? _masterBalls;
    private CoordinateTransform? _transform;
    private RecurringDieCell? _selectedDieCell;

    // ── Header info ───────────────────────────────────────────────────────
    private string _headerText = "No data loaded";
    public string HeaderText
    {
        get => _headerText;
        set => SetProperty(ref _headerText, value);
    }

    // ── Defect-type filter ────────────────────────────────────────────────
    public ObservableCollection<DefectTypeFilter> DefectTypeFilters { get; }

    // ── Die grid ──────────────────────────────────────────────────────────
    private ObservableCollection<RecurringDieCell> _dieGrid = new();
    public ObservableCollection<RecurringDieCell> DieGrid
    {
        get => _dieGrid;
        set => SetProperty(ref _dieGrid, value);
    }

    private int _gridColumns = 1;
    public int GridColumns
    {
        get => _gridColumns;
        set => SetProperty(ref _gridColumns, value);
    }

    // ── Ball map ──────────────────────────────────────────────────────────
    public BallMapCanvas? Canvas { get; set; }
    public CoordinateTransform? Transform => _transform;

    // ── Detail table ──────────────────────────────────────────────────────
    private string _locationsText = "Locations: 0";
    public string LocationsText
    {
        get => _locationsText;
        set => SetProperty(ref _locationsText, value);
    }

    private ObservableCollection<RecurringBallInfo> _displayedBalls = new();
    public ObservableCollection<RecurringBallInfo> DisplayedBalls
    {
        get => _displayedBalls;
        set => SetProperty(ref _displayedBalls, value);
    }

    // ── Consecutive-count filter ──────────────────────────────────────────
    private int _filterMinCount = 1;
    public int FilterMinCount
    {
        get => _filterMinCount;
        set
        {
            if (SetProperty(ref _filterMinCount, value))
                ApplyFilter();
        }
    }

    // ── Constructor ───────────────────────────────────────────────────────

    public RecurringDefectViewModel()
    {
        // Missing and Shift selected by default (matches reference application)
        DefectTypeFilters = new ObservableCollection<DefectTypeFilter>
        {
            new('M', "Missing", new[] { 2 },      defaultSelected: true),
            new('S', "Shift",   new[] { 3 },      defaultSelected: true),
            new('B', "Bridge",  new[] { 11 },     defaultSelected: false),
            new('E', "Extra",   new[] { 4 },      defaultSelected: false),
            new('C', "ETC",     new[] { 30 },     defaultSelected: false),
            new('D', "SD/LD",   new[] { 21, 22 }, defaultSelected: false),
        };
        foreach (var f in DefectTypeFilters)
            f.PropertyChanged += (_, _) => ApplyFilter();
    }

    // ── Load ──────────────────────────────────────────────────────────────

    /// <summary>Called from MainViewModel after .map files are loaded (Phase 1).</summary>
    public void Load(RecurringDefectData? data, MasterBall[]? masterBalls)
    {
        _data = data;
        _masterBalls = masterBalls;
        _selectedDieCell = null;

        if (data == null || masterBalls == null)
        {
            DieGrid = new ObservableCollection<RecurringDieCell>();
            GridColumns = 1;
            LocationsText = "Locations: 0";
            DisplayedBalls = new ObservableCollection<RecurringBallInfo>();
            HeaderText = "No data loaded";
            _transform = null;
            return;
        }

        var (minX, maxX, minY, maxY) = MasterCsvParser.GetBounds(masterBalls);
        _transform = new CoordinateTransform();
        _transform.SetBounds(minX, maxX, minY, maxY);

        HeaderText = $"Lot: {data.LotName}  |  {data.SubstrateCount} substrates";
        _filterMinCount = 1;
        OnPropertyChanged(nameof(FilterMinCount));

        RebuildDieGrid();
        DisplayedBalls = new ObservableCollection<RecurringBallInfo>();
        LocationsText = "Locations: 0";
        RequestRender(null);
    }

    /// <summary>Called from MainViewModel after .afa enrichment completes (Phase 2).</summary>
    public void RefreshBallData()
    {
        if (_selectedDieCell == null || _data == null) return;
        ShowDieBalls(_selectedDieCell.Row, _selectedDieCell.Col);
    }

    // ── Die cell interaction ──────────────────────────────────────────────

    public void OnDieCellClicked(RecurringDieCell cell)
    {
        if (cell.IsHeader) return;
        if (_selectedDieCell != null) _selectedDieCell.IsSelected = false;
        _selectedDieCell = cell;
        cell.IsSelected = true;
        ShowDieBalls(cell.Row, cell.Col);
    }

    private void ShowDieBalls(int row, int col)
    {
        if (_data == null) return;

        var die = _data.Dies[row, col];
        var selectedCodes = GetSelectedDefectCodes();

        // Build effective ball list — use per-defect count when filter is active
        var filtered = die.Balls
            .Select(b =>
            {
                int effectiveCount = selectedCodes.Count == 0 ? b.Count : b.GetCountForCodes(selectedCodes);
                return (Ball: b, EffCount: effectiveCount);
            })
            .Where(x => x.EffCount >= _filterMinCount)
            .OrderByDescending(x => x.EffCount)
            .ToList();

        // Materialise the table — update Judge to reflect selected defect type
        var displayList = filtered.Select(x =>
        {
            var ball = x.Ball;
            // If filtering by specific codes, recompute Judge from those codes only
            string judge = selectedCodes.Count == 0
                ? ball.Judge
                : (ball.DefectCodeCounts
                    .Where(kv => selectedCodes.Contains(kv.Key))
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => DefectTypes.GetName(kv.Key))
                    .FirstOrDefault() ?? ball.Judge);

            return new RecurringBallInfo
            {
                BallId           = ball.BallId,
                X                = ball.X,
                Y                = ball.Y,
                Diameter         = ball.Diameter,
                Count            = x.EffCount,
                Judge            = judge,
                DefectCodeCounts = ball.DefectCodeCounts
            };
        }).ToList();

        DisplayedBalls = new ObservableCollection<RecurringBallInfo>(displayList);
        LocationsText = $"Locations: {displayList.Count}";
        RequestRender(displayList);
    }

    // ── Apply filter (count threshold + defect type) ──────────────────────

    private void ApplyFilter()
    {
        if (_data == null) return;

        var selectedChars = GetSelectedDefectChars();

        foreach (var cell in DieGrid)
        {
            if (cell.IsHeader) continue;
            int effective = _data.Dies[cell.Row, cell.Col].GetCountForChars(selectedChars);
            cell.DisplayCount = effective >= _filterMinCount ? effective : 0;
        }

        if (_selectedDieCell != null)
            ShowDieBalls(_selectedDieCell.Row, _selectedDieCell.Col);
        else
            RequestRender(null);
    }

    // ── FIT ───────────────────────────────────────────────────────────────

    public void FitView()
    {
        _transform?.ResetToFit();
        RequestRenderCurrentSelection();
    }

    public void RequestRenderCurrentSelection()
    {
        if (_selectedDieCell != null)
            ShowDieBalls(_selectedDieCell.Row, _selectedDieCell.Col);
        else
            RequestRender(null);
    }

    public void RequestRender(List<RecurringBallInfo>? balls)
    {
        if (Canvas == null || _masterBalls == null || _transform == null) return;
        _transform.SetCanvasSize(Canvas.ActualWidth, Canvas.ActualHeight);

        List<Models.DefectBall>? defects = null;
        if (balls != null && balls.Count > 0)
        {
            defects = balls.Select(b => new Models.DefectBall
            {
                BallId     = b.BallId,
                DefectCode = 1000 + Math.Clamp(b.Count, 1, 10),
                X          = b.X,
                Y          = b.Y,
                Diameter   = b.Diameter
            }).ToList();
        }

        Canvas.RenderAll(_masterBalls, defects, _transform);
    }

    // ── Build die grid ────────────────────────────────────────────────────

    private void RebuildDieGrid()
    {
        if (_data == null) return;

        int rows = _data.Rows;
        int cols = _data.Cols;
        var selectedChars = GetSelectedDefectChars();

        GridColumns = cols + 1;

        var cells = new List<RecurringDieCell>();

        cells.Add(new RecurringDieCell(-1, -1, isHeader: true));
        for (int c = 0; c < cols; c++)
            cells.Add(new RecurringDieCell(-1, c, isHeader: true));

        for (int r = 0; r < rows; r++)
        {
            cells.Add(new RecurringDieCell(r, -1, isHeader: true));
            for (int c = 0; c < cols; c++)
            {
                var cell = new RecurringDieCell(r, c, isHeader: false);
                int effective = _data.Dies[r, c].GetCountForChars(selectedChars);
                cell.DisplayCount = effective >= _filterMinCount ? effective : 0;
                cells.Add(cell);
            }
        }

        DieGrid = new ObservableCollection<RecurringDieCell>(cells);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private HashSet<char> GetSelectedDefectChars()
        => DefectTypeFilters.Where(f => f.IsSelected).Select(f => f.Letter).ToHashSet();

    private HashSet<int> GetSelectedDefectCodes()
        => DefectTypeFilters.Where(f => f.IsSelected).SelectMany(f => f.DefectCodes).ToHashSet();
}
