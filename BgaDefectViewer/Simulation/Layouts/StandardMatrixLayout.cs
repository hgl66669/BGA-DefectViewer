using System.Windows;
using BgaDefectViewer.Simulation.Models;

namespace BgaDefectViewer.Simulation.Layouts;

public class StandardMatrixLayout : IPadLayout
{
    public LayoutType Type => LayoutType.StandardMatrix;
    public string DisplayName => "Standard Matrix";

    public (double X, double Y) GetPosition(int row, int col, SimulationParams p)
        => (col * p.PitchX, row * p.PitchY);

    public Rect GetBounds(SimulationParams p)
    {
        double pad = Math.Max(p.MasterDiameter, p.BlobDiameterMean);
        double w = (p.Cols - 1) * p.PitchX + 2 * pad;
        double h = (p.Rows - 1) * p.PitchY + 2 * pad;
        return new Rect(-pad, -pad, w, h);
    }

    public IEnumerable<(int Row, int Col)> EnumerateVisible(Rect dataRect, SimulationParams p)
    {
        int colMin = Math.Max(0, (int)Math.Floor(dataRect.X / p.PitchX));
        int colMax = Math.Min(p.Cols - 1, (int)Math.Ceiling((dataRect.X + dataRect.Width) / p.PitchX));
        int rowMin = Math.Max(0, (int)Math.Floor(dataRect.Y / p.PitchY));
        int rowMax = Math.Min(p.Rows - 1, (int)Math.Ceiling((dataRect.Y + dataRect.Height) / p.PitchY));

        for (int r = rowMin; r <= rowMax; r++)
            for (int c = colMin; c <= colMax; c++)
                yield return (r, c);
    }
}
