namespace BgaDefectViewer.Models;

/// <summary>
/// One physical substrate folder (new format = `kbgaresults/{Part}/{14-digit TS}/`).
/// Source-of-truth for substrate enumeration when the rolling `.summary.csv` may
/// lag behind real production (operator just finished an inspection but the row
/// hasn't been appended yet).
/// </summary>
public sealed record SubstrateFolderInfo(
    string FolderName,            // "20260506163921" (or legacy substrate name)
    DateTime? FolderTimestamp,    // parsed from FolderName when it's 14 digits, else null
    string SubstrateId,           // "20260506163921-2" (matches summary.csv Name column)
    string DisplayId,             // "2"  (filename suffix)
    string AfaPath,
    string MapPath,
    string? LotNoFromAfa);        // peeked from .afa LotNo; — null if unread, "" if empty
