namespace BgaDefectViewer.Simulation.Models;

public class SimulationFrame
{
    public required SimulatedMaster[] Masters { get; init; }
    public required SimulatedBlob[] Blobs { get; init; }
    public required SimulationParams Params { get; init; }
    public required SimulationStats Stats { get; init; }
}
