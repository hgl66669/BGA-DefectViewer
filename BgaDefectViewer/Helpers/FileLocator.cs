using System.Globalization;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Helpers;

public class FileCheckResult
{
    public string ExpectedPath { get; set; } = "";
    public string? ActualPath { get; set; }
    public bool Found => ActualPath != null;
    public string Label { get; set; } = "";
}

public static class FileLocator
{
    // ── Folder name variants ────────────────────────────────────────
    private static readonly string[] KbgaDataNames = { "kbgadata", "KBGA Data" };
    private static readonly string[] KbgaResultsNames = { "kbgaresults" };

    // ── New format constants ────────────────────────────────────────
    /// <summary>Internal Lot id prefix for the per-day virtual lot (test-batch grouping).</summary>
    public const string VirtualDayLotPrefix = "__day__";
    /// <summary>UI suffix appended to the virtual lot's display name.</summary>
    public const string VirtualDayLotDisplaySuffix = " (虛擬)";
    /// <summary>Filename of the Part-level rolling summary (dotfile, new format).</summary>
    public const string RollingSummaryName = ".summary.csv";

    // ── Session-only merged lot constants ───────────────────────────
    /// <summary>Internal Lot id prefix for a session-only merged virtual lot.</summary>
    public const string MergedLotPrefix = "__merged__";
    /// <summary>UI display prefix for a merged lot.</summary>
    public const string MergedLotDisplayPrefix = "合併 ";

    /// <summary>Open a file shared with concurrent writers (handles `.~lock` situations).</summary>
    public static FileStream OpenSharedRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    /// <summary>True when filename should be ignored during enumeration (LibreOffice locks etc.).</summary>
    public static bool IsIgnoredFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith(".~lock", StringComparison.Ordinal);
    }

    public static bool IsKbgaDataDir(string folderName)
        => KbgaDataNames.Any(n => string.Equals(n, folderName, StringComparison.OrdinalIgnoreCase));

    public static bool IsKbgaResultsDir(string folderName)
        => KbgaResultsNames.Any(n => string.Equals(n, folderName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Resolve the actual kbgadata directory under root. Returns null if none exists.</summary>
    public static string? ResolveKbgaDataDir(string rootPath)
    {
        foreach (var name in KbgaDataNames)
        {
            var candidate = Path.Combine(rootPath, name);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Resolve the actual kbgaresults directory under root. Returns null if none exists.</summary>
    public static string? ResolveKbgaResultsDir(string rootPath)
    {
        foreach (var name in KbgaResultsNames)
        {
            var candidate = Path.Combine(rootPath, name);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Analyzes a user-selected folder and resolves root path, part number, lot number.
    /// Supports selection at any level of the AthleteSYS / kbgadata / kbgaresults hierarchy.
    /// </summary>
    public static FolderAnalysisResult AnalyzeSelectedFolder(string selectedPath)
    {
        var result = new FolderAnalysisResult();
        var dirName = Path.GetFileName(selectedPath);
        var parent = Path.GetDirectoryName(selectedPath);
        var parentName = parent != null ? Path.GetFileName(parent) : null;
        var grandParent = parent != null ? Path.GetDirectoryName(parent) : null;
        var grandParentName = grandParent != null ? Path.GetFileName(grandParent) : null;
        var greatGrandParent = grandParent != null ? Path.GetDirectoryName(grandParent) : null;

        // Case 1: Selected folder IS kbgadata/KBGA Data or kbgaresults → root = parent
        if (IsKbgaDataDir(dirName) || IsKbgaResultsDir(dirName))
        {
            result.RootPath = parent ?? selectedPath;
            return result;
        }

        // Case 2: Parent IS kbgadata/KBGA Data → selected = part number folder
        if (parentName != null && IsKbgaDataDir(parentName))
        {
            result.RootPath = grandParent ?? selectedPath;
            result.PartNumber = dirName;
            return result;
        }

        // Case 3: Parent IS kbgaresults → selected = part number folder
        if (parentName != null && IsKbgaResultsDir(parentName))
        {
            result.RootPath = grandParent ?? selectedPath;
            result.PartNumber = dirName;
            return result;
        }

        // Case 4: Grandparent IS kbgaresults → selected = lot folder
        if (grandParentName != null && IsKbgaResultsDir(grandParentName))
        {
            result.RootPath = greatGrandParent ?? selectedPath;
            result.PartNumber = parentName;
            result.LotNumber = dirName;
            return result;
        }

        // Case 5: Selected folder contains kbgadata or kbgaresults → it IS the root
        if (ResolveKbgaDataDir(selectedPath) != null || ResolveKbgaResultsDir(selectedPath) != null)
        {
            result.RootPath = selectedPath;
            return result;
        }

        // Case 6: Flat structure — folder contains {folderName}.csv → part number folder
        if (!string.IsNullOrEmpty(dirName) && File.Exists(Path.Combine(selectedPath, dirName + ".csv")))
        {
            result.RootPath = parent ?? selectedPath;
            result.PartNumber = dirName;
            return result;
        }

        // Case 7: Flat structure — parent contains {parentName}.csv → parent is part, selected is lot
        if (parentName != null && parent != null && File.Exists(Path.Combine(parent, parentName + ".csv")))
        {
            result.RootPath = grandParent ?? selectedPath;
            result.PartNumber = parentName;
            result.LotNumber = dirName;
            return result;
        }

        // Fallback: treat as root
        result.RootPath = selectedPath;
        return result;
    }

    // --- Master CSV ---
    public static FileCheckResult FindMasterCsv(string athSysPath, string partNo)
    {
        var result = new FileCheckResult { Label = "Master" };
        var dataDir = ResolveKbgaDataDir(athSysPath);
        // Flat structure fallback: root itself contains part number folders
        var baseDir = dataDir ?? athSysPath;

        // Primary: {baseDir}/{partNo}/{partNo}.csv
        var primary = Path.Combine(baseDir, partNo, partNo + ".csv");
        result.ExpectedPath = primary;

        if (File.Exists(primary))
        {
            result.ActualPath = primary;
            return result;
        }

        // Fallback: any .csv in {baseDir}/{partNo}/
        var dir = Path.Combine(baseDir, partNo);
        if (Directory.Exists(dir))
        {
            var csvFiles = Directory.GetFiles(dir, "*.csv");
            if (csvFiles.Length > 0)
            {
                result.ActualPath = csvFiles[0];
                return result;
            }
        }

        return result;
    }

    // --- Summary CSV ---
    public static FileCheckResult FindSummaryCsv(string athSysPath, string partNo, string lotNo)
    {
        var result = new FileCheckResult { Label = "Summary" };

        // Session-only merged lots have no physical summary file.
        if (TryDecodeMergedLot(lotNo, out _))
        {
            result.ExpectedPath = "(merged lot, in-memory)";
            return result;
        }

        var partDir = GetPartResultsDir(athSysPath, partNo);

        // New format (virtual day lot OR new real lot from timestamp folders):
        // both share the rolling Part-level dotfile.
        bool isVirtualDay = TryDecodeVirtualDayLot(lotNo, out _);
        bool isLegacyDir  = !isVirtualDay && Directory.Exists(Path.Combine(partDir, lotNo));

        if (isVirtualDay || !isLegacyDir)
        {
            var rolling = Path.Combine(partDir, RollingSummaryName);
            result.ExpectedPath = rolling;
            if (File.Exists(rolling))
            {
                result.ActualPath = rolling;
                return result;
            }
            // For virtual day lots there's no other place to look — return not-found.
            if (isVirtualDay) return result;
            // Otherwise fall through to legacy lookup (returns ExpectedPath set to rolling though).
        }

        // Legacy: {partDir}/{lotNo}/{lotNo}.summary.csv (+ any .summary.csv in that dir)
        var legacyPrimary = Path.Combine(partDir, lotNo, lotNo + ".summary.csv");
        result.ExpectedPath = legacyPrimary;

        if (File.Exists(legacyPrimary))
        {
            result.ActualPath = legacyPrimary;
            return result;
        }

        var legacyDir = Path.Combine(partDir, lotNo);
        if (Directory.Exists(legacyDir))
        {
            var summaryFiles = Directory.GetFiles(legacyDir, "*.summary.csv");
            if (summaryFiles.Length > 0)
            {
                result.ActualPath = summaryFiles[0];
                return result;
            }
        }

        return result;
    }

    // --- Map CSV ---
    public static FileCheckResult FindMapCsv(string athSysPath, string lotNo)
    {
        var result = new FileCheckResult { Label = "Map" };

        // Primary: log/{lotNo}.map.csv
        var primary = Path.Combine(athSysPath, "log", lotNo + ".map.csv");
        result.ExpectedPath = primary;

        if (File.Exists(primary))
        {
            result.ActualPath = primary;
            return result;
        }

        // Fallback: any .map.csv containing lotNo in log/
        var logDir = Path.Combine(athSysPath, "log");
        if (Directory.Exists(logDir))
        {
            var mapFiles = Directory.GetFiles(logDir, "*" + lotNo + "*.map.csv");
            if (mapFiles.Length > 0)
            {
                result.ActualPath = mapFiles[0];
                return result;
            }
            // Second fallback: any .map.csv in log/
            mapFiles = Directory.GetFiles(logDir, "*.map.csv");
            var match = mapFiles.FirstOrDefault(f => Path.GetFileName(f).Contains(lotNo));
            if (match != null)
            {
                result.ActualPath = match;
                return result;
            }
        }

        return result;
    }

    // --- AFA file ---
    public static FileCheckResult FindAfaFile(string athSysPath, string partNo, string lotNo, string substrateId)
    {
        var result = new FileCheckResult { Label = "AFA" };

        // Session-only merged lots have no physical AFA file (they reference rows from
        // other lots, but the merged lot itself owns no folder).
        if (TryDecodeMergedLot(lotNo, out _))
        {
            result.ExpectedPath = "(merged lot, in-memory)";
            return result;
        }

        // New-format flow: substrateId is "{14-digit TS}-{Sub}", look inside that timestamp folder.
        var match = TryFindAfaInTimestampFolder(athSysPath, partNo, lotNo, substrateId);
        if (match != null)
        {
            result.ExpectedPath = match;
            result.ActualPath = match;
            return result;
        }

        // Legacy: scan the lot dir
        var resultDir = GetResultDir(athSysPath, partNo, lotNo);
        result.ExpectedPath = Path.Combine(resultDir, $"*{substrateId}*afa*");
        if (!Directory.Exists(resultDir)) return result;

        var files = Directory.GetFiles(resultDir, $"*{substrateId}*afa*").Where(p => !IsIgnoredFile(p)).ToArray();
        if (files.Length == 0)
            files = Directory.GetFiles(resultDir, $"*{substrateId}*").Where(p => !IsIgnoredFile(p)).ToArray();

        if (files.Length > 0)
            result.ActualPath = files.OrderByDescending(f => f).First();

        return result;
    }

    private static string? TryFindAfaInTimestampFolder(string athSysPath, string partNo, string lotNo, string substrateId)
    {
        // Check whether the lot is one of the new-format flavors and the substrate id has the
        // "{TS}-..." shape. If yes, look inside the dedicated folder.
        bool isVirtual = TryDecodeVirtualDayLot(lotNo, out _);
        var partDir = GetPartResultsDir(athSysPath, partNo);

        // Extract folder timestamp from substrate id (everything before the first dash, when it's 14 digits)
        var dashIdx = substrateId.IndexOf('-');
        var folderName = dashIdx > 0 ? substrateId.Substring(0, dashIdx) : substrateId;
        if (!TryParseTimestampFolderName(folderName, out _)) return null;

        // Skip if a legacy folder of the same name exists at this level (collision protection).
        // Virtual day lots and new-real-lots both use the timestamp folder layout.
        if (!isVirtual)
        {
            // For legacy lots a substrate of the same TS shape is improbable — but be safe:
            // only resolve to a TS folder if the legacy direct mapping doesn't exist.
            if (Directory.Exists(Path.Combine(partDir, lotNo))) return null;
        }

        var folder = Path.Combine(partDir, folderName);
        if (!Directory.Exists(folder)) return null;

        var primary = Path.Combine(folder, substrateId + ".afa");
        if (File.Exists(primary)) return primary;

        var any = Directory.GetFiles(folder, "*.afa").FirstOrDefault(p => !IsIgnoredFile(p));
        return any;
    }

    public static string GetResultDir(string athSysPath, string partNo, string lotNo)
    {
        var resultsDir = ResolveKbgaResultsDir(athSysPath);
        var baseDir = resultsDir ?? athSysPath;
        return Path.Combine(baseDir, partNo, lotNo);
    }

    public static string ExtractSubstrateId(string summaryName)
    {
        int idx = summaryName.IndexOf('-');
        return idx >= 0 ? summaryName.Substring(idx + 1) : summaryName;
    }

    public static string[] GetPartNumbers(string athSysPath, FolderSortMode sortMode = FolderSortMode.NameAsc)
    {
        var dirs = new List<string>();

        var dataDir = ResolveKbgaDataDir(athSysPath);
        if (dataDir != null) dirs.AddRange(Directory.GetDirectories(dataDir));

        var resultsDir = ResolveKbgaResultsDir(athSysPath);
        if (resultsDir != null) dirs.AddRange(Directory.GetDirectories(resultsDir));

        // Flat structure fallback: root itself contains part number folders
        if (dataDir == null && resultsDir == null && Directory.Exists(athSysPath))
            dirs.AddRange(Directory.GetDirectories(athSysPath));

        // A part may exist under both kbgadata and kbgaresults — keep the most-recent dir
        // for time-based sorting, but de-dup by folder name.
        var byName = dirs
            .GroupBy(d => Path.GetFileName(d)!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(GetFolderTime).First());

        return SortDirectories(byName, sortMode)
            .Select(d => Path.GetFileName(d)!)
            .ToArray();
    }

    public static string[] GetLotNumbers(string athSysPath, string partNo, FolderSortMode sortMode = FolderSortMode.NameAsc)
    {
        var partDir = GetPartResultsDir(athSysPath, partNo);
        if (!Directory.Exists(partDir)) return [];

        var hasRollingSummary = File.Exists(Path.Combine(partDir, RollingSummaryName));

        // Map of lot id → representative directory path (used only for time-based sort).
        var lotReprDir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lotInsertionOrder = new List<string>();
        var dayLotDates = new SortedSet<DateTime>();

        void Track(string lot, string reprPath)
        {
            if (lotReprDir.ContainsKey(lot))
            {
                // Keep the most-recently-touched directory as representative
                if (GetFolderTime(reprPath) > GetFolderTime(lotReprDir[lot]))
                    lotReprDir[lot] = reprPath;
                return;
            }
            lotReprDir[lot] = reprPath;
            lotInsertionOrder.Add(lot);
        }

        foreach (var sub in Directory.GetDirectories(partDir))
        {
            var name = Path.GetFileName(sub)!;
            if (TryParseTimestampFolderName(name, out var ts))
            {
                // Peek the .afa to learn whether this folder belongs to a real Lot
                var lotNo = PeekLotNoFromFolder(sub);
                if (string.IsNullOrEmpty(lotNo))
                {
                    // Test-batch — bucket by date
                    dayLotDates.Add(ts.Date);
                }
                else
                {
                    Track(lotNo!, sub);
                }
            }
            else
            {
                // Legacy named folder
                Track(name, sub);
            }
        }

        // Compose final list: legacy/real lots first, then virtual day lots
        var realLots = lotInsertionOrder.Where(l => l != null).ToList();
        var virtualLots = dayLotDates.Select(EncodeVirtualDayLot).ToList();

        // Sort each group independently so virtual day lots stay distinguishable
        var realSorted = SortLotIds(realLots, sortMode, id => lotReprDir.GetValueOrDefault(id) ?? "");
        var virtualSorted = SortLotIds(virtualLots, sortMode, id =>
        {
            if (TryDecodeVirtualDayLot(id, out var d))
                return d.ToString("yyyyMMdd"); // path placeholder isn't meaningful — sort by date string
            return id;
        });

        // For time-based sort of virtual lots, sort by the actual date itself.
        if (sortMode is FolderSortMode.TimeNewest or FolderSortMode.TimeOldest)
        {
            virtualSorted = virtualLots
                .Select(id => (id, date: TryDecodeVirtualDayLot(id, out var d) ? d : DateTime.MinValue))
                .OrderBy(p => p.date)
                .Select(p => p.id)
                .ToList();
            if (sortMode == FolderSortMode.TimeNewest) virtualSorted.Reverse();
        }

        return realSorted.Concat(virtualSorted).ToArray();
    }

    private static List<string> SortLotIds(IEnumerable<string> ids, FolderSortMode mode, Func<string, string> pathOf)
    {
        return mode switch
        {
            FolderSortMode.NameDesc   => ids.OrderByDescending(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
            FolderSortMode.TimeNewest => ids.OrderByDescending(id => GetFolderTime(pathOf(id))).ToList(),
            FolderSortMode.TimeOldest => ids.OrderBy(id => GetFolderTime(pathOf(id))).ToList(),
            _                         => ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    /// <summary>Returns `kbgaresults/{Part}/` (or its flat-structure fallback equivalent).</summary>
    public static string GetPartResultsDir(string athSysPath, string partNo)
    {
        var resultsDir = ResolveKbgaResultsDir(athSysPath);
        var baseDir = resultsDir ?? athSysPath;
        return Path.Combine(baseDir, partNo);
    }

    public static string EncodeVirtualDayLot(DateTime date) =>
        VirtualDayLotPrefix + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    public static bool TryDecodeVirtualDayLot(string lot, out DateTime date)
    {
        date = default;
        if (string.IsNullOrEmpty(lot) || !lot.StartsWith(VirtualDayLotPrefix, StringComparison.Ordinal)) return false;
        var s = lot.Substring(VirtualDayLotPrefix.Length);
        return DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>Encode a session-only merged-lot id with the given timestamp (typically DateTime.Now).</summary>
    public static string EncodeMergedLot(DateTime timestamp) =>
        MergedLotPrefix + timestamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    public static bool TryDecodeMergedLot(string lot, out DateTime timestamp)
    {
        timestamp = default;
        if (string.IsNullOrEmpty(lot) || !lot.StartsWith(MergedLotPrefix, StringComparison.Ordinal)) return false;
        var s = lot.Substring(MergedLotPrefix.Length);
        return DateTime.TryParseExact(s, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
    }

    /// <summary>UI display name for a lot id (decodes virtual day lots and merged lots).</summary>
    public static string FormatLotForDisplay(string lot)
    {
        if (TryDecodeMergedLot(lot, out var ts))
            return MergedLotDisplayPrefix + ts.ToString("yyyy-MM-dd HH:mm");
        if (TryDecodeVirtualDayLot(lot, out var d))
            return d.ToString("yyyy-MM-dd") + VirtualDayLotDisplaySuffix;
        return lot;
    }

    /// <summary>True for a folder name shaped exactly like a 14-digit timestamp `yyyyMMddHHmmss`.</summary>
    public static bool TryParseTimestampFolderName(string name, out DateTime ts)
    {
        ts = default;
        if (name.Length != 14 || !name.All(char.IsDigit)) return false;
        return DateTime.TryParseExact(name, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out ts);
    }

    /// <summary>
    /// Read the first ~50 lines of any `.afa` in <paramref name="folder"/> and return the
    /// `LotNo;` value (empty string when present-but-empty, null when no .afa or not found).
    /// Designed to be called once per timestamp folder during Lot enumeration.
    /// </summary>
    public static string? PeekLotNoFromFolder(string folder)
    {
        var afa = Directory.EnumerateFiles(folder, "*.afa")
                           .Where(p => !IsIgnoredFile(p))
                           .FirstOrDefault();
        if (afa == null) return null;
        try
        {
            using var fs = OpenSharedRead(afa);
            using var sr = new StreamReader(fs);
            string? line;
            int safety = 80;
            while (safety-- > 0 && (line = sr.ReadLine()) != null)
            {
                if (line.StartsWith("LotNo;", StringComparison.Ordinal))
                    return line.Substring("LotNo;".Length).Trim();
                if (line.StartsWith("INSPECTION=", StringComparison.Ordinal))
                    break; // header section ended
            }
        }
        catch { /* peek is best-effort */ }
        return null;
    }

    /// <summary>
    /// Resolve which physical substrate folders belong to <paramref name="lot"/>.
    /// Handles three flavors transparently:
    ///   - Virtual day lot          → all timestamp folders whose date matches
    ///   - Legacy named folder      → just `partDir/{lot}/`
    ///   - New real lot (LotNo set) → all timestamp folders whose .afa LotNo equals `lot`
    /// </summary>
    public static List<string> ResolveLotFolders(string athSysPath, string partNo, string lot)
    {
        // Session-only merged lots have no physical folders on disk.
        if (TryDecodeMergedLot(lot, out _)) return new();

        var partDir = GetPartResultsDir(athSysPath, partNo);
        if (!Directory.Exists(partDir)) return new();

        // Virtual day lot → filter timestamp folders by date and empty LotNo
        if (TryDecodeVirtualDayLot(lot, out var date))
        {
            var prefix = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            return Directory.GetDirectories(partDir)
                .Where(d =>
                {
                    var n = Path.GetFileName(d)!;
                    return TryParseTimestampFolderName(n, out _)
                           && n.StartsWith(prefix, StringComparison.Ordinal)
                           && string.IsNullOrEmpty(PeekLotNoFromFolder(d));
                })
                .ToList();
        }

        // Legacy named folder takes precedence when present
        var direct = Path.Combine(partDir, lot);
        if (Directory.Exists(direct))
            return new List<string> { direct };

        // Otherwise treat as new real lot (LotNo populated in some timestamp folders)
        return Directory.GetDirectories(partDir)
            .Where(d =>
            {
                var n = Path.GetFileName(d)!;
                if (!TryParseTimestampFolderName(n, out _)) return false;
                return string.Equals(PeekLotNoFromFolder(d), lot, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
    }

    /// <summary>
    /// Enumerate every substrate folder for <paramref name="lot"/>, returning enough
    /// metadata to drive a LEFT JOIN against the rolling summary CSV. If a folder has
    /// multiple `.afa` files (multi-substrate folder), each is returned as its own entry.
    /// </summary>
    public static List<SubstrateFolderInfo> EnumerateSubstratesForLot(string athSysPath, string partNo, string lot)
    {
        var folders = ResolveLotFolders(athSysPath, partNo, lot);
        var result = new List<SubstrateFolderInfo>();

        foreach (var folder in folders)
        {
            var folderName = Path.GetFileName(folder)!;
            DateTime? ts = TryParseTimestampFolderName(folderName, out var t) ? t : null;
            var lotNo = ts != null ? PeekLotNoFromFolder(folder) : null;

            var afas = Directory.GetFiles(folder, "*.afa").Where(p => !IsIgnoredFile(p)).ToList();
            if (afas.Count == 0)
            {
                // Folder without .afa — still surface it so the user can see it's incomplete.
                result.Add(new SubstrateFolderInfo(
                    FolderName: folderName,
                    FolderTimestamp: ts,
                    SubstrateId: folderName,
                    DisplayId: folderName,
                    AfaPath: "",
                    MapPath: "",
                    LotNoFromAfa: lotNo));
                continue;
            }

            foreach (var afa in afas)
            {
                var baseName = Path.GetFileNameWithoutExtension(afa); // "20260506163921-2"
                var displayId = ExtractSubstrateId(baseName);          // "2"
                var map = Path.Combine(folder, baseName + ".map");
                result.Add(new SubstrateFolderInfo(
                    FolderName: folderName,
                    FolderTimestamp: ts,
                    SubstrateId: baseName,
                    DisplayId: displayId,
                    AfaPath: afa,
                    MapPath: File.Exists(map) ? map : "",
                    LotNoFromAfa: lotNo));
            }
        }

        return result.OrderBy(s => s.FolderTimestamp ?? DateTime.MinValue)
                     .ThenBy(s => s.SubstrateId, StringComparer.OrdinalIgnoreCase)
                     .ToList();
    }

    /// <summary>All `.map` files belonging to <paramref name="lot"/>.</summary>
    public static List<string> EnumerateMapFilesForLot(string athSysPath, string partNo, string lot)
    {
        var folders = ResolveLotFolders(athSysPath, partNo, lot);
        var maps = new List<string>();
        foreach (var f in folders)
        {
            foreach (var m in Directory.GetFiles(f, "*.map"))
                if (!IsIgnoredFile(m)) maps.Add(m);
        }
        return maps;
    }

    private static IEnumerable<string> SortDirectories(IEnumerable<string> dirs, FolderSortMode sortMode) =>
        sortMode switch
        {
            FolderSortMode.NameDesc    => dirs.OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase),
            FolderSortMode.TimeNewest  => dirs.OrderByDescending(GetFolderTime),
            FolderSortMode.TimeOldest  => dirs.OrderBy(GetFolderTime),
            _                          => dirs.OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase),
        };

    /// <summary>
    /// Folder timestamp used for time-based sorting. Uses LastWriteTime so that a Lot
    /// directory's "freshness" reflects the most recent inspection result inside it,
    /// not just when the folder was first created.
    /// </summary>
    private static DateTime GetFolderTime(string path)
    {
        try { return Directory.GetLastWriteTime(path); }
        catch { return DateTime.MinValue; }
    }

    // --- Map file (per substrate) ---
    public static FileCheckResult FindMapFile(string athSysPath, string partNo, string lotNo, string substrateId)
    {
        var result = new FileCheckResult { Label = "Map" };

        // New-format flow first
        var newPath = TryFindMapInTimestampFolder(athSysPath, partNo, lotNo, substrateId);
        if (newPath != null)
        {
            result.ExpectedPath = newPath;
            result.ActualPath = newPath;
            return result;
        }

        var resultDir = GetResultDir(athSysPath, partNo, lotNo);

        // Primary: {substrateId}.map
        var primary = Path.Combine(resultDir, substrateId + ".map");
        result.ExpectedPath = primary;

        if (File.Exists(primary))
        {
            result.ActualPath = primary;
            return result;
        }

        // Fallback: any file ending with substrateId + ".map"
        if (Directory.Exists(resultDir))
        {
            var files = Directory.GetFiles(resultDir, $"*{substrateId}.map").Where(p => !IsIgnoredFile(p)).ToArray();
            if (files.Length > 0)
            {
                result.ActualPath = files[0];
                return result;
            }
        }

        return result;
    }

    private static string? TryFindMapInTimestampFolder(string athSysPath, string partNo, string lotNo, string substrateId)
    {
        bool isVirtual = TryDecodeVirtualDayLot(lotNo, out _);
        var partDir = GetPartResultsDir(athSysPath, partNo);

        var dashIdx = substrateId.IndexOf('-');
        var folderName = dashIdx > 0 ? substrateId.Substring(0, dashIdx) : substrateId;
        if (!TryParseTimestampFolderName(folderName, out _)) return null;

        if (!isVirtual && Directory.Exists(Path.Combine(partDir, lotNo))) return null;

        var folder = Path.Combine(partDir, folderName);
        if (!Directory.Exists(folder)) return null;

        var primary = Path.Combine(folder, substrateId + ".map");
        if (File.Exists(primary)) return primary;
        var any = Directory.GetFiles(folder, "*.map").FirstOrDefault(p => !IsIgnoredFile(p));
        return any;
    }

    /// <summary>計算目錄中的 _afa.txt 檔案數量（含新格式：時間戳子資料夾內的 .afa）</summary>
    public static int CountAfaFiles(string resultDir)
    {
        if (!Directory.Exists(resultDir)) return 0;
        int count = Directory.GetFiles(resultDir, "*afa*").Count(p => !IsIgnoredFile(p));
        foreach (var sub in Directory.GetDirectories(resultDir))
        {
            if (TryParseTimestampFolderName(Path.GetFileName(sub)!, out _))
                count += Directory.GetFiles(sub, "*.afa").Count(p => !IsIgnoredFile(p));
        }
        return count;
    }

    /// <summary>計算目錄中的 .map 檔案數量（含新格式：時間戳子資料夾內的 .map）</summary>
    public static int CountMapFiles(string resultDir)
    {
        if (!Directory.Exists(resultDir)) return 0;
        int count = Directory.GetFiles(resultDir, "*.map").Count(p => !IsIgnoredFile(p));
        foreach (var sub in Directory.GetDirectories(resultDir))
        {
            if (TryParseTimestampFolderName(Path.GetFileName(sub)!, out _))
                count += Directory.GetFiles(sub, "*.map").Count(p => !IsIgnoredFile(p));
        }
        return count;
    }

    /// <summary>計算 Part 目錄下的時間戳資料夾數量（新格式專屬）</summary>
    public static int CountTimestampFolders(string partResultsDir)
    {
        if (!Directory.Exists(partResultsDir)) return 0;
        return Directory.GetDirectories(partResultsDir)
            .Count(d => TryParseTimestampFolderName(Path.GetFileName(d)!, out _));
    }

    /// <summary>檢查 Part 目錄是否存在 rolling `.summary.csv` (新格式 dotfile)</summary>
    public static bool HasRollingSummary(string partResultsDir)
        => Directory.Exists(partResultsDir) &&
           File.Exists(Path.Combine(partResultsDir, RollingSummaryName));

    // ─── 下拉清單計數（給 ComboBox 顯示用，背景執行）────────────────────

    /// <summary>
    /// 計算指定 Part 下有多少個獨立 Lot#（含 legacy 命名、新格式真實批號、虛擬日批）。
    /// 邏輯與 <see cref="GetLotNumbers"/> 一致但只回傳數量，避免 sort 帶來的額外計算。
    /// 設計為背景執行，呼叫端應放在 <c>Task.Run</c> 中。
    /// </summary>
    public static int CountLotsForPart(string athSysPath, string partNo)
    {
        var partDir = GetPartResultsDir(athSysPath, partNo);
        if (!Directory.Exists(partDir)) return 0;

        var realLots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dayLotDates = new HashSet<DateTime>();

        try
        {
            foreach (var sub in Directory.GetDirectories(partDir))
            {
                var name = Path.GetFileName(sub)!;
                if (TryParseTimestampFolderName(name, out var ts))
                {
                    var lotNo = PeekLotNoFromFolder(sub);
                    if (string.IsNullOrEmpty(lotNo))
                        dayLotDates.Add(ts.Date);
                    else
                        realLots.Add(lotNo!);
                }
                else
                {
                    realLots.Add(name);
                }
            }
        }
        catch
        {
            // 容錯：列舉過程中目錄被刪／權限變動時回傳目前累計值
        }

        return realLots.Count + dayLotDates.Count;
    }

    /// <summary>
    /// 計算指定 Lot 內有多少個基板（含多 .afa 同資料夾的情況）。
    /// 對合併批 (<c>__merged__</c>) 直接回傳 0 — 合併批的基板數由 <c>MergedLotData</c> 自帶。
    /// 設計為背景執行。
    /// </summary>
    public static int CountSubstratesForLot(string athSysPath, string partNo, string lot)
    {
        if (TryDecodeMergedLot(lot, out _)) return 0;
        try
        {
            // EnumerateSubstratesForLot 已處理 3 種格式，直接借用其結果
            return EnumerateSubstratesForLot(athSysPath, partNo, lot).Count;
        }
        catch
        {
            return 0;
        }
    }
}
