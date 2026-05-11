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
    public double PitchX { get; init; } = 0.4;
    public double PitchY { get; init; } = 0.4;
    public double StaggerOffsetX { get; init; } = 0.2;

    // Master
    public double MasterDiameter { get; init; } = 0.3;
    public double MasterDiameterStdDev { get; init; } = 0.0;

    // Blob
    public double BlobDiameterMean { get; init; } = 0.3;
    public double BlobDiameterStdDev { get; init; } = 0.005;
    public double BlobAcircularityMean { get; init; } = 1.0;
    public double BlobAcircularityStdDev { get; init; } = 0.05;
    public double BlobScoreMean { get; init; } = 0.85;
    public double BlobScoreStdDev { get; init; } = 0.05;
    public byte BlobBrightnessMean { get; init; } = 200;
    public byte BlobBrightnessStdDev { get; init; } = 10;
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

    // Calibration (mm per pixel; KBGA typical ~5μm/px)
    public double MmPerPixel { get; init; } = 0.005;

    public int TotalPads => Rows * Cols;
}
