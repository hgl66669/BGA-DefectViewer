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

    // Align 2 auto-tracks the bottom-right FOV (diagonal partner of Align 1)
    // until the user edits either coordinate manually. LoadMaster re-enables
    // tracking because the new device may need a different grid size.
    private bool _align2AutoTrack = true;

    private int _align2FovX = 1;
    public int Align2FovX
    {
        get => _align2FovX;
        set
        {
            if (SetProperty(ref _align2FovX, value))
                _align2AutoTrack = false;
        }
    }

    private int _align2FovY = 1;
    public int Align2FovY
    {
        get => _align2FovY;
        set
        {
            if (SetProperty(ref _align2FovY, value))
                _align2AutoTrack = false;
        }
    }

    /// <summary>Programmatic update that does NOT disable auto-tracking.</summary>
    private void SetAlign2Internally(int x, int y)
    {
        if (_align2FovX != x)
        {
            _align2FovX = x;
            OnPropertyChanged(nameof(Align2FovX));
        }
        if (_align2FovY != y)
        {
            _align2FovY = y;
            OnPropertyChanged(nameof(Align2FovY));
        }
    }

    // ── Camera raw & display layer settings ───────────────────────────

    /// <summary>0 = Normal (94.52×62.87), 1 = Enlarged (109.66×72.95), 2 = Custom.</summary>
    private int _cameraTypeIndex;
    public int CameraTypeIndex
    {
        get => _cameraTypeIndex;
        set
        {
            if (SetProperty(ref _cameraTypeIndex, value))
            {
                ApplyCameraTypePreset();
                OnPropertyChanged(nameof(IsCameraCustom));
                RequestRender();
            }
        }
    }

    public bool IsCameraCustom => _cameraTypeIndex == 2;

    private double _cameraRawX = 94.52;
    public double CameraRawX
    {
        get => _cameraRawX;
        set
        {
            if (SetProperty(ref _cameraRawX, value)) RequestRender();
        }
    }

    private double _cameraRawY = 62.87;
    public double CameraRawY
    {
        get => _cameraRawY;
        set
        {
            if (SetProperty(ref _cameraRawY, value)) RequestRender();
        }
    }

    private bool _showCameraRawLayer;
    public bool ShowCameraRawLayer
    {
        get => _showCameraRawLayer;
        set
        {
            if (SetProperty(ref _showCameraRawLayer, value))
            {
                // Camera raw is much wider than FOV — if we just toggled it
                // on, expand bounds so the extra frame is visible.
                if (_hasResult) ExpandBoundsForFovGrid();
                RequestRender();
            }
        }
    }

    private bool _showFovLayer = true;
    public bool ShowFovLayer
    {
        get => _showFovLayer;
        set
        {
            if (SetProperty(ref _showFovLayer, value)) RequestRender();
        }
    }

    private bool _showEffectiveLayer = true;
    public bool ShowEffectiveLayer
    {
        get => _showEffectiveLayer;
        set
        {
            if (SetProperty(ref _showEffectiveLayer, value)) RequestRender();
        }
    }

    private void ApplyCameraTypePreset()
    {
        switch (_cameraTypeIndex)
        {
            case 0: // Normal
                CameraRawX = 94.52;
                CameraRawY = 62.87;
                break;
            case 1: // Enlarged
                CameraRawX = 109.66;
                CameraRawY = 72.95;
                break;
            // case 2: Custom → keep whatever the user typed
        }
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

        // New device → re-enable Align-2 auto-tracking so the diagonal
        // corner recomputes based on the next Execute's FovCount.
        _align2AutoTrack = true;

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

        // Auto-set Align 2 to the bottom-right FOV (diagonal to Align 1)
        // whenever the user has not manually edited it. This gives a sensible
        // default for any grid size: 2×2 → (2,2), 3×3 → (3,3), 2×3 → (2,3).
        if (_align2AutoTrack)
        {
            SetAlign2Internally(param.FovCountX, param.FovCountY);
            param.Alignment2FovX = param.FovCountX;
            param.Alignment2FovY = param.FovCountY;
        }

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
        double fovUnionX = (param.FovCountX - 1) * moveDistX + param.FovSizeX;
        double fovUnionY = (param.FovCountY - 1) * moveDistY + param.FovSizeY;

        // Validation indicators (P4 spec — three quick sanity checks)
        double unionMarginX = fovUnionX - param.DeviceAreaX;
        double unionMarginY = fovUnionY - param.DeviceAreaY;
        string unionCheck = (unionMarginX >= 0 && unionMarginY >= 0) ? "✓" : "✗";

        double rawMarginX = param.CameraRawX - param.FovSizeX;
        double rawMarginY = param.CameraRawY - param.FovSizeY;
        string rawCheck = (rawMarginX >= 0 && rawMarginY >= 0) ? "✓" : "✗";

        double maskSlackX = param.OverlapLengthX - 2 * param.BoundaryMaskX;
        double maskSlackY = param.OverlapLengthY - 2 * param.BoundaryMaskY;
        string maskCheck = (maskSlackX >= 0 && maskSlackY >= 0) ? "✓" : "⚠";

        ResultInfo =
            $"FOV Count: {param.FovCountX} x {param.FovCountY}\n" +
            $"FOV size (each rect): {param.FovSizeX:F2} x {param.FovSizeY:F2} mm\n" +
            $"Move Dist: {moveDistX:F2} x {moveDistY:F2} mm\n" +
            $"FOV union extent: {fovUnionX:F2} x {fovUnionY:F2} mm\n" +
            $"Device Area: {param.DeviceAreaX:F2} x {param.DeviceAreaY:F2} mm\n" +
            "─────────────\n" +
            $"{unionCheck} FOV union covers Device Area (margin X {unionMarginX:+0.00;-0.00}, Y {unionMarginY:+0.00;-0.00} mm)\n" +
            $"{rawCheck} Camera Raw fits FOV (slack X {rawMarginX:+0.00;-0.00}, Y {rawMarginY:+0.00;-0.00} mm)\n" +
            $"{maskCheck} Overlap ≥ 2·Boundary mask (slack X {maskSlackX:+0.00;-0.00}, Y {maskSlackY:+0.00;-0.00} mm)";
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
        CameraType = (CameraType)_cameraTypeIndex,
        CameraRawX = _cameraRawX,
        CameraRawY = _cameraRawY,
        ShowCameraRawLayer = _showCameraRawLayer,
        ShowFovLayer = _showFovLayer,
        ShowEffectiveLayer = _showEffectiveLayer,
    };

    /// <summary>
    /// Expand the coordinate transform bounds to include the full FOV grid,
    /// so FOV rectangles beyond the ball extent are visible.
    /// </summary>
    private void ExpandBoundsForFovGrid()
    {
        if (_transform == null || _fovCells.Count == 0) return;

        var (minX, maxX, minY, maxY) = MasterCsvParser.GetBounds(_masterBalls!);

        // Include FOV union extents
        foreach (var cell in _fovCells)
        {
            if (cell.Left < minX) minX = cell.Left;
            if (cell.Right > maxX) maxX = cell.Right;
            if (cell.Bottom < minY) minY = cell.Bottom;
            if (cell.Top > maxY) maxY = cell.Top;
        }

        // Include Device Area frame (centered at 0,0) so it is always visible
        // even when it is larger than both balls and FOV union.
        double halfDx = _deviceAreaX / 2.0;
        double halfDy = _deviceAreaY / 2.0;
        if (-halfDx < minX) minX = -halfDx;
        if (halfDx > maxX) maxX = halfDx;
        if (-halfDy < minY) minY = -halfDy;
        if (halfDy > maxY) maxY = halfDy;

        // If Camera Raw layer is visible, also include its extent (raw image
        // is wider than the FOV so the outermost cells extend far beyond).
        if (_showCameraRawLayer)
        {
            double halfRawX = _cameraRawX / 2.0;
            double halfRawY = _cameraRawY / 2.0;
            foreach (var cell in _fovCells)
            {
                double rawL = cell.CenterX - halfRawX;
                double rawR = cell.CenterX + halfRawX;
                double rawB = cell.CenterY - halfRawY;
                double rawT = cell.CenterY + halfRawY;
                if (rawL < minX) minX = rawL;
                if (rawR > maxX) maxX = rawR;
                if (rawB < minY) minY = rawB;
                if (rawT > maxY) maxY = rawT;
            }
        }

        // Add small margin
        double margin = 2.0;
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
