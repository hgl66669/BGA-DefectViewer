namespace BgaDefectViewer.Simulation.Models;

public enum LayoutType { StandardMatrix, Staggered }
public enum SimulationMode { AllPresent, RandomOffset, RandomMissing }
public enum QuantityMode { Probability, AbsoluteCount }

public record SimulationParams
{
    // Grid
    public LayoutType Layout { get; init; } = LayoutType.StandardMatrix;
    public int Rows { get; init; } = 10;
    public int Cols { get; init; } = 10;
    public double PitchX { get; init; } = 0.12;
    public double PitchY { get; init; } = 0.12;
    public double StaggerOffsetX { get; init; } = 0.06;

    // Master
    public double MasterDiameter { get; init; } = 0.08;
    public double MasterDiameterStdDev { get; init; } = 0.0;

    // Pad (copper recess) — physically fixed at Master positions. Under KBGA's
    // blue ring light the recessed copper floor reflects almost nothing, so
    // pads appear as faint dark disks slightly below background. Pad position
    // is FIXED — Blob offsets do NOT move the pad it sits on.
    //   Floor brightness ≈ Background − PadDepthUm · 0.4 (linear model).
    public bool PadEnabled { get; init; } = true;
    public double PadDiameter { get; init; } = 0.090;       // typically Master + 10 µm
    public int PadDepthUm { get; init; } = 20;              // physical recess depth
    public double PadEdgeSoftness { get; init; } = 0.15;    // 0=sharp disk, ~0.15=soft anti-alias band

    // Bowl-shape darkening: how much darker the geometric center is vs the
    // rest of the core. Models the fact that the recess floor reflects even
    // less near its deepest point under near-coaxial blue light. 0 = flat
    // disk; 0.3 = subtle bowl (default); 0.6 = pronounced "deep well".
    public double PadCenterDimming { get; init; } = 0.3;

    // Pad inner microstructure amplitude (fraction of pad darkness). Drawn
    // as a 2×2-block-aligned hash so neighbouring pixels correlate (matches
    // the "patchy" texture of copper/PCB recess in real frames). 0 = flat;
    // 0.4 = typical (default); 1.0 = strong mottling.
    public double PadTextureAmount { get; init; } = 0.4;

    // Two-component camera sensor noise model (8-bit units):
    //   σ(I)² = SensorReadNoise² + I × SensorShotNoise
    // SensorReadNoise is the dark-current/read-out floor (intensity-independent).
    // SensorShotNoise is the photon shot-noise coefficient (σ² grows linearly
    // with brightness). With defaults (2, 0.05), σ ranges from ≈ 2 in pad/bg
    // up to ≈ 4 on the bright blob rim — matching real KBGA grain. Both are
    // deterministic from (sx, sy, Seed), so the noise pattern is reproducible.
    public byte SensorReadNoise { get; init; } = 2;
    public double SensorShotNoise { get; init; } = 0.05;

    // Blob
    public double BlobDiameterMean { get; init; } = 0.08;
    public double BlobDiameterStdDev { get; init; } = 0.0;

    // KBGA's ACIRCULARITY = raw P²/(4πA). 1.0 = perfect circle; real
    // healthy balls measure 1.1–1.3 mostly from pixel-rasterization noise
    // on the perimeter measurement (perfect geometry would yield exactly 1.0).
    // KBGA's OK range (per recipe screenshot) is 0.700 ≤ acirc ≤ 3.000.
    // Drives the AZIMUTHAL BRIGHTNESS MODULATION — physically models uneven
    // rim reflection that fragments into C-shape under Stage 2 binarization.
    public double BlobAcircularityMean { get; init; } = 1.2;
    public double BlobAcircularityStdDev { get; init; } = 0.1;

    // RADIAL GEOMETRIC DEFORMATION amplitude γ — independent of acircularity.
    // Models the (usually small) physical out-of-round shape variation from
    // manufacturing tolerance, reflow surface-tension asymmetry, etc.
    // 0 = perfect circle; 0.02 ≈ ±2% radial (typical); 0.10+ = visible lumpy.
    public double BlobShapeDeformationMean { get; init; } = 0.02;
    public double BlobShapeDeformationStdDev { get; init; } = 0.01;

    public double BlobScoreMean { get; init; } = 1.0;
    public double BlobScoreStdDev { get; init; } = 1.0;
    public byte BlobBrightnessMean { get; init; } = 240;
    public byte BlobBrightnessStdDev { get; init; } = 0;
    public byte BackgroundBrightness { get; init; } = 30;

    // Mode
    public SimulationMode Mode { get; init; } = SimulationMode.AllPresent;

    // RandomOffset
    public QuantityMode OffsetQuantityMode { get; init; } = QuantityMode.Probability;
    public double OffsetProbability { get; init; } = 0.02;
    public int OffsetCount { get; init; } = 100;
    public double OffsetMinMm { get; init; } = 0.05;
    public double OffsetMaxMm { get; init; } = 0.15;

    // Collision resolution: when ON, a post-pass pushes any offset ball that
    // would penetrate a neighbor back to (r₁ + r₂ + gap) distance. Effective
    // radius uses (D/2)·(1+γ) so radial ShapeDeformation is accounted for.
    // Real BGA balls are solid and never overlap — they roll until they touch.
    public bool EnableCollision { get; init; } = true;

    // Per-pair contact gap (mm). Final separation = r₁+r₂ + gap.
    // gap is drawn uniformly from [Mean − Variance, Mean + Variance] using a
    // deterministic per-pair hash, then clamped to [−0.005, 0.050]. Negative
    // mean ⇒ tighter pack with light surface-tension compression; positive
    // mean ⇒ small physical gap (poor wetting, reflow incomplete).
    public double CollisionGapMean { get; init; } = 0.0;
    public double CollisionGapVariance { get; init; } = 0.005;  // ±5µm typical scatter

    // RandomMissing
    public QuantityMode MissingQuantityMode { get; init; } = QuantityMode.Probability;
    public double MissingProbability { get; init; } = 0.02;
    public int MissingCount { get; init; } = 100;

    // Reproducibility
    public int Seed { get; init; } = 42;

    // Calibration (mm per pixel; KBGA standard lens spec 6.66μm/px)
    public double MmPerPixel { get; init; } = 0.00666;

    public int TotalPads => Rows * Cols;
}
