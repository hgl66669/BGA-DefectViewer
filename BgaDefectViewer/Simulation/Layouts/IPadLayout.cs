using System.Windows;
using BgaDefectViewer.Simulation.Models;

namespace BgaDefectViewer.Simulation.Layouts;

public interface IPadLayout
{
    LayoutType Type { get; }
    string DisplayName { get; }

    (double X, double Y) GetPosition(int row, int col, SimulationParams p);
    Rect GetBounds(SimulationParams p);
    IEnumerable<(int Row, int Col)> EnumerateVisible(Rect dataRect, SimulationParams p);
}
