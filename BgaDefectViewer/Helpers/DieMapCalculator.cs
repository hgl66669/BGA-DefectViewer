using BgaDefectViewer.Models;

namespace BgaDefectViewer.Helpers;

/// <summary>
/// Calculates a lot-level defect heat map (DieMapData) from individual .map files.
/// Pre Repair  = INSPECTION=1 aggregate (first inspection, raw defects only).
/// Post Repair = last INSPECTION aggregate (final state after all repairs).
/// Only defect types with at least one count are included in the output.
/// </summary>
public static class DieMapCalculator
{
    // Uppercase die character → display name (from DieJudge.cs mapping)
    private static readonly (char Upper, string Name)[] DefectTypes =
    [
        ('M', "Miss"),
        ('E', "Extra"),
        ('S', "Shift"),
        ('C', "ETC"),
        ('B', "Bridge"),
        ('D', "Diameter"),
        ('U', "E.O."),
        ('F', "Failed"),
    ];

    public static DieMapData? Calculate(
        IEnumerable<SubstrateMap> substrateMaps, string lotName, string recipeName)
    {
        var maps = substrateMaps.ToList();

        // Pre Repair: INSPECTION=1 for each substrate (first inspection only)
        var pre = maps
            .Select(s => s.Inspections.FirstOrDefault(i => i.InspectionNumber == 1))
            .Where(i => i != null && i.Rows > 0 && i.Cols > 0)
            .ToList();

        // Post Repair: last inspection for each substrate
        var post = maps
            .Select(s => s.LastInspection)
            .Where(i => i != null && i.Rows > 0 && i.Cols > 0)
            .ToList();

        if (pre.Count == 0) return null;

        // Use grid dimensions from the first substrate (all substrates in a lot share the same die layout)
        int rows = pre[0]!.Rows;
        int cols = pre[0]!.Cols;

        var data = new DieMapData
        {
            LotName = lotName,
            RecipeName = recipeName,
            IsCalculated = true
        };

        foreach (var (upper, name) in DefectTypes)
        {
            var preMatrix  = BuildMatrix(pre!,  rows, cols, upper, name);
            var postMatrix = BuildMatrix(post!, rows, cols, upper, name);

            if (preMatrix.TotalCount == 0 && postMatrix.TotalCount == 0) continue;

            data.DefectMaps.Add(new DieMapPair { PreRepair = preMatrix, PostRepair = postMatrix });
        }

        return data.DefectMaps.Count > 0 ? data : null;
    }

    private static DieMatrix BuildMatrix(
        IList<MapInspection?> inspections, int rows, int cols, char upper, string name)
    {
        var values = new int[rows, cols];
        foreach (var insp in inspections)
        {
            if (insp == null || insp.Rows != rows || insp.Cols != cols) continue;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    if (char.ToUpper(insp.DieGrid[r, c]) == upper)
                        values[r, c]++;
        }
        return new DieMatrix { DefectName = name, Rows = rows, Cols = cols, Values = values };
    }
}
