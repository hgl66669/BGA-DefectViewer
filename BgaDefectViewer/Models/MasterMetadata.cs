namespace BgaDefectViewer.Models;

/// <summary>
/// Auxiliary master data parsed from the KBGA `.dat` sidecar of a master.
/// Holds the alignment fiducial positions registered on the real machine.
/// Units are millimeters in the device-stage coordinate system
/// (AlignmentCenterMm = (0, 0) by convention).
/// </summary>
public class MasterMetadata
{
    public (double X, double Y)? AlignmentPoint1Mm { get; set; }
    public (double X, double Y)? AlignmentPoint2Mm { get; set; }
    public (double X, double Y)? AlignmentCenterMm { get; set; }

    /// <summary>Physical substrate dimensions in mm: (X, Y, Z thickness).</summary>
    public (double X, double Y, double Z)? SubstrateSize { get; set; }

    /// <summary>Number of devices laid out on the substrate (N columns × M rows).</summary>
    public (int N, int M)? SubstrateDeviceCount { get; set; }

    /// <summary>Center-to-center pitch between adjacent devices in mm (X, Y).</summary>
    public (double X, double Y)? DevicePitch { get; set; }
}
