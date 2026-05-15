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

    /// <summary>
    /// 合併批 session 中，此 row 來源的 Lot#。一般批為 null（與
    /// <c>MainViewModel.SelectedLotNumber</c> 一致）。鏡像 <see cref="SubstrateMap.SourceLotId"/>，
    /// 用來消除合併批中相同 SubstrateId（如 Leg1-10 vs Leg2-10）的歧義。
    /// </summary>
    public string? SourceLotId { get; set; }
}
