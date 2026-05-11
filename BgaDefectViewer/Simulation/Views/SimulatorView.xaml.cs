using System.Windows;
using System.Windows.Controls;
using BgaDefectViewer.Simulation.Models;
using BgaDefectViewer.Simulation.ViewModels;

namespace BgaDefectViewer.Simulation.Views;

public partial class SimulatorView : UserControl
{
    private SimulatorViewModel? _vm;
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
        }
    }

    private void Detach()
    {
        if (_vm == null) return;
        _vm.FrameUpdated -= OnFrameUpdated;
        _vm.ResetViewRequested -= OnResetViewRequested;
        _vm = null;
    }

    private void OnFrameUpdated(SimulationFrame frame)
    {
        bool resetView = _lastFrameParams == null || BoundsChanged(_lastFrameParams, frame.Params);
        Canvas.SetFrame(frame, resetView);
        _lastFrameParams = frame.Params;
    }

    private void OnResetViewRequested() => Canvas.ResetView();

    private static bool BoundsChanged(SimulationParams a, SimulationParams b)
        => a.Rows != b.Rows
        || a.Cols != b.Cols
        || a.PitchX != b.PitchX
        || a.PitchY != b.PitchY
        || a.StaggerOffsetX != b.StaggerOffsetX
        || a.Layout != b.Layout
        || a.MasterDiameter != b.MasterDiameter;
}
