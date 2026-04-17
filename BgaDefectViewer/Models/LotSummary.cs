namespace BgaDefectViewer.Models;

public class LotSummaryLine
{
    public string Label { get; set; } = "";
    public string OK { get; set; } = "";
    public string Miss { get; set; } = "";
    public string Shift { get; set; } = "";
    public string SD { get; set; } = "";
    public string LD { get; set; } = "";
    public string ETC { get; set; } = "";
    public string Bridge { get; set; } = "";
    public string Extra { get; set; } = "";
    public string EO { get; set; } = "";
    public string NGDie { get; set; } = "";
    public string GDie { get; set; } = "";
    public string PPM { get; set; } = "";
    public string Judge { get; set; } = "";
    public string Stage { get; set; } = "";
    public string YieldPercent { get; set; } = "";
    public string DateTime { get; set; } = "";
}

public class LotSummary
{
    public List<LotSummaryLine> Lines { get; set; } = new();
    /// <summary>true = app calculated; false = read directly from CSV</summary>
    public bool IsCalculated { get; set; }
}
