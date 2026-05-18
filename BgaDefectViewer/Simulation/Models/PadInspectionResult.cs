namespace BgaDefectViewer.Simulation.Models;

/// <summary>One pad's inspection verdict + the raw signal feature used to
/// reach it. Kept as <c>struct</c> so a 1M-pad frame stays compact.</summary>
public struct PadInspectionResult
{
    public int MasterIndex;
    public DefectCode Code;
    public double WhiteFillRatio;  // fraction of binarized pixels above threshold inside the pad disk
}
