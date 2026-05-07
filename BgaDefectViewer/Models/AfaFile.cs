namespace BgaDefectViewer.Models;

public class AfaFile
{
    public string StartTime { get; set; } = "";
    public string MapFile { get; set; } = "";
    public string Recipe { get; set; } = "";
    public string LotNo { get; set; } = "";
    public string WaferId { get; set; } = "";
    public string BallName { get; set; } = "";
    public double BallDiameter { get; set; }
    public int Balls { get; set; }
    public int TotalBalls { get; set; }

    public List<InspectionResult> Inspections { get; set; } = new();

    /// <summary>
    /// Repair markers from `CorrectedData;` lines (new-format only). Each entry
    /// references a previously-defective ball that has been successfully repaired.
    /// Empty for legacy `.afa` files.
    /// </summary>
    public List<CorrectedBall> CorrectedBalls { get; set; } = new();

    /// <summary>
    /// Substrate layout decoded from the `Mapfile;` token. Defaults to
    /// <see cref="SubstrateLayout.SingleUnit"/> when the token is absent or
    /// unparseable (covers all legacy files).
    /// </summary>
    public SubstrateLayout Layout { get; set; } = new SubstrateLayout.SingleUnit();

    public string EndTime { get; set; } = "";
    public double DurationSeconds { get; set; }
}
