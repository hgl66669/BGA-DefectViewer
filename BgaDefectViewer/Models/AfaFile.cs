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

    public string EndTime { get; set; } = "";
    public double DurationSeconds { get; set; }
}
