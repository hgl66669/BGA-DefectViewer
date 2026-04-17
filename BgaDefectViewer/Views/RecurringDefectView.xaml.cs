using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BgaDefectViewer.Controls;
using BgaDefectViewer.ViewModels;

namespace BgaDefectViewer.Views;

public partial class RecurringDefectView : UserControl
{
    public RecurringDefectView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        BallCanvas.SizeChanged += OnCanvasSizeChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is RecurringDefectViewModel oldVm)
        {
            BallCanvas.RequestRedraw -= OnRequestRedraw;
            oldVm.Canvas = null;
        }

        if (e.NewValue is RecurringDefectViewModel vm)
        {
            vm.Canvas = BallCanvas;
            BallCanvas.RequestRedraw += OnRequestRedraw;
        }
    }

    private void OnRequestRedraw()
    {
        if (DataContext is RecurringDefectViewModel vm)
            vm.RequestRenderCurrentSelection();
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is RecurringDefectViewModel vm)
            vm.RequestRenderCurrentSelection();
    }

    // ── Die grid click → select die ───────────────────────────────────────

    private void DieGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not RecurringDefectViewModel vm) return;

        var hit = (e.OriginalSource as FrameworkElement)?.DataContext;
        if (hit is RecurringDieCell cell)
            vm.OnDieCellClicked(cell);
    }

    // ── FIT button ────────────────────────────────────────────────────────

    private void FitBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecurringDefectViewModel vm)
            vm.FitView();
    }

    // ── Filter radio buttons ──────────────────────────────────────────────

    private void FilterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RecurringDefectViewModel vm) return;
        if (sender is RadioButton rb && rb.Tag is string tag && int.TryParse(tag, out int threshold))
            vm.FilterMinCount = threshold;
    }

    // ── Viewport size change (GridSplitter drag) ──────────────────────────

    private void BallMapViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is RecurringDefectViewModel vm)
            vm.RequestRenderCurrentSelection();
    }
}
