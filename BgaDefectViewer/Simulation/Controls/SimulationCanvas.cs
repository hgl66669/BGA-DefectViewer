using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;
using BgaDefectViewer.Simulation.Layouts;
using BgaDefectViewer.Simulation.Models;

namespace BgaDefectViewer.Simulation.Controls;

public class SimulationCanvas : FrameworkElement
{
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _backgroundVisual = new();
    private readonly DrawingVisual _ballVisual = new();
    private readonly DrawingVisual _hoverVisual = new();

    private WriteableBitmap? _pixelBitmap;
    private Size _lastBackgroundSize = Size.Empty;

    private SimulationFrame? _frame;
    private CoordinateTransform? _transform;
    private IPadLayout? _layout;

    private bool _isMiddleDragging;
    private Point _lastMousePos;

    // Pre-built frozen brushes — NEVER allocate per-ball at 1M scale
    private static readonly SolidColorBrush BgBrush = CreateFrozenBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly SolidColorBrush MasterBrush = CreateFrozenBrush(Color.FromRgb(0x70, 0x70, 0x70));
    private static readonly SolidColorBrush OkBrush = CreateFrozenBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
    private static readonly SolidColorBrush MissingBrush = CreateFrozenBrush(DefectTypes.GetCanvasColor(2)); // Cyan
    private static readonly SolidColorBrush ShiftBrush = CreateFrozenBrush(DefectTypes.GetCanvasColor(3));   // Yellow
    private static readonly Pen MissingRingPen = CreateFrozenPen(MissingBrush, 1.5);

    // BGRA32 little-endian: 0xAARRGGBB
    private const uint BgraOk = 0xFFC0C0C0u;
    private const uint BgraMissing = 0xFF00FFFFu;
    private const uint BgraShift = 0xFFFFFF00u;

    private const double LodRadiusThreshold = 3.0;

    // KBGA reference: max 500 balls per detection batch is a DETECTION constraint,
    // NOT a rendering one — we may render millions per frame.

    static SolidColorBrush CreateFrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    static Pen CreateFrozenPen(SolidColorBrush brush, double thickness)
    {
        var p = new Pen(brush, thickness);
        p.Freeze();
        return p;
    }

    public SimulationCanvas()
    {
        _visuals = new VisualCollection(this)
        {
            _backgroundVisual,
            _ballVisual,
            _hoverVisual,
        };
        ClipToBounds = true;
        Focusable = true;
        MouseWheel += OnMouseWheel;
        MouseDown += OnMouseDown;
        MouseUp += OnMouseUp;
        MouseMove += OnMouseMove;
    }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];

    public void SetFrame(SimulationFrame frame, bool resetView)
    {
        bool firstFrame = _frame == null;
        _frame = frame;
        _layout = LayoutRegistry.Get(frame.Params.Layout);

        var bounds = _layout.GetBounds(frame.Params);
        if (_transform == null || resetView || firstFrame)
        {
            _transform = new CoordinateTransform();
            _transform.SetBounds(bounds.X, bounds.X + bounds.Width, bounds.Y, bounds.Y + bounds.Height);
            if (ActualWidth > 0 && ActualHeight > 0)
                _transform.SetCanvasSize(ActualWidth, ActualHeight);
            _transform.ResetToFit();
        }
        else
        {
            _transform.SetBounds(bounds.X, bounds.X + bounds.Width, bounds.Y, bounds.Y + bounds.Height);
            if (ActualWidth > 0 && ActualHeight > 0)
                _transform.SetCanvasSize(ActualWidth, ActualHeight);
        }
        _pixelBitmap = null;
        RenderAll();
    }

    public void ResetView()
    {
        if (_transform == null) return;
        _transform.ResetToFit();
        _pixelBitmap = null;
        RenderAll();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _lastBackgroundSize = Size.Empty;
        _pixelBitmap = null;
        _transform?.SetCanvasSize(ActualWidth, ActualHeight);
        if (_frame != null) RenderAll();
    }

    private void RenderAll()
    {
        if (_frame == null || _transform == null || _layout == null) return;
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        RenderBackground();
        double radius = _transform.BallRadiusPixels(_frame.Params.MasterDiameter);
        if (radius >= LodRadiusThreshold) RenderAsCircles();
        else RenderAsPixels();
    }

    private void RenderBackground()
    {
        int w = (int)ActualWidth;
        int h = (int)ActualHeight;
        var sz = new Size(w, h);
        if (sz == _lastBackgroundSize) return;
        using var dc = _backgroundVisual.RenderOpen();
        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));
        _lastBackgroundSize = sz;
    }

    private unsafe void RenderAsPixels()
    {
        if (_frame == null || _transform == null || _layout == null) return;
        int w = (int)ActualWidth;
        int h = (int)ActualHeight;
        if (w <= 0 || h <= 0) return;

        if (_pixelBitmap == null || _pixelBitmap.PixelWidth != w || _pixelBitmap.PixelHeight != h)
        {
            _pixelBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            using var setupDc = _ballVisual.RenderOpen();
            setupDc.DrawImage(_pixelBitmap, new Rect(0, 0, w, h));
        }

        _pixelBitmap.Lock();
        var pBack = (byte*)_pixelBitmap.BackBuffer;
        int stride = _pixelBitmap.BackBufferStride;
        long bytes = (long)stride * h;
        // Fast clear via Span — uses vectorized memset internally
        new Span<byte>(pBack, checked((int)bytes)).Clear();

        var p = _frame.Params;
        var blobs = _frame.Blobs;
        var dataRect = ComputeVisibleDataRect(w, h);

        foreach (var (row, col) in _layout.EnumerateVisible(dataRect, p))
        {
            int idx = row * p.Cols + col;
            var blob = blobs[idx];

            uint color;
            if (!blob.IsPresent) color = BgraMissing;
            else if (blob.ShiftX != 0 || blob.ShiftY != 0) color = BgraShift;
            else color = BgraOk;

            var (px, py) = _transform.DataToScreen(blob.CenterX, blob.CenterY);
            if ((uint)px >= (uint)w || (uint)py >= (uint)h) continue;
            *(uint*)(pBack + py * stride + px * 4) = color;
        }

        _pixelBitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
        _pixelBitmap.Unlock();
    }

    private void RenderAsCircles()
    {
        if (_frame == null || _transform == null || _layout == null) return;
        _pixelBitmap = null;
        int w = (int)ActualWidth;
        int h = (int)ActualHeight;
        var p = _frame.Params;
        var blobs = _frame.Blobs;
        var masters = _frame.Masters;
        var dataRect = ComputeVisibleDataRect(w, h);

        using var dc = _ballVisual.RenderOpen();
        foreach (var (row, col) in _layout.EnumerateVisible(dataRect, p))
        {
            int idx = row * p.Cols + col;
            var master = masters[idx];
            var blob = blobs[idx];

            // Master dot (gray, smaller — peeks out beneath blob)
            var (mpx, mpy) = _transform.DataToScreen(master.X, master.Y);
            double masterR = Math.Max(2.0, _transform.BallRadiusPixels(master.Diameter) * 0.55);
            dc.DrawEllipse(MasterBrush, null, new Point(mpx, mpy), masterR, masterR);

            if (!blob.IsPresent)
            {
                // Missing: hollow cyan ring at master position
                double r = Math.Max(3.0, _transform.BallRadiusPixels(master.Diameter));
                dc.DrawEllipse(null, MissingRingPen, new Point(mpx, mpy), r, r);
            }
            else
            {
                var (px, py) = _transform.DataToScreen(blob.CenterX, blob.CenterY);
                double r = Math.Max(2.0, _transform.BallRadiusPixels(blob.Diameter));
                bool isShifted = blob.ShiftX != 0 || blob.ShiftY != 0;
                dc.DrawEllipse(isShifted ? ShiftBrush : OkBrush, null, new Point(px, py), r, r);
            }
        }
    }

    private Rect ComputeVisibleDataRect(int w, int h)
    {
        if (_transform == null) return Rect.Empty;
        var (dxMin, dyMax) = _transform.ScreenToData(0, 0);
        var (dxMax, dyMin) = _transform.ScreenToData(w, h);
        if (dxMax < dxMin) (dxMin, dxMax) = (dxMax, dxMin);
        if (dyMax < dyMin) (dyMin, dyMax) = (dyMax, dyMin);
        return new Rect(dxMin, dyMin, dxMax - dxMin, dyMax - dyMin);
    }

    // ── Interaction ───────────────────────────────────────────────────────

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_transform == null) return;
        Focus();
        var pos = e.GetPosition(this);
        var (dx, dy) = _transform.ScreenToData(pos.X, pos.Y);
        double factor = e.Delta > 0 ? 1.25 : 0.8;
        _transform.Zoom = Math.Max(0.1, Math.Min(2000, _transform.Zoom * factor));
        var (npx, npy) = _transform.DataToScreen(dx, dy);
        _transform.PanX += pos.X - npx;
        _transform.PanY += pos.Y - npy;
        _pixelBitmap = null;
        RenderAll();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _isMiddleDragging = true;
            _lastMousePos = e.GetPosition(this);
            CaptureMouse();
            Focus();
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _isMiddleDragging = false;
            ReleaseMouseCapture();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isMiddleDragging || _transform == null) return;
        var pos = e.GetPosition(this);
        _transform.PanX += pos.X - _lastMousePos.X;
        _transform.PanY += pos.Y - _lastMousePos.Y;
        _lastMousePos = pos;
        _pixelBitmap = null;
        RenderAll();
    }
}
