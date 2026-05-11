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
