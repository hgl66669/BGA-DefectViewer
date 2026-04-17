using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BgaDefectViewer.Controls;
using BgaDefectViewer.Models;
using BgaDefectViewer.ViewModels;

namespace BgaDefectViewer.Views;

public partial class SubstrateViewerView : UserControl
{
    public SubstrateViewerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        BallCanvas.SizeChanged += OnCanvasSizeChanged;

        // Wire canvas-level events once (DataContext resolved at call time)
        BallCanvas.BlankClicked += () =>
        {
            if (DataContext is SubstrateViewerViewModel vm)
                vm.OnCanvasBlankClicked();
        };
        // MasterBallProbed: AddProbeStamp is already called inside BallMapCanvas.OnMouseLeftButtonDown;
        // no additional handling needed here.
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SubstrateViewerViewModel oldVm)
        {
            oldVm.PropertyChanged -= Vm_PropertyChanged;
            BallCanvas.DefectClicked -= oldVm.OnCanvasDefectClicked;
            BallCanvas.RequestRedraw -= OnRequestRedraw;
        }

        if (e.NewValue is SubstrateViewerViewModel vm)
        {
            vm.Canvas = BallCanvas;
            vm.PropertyChanged += Vm_PropertyChanged;
            BallCanvas.DefectClicked += vm.OnCanvasDefectClicked;
            BallCanvas.RequestRedraw += OnRequestRedraw;
        }
    }

    private void OnRequestRedraw()
    {
        if (DataContext is SubstrateViewerViewModel vm)
            vm.RequestRender();
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SubstrateViewerViewModel.SelectedDefect))
        {
            var vm = (SubstrateViewerViewModel)sender!;
            if (vm.SelectedDefect != null)
                DefectDataGrid.ScrollIntoView(vm.SelectedDefect);
        }
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is SubstrateViewerViewModel vm)
            vm.RequestRender();
    }

    private void DefectDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is SubstrateViewerViewModel vm && vm.SelectedDefect != null)
            DefectDataGrid.ScrollIntoView(vm.SelectedDefect);
    }

    // ── Interaction Mode Buttons ──────────────────────────────────────

    private void NormalModeBtn_Checked(object sender, RoutedEventArgs e)
    {
        if (ProbeModeBtn == null) return; // Guard: fires during InitializeComponent before ProbeModeBtn is assigned
        ProbeModeBtn.IsChecked = false;
        PreciseModeBtn.IsChecked = false;
        BallCanvas.Mode = CanvasInteractionMode.Normal;
        BallCanvas.Cursor = null;
        if (DataContext is SubstrateViewerViewModel vm)
            vm.RequestRender();
    }

    private void NormalModeBtn_Unchecked(object sender, RoutedEventArgs e)
    {
        if (ProbeModeBtn.IsChecked != true && PreciseModeBtn.IsChecked != true)
            NormalModeBtn.IsChecked = true;
    }

    private void ProbeModeBtn_Checked(object sender, RoutedEventArgs e)
    {
        NormalModeBtn.IsChecked = false;
        PreciseModeBtn.IsChecked = false;
        BallCanvas.Mode = CanvasInteractionMode.Probe;
        BallCanvas.Cursor = Cursors.Arrow;
        if (DataContext is SubstrateViewerViewModel vm)
            vm.RequestRender();
    }

    private void ProbeModeBtn_Unchecked(object sender, RoutedEventArgs e)
    {
        if (NormalModeBtn.IsChecked != true && PreciseModeBtn.IsChecked != true)
            ProbeModeBtn.IsChecked = true;
    }

    private void PreciseModeBtn_Checked(object sender, RoutedEventArgs e)
    {
        NormalModeBtn.IsChecked = false;
        ProbeModeBtn.IsChecked = false;
        BallCanvas.Mode = CanvasInteractionMode.PreciseMeasure;
        BallCanvas.Cursor = Cursors.Cross;
        if (DataContext is SubstrateViewerViewModel vm)
            vm.RequestRender();
    }

    private void PreciseModeBtn_Unchecked(object sender, RoutedEventArgs e)
    {
        if (NormalModeBtn.IsChecked != true && ProbeModeBtn.IsChecked != true)
            PreciseModeBtn.IsChecked = true;
    }

    // ── FIT Button ────────────────────────────────────────────────────

    private void FitButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SubstrateViewerViewModel vm)
            vm.FitView();
        NormalModeBtn.IsChecked = true; // Also switches mode via NormalModeBtn_Checked
    }

    // ── DataGrid Double-Click → Jump to Defect ────────────────────────

    private void DefectDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SubstrateViewerViewModel vm &&
            DefectDataGrid.SelectedItem is DefectBall defect)
            vm.JumpToDefect(defect);
    }
}
