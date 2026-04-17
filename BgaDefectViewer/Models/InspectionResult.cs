namespace BgaDefectViewer.Models;

public class InspectionResult
{
    public int InspectionNumber { get; set; }   // 1, 2, 3...
    public int DieIndex { get; set; }
    public string DieCol { get; set; } = "";    // "1C"
    public string DieRow { get; set; } = "";    // "1R"
    public int WorstCode { get; set; }
    public string WorstName { get; set; } = "";
    public List<DefectBall> Defects { get; set; } = new();
}
