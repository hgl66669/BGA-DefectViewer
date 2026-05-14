using System.Collections.ObjectModel;
using System.Windows.Input;
using BgaDefectViewer.Controls;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;
using BgaDefectViewer.Parsers;

namespace BgaDefectViewer.ViewModels;

/// <summary>
/// Which metric drives the die-cell number and colour in the left panel.
/// </summary>
public enum DieMetricMode
{
    /// <summary>Cell shows max recurring count of any single ball in the die.</summary>
    Max,
    /// <summary>Cell shows count of ball positions whose recurrence ≥ threshold.</summary>
    Count,
}

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

    // Cached defect markers from the last filter/selection. Pan/zoom/resize
    // reuse this list instead of rebuilding the table + LINQ pipeline each frame.
    private List<DefectBall> _cachedDefects = new();

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

    // ── Die metric mode ───────────────────────────────────────────────────
    private DieMetricMode _dieMetric = DieMetricMode.Max;
    public DieMetricMode DieMetric
    {
        get => _dieMetric;
        set
        {
            if (SetProperty(ref _dieMetric, value))
            {
                OnPropertyChanged(nameof(IsDieMetricMax));
                OnPropertyChanged(nameof(IsDieMetricCount));
                ApplyFilter();
            }
        }
    }

    public bool IsDieMetricMax
    {
        get => _dieMetric == DieMetricMode.Max;
        set { if (value) DieMetric = DieMetricMode.Max; }
    }
    public bool IsDieMetricCount
    {
        get => _dieMetric == DieMetricMode.Count;
        set { if (value) DieMetric = DieMetricMode.Count; }
    }

    // ── Defect type bulk-toggle commands ──────────────────────────────────
    public ICommand SelectAllDefectTypesCommand { get; }
    public ICommand ClearAllDefectTypesCommand { get; }

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

        SelectAllDefectTypesCommand = new RelayCommand(_ => SetAllDefectTypes(true));
        ClearAllDefectTypesCommand  = new RelayCommand(_ => SetAllDefectTypes(false));
    }

    private void SetAllDefectTypes(bool selected)
    {
        foreach (var f in DefectTypeFilters)
            f.IsSelected = selected;
    }

    // ── Load ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Clear the canvas selection ring (used on Lot switch, Die switch, and
    /// when the user clicks blank canvas). Safe to call when no selection
    /// exists — Canvas.HighlightBall(null, ...) handles that gracefully.
    /// </summary>
    public void ClearSelection() => Canvas?.HighlightBall(null, _transform);

    /// <summary>Called from MainViewModel after .map files are loaded (Phase 1).</summary>
    public void Load(RecurringDefectData? data, MasterBall[]? masterBalls)
    {
        // Drop the old highlight before we tear down state — a Lot switch
        // would otherwise leave a stale ring drawn at the previous Lot's
        // ball coordinates.
        ClearSelection();

        _data = data;
        _masterBalls = masterBalls;
        _selectedDieCell = null;

        // Reset all UI state up-front so a Reload or Lot switch always clears
        // the previous Lot's grid, table, and (via RequestRender below) the
        // defect markers on the canvas — even when the new Lot has no data.
        DieGrid = new ObservableCollection<RecurringDieCell>();
        GridColumns = 1;
        DisplayedBalls = new ObservableCollection<RecurringBallInfo>();
        LocationsText = "Locations: 0";

        if (masterBalls != null)
        {
            var (minX, maxX, minY, maxY) = MasterCsvParser.GetBounds(masterBalls);
            _transform = new CoordinateTransform();
            _transform.SetBounds(minX, maxX, minY, maxY);
        }
        else
        {
            _transform = null;
        }

        if (data == null || masterBalls == null)
        {
            HeaderText = "No data loaded";
            RequestRender(null);
            return;
        }

        HeaderText = $"Lot: {data.LotName}  |  {data.SubstrateCount} substrates";
        _filterMinCount = 1;
        OnPropertyChanged(nameof(FilterMinCount));

        RebuildDieGrid();
        RequestRender(null);
    }

    /// <summary>Called from MainViewModel after .afa enrichment completes (Phase 2).
    /// Ball-level data is now available, so we recompute the whole die grid
    /// (max-ball-recurring semantics) and refresh the selected die's table.</summary>
    public void RefreshBallData()
    {
        if (_data == null) return;
        ApplyFilter();
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

        // The previous highlight pointed at a ball from the previous die /
        // previous filter state; clear it before we rebuild the ball list so
        // the ring doesn't survive into a context where it no longer makes
        // sense (e.g. the highlighted ball got filtered out).
        ClearSelection();

        var die = _data.Dies[row, col];
        var selectedCodes = GetSelectedDefectCodes();

        // Build effective ball list. With no defect type selected,
        // GetCountForCodes returns 0 → the Where filter drops everything,
        // matching the greyed-out die grid.
        var filtered = die.Balls
            .Select(b => (Ball: b, EffCount: b.GetCountForCodes(selectedCodes)))
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
        var selectedCodes = GetSelectedDefectCodes();

        foreach (var cell in DieGrid)
        {
            if (cell.IsHeader) continue;
            cell.DisplayCount = ComputeDieDisplayCount(
                _data.Dies[cell.Row, cell.Col], selectedCodes, selectedChars);
        }

        if (_selectedDieCell != null)
            ShowDieBalls(_selectedDieCell.Row, _selectedDieCell.Col);
        else
            RequestRender(null);
    }

    /// <summary>
    /// Per-die value shown in the left grid. Dispatch by metric mode:
    /// - Max:   max ball recurring count in this die under filter; threshold
    ///          filters the cell (greys out when max &lt; threshold).
    /// - Count: number of ball positions in this die with recurring ≥ threshold.
    ///          Threshold defines what "recurring" means; the cell shows
    ///          the position count directly (no further threshold gate).
    /// </summary>
    private int ComputeDieDisplayCount(RecurringDieInfo die,
                                       ISet<int> selectedCodes,
                                       ISet<char> selectedChars)
    {
        if (_dieMetric == DieMetricMode.Count)
        {
            return die.GetRecurringPositionCount(selectedCodes, _filterMinCount);
        }
        int max = die.GetMaxBallCountForCodes(selectedCodes, selectedChars);
        return max >= _filterMinCount ? max : 0;
    }

    // ── FIT ───────────────────────────────────────────────────────────────

    public void FitView()
    {
        _transform?.ResetToFit();
        RequestRenderCurrentSelection();
    }

    /// <summary>
    /// Double-click on the detail list → center the canvas on this ball and
    /// zoom so the ball is comfortably visible (~15px radius). Highlight ring
    /// stays on after the jump until the next selection / blank click.
    /// Mirrors SubstrateViewerViewModel.JumpToDefect.
    /// </summary>
    public void JumpToBall(RecurringBallInfo info)
    {
        if (_transform == null || Canvas == null) return;

        double curR = _transform.BallRadiusPixels(info.Diameter);
        double targetZoom = curR > 0 ? _transform.Zoom * 15.0 / curR : 5.0;
        _transform.CenterOn(info.X, info.Y, Canvas.ActualWidth, Canvas.ActualHeight, targetZoom);

        // Re-render at the new pan/zoom first (this also resets the pan anchor
        // so the highlight ring below is drawn at the correct screen position).
        RequestRenderCurrentSelection();

        // The defect markers on canvas are synthetic DefectBalls created by
        // RequestRender — find the one matching this RecurringBallInfo by
        // BallId so the highlight ring lands on the right circle.
        var match = _cachedDefects.FirstOrDefault(d => d.BallId == info.BallId);
        if (match != null)
            Canvas.HighlightBall(match, _transform);
    }

    /// <summary>
    /// Lightweight redraw used by pan/zoom/resize. Reuses the cached defect list
    /// so we don't rebuild the DataGrid items or rerun the filter LINQ pipeline
    /// (which would tank perf during drag at 60-120 mouse-move events/sec).
    /// </summary>
    public void RequestRenderCurrentSelection()
    {
        if (Canvas == null || _masterBalls == null || _transform == null) return;
        _transform.SetCanvasSize(Canvas.ActualWidth, Canvas.ActualHeight);
        Canvas.RenderAll(_masterBalls, _cachedDefects, _transform);
    }

    public void RequestRender(List<RecurringBallInfo>? balls)
    {
        if (Canvas == null || _masterBalls == null || _transform == null) return;
        _transform.SetCanvasSize(Canvas.ActualWidth, Canvas.ActualHeight);

        // Always pass a defects list (empty when no balls), never null.
        // BallMapCanvas.RenderAll treats null as "keep previous defects",
        // which would leave stale markers on the canvas after a Lot switch.
        var defects = (balls ?? Enumerable.Empty<RecurringBallInfo>())
            .Select(b => new Models.DefectBall
            {
                BallId     = b.BallId,
                DefectCode = 1000 + Math.Clamp(b.Count, 1, 10),
                X          = b.X,
                Y          = b.Y,
                Diameter   = b.Diameter
            }).ToList();

        _cachedDefects = defects;
        Canvas.RenderAll(_masterBalls, defects, _transform);
    }

    // ── Build die grid ────────────────────────────────────────────────────

    private void RebuildDieGrid()
    {
        if (_data == null) return;

        int rows = _data.Rows;
        int cols = _data.Cols;
        var selectedChars = GetSelectedDefectChars();
        var selectedCodes = GetSelectedDefectCodes();

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
                cell.DisplayCount = ComputeDieDisplayCount(
                    _data.Dies[r, c], selectedCodes, selectedChars);
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
