using System.Windows.Media;

namespace BgaDefectViewer.Models;

public class DefectTypeInfo
{
    public int Code { get; set; }
    public string Name { get; set; } = "";
    public Color CanvasColor { get; set; }
    public Color GridColor { get; set; }
}
