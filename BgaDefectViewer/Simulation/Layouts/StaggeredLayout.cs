using System.Windows;
using BgaDefectViewer.Simulation.Models;

namespace BgaDefectViewer.Simulation.Layouts;

public class StaggeredLayout : IPadLayout
{
    public LayoutType Type => LayoutType.Staggered;
    public string DisplayName => "Staggered (千鳥)";

    public (double X, double Y) GetPosition(int row, int col, SimulationParams p)
    {
        double offsetX = (row % 2 == 1) ? p.StaggerOffsetX : 0.0;
        return (col * p.PitchX + offsetX, row * p.PitchY);
    }

    public Rect GetBounds(SimulationParams p)
    {
        double pad = Math.Max(p.MasterDiameter, p.BlobDiameterMean);
        double extraX = p.Rows > 1 ? p.StaggerOffsetX : 0;
        double w = (p.Cols - 1) * p.PitchX + extraX + 2 * pad;
        double h = (p.Rows - 1) * p.PitchY + 2 * pad;
        return new Rect(-pad, -pad, w, h);
    }

    public IEnumerable<(int Row, int Col)> EnumerateVisible(Rect dataRect, SimulationParams p)
    {
        int rowMin = Math.Max(0, (int)Math.Floor(dataRect.Y / p.PitchY));
        int rowMax = Math.Min(p.Rows - 1, (int)Math.Ceiling((dataRect.Y + dataRect.Height) / p.PitchY));

        for (int r = rowMin; r <= rowMax; r++)
        {
            double rowOffsetX = (r % 2 == 1) ? p.StaggerOffsetX : 0.0;
            int colMin = Math.Max(0, (int)Math.Floor((dataRect.X - rowOffsetX) / p.PitchX));
            int colMax = Math.Min(p.Cols - 1, (int)Math.Ceiling((dataRect.X + dataRect.Width - rowOffsetX) / p.PitchX));
            for (int c = colMin; c <= colMax; c++)
                yield return (r, c);
        }
    }
}
