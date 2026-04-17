using BgaDefectViewer.Models;

namespace BgaDefectViewer.Parsers;

public static class MapCsvParser
{
    public static DieMapData Parse(string filePath)
    {
        var data = new DieMapData();
        var lines = File.ReadAllLines(filePath);
        int i = 0;

        // Parse header
        while (i < lines.Length)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("Recipe No."))
            {
                var parts = line.Split(',');
                if (parts.Length >= 2) data.RecipeNo = parts[1].Trim();
            }
            else if (line.StartsWith("Recipe Name"))
            {
                var parts = line.Split(',');
                if (parts.Length >= 2) data.RecipeName = parts[1].Trim();
            }
            else if (line.StartsWith("Lot Name"))
            {
                var parts = line.Split(',');
                if (parts.Length >= 2) data.LotName = parts[1].Trim();
            }
            else if (line.StartsWith("Pre Repair"))
            {
                i++;
                break;
            }
            i++;
        }

        // Parse defect type blocks
        while (i < lines.Length)
        {
            // Skip empty lines and "Pre Repair" header lines
            if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].TrimStart().StartsWith("Pre Repair"))
            {
                i++;
                continue;
            }

            var pair = ParseDefectBlock(lines, ref i);
            if (pair != null)
                data.DefectMaps.Add(pair);
        }

        return data;
    }

    private static DieMapPair? ParseDefectBlock(string[] lines, ref int i)
    {
        if (i >= lines.Length) return null;

        // Parse header line: "{DefectName},{numCols}, ,{DefectName},{numCols}"
        var cells = lines[i].Split(',');
        if (cells.Length < 5) { i++; return null; }

        string preName = cells[0].Trim();
        if (!int.TryParse(cells[1].Trim(), out int preCols)) { i++; return null; }

        // Find separator (empty cell) and post-repair info
        int sepIdx = 2;
        string postName = cells.Length > sepIdx + 1 ? cells[sepIdx + 1].Trim() : preName;
        int postCols = cells.Length > sepIdx + 2 && int.TryParse(cells[sepIdx + 2].Trim(), out int pc) ? pc : preCols;

        i++;

        // Read data rows
        var preRows = new List<int[]>();
        var postRows = new List<int[]>();

        while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i])
               && !lines[i].TrimStart().StartsWith("Pre Repair"))
        {
            var rowCells = lines[i].Split(',');

            // Pre side: cells[0]=rowIdx, cells[1..preCols]=values
            int[] preVals = new int[preCols];
            for (int c = 0; c < preCols && (1 + c) < rowCells.Length; c++)
                int.TryParse(rowCells[1 + c].Trim(), out preVals[c]);
            preRows.Add(preVals);

            // Post side: after separator
            int postStart = preCols + 2;
            int[] postVals = new int[postCols];
            for (int c = 0; c < postCols && (postStart + 1 + c) < rowCells.Length; c++)
                int.TryParse(rowCells[postStart + 1 + c].Trim(), out postVals[c]);
            postRows.Add(postVals);

            i++;
        }

        return new DieMapPair
        {
            PreRepair = BuildMatrix(preName, preCols, preRows),
            PostRepair = BuildMatrix(postName, postCols, postRows)
        };
    }

    private static DieMatrix BuildMatrix(string name, int cols, List<int[]> rows)
    {
        var m = new DieMatrix
        {
            DefectName = name,
            Rows = rows.Count,
            Cols = cols,
            Values = new int[rows.Count, cols]
        };
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < cols; c++)
                m.Values[r, c] = rows[r][c];
        return m;
    }
}
