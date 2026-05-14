using System.Windows;
using System.Windows.Controls;
using BgaDefectViewer.ViewModels;

namespace BgaDefectViewer.Views;

public partial class OverlapInspectionView : UserControl
{
    public OverlapInspectionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        BallCanvas.SizeChanged += OnCanvasSizeChanged;

        // Share the BallMapCanvas pan-translate with the FOV overlay so the
        // grid lines slide with the master balls during drag. RenderAll resets
        // PanTransform to (0,0), at which point both layers re-render at the
        // new anchor pan — no manual reset needed here.
        FovOverlay.RenderTransform = BallCanvas.PanTransform;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is OverlapInspectionViewModel oldVm)
        {
            BallCanvas.RequestRedraw -= OnRequestRedraw;
            oldVm.Canvas = null;
            oldVm.OverlayCanvas = null;
        }

        if (e.NewValue is OverlapInspectionViewModel vm)
        {
            vm.Canvas = BallCanvas;
            vm.OverlayCanvas = FovOverlay;
            BallCanvas.RequestRedraw += OnRequestRedraw;
        }
    }

    private void OnRequestRedraw()
    {
        if (DataContext is OverlapInspectionViewModel vm)
            vm.RequestRender();
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is OverlapInspectionViewModel vm)
            vm.RequestRender();
    }
}
