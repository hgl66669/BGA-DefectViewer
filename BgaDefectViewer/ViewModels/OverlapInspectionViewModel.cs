using System.Collections.ObjectModel;
using System.Windows.Input;
using BgaDefectViewer.Controls;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;
using BgaDefectViewer.Parsers;

namespace BgaDefectViewer.ViewModels;

public class OverlapInspectionViewModel : ViewModelBase
{
    private MasterBall[]? _masterBalls;
    private CoordinateTransform? _transform;
    private List<FovCell> _fovCells = new();
    private List<(double left, double bottom, double right, double top)> _overlapRegions = new();
    private List<DuplicateBallPair> _duplicates = new();
    private (double x, double y) _clusterCenter;
    private (double spanX, double spanY) _clusterSpan;
    private bool _hasResult;

    // ── Canvas references (set by View code-behind) ──
    public BallMapCanvas? Canvas { get; set; }
    public FovOverlayCanvas? OverlayCanvas { get; set; }
    public CoordinateTransform? Transform => _transform;

    // ── Input parameters (no auto-recalculate — user clicks Execute) ──

    private bool _enableOverlapInspection = true;
    public bool EnableOverlapInspection
    {
        get => _enableOverlapInspection;
        set => SetProperty(ref _enableOverlapInspection, value);
    }

    private double _deviceAreaX = 90.0;
    public double DeviceAreaX
    {
        get => _deviceAreaX;
        set => SetProperty(ref _deviceAreaX, value);
    }

    private double _deviceAreaY = 90.0;
    public double DeviceAreaY
    {
        get => _deviceAreaY;
        set => SetProperty(ref _deviceAreaY, value);
    }

    private double _fovSizeX = 60.0;
    public double FovSizeX
    {
        get => _fovSizeX;
        set => SetProperty(ref _fovSizeX, value);
    }

    private double _fovSizeY = 60.0;
    public double FovSizeY
    {
        get => _fovSizeY;
        set => SetProperty(ref _fovSizeY, value);
    }

    private double _overlapLengthX = 0.3;
    public double OverlapLengthX
    {
        get => _overlapLengthX;
        set => SetProperty(ref _overlapLengthX, value);
    }

    private double _overlapLengthY = 0.3;
    public double OverlapLengthY
    {
        get => _overlapLengthY;
        set => SetProperty(ref _overlapLengthY, value);
    }

    private double _boundaryMaskX;
    public double BoundaryMaskX
    {
        get => _boundaryMaskX;
        set => SetProperty(ref _boundaryMaskX, value);
    }

    private double _boundaryMaskY;
    public double BoundaryMaskY
    {
        get => _boundaryMaskY;
        set => SetProperty(ref _boundaryMaskY, value);
    }

    private bool _isStaggeredPattern;
    public bool IsStaggeredPattern
    {
        get => _isStaggeredPattern;
        set => SetProperty(ref _isStaggeredPattern, value);
    }

    private double _duplicationAllowancePix = 10.0;
    public double DuplicationAllowancePix
    {
        get => _duplicationAllowancePix;
        set => SetProperty(ref _duplicationAllowancePix, value);
    }

    private int _align1FovX = 1;
    public int Align1FovX
    {
        get => _align1FovX;
        set => SetProperty(ref _align1FovX, value);
    }

    private int _align1FovY = 1;
    public int Align1FovY
    {
        get => _align1FovY;
        set => SetProperty(ref _align1FovY, value);
    }

    private int _align2FovX = 1;
    public int Align2FovX
    {
        get => _align2FovX;
        set => SetProperty(ref _align2FovX, value);
    }

    private int _align2FovY = 1;
    public int Align2FovY
    {
        get => _align2FovY;
        set => SetProperty(ref _align2FovY, value);
    }

    // ── Read-only display properties ─────────────────────────────────

    private string _summaryText = "";
    public string SummaryText
    {
        get => _summaryText;
        set => SetProperty(ref _summaryText, value);
    }

    private string _clusterCenterText = "";
    public string ClusterCenterText
    {
        get => _clusterCenterText;
        set => SetProperty(ref _clusterCenterText, value);
    }

    private string _resultInfo = "";
    public string ResultInfo
    {
        get => _resultInfo;
        set => SetProperty(ref _resultInfo, value);
    }

    private int _totalDuplicates;
    public int TotalDuplicates
    {
        get => _totalDuplicates;
        set => SetProperty(ref _totalDuplicates, value);
    }

    private string _validationErrors = "";
    public string ValidationErrors
    {
        get => _validationErrors;
        set => SetProperty(ref _validationErrors, value);
    }

    private ObservableCollection<FovBallCount> _fovBallCounts = new();
    public ObservableCollection<FovBallCount> FovBallCounts
    {
        get => _fovBallCounts;
        set => SetProperty(ref _fovBallCounts, value);
    }

    // ── Commands ─────────────────────────────────────────────────────

    public ICommand ExecuteCommand { get; }
    public ICommand FitViewCommand { get; }

    public OverlapInspectionViewModel()
    {
        ExecuteCommand = new RelayCommand(_ => Execute());
        FitViewCommand = new RelayCommand(_ => FitView());
    }

    // ── Public methods ───────────────────────────────────────────────

    /// <summary>Called by MainViewModel when master balls are loaded.</summary>
    public void LoadMaster(MasterBall[] balls)
    {
        _masterBalls = balls;

        var (cx, cy, sx, sy) = FovGridCalculator.CalculateBallClusterCenter(balls);
        _clusterCenter = (cx, cy);
        _clusterSpan = (sx, sy);
        ClusterCenterText = $"Center: ({cx:F3}, {cy:F3}) | Span: {sx:F3} x {sy:F3} mm";

        // Auto-populate Device Area from ball span + margin
        DeviceAreaX = Math.Ceiling(sx + 2.0);
        DeviceAreaY = Math.Ceiling(sy + 2.0);

        // Reset result state
        _hasResult = false;
        _fovCells.Clear();
        _overlapRegions.Clear();
        _duplicates.Clear();
        FovBallCounts = new ObservableCollection<FovBallCount>();
        TotalDuplicates = 0;
        ValidationErrors = "";
        ResultInfo = "";
        SummaryText = "Press [Execute] to run overlap simulation";

        // Set up transform from ball bounds and render balls only
        var (minX, maxX, minY, maxY) = MasterCsvParser.GetBounds(balls);
        _transform = new CoordinateTransform();
        _transform.SetBounds(minX, maxX, minY, maxY);
        RequestRender();
    }

    public void RequestRender()
    {
        if (Canvas == null || OverlayCanvas == null ||
            _masterBalls == null || _transform == null) return;

        _transform.SetCanvasSize(Canvas.ActualWidth, Canvas.ActualHeight);

        // Render master balls (no defects in overlap simulation)
        Canvas.RenderAll(_masterBalls, new List<DefectBall>(), _transform);

        // Render FOV overlay only if we have results
        OverlayCanvas.SetTransform(_transform);
        if (_hasResult)
        {
            OverlayCanvas.RenderAll(
                _fovCells, _overlapRegions,
                _clusterCenter, _clusterSpan,
                _duplicates, BuildParams());
        }
        else
        {
            OverlayCanvas.Clear();
        }
    }

    public void FitView()
    {
        _transform?.ResetToFit();
        RequestRender();
    }

    // ── Private methods ──────────────────────────────────────────────

    private void Execute()
    {
        if (_masterBalls == null || _transform == null) return;

        if (!_enableOverlapInspection)
        {
            _hasResult = false;
            _fovCells.Clear();
            _overlapRegions.Clear();
            _duplicates.Clear();
            FovBallCounts = new ObservableCollection<FovBallCount>();
            TotalDuplicates = 0;
            ValidationErrors = "";
            ResultInfo = "";
            SummaryText = "Overlap Inspection disabled";
            RequestRender();
            return;
        }

        var param = BuildParams();

        // Validate (hard errors block execution; warnings still render)
        var (errors, warnings) = FovGridCalculator.ValidateParams(param);
        var messages = new List<string>();
        messages.AddRange(errors);
        messages.AddRange(warnings.Select(w => "Warning: " + w));
        ValidationErrors = messages.Count > 0 ? string.Join("\n", messages) : "";
        if (errors.Count > 0)
        {
            _hasResult = false;
            _fovCells.Clear();
            _overlapRegions.Clear();
            _duplicates.Clear();
            FovBallCounts = new ObservableCollection<FovBallCount>();
            TotalDuplicates = 0;
            ResultInfo = "";
            SummaryText = "Invalid parameters";
            RequestRender();
            return;
        }

        // Device center is fixed at (0, 0) for the simulator — this matches the
        // real machine where MP_StgInspOrg defines the stage inspection origin.
        _fovCells = FovGridCalculator.CalculateFovGrid(param, 0.0, 0.0);

        // Assign balls to FOV cells
        FovGridCalculator.AssignBallsToFovCells(_fovCells, _masterBalls);

        // Calculate overlap regions
        _overlapRegions = FovGridCalculator.CalculateOverlapRegions(_fovCells);

        // Detect duplicates (balls in multiple FOVs)
        _duplicates = FovGridCalculator.DetectDuplicateBalls(_fovCells, _masterBalls);

        // Expand transform bounds to include the full FOV grid extent
        ExpandBoundsForFovGrid();

        _hasResult = true;

        // Update display
        TotalDuplicates = _duplicates.Count;
        double moveDistX = param.MoveDistX;
        double moveDistY = param.MoveDistY;
        ResultInfo = $"FOV Count: {param.FovCountX} x {param.FovCountY} | " +
                     $"Move Dist: {moveDistX:F2} x {moveDistY:F2} mm";
        UpdateFovBallCounts();
        BuildSummaryText(param);

        RequestRender();
    }

    private OverlapParams BuildParams() => new()
    {
        Enabled = _enableOverlapInspection,
        DeviceAreaX = _deviceAreaX,
        DeviceAreaY = _deviceAreaY,
        FovSizeX = _fovSizeX,
        FovSizeY = _fovSizeY,
        OverlapLengthX = _overlapLengthX,
        OverlapLengthY = _overlapLengthY,
        BoundaryMaskX = _boundaryMaskX,
        BoundaryMaskY = _boundaryMaskY,
        IsStaggeredPattern = _isStaggeredPattern,
        DuplicationAllowancePix = _duplicationAllowancePix,
        Alignment1FovX = _align1FovX,
        Alignment1FovY = _align1FovY,
        Alignment2FovX = _align2FovX,
        Alignment2FovY = _align2FovY,
    };

    /// <summary>
    /// Expand the coordinate transform bounds to include the full FOV grid,
    /// so FOV rectangles beyond the ball extent are visible.
    /// </summary>
    private void ExpandBoundsForFovGrid()
    {
        if (_transform == null || _fovCells.Count == 0) return;

        var (minX, maxX, minY, maxY) = MasterCsvParser.GetBounds(_masterBalls!);

        foreach (var cell in _fovCells)
        {
            if (cell.Left < minX) minX = cell.Left;
            if (cell.Right > maxX) maxX = cell.Right;
            if (cell.Bottom < minY) minY = cell.Bottom;
            if (cell.Top > maxY) maxY = cell.Top;
        }

        // Add small margin
        double margin = 1.0;
        _transform.SetBounds(minX - margin, maxX + margin, minY - margin, maxY + margin);
        _transform.ResetToFit();
    }

    private void UpdateFovBallCounts()
    {
        var counts = new ObservableCollection<FovBallCount>();
        foreach (var cell in _fovCells.OrderBy(c => c.ScanIndex))
        {
            counts.Add(new FovBallCount
            {
                ScanIndex = cell.ScanIndex,
                GridPosition = $"({cell.GridX},{cell.GridY})",
                BallCount = cell.BallIds.Count,
            });
        }
        FovBallCounts = counts;
    }

    private void BuildSummaryText(OverlapParams param)
    {
        int totalBalls = _masterBalls?.Length ?? 0;
        int totalFovs = _fovCells.Count;

        SummaryText = $"FOVs: {totalFovs} ({param.FovCountX}x{param.FovCountY}) | " +
                      $"Balls: {totalBalls} | " +
                      $"Overlap: {totalFovs - 1} zones, {param.OverlapLengthX:F2}x{param.OverlapLengthY:F2}mm | " +
                      $"Duplicates: {_duplicates.Count}";
    }
}

/// <summary>Display model for per-FOV ball count list.</summary>
public class FovBallCount
{
    public int ScanIndex { get; set; }
    public string GridPosition { get; set; } = "";
    public int BallCount { get; set; }
}
