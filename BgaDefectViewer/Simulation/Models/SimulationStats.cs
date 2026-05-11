namespace BgaDefectViewer.Simulation.Models;

public record SimulationStats(
    int TotalPads,
    int PresentCount,
    int MissingCount,
    int ShiftedCount,
    long GenMs);
