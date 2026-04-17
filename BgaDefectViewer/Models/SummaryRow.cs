using BgaDefectViewer.Helpers;

namespace BgaDefectViewer.Models;

public class SummaryRow
{
    public string Name { get; set; } = "";
    public int OK { get; set; }
    public int Miss { get; set; }
    public int Shift { get; set; }
    public int SD { get; set; }
    public int LD { get; set; }
    public int ETC { get; set; }
    public int Bridge { get; set; }
    public int Extra { get; set; }
    public int EO { get; set; }
    public int NGDie { get; set; }
    public int GDie { get; set; }
    public double PPM { get; set; }
    public string Judge { get; set; } = "";
    public int Stage { get; set; }
    public double YieldPercent { get; set; }
    public string DateTime { get; set; } = "";

    public string SubstrateId => FileLocator.ExtractSubstrateId(Name);
    public int RowIndex { get; set; }
}
