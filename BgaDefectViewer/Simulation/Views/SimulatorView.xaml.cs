using System.Windows;
using System.Windows.Controls;
using BgaDefectViewer.Simulation.Models;
using BgaDefectViewer.Simulation.Processing;
using BgaDefectViewer.Simulation.ViewModels;

namespace BgaDefectViewer.Simulation.Views;

public partial class SimulatorView : UserControl
{
    private SimulatorViewModel? _vm;
    private SimulationFrame? _lastFrame;
    private SimulationParams? _lastFrameParams;

    public SimulatorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Detach();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        if (e.NewValue is SimulatorViewModel vm)
        {
            _vm = vm;
            _vm.FrameUpdated += OnFrameUpdated;
            _vm.ResetViewRequested += OnResetViewRequested;
            _vm.BinarizeRequested += OnBinarizeRequested;
            _vm.InspectionRequested += OnInspectionRequested;
            _vm.ViewModeChanged += OnViewModeChanged;
            _vm.MarkerVisibilityChanged += OnMarkerVisibilityChanged;
        }
    }

    private void Detach()
    {
        if (_vm == null) return;
        _vm.FrameUpdated -= OnFrameUpdated;
        _vm.ResetViewRequested -= OnResetViewRequested;
        _vm.BinarizeRequested -= OnBinarizeRequested;
        _vm.InspectionRequested -= OnInspectionRequested;
        _vm.ViewModeChanged -= OnViewModeChanged;
        _vm.MarkerVisibilityChanged -= OnMarkerVisibilityChanged;
        _vm = null;
    }

    private void OnFrameUpdated(SimulationFrame frame)
    {
        bool resetView = _lastFrameParams == null || BoundsChanged(_lastFrameParams, frame.Params);
        Canvas.SetFrame(frame, resetView);
        _lastFrame = frame;
        _lastFrameParams = frame.Params;
        // Canvas resets _viewMode/_inspection internally; reflect that on the canvas
        // surface for marker toggles (VM has just reset its own state in RunGenerateAsync).
        Canvas.SetMarkerVisibility(_vm?.ShowDefectMarkers ?? true, _vm?.ShowMasterBalls ?? false);
    }

    private void OnResetViewRequested() => Canvas.ResetView();

    // ── Stage 2 glue ─────────────────────────────────────────────────────

    private void OnBinarizeRequested(byte threshold)
    {
        Canvas.Binarize(threshold);
        // KBGA mirrors what the user just did — flipping to binary view after
        // 二値化 is the natural confirmation. Then they can switch back.
        if (_vm != null) _vm.ViewMode = ViewMode.Binary;
    }

    private void OnInspectionRequested()
    {
        if (_vm == null || _lastFrame == null) return;
        // Ensure binary bitmap exists (auto-binarize with current threshold).
        if (Canvas.BinaryBitmap == null) Canvas.Binarize(_vm.BinLevel);
        var binary = Canvas.BinaryBitmap;
        var xform = Canvas.Transform;
        if (binary == null || xform == null) return;

        var binParams = _vm.BuildBinarizationParams();
        var inspection = Inspector.RunMissingDetection(
            binary, xform, _lastFrame.Masters, binParams, _lastFrame.Params);
        Canvas.SetInspection(inspection);
        _vm.InspectionText = inspection.SummaryText;
        // Auto-enable defect markers so the user immediately sees the result.
        if (!_vm.ShowDefectMarkers) _vm.ShowDefectMarkers = true;
        else Canvas.SetMarkerVisibility(_vm.ShowDefectMarkers, _vm.ShowMasterBalls);
    }

    private void OnViewModeChanged(ViewMode mode) => Canvas.SetViewMode(mode);

    private void OnMarkerVisibilityChanged(bool showDefect, bool showMaster)
        => Canvas.SetMarkerVisibility(showDefect, showMaster);

    private static bool BoundsChanged(SimulationParams a, SimulationParams b)
        => a.Rows != b.Rows
        || a.Cols != b.Cols
        || a.PitchX != b.PitchX
        || a.PitchY != b.PitchY
        || a.StaggerOffsetX != b.StaggerOffsetX
        || a.Layout != b.Layout
        || a.MasterDiameter != b.MasterDiameter;
}
