namespace BgaDefectViewer.Models;

public class DefectBall
{
    public int BallId { get; set; }       // -1 for Extra balls
    public int DefectCode { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Diameter { get; set; }
    public int Unknown { get; set; }

    public string DefectName => DefectTypes.GetName(DefectCode);
    public bool IsExtra => BallId == -1;
    public string DisplayX => X.ToString("F3");
    public string DisplayY => Y.ToString("F3");
    public string DisplayDia => Diameter.ToString("F6");
}
