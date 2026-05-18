namespace BgaDefectViewer.Simulation.Models;

public enum ViewMode { Grayscale, Binary }

/// <summary>
/// Stage 2 inputs: binarization threshold + result-marker visibility toggles.
/// <para>
/// <c>BinLevel</c> matches KBGA's <c>BINLEVEL[0]</c> (LED4Pad) — simple
/// threshold on 8-bit grayscale.
/// </para>
/// <para>
/// <c>MissingFillRatio</c> is the prototype's per-pad judge: pixels brighter
/// than <c>BinLevel</c> within the pad-disk are counted; if the ratio is
/// below this value the pad is reported as <see cref="DefectCode.Miss"/>.
/// </para>
/// </summary>
public record BinarizationParams
{
    public byte BinLevel { get; init; } = 38;
    public double MissingFillRatio { get; init; } = 0.10;
    public ViewMode ViewMode { get; init; } = ViewMode.Grayscale;
    public bool ShowDefectMarkers { get; init; } = true;
    public bool ShowMasterBalls { get; init; } = false;
}
