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
        MouseLeave += OnMouseLeave;
    }

    /// <summary>Brightness (0..255) of the simulated pixel under the cursor;
    /// null when cursor is outside the canvas or no frame is loaded. KBGA UI
    /// shows the same "Value: nn" readout next to its grid toolbar.</summary>
    public static readonly DependencyProperty HoverValueProperty =
        DependencyProperty.Register(nameof(HoverValue), typeof(int?), typeof(SimulationCanvas),
            new PropertyMetadata(null));
    public int? HoverValue
    {
        get => (int?)GetValue(HoverValueProperty);
        private set => SetValue(HoverValueProperty, value);
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
    /// Each blob renders as a donut; pad/blob/noise passes layer in order.
    /// Blob writes use ADDITIVE blending — when two balls overlap their rim
    /// contributions sum (incoherent light addition), producing the bright
    /// cusp/lens band visible where adjacent balls touch in real frames.
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

        // ── Pad pass ── Dark disks at fixed Master grid positions. Drawn
        // before blobs (which sit on top) and after bg fill (direct overwrite).
        int padDarkness = p.PadEnabled ? (int)Math.Round(p.PadDepthUm * PadDarkenPerMicron) : 0;
        if (padDarkness > 0 && p.PadDiameter > 0)
        {
            var masters = _frame.Masters;
            double padSoftness = Math.Clamp(p.PadEdgeSoftness, 0.0, 1.0);
            double padCoreRatio = 1.0 - padSoftness;
            double padCenterDim = Math.Clamp(p.PadCenterDimming, 0.0, 1.0);
            double padTexAmount = Math.Max(0.0, p.PadTextureAmount);
            uint padTexSeed = (uint)p.Seed * 0xC2B2AE35u;
            foreach (var (row, col) in _layout.EnumerateVisible(dataRect, p))
            {
                int idx = row * p.Cols + col;
                DrawPad(pBack, stridePixels, w, h, masters[idx],
                        p.PadDiameter, padDarkness, padCoreRatio, padSoftness,
                        padCenterDim, padTexAmount, padTexSeed, bg);
            }
        }

        foreach (var (row, col) in _layout.EnumerateVisible(dataRect, p))
        {
            int idx = row * p.Cols + col;
            var blob = blobs[idx];
            if (!blob.IsPresent) continue;            // Missing pad → just background
            if (blob.Brightness <= bg) continue;       // No contrast → invisible

            DrawBlobDonut(pBack, stridePixels, w, h, blob, bg);
        }

        // ── Noise pass ── Signal-dependent Gaussian jitter sampled per
        // CAMERA pixel (not per screen pixel). Multiple screen pixels that
        // map to the same camera pixel share one noise sample — noise grain
        // is therefore the same physical size as a rendering pixel, invariant
        // under zoom, and is the only granularity Stage 2 binarization will
        // see. Per-pixel cost: incremental dx/dy add, one floor, one byte
        // table lookup (sigma + Gaussian), one mul-div.
        //   σ²(I) = readNoise² + I·shotCoef  (read floor + photon shot).
        int readNoise = p.SensorReadNoise;
        double shotCoef = p.SensorShotNoise;
        if ((readNoise > 0 || shotCoef > 0) && p.MmPerPixel > 0)
        {
            // Pre-compute σ per intensity — kills per-pixel sqrt.
            double readVar = (double)readNoise * readNoise;
            Span<double> sigmaLut = stackalloc double[256];
            for (int i = 0; i < 256; i++) sigmaLut[i] = Math.Sqrt(readVar + i * shotCoef);

            // Affine: data = origin + sx·dxStep (and same for y). Computed
            // once from two ScreenToData probes; inner loop only adds.
            double invMmPerPx = 1.0 / p.MmPerPixel;
            var (dx0, dy0) = _transform.ScreenToData(0.5, 0.5);
            var (dx1, _)   = _transform.ScreenToData(1.5, 0.5);
            var (_, dyR1)  = _transform.ScreenToData(0.5, 1.5);
            double dxStep = dx1 - dx0;
            double dyStep = dyR1 - dy0;

            uint seedHash = (uint)p.Seed * 2654435761u + 0xDEADBEEFu;
            uint* pUint = (uint*)pBack;
            for (int sy = 0; sy < h; sy++)
            {
                int camY = (int)Math.Floor((dy0 + sy * dyStep) * invMmPerPx);
                uint rowHash = ((uint)camY * 0x85EBCA6Bu) ^ seedHash;
                uint* rowPtr = pUint + sy * stridePixels;

                double dxHere = dx0;
                int prevCamX = int.MinValue;
                sbyte cellSample = 0;
                for (int sx = 0; sx < w; sx++)
                {
                    int camX = (int)Math.Floor(dxHere * invMmPerPx);
                    if (camX != prevCamX)
                    {
                        uint hh = (uint)camX * 0x9E3779B9u ^ rowHash;
                        hh ^= hh >> 16; hh *= 0x7FEB352Du;
                        cellSample = GaussianLUT[hh & 0xFF];
                        prevCamX = camX;
                    }
                    if (cellSample != 0)
                    {
                        int intensity = (int)(rowPtr[sx] & 0xFFu);
                        int noise = (int)(cellSample * sigmaLut[intensity] / 10.0);
                        if (noise != 0)
                        {
                            int v = intensity + noise;
                            if (v < 0) v = 0;
                            else if (v > 255) v = 255;
                            rowPtr[sx] = MakeGrayPixel((byte)v);
                        }
                    }
                    dxHere += dxStep;
                }
            }
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

    // Donut intensity profile — asymmetric, calibrated against KBGA reference
    // images of dome-lit BGA balls. The previous symmetric (peak 0.75, halfwidth
    // 0.30) profile left a central dark core ~55% of diameter and a thin bright
    // band. Real balls show a wide bright rim covering ~70% of diameter and only
    // a small dark spot at the apex specular shadow.
    private const double DonutPeakRadius = 0.55;
    private const double DonutInnerHalfwidth = 0.38;  // peak → dark core boundary
    private const double DonutOuterHalfwidth = 0.45;  // peak → background boundary

    // Pad rendering: depth → darkness offset. 20µm → 8 grey levels below bg.
    private const double PadDarkenPerMicron = 0.4;

    // Contact-pair effects (cusp specular + back-rim shadow). Both kick in
    // when centre-to-centre distance ≤ (r₁+r₂)·CuspContactMargin. With
    // collision resolution ON, neighbouring offset balls park at exactly
    // r₁+r₂ where each donut profile alone gives 0 at the contact tangent
    // (both normD = 1.0). The cusp specular fills that geometric gap with
    // a Gaussian highlight; the back-rim shadow dims the outer rim that
    // faces away from the neighbour (light source partially occluded).
    private const double CuspContactMargin = 1.15;
    private const double CuspSpecularPeak = 0.20;        // per side; gated by (1 − smooth) so it doesn't stack on donut peak
    private const double CuspSpecularInvSigmaSq2 = 50.0; // 1/(2σ²), σ ≈ 0.1 in normD units
    private const double ContactShadowMax = 0.25;        // max dim on far rim
    private const double ContactShadowRimStart = 0.70;   // shadow only for normD ≥ 0.70

    private struct ContactInfo
    {
        public double cx, cy;     // neighbour centre (mm)
        public double rMm;        // neighbour radius (mm)
        public double rOuter2;    // (rMm × 1.05)² for fast outside-bbox skip
        public double absLim;     // manhattan early-cull (rMm × 1.05)
        public double toNbX, toNbY; // unit vector self→nb
        public int amp;           // nb.Brightness − bg
    }

    // Pre-generated Gaussian samples scaled by 10× — sum of 12 uniforms gives
    // approximate N(0,1), then ×10 fits sbyte cleanly. Runtime noise uses
    // (lut[h] * sigma / 10) which costs one hash + one table lookup + one
    // integer mul-div per pixel. ~2-4× faster than computing Box-Muller.
    private static readonly sbyte[] GaussianLUT = BuildGaussianLUT();
    private static sbyte[] BuildGaussianLUT()
    {
        var lut = new sbyte[256];
        var rng = new Random(0x5A17C0DE);
        for (int i = 0; i < 256; i++)
        {
            double sum = 0;
            for (int j = 0; j < 12; j++) sum += rng.NextDouble() - 0.5;
            int v = (int)Math.Round(sum * 10);  // store at 10× scale
            if (v > 127) v = 127;
            else if (v < -127) v = -127;
            lut[i] = (sbyte)v;
        }
        return lut;
    }

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
            WriteAdditivePixel(pUint + py * stridePixels + px, v, bg);
            return;
        }

        // ── Contact detection ── Find ≤ 8 grid neighbours within touching
        // distance for cusp specular + back-rim shadow.
        Span<ContactInfo> contacts = stackalloc ContactInfo[8];
        int contactCount = 0;
        {
            int cols = _frame.Params.Cols;
            int rows = _frame.Params.Rows;
            int row = blob.MasterIndex / cols;
            int col = blob.MasterIndex % cols;
            double rSelfMax = radiusMm * (1.0 + perturbGamma);
            var allBlobs = _frame.Blobs;
            for (int dr = -1; dr <= 1 && contactCount < 8; dr++)
            {
                int nr = row + dr;
                if (nr < 0 || nr >= rows) continue;
                for (int dc = -1; dc <= 1 && contactCount < 8; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int nc = col + dc;
                    if (nc < 0 || nc >= cols) continue;
                    ref readonly var nb = ref allBlobs[nr * cols + nc];
                    if (!nb.IsPresent) continue;
                    if (nb.Brightness <= bg) continue;

                    double dxNb = nb.CenterX - cx;
                    double dyNb = nb.CenterY - cy;
                    double dNb2 = dxNb * dxNb + dyNb * dyNb;
                    double rNb = nb.Diameter / 2.0;
                    double rNbMax = rNb * (1.0 + nb.ShapeDeformation);
                    double thresh = (rSelfMax + rNbMax) * CuspContactMargin;
                    if (dNb2 > thresh * thresh) continue;
                    double dNb = Math.Sqrt(dNb2);
                    if (dNb < 1e-9) continue;  // degenerate co-centric — skip

                    ref var slot = ref contacts[contactCount];
                    slot.cx = nb.CenterX;
                    slot.cy = nb.CenterY;
                    slot.rMm = rNb;
                    slot.rOuter2 = (rNb * 1.05) * (rNb * 1.05);
                    slot.absLim = rNb * 1.05;
                    slot.toNbX = dxNb / dNb;
                    slot.toNbY = dyNb / dNb;
                    slot.amp = nb.Brightness - bg;
                    contactCount++;
                }
            }
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
                // Asymmetric donut profile (see DonutPeakRadius / Halfwidth consts).
                double dFromPeak = normD - DonutPeakRadius;
                double t = dFromPeak >= 0
                    ? 1.0 - dFromPeak / DonutOuterHalfwidth
                    : 1.0 + dFromPeak / DonutInnerHalfwidth;

                int val = bg;
                double smooth = 0;
                if (t > 0)
                {
                    if (t > 1) t = 1;
                    smooth = t * t * (3.0 - 2.0 * t);
                    val = bg + (int)(amp * smooth);

                    // Layer 2: azimuthal brightness modulation — dims rim sectors.
                    // High-acirc balls develop dark gaps that Stage 2 binarization
                    // will turn into open C-shapes.
                    if (perturbBeta > 0 && val > bg)
                    {
                        double sumAz = 0;
                        for (int k = 0; k < ShapeHarmonicCount; k++)
                            sumAz += azAmps[k] * Math.Cos((k + ShapeHarmonicMinMode) * theta + azPhases[k]);
                        double factor = 1.0 + perturbBeta * sumAz;
                        if (factor <= 0) val = bg;
                        else
                        {
                            if (factor > 1) factor = 1;
                            val = bg + (int)((val - bg) * factor);
                        }
                    }
                }

                // ── Contact effects ── Cusp specular fills the tangent gap
                // between near-touching balls; gated by (1 − smooth) so it
                // attenuates in donut peak region (prevents over-saturating
                // pixels that already get full donut brightness). Back-rim
                // shadow dims pixels whose surface normal faces away.
                if (contactCount > 0)
                {
                    double dirMag = normD * rLocal;
                    double invDirMag = dirMag > 1e-9 ? 1.0 / dirMag : 0;
                    double cuspMask = 1.0 - smooth;          // 1 at rim/edge, 0 at donut peak
                    double cuspBoost = 0;
                    double shadowMul = 1.0;
                    for (int n = 0; n < contactCount; n++)
                    {
                        ref var nb = ref contacts[n];
                        double nDx = cdx - nb.cx;
                        double nDy = cdy - nb.cy;
                        if (Math.Abs(nDx) > nb.absLim || Math.Abs(nDy) > nb.absLim) continue;
                        double nD2 = nDx * nDx + nDy * nDy;
                        if (nD2 > nb.rOuter2) continue;
                        double nNormD = Math.Sqrt(nD2) / nb.rMm;

                        // Cusp specular — peak Gaussian centred on (1.0, 1.0)
                        double devSelf = normD - 1.0;
                        double devNb = nNormD - 1.0;
                        if (devSelf >= -0.20 && devSelf <= 0.10
                            && devNb >= -0.20 && devNb <= 0.10)
                        {
                            double wCusp = Math.Exp(-(devSelf * devSelf + devNb * devNb) * CuspSpecularInvSigmaSq2);
                            cuspBoost += nb.amp * CuspSpecularPeak * wCusp * cuspMask;
                        }

                        // Back-rim shadow — pixel direction · toNb < 0 → away from neighbour
                        if (val > bg && normD >= ContactShadowRimStart && invDirMag > 0)
                        {
                            double dot = (ddx * nb.toNbX + ddy * nb.toNbY) * invDirMag;
                            if (dot < 0)
                            {
                                double rimWeight = (normD - ContactShadowRimStart) / (1.0 - ContactShadowRimStart);
                                if (rimWeight > 1) rimWeight = 1;
                                shadowMul *= 1.0 - (-dot) * ContactShadowMax * rimWeight;
                            }
                        }
                    }
                    if (shadowMul < 1.0 && val > bg)
                        val = bg + (int)((val - bg) * shadowMul);
                    if (cuspBoost > 0)
                    {
                        val += (int)cuspBoost;
                        if (val > 255) val = 255;
                    }
                }

                if (val <= bg) continue;
                WriteAdditivePixel(rowPtr + sx, val, bg);
            }
        }
    }

    /// <summary>Render a single pad as a soft-edged bowl with copper/PCB
    /// microstructure. Pad is fixed at Master position (does NOT move with
    /// blob offset). Direct overwrite — runs after bg fill, before blob pass.
    /// floor profile is a bowl: extra dimming at the geometric center fading
    /// to flat across the core, then smoothstep falloff over the outer
    /// softness band. Microstructure is a 2×2-block-aligned per-pixel hash
    /// scaled by padTextureAmount (matches the "patchy" look of real recess).</summary>
    private unsafe void DrawPad(byte* pBuffer, int stridePixels, int w, int h,
                                 in SimulatedMaster master, double padDiameter,
                                 int padDarkness, double padCoreRatio,
                                 double padSoftness, double padCenterDimming,
                                 double padTextureAmount, uint padTextureSeed,
                                 byte bg)
    {
        if (_transform == null || _frame == null) return;

        double cx = master.X;
        double cy = master.Y;
        double radiusMm = padDiameter / 2.0;
        double radiusPx = _transform.BallRadiusPixels(padDiameter);
        uint* pUint = (uint*)pBuffer;

        // Sub-pixel pad: collapse to single pixel at floor brightness.
        if (radiusPx < 1.0)
        {
            var (px, py) = _transform.DataToScreen(cx, cy);
            if ((uint)px >= (uint)w || (uint)py >= (uint)h) return;
            int floorV = bg - padDarkness;
            if (floorV < 0) floorV = 0;
            pUint[py * stridePixels + px] = MakeGrayPixel((byte)floorV);
            return;
        }

        var (sxMin0, syMax0) = _transform.DataToScreen(cx - radiusMm, cy - radiusMm);
        var (sxMax0, syMin0) = _transform.DataToScreen(cx + radiusMm, cy + radiusMm);
        int sxMin = Math.Max(0, sxMin0);
        int sxMax = Math.Min(w - 1, sxMax0);
        int syMin = Math.Max(0, syMin0);
        int syMax = Math.Min(h - 1, syMax0);
        if (sxMax < sxMin || syMax < syMin) return;

        double r2 = radiusMm * radiusMm;
        double mmPerPx = _frame.Params.MmPerPixel;
        double invMmPerPx = 1.0 / mmPerPx;

        // Texture σ ≈ amount × padDarkness. Scaled by /10 because GaussianLUT
        // samples are stored at 10× scale.
        double texSigma = padTextureAmount * padDarkness;
        uint padHash = (uint)master.Row * 0x9E3779B9u ^ (uint)master.Col * 0x85EBCA6Bu ^ padTextureSeed;

        for (int sy = syMin; sy <= syMax; sy++)
        {
            uint* rowPtr = pUint + sy * stridePixels;
            for (int sx = sxMin; sx <= sxMax; sx++)
            {
                var (dx, dy) = _transform.ScreenToData(sx + 0.5, sy + 0.5);
                // Snap to camera pixel-grid centre (same as blob pass).
                int camX = (int)Math.Floor(dx * invMmPerPx);
                int camY = (int)Math.Floor(dy * invMmPerPx);
                double cdx = (camX + 0.5) * mmPerPx;
                double cdy = (camY + 0.5) * mmPerPx;
                double ddx = cdx - cx;
                double ddy = cdy - cy;
                double d2 = ddx * ddx + ddy * ddy;
                if (d2 > r2) continue;

                double normD = Math.Sqrt(d2) / radiusMm;

                // Bowl profile: floor ≥ 1 across the core (1 + bowl dim at
                // center, falling smoothly to 1 at coreRatio), then smoothstep
                // down to 0 over the outer softness band.
                double floor;
                if (normD <= padCoreRatio)
                {
                    if (padCenterDimming > 0 && padCoreRatio > 1e-6)
                    {
                        double radialT = normD / padCoreRatio;       // 0 center → 1 edge of core
                        double bowl = 1.0 - radialT * radialT;       // quadratic bowl falloff
                        floor = 1.0 + padCenterDimming * bowl;
                    }
                    else
                    {
                        floor = 1.0;
                    }
                }
                else if (padSoftness > 1e-6)
                {
                    double t = (normD - padCoreRatio) / padSoftness;
                    if (t > 1) t = 1;
                    floor = 1.0 - t * t * (3.0 - 2.0 * t);
                }
                else
                {
                    continue;  // beyond core, no soft band → outside pad
                }

                int v = bg - (int)(padDarkness * floor);
                if (texSigma > 0)
                {
                    // Hash by CAMERA-pixel index — texture grain is the same
                    // physical size as a rendering pixel (invariant under zoom
                    // and matches noise pass granularity).
                    uint th = ((uint)camX * 0x6C8E9CF5u)
                            ^ ((uint)camY * 0x47A4D87Bu)
                            ^ padHash;
                    th ^= th >> 16; th *= 0x7FEB352Du;
                    v += (int)(GaussianLUT[th & 0xFF] * texSigma / 10.0);
                }
                if (v < 0) v = 0;
                else if (v > 255) v = 255;
                rowPtr[sx] = MakeGrayPixel((byte)v);
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

    /// <summary>Additive blend (saturating at 255). Treats existing brightness
    /// below background (pad recess) as 0 contribution — blob reflection
    /// overrides the pad floor. Two overlapping balls' rim contributions sum,
    /// producing a bright cusp/lens band wherever the donuts overlap.</summary>
    private static unsafe void WriteAdditivePixel(uint* pixel, int newVal, byte bg)
    {
        if (newVal <= bg) return;
        uint existing = *pixel;
        int existingV = (int)(existing & 0xFFu);
        int existingBoost = existingV - bg;
        if (existingBoost < 0) existingBoost = 0;
        int combined = bg + existingBoost + (newVal - bg);
        if (combined > 255) combined = 255;
        *pixel = MakeGrayPixel((byte)combined);
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
        var pos = e.GetPosition(this);
        if (_isPanning && _transform != null)
        {
            _transform.PanX += pos.X - _lastMousePos.X;
            _transform.PanY += pos.Y - _lastMousePos.Y;
            _lastMousePos = pos;
            _pixelBitmap = null;
            RenderAll();
        }
        UpdateHoverValue(pos);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e) => HoverValue = null;

    private unsafe void UpdateHoverValue(Point pos)
    {
        var bmp = _pixelBitmap;
        if (bmp == null) { HoverValue = null; return; }
        int x = (int)pos.X;
        int y = (int)pos.Y;
        if ((uint)x >= (uint)bmp.PixelWidth || (uint)y >= (uint)bmp.PixelHeight)
        {
            HoverValue = null;
            return;
        }
        // BGRA32, grayscale (B=G=R): read B byte. BackBuffer is readable outside Lock.
        byte* p = (byte*)bmp.BackBuffer + y * bmp.BackBufferStride + x * 4;
        HoverValue = p[0];
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
