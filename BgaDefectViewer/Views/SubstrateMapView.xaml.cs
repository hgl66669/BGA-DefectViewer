using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BgaDefectViewer.Models;
using BgaDefectViewer.ViewModels;

namespace BgaDefectViewer.Views;

public partial class SubstrateMapView : UserControl
{
    // ── Transform state ──────────────────────────────────────────────────────
    private readonly ScaleTransform     _dieScale = new(1, 1);
    private readonly TranslateTransform _dieTrans = new(0, 0);

    // ── Drag state ───────────────────────────────────────────────────────────
    private bool  _isTrackingDrag;
    private bool  _isDragging;
    private Point _dragStart;
    private Point _lastDragPos;
    private const double DragThreshold = 4.0;

    public SubstrateMapView()
    {
        InitializeComponent();

        var tg = new TransformGroup();
        tg.Children.Add(_dieScale);
        tg.Children.Add(_dieTrans);
        DieGridItemsControl.RenderTransform = tg;

        DataContextChanged += (s, e) =>
        {
            if (e.OldValue is SubstrateMapViewModel oldVm)
                oldVm.PropertyChanged -= OnVmPropertyChanged;
            if (e.NewValue is SubstrateMapViewModel vm)
                vm.PropertyChanged += OnVmPropertyChanged;
        };
    }

    private SubstrateMapViewModel? ViewModel => DataContext as SubstrateMapViewModel;

    // ── Auto-fit when die grid is rebuilt ────────────────────────────────────

    private void OnVmPropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SubstrateMapViewModel.CurrentDieGrid))
            Dispatcher.BeginInvoke(FitDieGrid);
    }

    // ── ListBox events ───────────────────────────────────────────────────────

    private void SubstrateListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is SubstrateMap map)
            ViewModel?.OnSubstrateSelected(map);
    }

    private void SubstrateListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SubstrateListBox.SelectedItem is SubstrateMap map)
            ViewModel?.OnSubstrateDoubleClick(map);
    }

    // ── Die cell click: select (blue border highlight) ───────────────────────

    private void DieGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep != null)
        {
            if (dep is FrameworkElement fe && fe.DataContext is DieCell cell)
            {
                ViewModel?.OnDieCellClicked(cell);
                return;
            }
            dep = VisualTreeHelper.GetParent(dep);
        }
    }

    // ── Die cell double-click: open Substrate Viewer ─────────────────────────

    private void DieGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep != null)
        {
            if (dep is FrameworkElement fe && fe.DataContext is DieCell cell)
            {
                var map = ViewModel?.SelectedSubstrateMap;
                if (map != null) ViewModel?.OnDieDoubleClick(map, cell);
                return;
            }
            dep = VisualTreeHelper.GetParent(dep);
        }
        // Fallback: open the whole substrate if no specific die cell found
        var fallback = ViewModel?.SelectedSubstrateMap;
        if (fallback != null) ViewModel?.OnSubstrateDoubleClick(fallback);
    }

    // ── Mouse-wheel zoom (around cursor) ─────────────────────────────────────

    private void DieMapViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor    = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        var    pos       = e.GetPosition(DieMapViewport);
        double newScale  = Math.Clamp(_dieScale.ScaleX * factor, 0.1, 10.0);
        double actual    = newScale / _dieScale.ScaleX;
        _dieTrans.X      = pos.X - (pos.X - _dieTrans.X) * actual;
        _dieTrans.Y      = pos.Y - (pos.Y - _dieTrans.Y) * actual;
        _dieScale.ScaleX = _dieScale.ScaleY = newScale;
        e.Handled = true;
    }

    // ── Left-drag pan (capture mouse only after drag threshold exceeded,
    //    so normal click/double-click still fire on DieGridItemsControl) ──────

    private void DieMapViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isTrackingDrag = true;
        _isDragging = false;
        _dragStart = _lastDragPos = e.GetPosition(DieMapViewport);
    }

    private void DieMapViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isTrackingDrag || e.LeftButton != MouseButtonState.Pressed)
        {
            _isTrackingDrag = false;
            return;
        }

        var pos = e.GetPosition(DieMapViewport);

        if (!_isDragging && (pos - _dragStart).Length > DragThreshold)
        {
            _isDragging = true;
            DieMapViewport.CaptureMouse();
        }

        if (_isDragging)
        {
            _dieTrans.X += pos.X - _lastDragPos.X;
            _dieTrans.Y += pos.Y - _lastDragPos.Y;
        }

        _lastDragPos = pos;
    }

    private void DieMapViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging) DieMapViewport.ReleaseMouseCapture();
        _isTrackingDrag = _isDragging = false;
    }

    // ── FIT button ───────────────────────────────────────────────────────────

    private void FitBtn_Click(object sender, RoutedEventArgs e) => FitDieGrid();

    // ── Container resize → re-fit ────────────────────────────────────────────

    private void DieMapViewport_SizeChanged(object sender, SizeChangedEventArgs e) => FitDieGrid();

    // ── Compute and apply fit transform ──────────────────────────────────────

    private void FitDieGrid()
    {
        var vm = DataContext as SubstrateMapViewModel;
        if (vm == null) return;

        int totalCells = vm.CurrentDieGrid.Count;
        int cols       = vm.GridColumns;
        if (cols <= 0 || totalCells == 0) return;

        int    rows   = (totalCells + cols - 1) / cols;
        double gridW  = cols * 36.0;
        double gridH  = rows * 36.0;
        double cw     = DieMapViewport.ActualWidth;
        double ch     = DieMapViewport.ActualHeight;
        if (cw <= 0 || ch <= 0) return;

        const double margin = 10;
        double scale = Math.Clamp(
            Math.Min((cw - 2 * margin) / gridW, (ch - 2 * margin) / gridH),
            0.1, 10.0);

        _dieScale.ScaleX = _dieScale.ScaleY = scale;
        _dieTrans.X = (cw - gridW * scale) / 2;
        _dieTrans.Y = (ch - gridH * scale) / 2;
    }
}
