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
        var resultsDir = ResolveKbgaResultsDir(athSysPath);
        // Flat structure fallback: root itself contains part number folders
        var baseDir = resultsDir ?? athSysPath;

        // Primary: {baseDir}/{partNo}/{lotNo}/{lotNo}.summary.csv
        var primary = Path.Combine(baseDir, partNo, lotNo, lotNo + ".summary.csv");
        result.ExpectedPath = primary;

        if (File.Exists(primary))
        {
            result.ActualPath = primary;
            return result;
        }

        // Fallback: any .summary.csv in {baseDir}/{partNo}/{lotNo}/
        var dir = Path.Combine(baseDir, partNo, lotNo);
        if (Directory.Exists(dir))
        {
            var summaryFiles = Directory.GetFiles(dir, "*.summary.csv");
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
        var resultDir = GetResultDir(athSysPath, partNo, lotNo);
        result.ExpectedPath = Path.Combine(resultDir, $"*{substrateId}*afa*");

        if (!Directory.Exists(resultDir)) return result;

        // Try: *{substrateId}*afa*
        var files = Directory.GetFiles(resultDir, $"*{substrateId}*afa*");
        if (files.Length == 0)
        {
            // Try: *{substrateId}*
            files = Directory.GetFiles(resultDir, $"*{substrateId}*");
        }

        if (files.Length > 0)
        {
            result.ActualPath = files.OrderByDescending(f => f).First();
        }

        return result;
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

    public static string[] GetPartNumbers(string athSysPath)
    {
        var parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var dataDir = ResolveKbgaDataDir(athSysPath);
        if (dataDir != null)
            foreach (var d in Directory.GetDirectories(dataDir))
                parts.Add(Path.GetFileName(d)!);

        var resultsDir = ResolveKbgaResultsDir(athSysPath);
        if (resultsDir != null)
            foreach (var d in Directory.GetDirectories(resultsDir))
                parts.Add(Path.GetFileName(d)!);

        // Flat structure fallback: root itself contains part number folders
        if (dataDir == null && resultsDir == null && Directory.Exists(athSysPath))
            foreach (var d in Directory.GetDirectories(athSysPath))
                parts.Add(Path.GetFileName(d)!);

        return parts.OrderBy(p => p).ToArray();
    }

    public static string[] GetLotNumbers(string athSysPath, string partNo)
    {
        var resultsDir = ResolveKbgaResultsDir(athSysPath);
        // Flat structure fallback: root itself contains part number folders
        var baseDir = resultsDir ?? athSysPath;
        var dir = Path.Combine(baseDir, partNo);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetDirectories(dir).Select(Path.GetFileName).Where(n => n != null).ToArray()!;
    }

    // --- Map file (per substrate) ---
    public static FileCheckResult FindMapFile(string athSysPath, string partNo, string lotNo, string substrateId)
    {
        var result = new FileCheckResult { Label = "Map" };
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
            var files = Directory.GetFiles(resultDir, $"*{substrateId}.map");
            if (files.Length > 0)
            {
                result.ActualPath = files[0];
                return result;
            }
        }

        return result;
    }

    /// <summary>計算目錄中的 _afa.txt 檔案數量</summary>
    public static int CountAfaFiles(string resultDir)
    {
        if (!Directory.Exists(resultDir)) return 0;
        return Directory.GetFiles(resultDir, "*afa*").Length;
    }

    /// <summary>計算目錄中的 .map 檔案數量</summary>
    public static int CountMapFiles(string resultDir)
    {
        if (!Directory.Exists(resultDir)) return 0;
        return Directory.GetFiles(resultDir, "*.map").Length;
    }
}
