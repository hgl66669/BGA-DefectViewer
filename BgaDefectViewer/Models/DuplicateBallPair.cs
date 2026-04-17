namespace BgaDefectViewer.Models;

/// <summary>
/// A pair of balls from different FOVs within the duplication
/// allowance distance, indicating the same physical ball is
/// captured in multiple FOVs.
/// </summary>
public class DuplicateBallPair
{
    public MasterBall BallA { get; set; }
    public MasterBall BallB { get; set; }
    public int FovA { get; set; }  // ScanIndex of first FOV
    public int FovB { get; set; }  // ScanIndex of second FOV
    public double Distance { get; set; }
}
