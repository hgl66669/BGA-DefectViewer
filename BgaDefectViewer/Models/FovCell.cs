namespace BgaDefectViewer.Models;

/// <summary>
/// Represents one FOV cell in the grid with its physical position,
/// scan order, and the list of master balls captured within it.
/// </summary>
public class FovCell
{
    public int GridX { get; set; }      // 1-based column
    public int GridY { get; set; }      // 1-based row
    public int ScanIndex { get; set; }  // 1-based serpentine order
    public double CenterX { get; set; } // mm, in master ball coordinate system
    public double CenterY { get; set; } // mm
    public double HalfWidth { get; set; }
    public double HalfHeight { get; set; }

    public double Left => CenterX - HalfWidth;
    public double Right => CenterX + HalfWidth;
    public double Top => CenterY + HalfHeight;
    public double Bottom => CenterY - HalfHeight;

    public List<int> BallIds { get; set; } = new();
}
