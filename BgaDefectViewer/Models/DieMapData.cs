namespace BgaDefectViewer.Models;

public class DieMatrix
{
    public string DefectName { get; set; } = "";
    public int Rows { get; set; }
    public int Cols { get; set; }
    public int[,] Values { get; set; } = new int[0, 0];

    public int TotalCount
    {
        get
        {
            int sum = 0;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    sum += Values[r, c];
            return sum;
        }
    }

    public int MaxValue
    {
        get
        {
            int max = 0;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    if (Values[r, c] > max) max = Values[r, c];
            return max;
        }
    }
}

public class DieMapPair
{
    public DieMatrix PreRepair { get; set; } = new();
    public DieMatrix PostRepair { get; set; } = new();
}

public class DieMapData
{
    public string RecipeNo { get; set; } = "";
    public string RecipeName { get; set; } = "";
    public string LotName { get; set; } = "";
    public List<DieMapPair> DefectMaps { get; set; } = new();
    /// <summary>true = calculated by app from .map files; false = read from map.csv</summary>
    public bool IsCalculated { get; set; }
}
