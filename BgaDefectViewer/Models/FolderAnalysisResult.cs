namespace BgaDefectViewer.Models;

/// <summary>
/// Result of analyzing a user-selected folder to determine
/// the AthleteSYS root, part number, and lot number.
/// </summary>
public class FolderAnalysisResult
{
    /// <summary>The resolved root path (parent of kbgadata/kbgaresults).</summary>
    public string RootPath { get; set; } = "";

    /// <summary>Auto-detected part number, or null if not determinable.</summary>
    public string? PartNumber { get; set; }

    /// <summary>Auto-detected lot number, or null if not determinable.</summary>
    public string? LotNumber { get; set; }
}
