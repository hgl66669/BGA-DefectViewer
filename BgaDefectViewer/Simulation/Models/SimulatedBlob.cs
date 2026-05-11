namespace BgaDefectViewer.Simulation.Models;

public struct SimulatedBlob
{
    public int MasterIndex;
    public double CenterX;
    public double CenterY;
    public double Diameter;
    public double Acircularity;        // Drives azimuthal brightness modulation (β)
    public double ShapeDeformation;    // Drives radial geometric perturbation (γ); 0 = perfect circle
    public double Score;
    public double Area;
    public double Perimeter;
    public double ShiftX;
    public double ShiftY;
    public byte Brightness;
    public bool IsPresent;
}
