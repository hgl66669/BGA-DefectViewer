namespace BgaDefectViewer.Models;

/// <summary>
/// Camera lens type. From 2200294リペア工程.pdf "Inspection camera calibration":
///   Normal view: 6.66 μm/pixel × 14192×9440 sensor → 94.52 × 62.87 mm
///   Enlarged:    7.728 μm/pixel × 14192×9440 sensor → 109.66 × 72.95 mm
/// </summary>
public enum CameraType
{
    Normal = 0,
    Enlarged = 1,
    Custom = 2,
}

/// <summary>
/// User-configurable parameters for overlap inspection simulation.
/// Matches KBGA recipe parameter structure.
/// All length units are millimeters unless noted otherwise.
///
/// Every inspection position is three concentric rectangles:
///   Camera raw  — actual image captured by the camera (3:2 landscape)
///   FOV         — the logical inspection area set by the user (square)
///   Effective   — FOV minus the Boundary mask border (true ball-check area)
/// </summary>
public class OverlapParams
{
    // Whether overlap inspection is enabled (KBGA Settings checkbox)
    public bool Enabled { get; set; } = true;

    // Camera raw image dimensions (mm). Defaults to Normal lens.
    public CameraType CameraType { get; set; } = CameraType.Normal;
    public double CameraRawX { get; set; } = 94.52;
    public double CameraRawY { get; set; } = 62.87;

    // Display layer toggles (UI only — do not affect geometry)
    public bool ShowCameraRawLayer { get; set; }        // default off
    public bool ShowFovLayer { get; set; } = true;
    public bool ShowEffectiveLayer { get; set; } = true;

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
