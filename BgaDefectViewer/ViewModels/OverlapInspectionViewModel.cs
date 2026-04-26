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
    private MasterBall[]? _visibleBalls;     // _masterBalls minus mask-hidden
    private HashSet<int> _hiddenBallIds = new();
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

    // Alignment mm positions (from KBGA .dat file). When _align*UseMm is on,
    // the mm value is the source of truth, the grid index is auto-derived,
    // and rendering uses the exact mm point (not the FOV center).
    // Manual edit of the grid-index TextBoxes turns off mm mode for that point.
    private bool _align1UseMm;
    private bool _align2UseMm;
    private double _align1MmX, _align1MmY;
    private double _align2MmX, _align2MmY;

    public bool Align1UseMm => _align1UseMm;
    public bool Align2UseMm => _align2UseMm;
    public double Align1MmX => _align1MmX;
    public double Align1MmY => _align1MmY;
    public double Align2MmX => _align2MmX;
    public double Align2MmY => _align2MmY;

    private string _alignSourceText = "";
    public string AlignSourceText
    {
        get => _alignSourceText;
        set => SetProperty(ref _alignSourceText, value);
    }

    // Substrate outline from `SubstrateSize=X,Y,Z` in the .dat file.
    private double? _substrateSizeX;
    private double? _substrateSizeY;
    public double? SubstrateSizeX => _substrateSizeX;
    public double? SubstrateSizeY => _substrateSizeY;
    public bool HasSubstrateSize => _substrateSizeX.HasValue && _substrateSizeY.HasValue;

    private string _substrateSizeText = "";
    public string SubstrateSizeText
    {
        get => _substrateSizeText;
        set => SetProperty(ref _substrateSizeText, value);
    }

    private bool _showSubstrate;
    public bool ShowSubstrate
    {
        get => _showSubstrate;
        set
        {
            if (SetProperty(ref _showSubstrate, value))
            {
                // Substrate may be larger than the FOV union / device area —
                // re-expand bounds so the green frame is fully visible.
                if (_hasResult) ExpandBoundsForFovGrid();
                RequestRender();
            }
        }
    }

    private int _align1FovX = 1;
    public int Align1FovX
    {
        get => _align1FovX;
        set
        {
            if (SetProperty(ref _align1FovX, value))
                _align1UseMm = false;
        }
    }

    private int _align1FovY = 1;
    public int Align1FovY
    {
        get => _align1FovY;
        set
        {
            if (SetProperty(ref _align1FovY, value))
                _align1UseMm = false;
        }
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
            {
                _align2AutoTrack = false;
                _align2UseMm = false;
            }
        }
    }

    private int _align2FovY = 1;
    public int Align2FovY
    {
        get => _align2FovY;
        set
        {
            if (SetProperty(ref _align2FovY, value))
            {
                _align2AutoTrack = false;
                _align2UseMm = false;
            }
        }
    }

    /// <summary>Programmatic update of grid index that leaves tracking flags alone.</summary>
    private void SetAlign1Internally(int x, int y)
    {
        if (_align1FovX != x)
        {
            _align1FovX = x;
            OnPropertyChanged(nameof(Align1FovX));
        }
        if (_align1FovY != y)
        {
            _align1FovY = y;
            OnPropertyChanged(nameof(Align1FovY));
        }
    }

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

    private string _ballTotalsText = "";
    public string BallTotalsText
    {
        get => _ballTotalsText;
        set => SetProperty(ref _ballTotalsText, value);
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
    public void LoadMaster(MasterBall[] balls, MasterMetadata? metadata = null)
    {
        _masterBalls = balls;
        _visibleBalls = balls;
        _hiddenBallIds.Clear();

        // If the KBGA .dat sidecar provided alignment fiducials, they take
        // priority over the defaults (both in mm, same coordinate system
        // as the ball CSV).
        if (metadata?.AlignmentPoint1Mm is { } p1)
        {
            _align1MmX = p1.X; _align1MmY = p1.Y;
            _align1UseMm = true;
            OnPropertyChanged(nameof(Align1MmX));
            OnPropertyChanged(nameof(Align1MmY));
            OnPropertyChanged(nameof(Align1UseMm));
        }
        else
        {
            _align1UseMm = false;
            OnPropertyChanged(nameof(Align1UseMm));
        }
        if (metadata?.AlignmentPoint2Mm is { } p2)
        {
            _align2MmX = p2.X; _align2MmY = p2.Y;
            _align2UseMm = true;
            _align2AutoTrack = false; // mm overrides the default diagonal
            OnPropertyChanged(nameof(Align2MmX));
            OnPropertyChanged(nameof(Align2MmY));
            OnPropertyChanged(nameof(Align2UseMm));
        }
        else
        {
            _align2UseMm = false;
            OnPropertyChanged(nameof(Align2UseMm));
        }

        // Substrate outline
        if (metadata?.SubstrateSize is { } sub)
        {
            _substrateSizeX = sub.X;
            _substrateSizeY = sub.Y;
            SubstrateSizeText = $"Substrate: {sub.X:F2} x {sub.Y:F2} mm  (Z {sub.Z:F2})";
        }
        else
        {
            _substrateSizeX = null;
            _substrateSizeY = null;
            SubstrateSizeText = "Substrate: unknown (no .dat SubstrateSize)";
        }
        OnPropertyChanged(nameof(SubstrateSizeX));
        OnPropertyChanged(nameof(SubstrateSizeY));
        OnPropertyChanged(nameof(HasSubstrateSize));

        var (cx, cy, sx, sy) = FovGridCalculator.CalculateBallClusterCenter(balls);
        _clusterCenter = (cx, cy);
        _clusterSpan = (sx, sy);
        ClusterCenterText = $"Center: ({cx:F3}, {cy:F3}) | Span: {sx:F3} x {sy:F3} mm";

        // Build the alignment-source diagnostic — the .dat values are
        // FOV-relative offsets; absolute position is computed at Execute
        // when the FOV grid is known. We still preview the offsets here.
        if (_align1UseMm || _align2UseMm)
        {
            string diag = "";
            if (_align1UseMm)
                diag += $"\n  A1 offset ({_align1MmX:F3}, {_align1MmY:F3}) mm from owning FOV center → upper-right corner FOV";
            if (_align2UseMm)
                diag += $"\n  A2 offset ({_align2MmX:F3}, {_align2MmY:F3}) mm from owning FOV center → lower-left corner FOV";
            AlignSourceText = "Align from Master.dat (FOV-relative):" + diag;
        }
        else
        {
            AlignSourceText = "Align source: grid index (no .dat fiducials)";
        }

        // Auto-populate Device Area from ball span + margin
        DeviceAreaX = Math.Ceiling(sx + 2.0);
        DeviceAreaY = Math.Ceiling(sy + 2.0);

        // Reset result state
        _hasResult = false;
        _fovCells.Clear();
        _overlapRegions.Clear();
        _duplicates.Clear();
        FovBallCounts = new ObservableCollection<FovBallCount>();
        BallTotalsText = "";
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

        // Render balls minus any masked-out ones, so the canvas matches
        // what the real machine would actually inspect.
        var ballsToRender = _visibleBalls ?? _masterBalls;
        Canvas.RenderAll(ballsToRender, new List<DefectBall>(), _transform);

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
            _hiddenBallIds.Clear();
            _visibleBalls = _masterBalls;
            FovBallCounts = new ObservableCollection<FovBallCount>();
            TotalDuplicates = 0;
            ValidationErrors = "";
            ResultInfo = "";
            SummaryText = "Overlap Inspection disabled";
            RequestRender();
            return;
        }

        var param = BuildParams();

        // .dat convention (verified on the real machine): when overlap
        // inspection is enabled, AlignmentPoint1mm/2mm are stored as the
        // offset from a CORNER FOV's center, not as absolute stage
        // coordinates. The owning FOV is fixed by which point it is —
        //   A1 → (FovCountX, 1)        upper-right
        //   A2 → (1, FovCountY)        lower-left
        // Absolute stage position = FOV-center + offset, which moves the
        // fiducial out toward the substrate margin (matching the photo).
        // For a 1×1 grid the FOV center is (0,0), so the offset is the
        // absolute position — same as the no-overlap setup view.
        if (_align1UseMm)
        {
            int gx = param.FovCountX, gy = 1;
            var (cx, cy) = ComputeFovCenterMm(gx, gy, param);
            SetAlign1Internally(gx, gy);
            param.Alignment1FovX = gx;
            param.Alignment1FovY = gy;
            param.Align1Mm = (cx + _align1MmX, cy + _align1MmY);
        }
        if (_align2UseMm)
        {
            int gx = 1, gy = param.FovCountY;
            var (cx, cy) = ComputeFovCenterMm(gx, gy, param);
            SetAlign2Internally(gx, gy);
            param.Alignment2FovX = gx;
            param.Alignment2FovY = gy;
            param.Align2Mm = (cx + _align2MmX, cy + _align2MmY);
        }
        else if (_align2AutoTrack)
        {
            // No .dat registered — default to the diagonal of Align 1.
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
            _hiddenBallIds.Clear();
            _visibleBalls = _masterBalls;
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

        // Detect duplicates BEFORE assignment — AssignBallsToFovCells dedups
        // each ball onto the latest-scan cell, so afterwards BallIds alone
        // no longer reveals which balls were shared.
        _duplicates = FovGridCalculator.DetectDuplicateBalls(_fovCells, _masterBalls, param);

        // Assign balls to FOV cells (dedup by scan order + mask-aware).
        // Returned set = balls that fell only in boundary mask zones; they
        // should be hidden from rendering to simulate the real machine.
        _hiddenBallIds = FovGridCalculator.AssignBallsToFovCells(_fovCells, _masterBalls, param);
        _visibleBalls = _hiddenBallIds.Count == 0
            ? _masterBalls
            : _masterBalls.Where(b => !_hiddenBallIds.Contains(b.Id)).ToArray();

        // Calculate overlap regions (uses full FOV rects — these represent
        // what the camera photographs, not what gets inspected)
        _overlapRegions = FovGridCalculator.CalculateOverlapRegions(_fovCells);

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

    private OverlapParams BuildParams()
    {
        var p = new OverlapParams
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
            SubstrateSizeX = _substrateSizeX,
            SubstrateSizeY = _substrateSizeY,
            ShowSubstrate = _showSubstrate && _substrateSizeX.HasValue && _substrateSizeY.HasValue,
        };

        // Resolve FOV-relative .dat offsets to absolute stage coordinates
        // using the FOV grid implied by the current params. A1 lives in the
        // upper-right corner FOV; A2 in the lower-left.
        if (_align1UseMm)
        {
            int gx = p.FovCountX, gy = 1;
            var (cx, cy) = ComputeFovCenterMm(gx, gy, p);
            p.Align1Mm = (cx + _align1MmX, cy + _align1MmY);
        }
        if (_align2UseMm)
        {
            int gx = 1, gy = p.FovCountY;
            var (cx, cy) = ComputeFovCenterMm(gx, gy, p);
            p.Align2Mm = (cx + _align2MmX, cy + _align2MmY);
        }
        return p;
    }

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

        // Alignment crosses (absolute mm). They can sit beyond the FOV union
        // when the .dat fiducials place them on the substrate margin.
        var paramsForBounds = BuildParams();
        if (paramsForBounds.Align1Mm is { } a1)
        {
            if (a1.X < minX) minX = a1.X;
            if (a1.X > maxX) maxX = a1.X;
            if (a1.Y < minY) minY = a1.Y;
            if (a1.Y > maxY) maxY = a1.Y;
        }
        if (paramsForBounds.Align2Mm is { } a2)
        {
            if (a2.X < minX) minX = a2.X;
            if (a2.X > maxX) maxX = a2.X;
            if (a2.Y < minY) minY = a2.Y;
            if (a2.Y > maxY) maxY = a2.Y;
        }

        // If the substrate outline is being shown, include it so the green
        // frame is always visible (substrate is typically the largest rect).
        if (_showSubstrate && _substrateSizeX.HasValue && _substrateSizeY.HasValue)
        {
            double halfSx = _substrateSizeX.Value / 2.0;
            double halfSy = _substrateSizeY.Value / 2.0;
            if (-halfSx < minX) minX = -halfSx;
            if (halfSx > maxX) maxX = halfSx;
            if (-halfSy < minY) minY = -halfSy;
            if (halfSy > maxY) maxY = halfSy;
        }

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

        // Totals summary (shown just below the Balls-per-FOV table)
        int inspected = _fovCells.Sum(c => c.BallIds.Count);
        int total = _masterBalls?.Length ?? 0;
        int hidden = _hiddenBallIds.Count;
        int shared = _duplicates.Count;

        string line1 = $"Inspected: {inspected,7:N0}   Total: {total,7:N0}";
        string line2 = hidden > 0
            ? $"Masked:    {hidden,7:N0}   Shared: {shared,7:N0}"
            : $"Shared:    {shared,7:N0}";
        BallTotalsText = line1 + "\n" + line2;
    }

    /// <summary>
    /// Stage-coordinate center of FOV (gx, gy), using the same formula
    /// as CalculateFovGrid. Origin is the device stage origin (0, 0);
    /// +Y is up, so y=1 is the top row.
    /// </summary>
    private static (double cx, double cy) ComputeFovCenterMm(int gx, int gy, OverlapParams p)
    {
        double cfx = (1 + p.FovCountX) / 2.0;
        double cfy = (1 + p.FovCountY) / 2.0;
        double cx = (gx - cfx) * p.MoveDistX;
        double cy = -(gy - cfy) * p.MoveDistY;
        return (cx, cy);
    }

    private void BuildSummaryText(OverlapParams param)
    {
        int totalBalls = _masterBalls?.Length ?? 0;
        int inspectedBalls = _fovCells.Sum(c => c.BallIds.Count);
        int hiddenBalls = _hiddenBallIds.Count;
        int totalFovs = _fovCells.Count;

        string ballSummary = hiddenBalls > 0
            ? $"Balls: {inspectedBalls} inspected / {totalBalls} total ({hiddenBalls} masked)"
            : $"Balls: {inspectedBalls} / {totalBalls}";

        SummaryText = $"FOVs: {totalFovs} ({param.FovCountX}x{param.FovCountY}) | " +
                      $"{ballSummary} | " +
                      $"Overlap: {totalFovs - 1} zones, {param.OverlapLengthX:F2}x{param.OverlapLengthY:F2}mm | " +
                      $"Shared (dedup'd): {_duplicates.Count}";
    }
}

/// <summary>Display model for per-FOV ball count list.</summary>
public class FovBallCount
{
    public int ScanIndex { get; set; }
    public string GridPosition { get; set; } = "";
    public int BallCount { get; set; }
}
