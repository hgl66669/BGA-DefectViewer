using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BgaDefectViewer.Helpers;
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

    private bool _isPanning;
    private Point _lastMousePos;

    // Pre-built frozen background brush. Pixel writes use raw BGRA32; no per-ball brush allocation.
    private static readonly SolidColorBrush BgBrush = CreateFrozenBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

    // KBGA reference: max 500 balls per detection batch is a DETECTION constraint,
    // NOT a rendering one — we may render millions per frame.

    static SolidColorBrush CreateFrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
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
        Cursor = System.Windows.Input.Cursors.Hand;
        MouseWheel += OnMouseWheel;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
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
        // Always pixel-level rasterize (donut model). No vector LOD path —
        // we want the playground to look like the actual KBGA camera frame.
        RenderAsPixelImage();
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

    /// <summary>
    /// Pixel-level rasterization simulating a real KBGA camera frame.
    /// Each blob renders as a donut: bright rim near r·0.75 falling off to background.
    /// Models the specular highlight pattern of LED-lit spherical solder balls.
    /// </summary>
    private unsafe void RenderAsPixelImage()
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

        var p = _frame.Params;
        var blobs = _frame.Blobs;
        byte bg = p.BackgroundBrightness;
        uint bgPixel = MakeGrayPixel(bg);

        _pixelBitmap.Lock();
        byte* pBack = (byte*)_pixelBitmap.BackBuffer;
        int stride = _pixelBitmap.BackBufferStride;
        int stridePixels = stride / 4;
        long pixelCount = (long)stridePixels * h;

        // Uniform background fill (vectorized SIMD memset via Span<uint>.Fill)
        new Span<uint>(pBack, checked((int)pixelCount)).Fill(bgPixel);

        var dataRect = ComputeVisibleDataRect(w, h);

        foreach (var (row, col) in _layout.EnumerateVisible(dataRect, p))
        {
            int idx = row * p.Cols + col;
            var blob = blobs[idx];
            if (!blob.IsPresent) continue;            // Missing pad → just background
            if (blob.Brightness <= bg) continue;       // No contrast → invisible

            DrawBlobDonut(pBack, stridePixels, w, h, blob, bg);
        }

        _pixelBitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
        _pixelBitmap.Unlock();
    }

    // Two-layer shape model — both effects stacked, independent harmonics.
    //
    // 1) AZIMUTHAL brightness modulation (driven by Acircularity, β):
    //    Real BGA balls stay geometrically round even when defective. KBGA's
    //    ACIRCULARITY = P²/(4πA) inflates mainly because UNEVEN RIM REFLECTION
    //    (ball tilt, apex damage, co-planarity errors) makes some sectors dim,
    //    drop below the binarization threshold, and disappear — leaving a
    //    C-shape arc with exploded perimeter.
    //      factor(θ) = clamp(1 + β · Σ aₖ cos(kθ + φₖ), 0, 1)
    //      intensity = bg + (donut(d) − bg) · factor(θ)
    //      β = clamp((acirc − 1.0) · 0.4, 0, 1.0)
    //
    // 2) RADIAL geometric deformation (driven by ShapeDeformation, γ):
    //    Manufacturing tolerance and reflow surface-tension asymmetry give real
    //    balls a small physical out-of-round component on top of the reflection
    //    asymmetry — kept small by default (γ≈0.02 = ±2% radial).
    //      r(θ) = r₀ · (1 + γ · Σ bₖ cos(kθ + ψₖ))
    //      γ = blob.ShapeDeformation directly (already an amplitude ratio)
    //
    // Each blob uses 5 cosine modes (k=2..6) with deterministic per-blob amps
    // and phases derived from independent hashes of (MasterIndex, k) so radial
    // and azimuthal asymmetries don't artificially correlate.
    private const int ShapeHarmonicCount = 5;
    private const int ShapeHarmonicMinMode = 2; // skip k=0 (DC) and k=1 (translation)
    private const double PerturbSlope = 0.4;
    private const double PerturbMax = 1.0;
    private const double PerturbAcircBaseline = 1.0;
    private const double RadialFloorRatio = 0.3; // r(θ) ≥ 0.3·r₀ for sanity

    private unsafe void DrawBlobDonut(byte* pBuffer, int stridePixels, int w, int h,
                                       in SimulatedBlob blob, byte bg)
    {
        if (_transform == null || _frame == null) return;

        double cx = blob.CenterX;
        double cy = blob.CenterY;
        double radiusMm = blob.Diameter / 2.0;
        double radiusPx = _transform.BallRadiusPixels(blob.Diameter);
        byte peak = blob.Brightness;
        int amp = peak - bg;
        uint* pUint = (uint*)pBuffer;

        double mmPerPx = _frame.Params.MmPerPixel;
        double invMmPerPx = 1.0 / mmPerPx;

        // Azimuthal (brightness) coefficient β — from acircularity
        double perturbBeta = (blob.Acircularity - PerturbAcircBaseline) * PerturbSlope;
        if (perturbBeta < 0) perturbBeta = 0;
        if (perturbBeta > PerturbMax) perturbBeta = PerturbMax;

        // Radial (geometric) coefficient γ — from shape deformation
        double perturbGamma = blob.ShapeDeformation;
        if (perturbGamma < 0) perturbGamma = 0;
        if (perturbGamma > 0.5) perturbGamma = 0.5;

        // Independent harmonic sets — same index, different hash domains
        Span<double> azAmps = stackalloc double[ShapeHarmonicCount];
        Span<double> azPhases = stackalloc double[ShapeHarmonicCount];
        Span<double> radAmps = stackalloc double[ShapeHarmonicCount];
        Span<double> radPhases = stackalloc double[ShapeHarmonicCount];
        if (perturbBeta > 0) BuildHarmonics(blob.MasterIndex, hashOffset: 0, azAmps, azPhases);
        if (perturbGamma > 0) BuildHarmonics(blob.MasterIndex, hashOffset: 100, radAmps, radPhases);
        bool needTheta = perturbBeta > 0 || perturbGamma > 0;

        // Sub-pixel ball (zoomed far out): single pixel with donut's area-weighted mean.
        if (radiusPx < 1.0)
        {
            var (px, py) = _transform.DataToScreen(cx, cy);
            if ((uint)px >= (uint)w || (uint)py >= (uint)h) return;
            int v = bg + (int)(amp * 0.40);
            WriteMaxPixel(pUint + py * stridePixels + px, v);
            return;
        }

        // Screen-space bounding box. Radial perturbation can grow the ball
        // up to (1+γ)·r₀; azimuthal modulation never changes geometry, so
        // the bbox only depends on γ.
        double bboxMargin = 1.1 * (1.0 + perturbGamma);
        var (sxMin0, syMax0) = _transform.DataToScreen(cx - radiusMm * bboxMargin, cy - radiusMm * bboxMargin);
        var (sxMax0, syMin0) = _transform.DataToScreen(cx + radiusMm * bboxMargin, cy + radiusMm * bboxMargin);
        int sxMin = Math.Max(0, sxMin0);
        int sxMax = Math.Min(w - 1, sxMax0);
        int syMin = Math.Max(0, syMin0);
        int syMax = Math.Min(h - 1, syMax0);
        if (sxMax < sxMin || syMax < syMin) return;

        double r = radiusMm;
        // Conservative outer cull when all radial harmonics align in phase
        double rMaxFactor = 1.1 * (1.0 + perturbGamma);
        double rMax2 = r * r * rMaxFactor * rMaxFactor;

        for (int sy = syMin; sy <= syMax; sy++)
        {
            uint* rowPtr = pUint + sy * stridePixels;
            for (int sx = sxMin; sx <= sxMax; sx++)
            {
                var (dx, dy) = _transform.ScreenToData(sx + 0.5, sy + 0.5);
                // Snap to camera-pixel-grid center
                double cdx = (Math.Floor(dx * invMmPerPx) + 0.5) * mmPerPx;
                double cdy = (Math.Floor(dy * invMmPerPx) + 0.5) * mmPerPx;
                double ddx = cdx - cx;
                double ddy = cdy - cy;
                double d2 = ddx * ddx + ddy * ddy;
                if (d2 > rMax2) continue;

                double theta = 0;
                if (needTheta) theta = Math.Atan2(ddy, ddx);

                // Layer 1: radial perturbation → local radius
                double rLocal = r;
                if (perturbGamma > 0)
                {
                    double sumRad = 0;
                    for (int k = 0; k < ShapeHarmonicCount; k++)
                        sumRad += radAmps[k] * Math.Cos((k + ShapeHarmonicMinMode) * theta + radPhases[k]);
                    rLocal = r * (1.0 + perturbGamma * sumRad);
                    if (rLocal < r * RadialFloorRatio) rLocal = r * RadialFloorRatio;
                }
                double rOuter2 = rLocal * rLocal * 1.21;
                if (d2 > rOuter2) continue;

                double normD = Math.Sqrt(d2) / rLocal;
                // Donut profile: bright rim at normD = 0.75, halfwidth 0.30
                double t = 1.0 - Math.Abs(normD - 0.75) / 0.30;
                if (t <= 0) continue;
                if (t > 1) t = 1;
                double smooth = t * t * (3.0 - 2.0 * t);
                int val = bg + (int)(amp * smooth);
                if (val <= bg) continue;

                // Layer 2: azimuthal brightness modulation — dims rim sectors.
                // High-acirc balls develop dark gaps that Stage 2 binarization
                // will turn into open C-shapes.
                if (perturbBeta > 0)
                {
                    double sumAz = 0;
                    for (int k = 0; k < ShapeHarmonicCount; k++)
                        sumAz += azAmps[k] * Math.Cos((k + ShapeHarmonicMinMode) * theta + azPhases[k]);
                    double factor = 1.0 + perturbBeta * sumAz;
                    if (factor <= 0) continue;
                    if (factor > 1) factor = 1;
                    val = bg + (int)((val - bg) * factor);
                    if (val <= bg) continue;
                }

                WriteMaxPixel(rowPtr + sx, val);
            }
        }
    }

    /// <summary>Populate harmonic amp/phase arrays from deterministic per-blob
    /// hashes; normalize amps so their sum is 1 (keeps perturbation amplitude
    /// bounded regardless of mode count).</summary>
    private static void BuildHarmonics(int blobIndex, int hashOffset,
                                       Span<double> amps, Span<double> phases)
    {
        double sum = 0;
        for (int k = 0; k < amps.Length; k++)
        {
            amps[k] = Hash01(blobIndex, hashOffset + k * 2);
            phases[k] = Hash01(blobIndex, hashOffset + k * 2 + 1) * (2 * Math.PI);
            sum += amps[k];
        }
        if (sum > 1e-9)
        {
            double inv = 1.0 / sum;
            for (int k = 0; k < amps.Length; k++) amps[k] *= inv;
        }
    }

    /// <summary>Stateless deterministic 0..1 hash from (idx, k). Used to pick
    /// per-blob harmonic amplitudes/phases without bloating SimulatedBlob.</summary>
    private static double Hash01(int idx, int k)
    {
        uint h = (uint)idx * 2654435761u ^ (uint)k * 0x9E3779B9u;
        h ^= h >> 16;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        return (h & 0xFFFFFFu) / (double)0x1000000u;
    }

    private static unsafe void WriteMaxPixel(uint* pixel, int newVal)
    {
        if (newVal <= 0) return;
        if (newVal > 255) newVal = 255;
        uint existing = *pixel;
        byte existingV = (byte)(existing & 0xFF);
        if (newVal > existingV)
            *pixel = MakeGrayPixel((byte)newVal);
    }

    private static uint MakeGrayPixel(byte v)
        => 0xFF000000u | ((uint)v << 16) | ((uint)v << 8) | v;

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

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        StartPan(e.GetPosition(this));
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndPan();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
            StartPan(e.GetPosition(this));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
            EndPan();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning || _transform == null) return;
        var pos = e.GetPosition(this);
        _transform.PanX += pos.X - _lastMousePos.X;
        _transform.PanY += pos.Y - _lastMousePos.Y;
        _lastMousePos = pos;
        _pixelBitmap = null;
        RenderAll();
    }

    private void StartPan(Point pos)
    {
        _isPanning = true;
        _lastMousePos = pos;
        CaptureMouse();
        Focus();
        Cursor = System.Windows.Input.Cursors.SizeAll;
    }

    private void EndPan()
    {
        _isPanning = false;
        ReleaseMouseCapture();
        Cursor = System.Windows.Input.Cursors.Hand;
    }
}
