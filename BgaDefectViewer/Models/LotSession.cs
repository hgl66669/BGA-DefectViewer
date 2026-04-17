namespace BgaDefectViewer.Models;

public class LotSession
{
    public string LotName { get; set; } = "";
    public int SubstrateCount { get; set; }
    public List<SummaryRow> Rows { get; set; } = new();
    public LotSummary Summary { get; set; } = new();
}
