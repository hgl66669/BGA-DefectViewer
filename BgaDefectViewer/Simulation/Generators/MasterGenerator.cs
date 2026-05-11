using BgaDefectViewer.Simulation.Layouts;
using BgaDefectViewer.Simulation.Models;

namespace BgaDefectViewer.Simulation.Generators;

public static class MasterGenerator
{
    public static SimulatedMaster[] Generate(SimulationParams p)
    {
        var layout = LayoutRegistry.Get(p.Layout);
        var masters = new SimulatedMaster[p.TotalPads];

        Parallel.For(0, p.Rows, row =>
        {
            var rng = new Random(p.Seed ^ row ^ 0x1234);
            int baseIdx = row * p.Cols;
            for (int col = 0; col < p.Cols; col++)
            {
                var (x, y) = layout.GetPosition(row, col, p);
                double diameter = p.MasterDiameterStdDev > 0
                    ? Distributions.NextGaussianClamped(rng, p.MasterDiameter, p.MasterDiameterStdDev,
                        p.MasterDiameter * 0.5, p.MasterDiameter * 1.5)
                    : p.MasterDiameter;
                masters[baseIdx + col] = new SimulatedMaster
                {
                    Row = row,
                    Col = col,
                    X = x,
                    Y = y,
                    Diameter = diameter
                };
            }
        });
        return masters;
    }
}
