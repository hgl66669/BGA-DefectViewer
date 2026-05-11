using BgaDefectViewer.Simulation.Models;

namespace BgaDefectViewer.Simulation.Layouts;

public static class LayoutRegistry
{
    public static IReadOnlyList<IPadLayout> All { get; } = new IPadLayout[]
    {
        new StandardMatrixLayout(),
        new StaggeredLayout(),
    };

    public static IPadLayout Get(LayoutType type)
        => All.FirstOrDefault(l => l.Type == type) ?? All[0];
}
