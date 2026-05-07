namespace BgaDefectViewer.Models;

/// <summary>
/// New-format `.afa` token: `CorrectedData;<DieIndex>,<DieCol>,<DieRow>,<BallId>,<Flag>`
///
/// Recorded inside `INSPECTION=N` (N >= 2) to mark that a ball flagged as defective
/// in the previous round has been repaired. Flag = 1 means "repair confirmed".
/// </summary>
public sealed class CorrectedBall
{
    public int InspectionNumber { get; init; }
    public int DieIndex { get; init; }
    public string DieCol { get; init; } = "";
    public string DieRow { get; init; } = "";
    public int BallId { get; init; }
    public int Flag { get; init; }
}
