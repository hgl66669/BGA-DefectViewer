namespace BgaDefectViewer.Models;

/// <summary>
/// User-configurable parameters for overlap inspection simulation.
/// Matches KBGA recipe parameter structure.
/// All length units are millimeters unless noted otherwise.
/// </summary>
public class OverlapParams
{
    // Whether overlap inspection is enabled (KBGA Settings checkbox)
    public bool Enabled { get; set; } = true;

    // Device Area: total area including device balls and alignment marks [mm]
    public double DeviceAreaX { get; set; } = 90.0;
    public double DeviceAreaY { get; set; } = 90.0;

    // Camera FOV size [mm] (standard: 60mm square)
    public double FovSizeX { get; set; } = 60.0;
    public double FovSizeY { get; set; } = 60.0;

    // Overlapping length with adjacent FOV [mm]
    public double OverlapLengthX { get; set; } = 0.3;
    public double OverlapLengthY { get; set; } = 0.3;

    // Derived: Move distance = FOV size - Overlap length
    public double MoveDistX => FovSizeX - OverlapLengthX;
    public double MoveDistY => FovSizeY - OverlapLengthY;

    // Derived: FOV count = ceil((DeviceArea - Overlap) / (FOV - Overlap))
    public int FovCountX => MoveDistX > 0
        ? Math.Max(1, (int)Math.Ceiling((DeviceAreaX - OverlapLengthX) / MoveDistX))
        : 1;
    public int FovCountY => MoveDistY > 0
        ? Math.Max(1, (int)Math.Ceiling((DeviceAreaY - OverlapLengthY) / MoveDistY))
        : 1;

    // Boundary mask length [mm] (excluded zone at FOV edge)
    public double BoundaryMaskX { get; set; }
    public double BoundaryMaskY { get; set; }

    // Is staggered pattern (千鳥配置).
    // Only affects inspection algorithm / processing time (~+10%),
    // does NOT change FOV geometric arrangement.
    public bool IsStaggeredPattern { get; set; }

    // Ball duplication judgment allowance [pix]
    public double DuplicationAllowancePix { get; set; } = 10.0;

    // Alignment mark FOV positions (1-based grid coordinates).
    // Per PDF P3: when not yet registered, both default to (1, 1).
    public int Alignment1FovX { get; set; } = 1;
    public int Alignment1FovY { get; set; } = 1;
    public int Alignment2FovX { get; set; } = 1;
    public int Alignment2FovY { get; set; } = 1;
}
