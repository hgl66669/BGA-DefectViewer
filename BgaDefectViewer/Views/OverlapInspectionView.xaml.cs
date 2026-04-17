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
