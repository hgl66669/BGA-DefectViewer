using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Controls;

public enum CanvasInteractionMode { Normal, Probe, PreciseMeasure }

public class BallMapCanvas : FrameworkElement
{
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _backgroundVisual; // Static dark bg (never reallocated unless resize)
    private readonly DrawingVisual _bitmapVisual;
    private readonly DrawingVisual _defectVisual;
    private readonly DrawingVisual _selectionVisual;
    private readonly DrawingVisual _dimensionVisual;  // Dimension annotation lines (Probe/PreciseMeasure mode)
    private readonly DrawingVisual _measureVisual;   // Precise measurement result lines (persistent)
    private readonly DrawingVisual _stampVisual;      // Probe-mode text stamps
    private readonly DrawingVisual _hoverVisual;     // Hover highlight + tooltip (topmost)

    private WriteableBitmap? _okBitmap;
    private bool _pixelModeActive;
    private Size _lastBackgroundSize = Size.Empty;

    // Tracks pixels painted last render so we can erase only those (O(N_balls) not O(w*h))
    private readonly List<(int px, int py)> _lastBallPixels = new();

    private bool _isDragging;
    private bool _isMiddleDragging;
    private Point _lastMousePos;

    private MasterBall[]? _masterBalls;
    private List<DefectBall>? _defects;
    private CoordinateTransform? _transform;

    // Tracked so the selection can be re-drawn after pan/zoom
    private DefectBall? _selectedDefect;

    // Probe-mode stamps (max 10, FIFO)
    private readonly List<MasterBall> _probeStamps = new();

    // Precise measurement state
    private MasterBall? _preciseMeasureStart;
    private readonly List<(MasterBall Start, MasterBall End)> _measurements = new();

    public CanvasInteractionMode Mode { get; set; } = CanvasInteractionMode.Normal;

    public event Action<DefectBall>? DefectClicked;
    public event Action? BlankClicked;
    public event Action<MasterBall>? MasterBallProbed;
    public event Action? RequestRedraw;

    public BallMapCanvas()
    {
        _visuals = new VisualCollection(this);
        _backgroundVisual = new DrawingVisual();
        _bitmapVisual = new DrawingVisual();
        _defectVisual = new DrawingVisual();
        _selectionVisual = new DrawingVisual();
        _dimensionVisual = new DrawingVisual();
        _measureVisual = new DrawingVisual();
        _stampVisual = new DrawingVisual();
        _hoverVisual = new DrawingVisual();
        _visuals.Add(_backgroundVisual);
        _visuals.Add(_bitmapVisual);
        _visuals.Add(_defectVisual);
        _visuals.Add(_selectionVisual);
        _visuals.Add(_dimensionVisual);
        _visuals.Add(_measureVisual);
        _visuals.Add(_stampVisual);
        _visuals.Add(_hoverVisual);

        ClipToBounds = true;

        MouseWheel += OnMouseWheel;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseMove += OnMouseMove;
        MouseRightButtonDown += OnMouseRightButtonDown;
        MouseDown += OnMouseDown;
        MouseUp += OnMouseUp;
    }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _lastBackgroundSize = Size.Empty;
        _okBitmap = null;
        _lastBallPixels.Clear();

        if (_masterBalls != null && _defects != null && _transform != null)
            RenderAll(null, null, null);
    }

    public void SetData(MasterBall[] masters, List<DefectBall> defects, CoordinateTransform transform)
    {
        _masterBalls = masters;
        _defects = defects;
        _transform = transform;
    }

    public void RenderAll(MasterBall[]? masters, List<DefectBall>? defects, CoordinateTransform? transform)
    {
        if (masters != null) _masterBalls = masters;
        if (defects != null) _defects = defects;
        if (transform != null) _transform = transform;

        if (_masterBalls == null || _defects == null || _transform == null) return;

        RenderBackground();
        RenderOkBalls(_masterBalls, _defects, _transform);
        RenderDefectBalls(_defects, _transform);

        // Re-render selection at updated coordinates after every pan/zoom
        if (_selectedDefect != null)
            DrawSelectionVisual(_selectedDefect, _transform);

        RenderDimensionLines(_transform);
        RenderMeasurements(_transform);
        RenderStamps(_transform);

        // Clear hover visual and reset pending start when not in PreciseMeasure mode
        if (Mode != CanvasInteractionMode.PreciseMeasure)
        {
            _preciseMeasureStart = null;
            using var dc = _hoverVisual.RenderOpen();
        }
    }

    /// <summary>背景只在尺寸改變時重繪，其餘使用快取</summary>
    private void RenderBackground()
    {
        int w = (int)ActualWidth;
        int h = (int)ActualHeight;
        if (w <= 0 || h <= 0) return;
        var sz = new Size(w, h);
        if (sz == _lastBackgroundSize) return;

        var bg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        bg.Freeze();
        using var dc = _backgroundVisual.RenderOpen();
        dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));
        _lastBackgroundSize = sz;
    }

    // 球半徑 >= 此像素值時切換為圓形模式
    private const double LodRadiusThreshold = 3.0;

    private unsafe void RenderOkBalls(MasterBall[] masters, List<DefectBall> defects, CoordinateTransform transform)
    {
        double sampleRadius = masters.Length > 0
            ? transform.BallRadiusPixels(masters[0].Diameter)
            : 0;

        if (sampleRadius >= LodRadiusThreshold)
            RenderOkBallsAsCircles(masters, transform);
        else
            RenderOkBallsAsPixels(masters, transform);
    }

    /// <summary>低縮放：WriteableBitmap 單像素，所有 master 球都顯示為灰色像素。
    /// 效能關鍵：重用 Bitmap，只清除/塗寫球像素（O(N_balls)），不重填整個背景。</summary>
    private unsafe void RenderOkBallsAsPixels(MasterBall[] masters, CoordinateTransform transform)
    {
        int w = (int)ActualWidth;
        int h = (int)ActualHeight;
        if (w <= 0 || h <= 0) return;

        if (_okBitmap == null || _okBitmap.PixelWidth != w || _okBitmap.PixelHeight != h || !_pixelModeActive)
        {
            _okBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            _lastBallPixels.Clear();
            _pixelModeActive = true;
            using var setupDc = _bitmapVisual.RenderOpen();
            setupDc.DrawImage(_okBitmap, new Rect(0, 0, w, h));
        }

        _okBitmap.Lock();
        var pBackBuffer = (byte*)_okBitmap.BackBuffer;
        int stride = _okBitmap.BackBufferStride;

        // Erase previously rendered pixels (O(N_balls) not O(w*h))
        foreach (var (lpx, lpy) in _lastBallPixels)
            *((uint*)(pBackBuffer + lpy * stride + lpx * 4)) = 0u;
        _lastBallPixels.Clear();

        // BGRA32 little-endian: (A<<24)|(R<<16)|(G<<8)|B
        const uint grayPixel = 0xFFC0C0C0u; // opaque mid-gray

        // Draw ALL master balls (defect positions also get gray pixel; defect circles render on top)
        foreach (var ball in masters)
        {
            var (px, py) = transform.DataToScreen(ball.X, ball.Y);
            if (px < 0 || px >= w || py < 0 || py >= h) continue;

            *((uint*)(pBackBuffer + py * stride + px * 4)) = grayPixel;
            _lastBallPixels.Add((px, py));
        }

        _okBitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
        _okBitmap.Unlock();
    }

    /// <summary>高縮放：每個 master 球畫實心小灰圓（半徑為缺陷圓的 55%），
    /// 缺陷圓在上層疊加，非缺陷位置可清楚看到灰點。</summary>
    private void RenderOkBallsAsCircles(MasterBall[] masters, CoordinateTransform transform)
    {
        int w = (int)ActualWidth;
        int h = (int)ActualHeight;

        _okBitmap = null;
        _lastBallPixels.Clear();
        _pixelModeActive = false;

        var grayBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
        grayBrush.Freeze();

        using var dc = _bitmapVisual.RenderOpen();

        foreach (var ball in masters)
        {
            var (px, py) = transform.DataToScreen(ball.X, ball.Y);
            if (px < -50 || px > w + 50 || py < -50 || py > h + 50) continue;

            // Master dot: solid, slightly smaller than defect circle so it peeks out
            double r = Math.Max(2.0, transform.BallRadiusPixels(ball.Diameter) * 0.55);
            dc.DrawEllipse(grayBrush, null, new Point(px, py), r, r);
        }
    }

    private void RenderDefectBalls(List<DefectBall> defects, CoordinateTransform transform)
    {
        using var dc = _defectVisual.RenderOpen();
        foreach (var defect in defects)
        {
            var color = DefectTypes.GetCanvasColor(defect.DefectCode);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var (px, py) = transform.DataToScreen(defect.X, defect.Y);
            double radius = Math.Max(3, transform.BallRadiusPixels(defect.Diameter));
            dc.DrawEllipse(brush, null, new Point(px, py), radius, radius);
        }
    }

    public void HighlightBall(DefectBall? defect, CoordinateTransform? transform)
    {
        _selectedDefect = defect;
        DrawSelectionVisual(defect, transform ?? _transform);
    }

    private void DrawSelectionVisual(DefectBall? defect, CoordinateTransform? transform)
    {
        if (transform == null) return;

        using var dc = _selectionVisual.RenderOpen();
        if (defect == null) return;

        // Outer highlight ring: blue stroke
        var (px, py) = transform.DataToScreen(defect.X, defect.Y);
        double innerR = Math.Max(4, transform.BallRadiusPixels(defect.Diameter));
        double ringR = innerR + 3;

        var ringPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7)), 2.0);
        ringPen.Freeze();
        dc.DrawEllipse(null, ringPen, new Point(px, py), ringR, ringR);
    }

    // ── Dimension Lines (Probe mode) ────────────────────────────────

    private void RenderDimensionLines(CoordinateTransform transform)
    {
        using var dc = _dimensionVisual.RenderOpen();
        if (Mode != CanvasInteractionMode.Probe && Mode != CanvasInteractionMode.PreciseMeasure)
            return; // Empty context clears the visual

        double xSpan = transform.MaxX - transform.MinX;
        double ySpan = transform.MaxY - transform.MinY;
        if (xSpan <= 0 && ySpan <= 0) return;

        // Screen positions of outermost bumps
        var (leftPx, topPx) = transform.DataToScreen(transform.MinX, transform.MaxY);
        var (rightPx, bottomPx) = transform.DataToScreen(transform.MaxX, transform.MinY);

        const double lineOffset = 30;   // px from bump edge to dimension line
        const double capHalfLen = 6;    // half-length of T-mark end caps
        const double textGap = 5;       // gap between line and label

        var pen = new Pen(Brushes.Green, 1.5);
        pen.Freeze();
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // ── Vertical dimension line (Y, left side) ──
        if (ySpan > 0)
        {
            double vx = leftPx - lineOffset;
            dc.DrawLine(pen, new Point(vx, topPx), new Point(vx, bottomPx));
            dc.DrawLine(pen, new Point(vx - capHalfLen, topPx), new Point(vx + capHalfLen, topPx));
            dc.DrawLine(pen, new Point(vx - capHalfLen, bottomPx), new Point(vx + capHalfLen, bottomPx));

            var yFt = new FormattedText(
                $"Y:{ySpan:F3}mm",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 11, Brushes.Green, dpi);
            double ytx = vx - yFt.Width - textGap;
            double yty = (topPx + bottomPx) / 2.0 - yFt.Height / 2.0;
            dc.DrawText(yFt, new Point(ytx, yty));
        }

        // ── Horizontal dimension line (X, bottom side) ──
        if (xSpan > 0)
        {
            double hy = bottomPx + lineOffset;
            dc.DrawLine(pen, new Point(leftPx, hy), new Point(rightPx, hy));
            dc.DrawLine(pen, new Point(leftPx, hy - capHalfLen), new Point(leftPx, hy + capHalfLen));
            dc.DrawLine(pen, new Point(rightPx, hy - capHalfLen), new Point(rightPx, hy + capHalfLen));

            var xFt = new FormattedText(
                $"X:{xSpan:F3}mm",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 11, Brushes.Green, dpi);
            double xtx = (leftPx + rightPx) / 2.0 - xFt.Width / 2.0;
            double xty = hy + textGap;
            dc.DrawText(xFt, new Point(xtx, xty));
        }
    }

    // ── Precise Measurement ─────────────────────────────────────────

    private void RenderMeasurements(CoordinateTransform transform)
    {
        using var dc = _measureVisual.RenderOpen();
        if (_measurements.Count == 0) return;

        var linePen = new Pen(Brushes.Cyan, 1.5);
        linePen.Freeze();
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var bgBrush = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20));
        bgBrush.Freeze();

        foreach (var (start, end) in _measurements)
        {
            var (sx, sy) = transform.DataToScreen(start.X, start.Y);
            var (ex, ey) = transform.DataToScreen(end.X, end.Y);
            var sp = new Point(sx, sy);
            var ep = new Point(ex, ey);

            // Line
            dc.DrawLine(linePen, sp, ep);
            // End-point dots
            dc.DrawEllipse(Brushes.Cyan, null, sp, 4, 4);
            dc.DrawEllipse(Brushes.Cyan, null, ep, 4, 4);

            // Distance label at midpoint
            double dist = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
            var ft = new FormattedText(
                $"{dist:F3}mm",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 11, Brushes.White, dpi);
            double mx = (sx + ex) / 2.0;
            double my = (sy + ey) / 2.0;
            dc.DrawRectangle(bgBrush, null, new Rect(mx - 2, my - ft.Height - 4, ft.Width + 4, ft.Height + 4));
            dc.DrawText(ft, new Point(mx, my - ft.Height - 2));
        }
    }

    public void ClearMeasurements()
    {
        _measurements.Clear();
        _preciseMeasureStart = null;
        using var dc = _measureVisual.RenderOpen();
    }

    private void RenderHoverVisual(Point mousePos)
    {
        using var dc = _hoverVisual.RenderOpen();
        if (Mode != CanvasInteractionMode.PreciseMeasure || _transform == null) return;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var bgBrush = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20));
        bgBrush.Freeze();

        // Draw start-point marker if already selected
        if (_preciseMeasureStart.HasValue)
        {
            var (spx, spy) = _transform.DataToScreen(_preciseMeasureStart.Value.X, _preciseMeasureStart.Value.Y);
            dc.DrawEllipse(Brushes.Cyan, null, new Point(spx, spy), 5, 5);
        }

        // Find nearest master ball and draw magnified highlight
        var hovered = HitTestMaster(mousePos, 30);
        if (hovered.HasValue)
        {
            var (hx, hy) = _transform.DataToScreen(hovered.Value.X, hovered.Value.Y);
            double r = Math.Max(8, _transform.BallRadiusPixels(hovered.Value.Diameter) * 2);
            var highlightPen = new Pen(Brushes.White, 2.0);
            highlightPen.Freeze();
            var fillBrush = new SolidColorBrush(Color.FromArgb(80, 0, 255, 255));
            fillBrush.Freeze();
            dc.DrawEllipse(fillBrush, highlightPen, new Point(hx, hy), r, r);
        }

        // Tooltip text near cursor
        string tooltip = _preciseMeasureStart.HasValue
            ? "請右鍵點擊選擇測量結束點"
            : "請左鍵點擊選擇測量起始點";
        var ft = new FormattedText(
            tooltip,
            CultureInfo.GetCultureInfo("zh-TW"), FlowDirection.LeftToRight,
            new Typeface("Microsoft JhengHei"), 12, Brushes.White, dpi);
        double tx = mousePos.X + 15;
        double ty = mousePos.Y + 15;
        // Keep tooltip within canvas bounds
        if (tx + ft.Width + 4 > ActualWidth) tx = mousePos.X - ft.Width - 15;
        if (ty + ft.Height + 4 > ActualHeight) ty = mousePos.Y - ft.Height - 15;
        dc.DrawRectangle(bgBrush, null, new Rect(tx - 2, ty - 2, ft.Width + 4, ft.Height + 4));
        dc.DrawText(ft, new Point(tx, ty));
    }

    // ── Probe Stamps ──────────────────────────────────────────────────

    public void AddProbeStamp(MasterBall ball)
    {
        if (_probeStamps.Count >= 10)
            _probeStamps.RemoveAt(0); // FIFO: remove oldest
        _probeStamps.Add(ball);
        if (_transform != null)
            RenderStamps(_transform); // Partial redraw: stamps layer only
    }

    public void ClearProbeStamps()
    {
        _probeStamps.Clear();
        using var dc = _stampVisual.RenderOpen(); // Empty context clears the visual
    }

    private void RenderStamps(CoordinateTransform transform)
    {
        using var dc = _stampVisual.RenderOpen();
        if (_probeStamps.Count == 0) return;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var bgBrush = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20));
        bgBrush.Freeze();

        foreach (var ball in _probeStamps)
        {
            var (px, py) = transform.DataToScreen(ball.X, ball.Y);
            string text = $"X:{ball.X:F3}\nY:{ball.Y:F3}\nDia:{ball.Diameter:F6}";

            var ft = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Consolas"),
                10,
                Brushes.White,
                dpi);

            double tx = px + 8;
            double ty = py - ft.Height - 6;
            dc.DrawRectangle(bgBrush, null, new Rect(tx - 2, ty - 2, ft.Width + 4, ft.Height + 4));
            dc.DrawText(ft, new Point(tx, ty));
            // Yellow marker dot at the probed ball
            dc.DrawEllipse(Brushes.Yellow, null, new Point(px, py), 3, 3);
        }
    }

    // ── Hit Testing ───────────────────────────────────────────────────

    public DefectBall? HitTestDefect(Point screenPoint, double threshold = 10)
    {
        if (_defects == null || _transform == null) return null;
        DefectBall? closest = null;
        double minDist = threshold;
        foreach (var d in _defects)
        {
            var (px, py) = _transform.DataToScreen(d.X, d.Y);
            double dist = Math.Sqrt(Math.Pow(screenPoint.X - px, 2) + Math.Pow(screenPoint.Y - py, 2));
            if (dist < minDist)
            {
                minDist = dist;
                closest = d;
            }
        }
        return closest;
    }

    public MasterBall? HitTestMaster(Point screenPoint, double threshold = 10)
    {
        if (_masterBalls == null || _transform == null) return null;
        MasterBall? closest = null;
        double minDist = threshold;
        bool found = false;
        foreach (var m in _masterBalls)
        {
            var (px, py) = _transform.DataToScreen(m.X, m.Y);
            double dist = Math.Sqrt(Math.Pow(screenPoint.X - px, 2) + Math.Pow(screenPoint.Y - py, 2));
            if (dist < minDist) { minDist = dist; closest = m; found = true; }
        }
        return found ? closest : null;
    }

    // ── Mouse Events ──────────────────────────────────────────────────

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_transform == null) return;
        var mousePos = e.GetPosition(this);
        double zoomFactor = e.Delta > 0 ? 1.4 : 1 / 1.4;
        double oldZoom = _transform.Zoom;
        double newZoom = Math.Max(0.1, Math.Min(50, oldZoom * zoomFactor));

        _transform.PanX = mousePos.X - (mousePos.X - _transform.PanX) * newZoom / oldZoom;
        _transform.PanY = mousePos.Y - (mousePos.Y - _transform.PanY) * newZoom / oldZoom;
        _transform.Zoom = newZoom;

        RequestRedraw?.Invoke();
        e.Handled = true;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (Mode == CanvasInteractionMode.PreciseMeasure)
        {
            if (e.ClickCount == 2)
            {
                // Double-click: clear all measurements
                ClearMeasurements();
                if (_transform != null) RenderMeasurements(_transform);
                RenderHoverVisual(pos);
            }
            else
            {
                // Single-click: select start point
                var master = HitTestMaster(pos, 30);
                if (master.HasValue)
                {
                    _preciseMeasureStart = master.Value;
                    RenderHoverVisual(pos);
                }
            }
            e.Handled = true;
            return;
        }

        if (Mode == CanvasInteractionMode.Probe)
        {
            var master = HitTestMaster(pos);
            if (master.HasValue)
            {
                MasterBallProbed?.Invoke(master.Value);
                AddProbeStamp(master.Value);
            }
            return; // No drag in Probe mode
        }

        // Normal mode
        var hit = HitTestDefect(pos);
        if (hit != null)
        {
            DefectClicked?.Invoke(hit);
            return;
        }

        BlankClicked?.Invoke(); // Notify view to deselect
        _isDragging = true;
        _lastMousePos = pos;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        // Middle-button drag: pan in any mode
        if (_isMiddleDragging && _transform != null)
        {
            _transform.PanX += pos.X - _lastMousePos.X;
            _transform.PanY += pos.Y - _lastMousePos.Y;
            _lastMousePos = pos;
            RequestRedraw?.Invoke();
            return;
        }

        if (Mode == CanvasInteractionMode.PreciseMeasure)
        {
            RenderHoverVisual(pos);
            return;
        }

        if (!_isDragging || _transform == null) return;
        _transform.PanX += pos.X - _lastMousePos.X;
        _transform.PanY += pos.Y - _lastMousePos.Y;
        _lastMousePos = pos;
        RequestRedraw?.Invoke();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ReleaseMouseCapture();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _isMiddleDragging = true;
            _lastMousePos = e.GetPosition(this);
            CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _isMiddleDragging)
        {
            _isMiddleDragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Mode == CanvasInteractionMode.PreciseMeasure)
        {
            if (_preciseMeasureStart.HasValue && _transform != null)
            {
                var pos = e.GetPosition(this);
                var endBall = HitTestMaster(pos, 30);
                if (endBall.HasValue)
                {
                    if (_measurements.Count >= 8)
                        _measurements.RemoveAt(0); // FIFO
                    _measurements.Add((_preciseMeasureStart.Value, endBall.Value));
                    _preciseMeasureStart = null;
                    RenderMeasurements(_transform);
                    RenderHoverVisual(pos);
                }
            }
            e.Handled = true;
            return;
        }

        if (Mode == CanvasInteractionMode.Probe && _probeStamps.Count > 0)
        {
            ClearProbeStamps();
            e.Handled = true;
        }
    }
}
