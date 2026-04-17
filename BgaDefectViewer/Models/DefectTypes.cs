using BgaDefectViewer.Converters;
using System.Windows.Media;

namespace BgaDefectViewer.Models;

public static class DefectTypes
{
    private static readonly Dictionary<int, DefectTypeInfo> _types = new()
    {
        { 2,  new DefectTypeInfo { Code = 2,  Name = "Missing", CanvasColor = Colors.Cyan,      GridColor = Colors.Cyan } },
        { 3,  new DefectTypeInfo { Code = 3,  Name = "Shift",   CanvasColor = Colors.Yellow,    GridColor = Colors.Yellow } },
        { 4,  new DefectTypeInfo { Code = 4,  Name = "Extra",   CanvasColor = Colors.Red,       GridColor = Colors.Red } },
        { 11, new DefectTypeInfo { Code = 11, Name = "Bridge",  CanvasColor = Colors.Magenta,   GridColor = Colors.Magenta } },
        { 21, new DefectTypeInfo { Code = 21, Name = "SD",      CanvasColor = Colors.Orange,    GridColor = Colors.Orange } },
        { 22, new DefectTypeInfo { Code = 22, Name = "LD",      CanvasColor = Colors.Orange,    GridColor = Colors.Orange } },
        { 30, new DefectTypeInfo { Code = 30, Name = "ETC",     CanvasColor = Colors.Orange,    GridColor = Colors.Orange } },
    };

    static DefectTypes()
    {
        // Pseudo-codes 1001-1010 used by the Recurring Defect tab to colour balls by occurrence count.
        for (int i = 1; i <= 10; i++)
        {
            Color c = CountToColorConverter.GetColor(i);
            _types[1000 + i] = new DefectTypeInfo { Code = 1000 + i, Name = $"{i}×", CanvasColor = c, GridColor = c };
        }
    }

    public static DefectTypeInfo GetOrCreate(int code)
    {
        if (!_types.TryGetValue(code, out var info))
        {
            info = new DefectTypeInfo { Code = code, Name = "E.O.", CanvasColor = Colors.White, GridColor = Colors.White };
            _types[code] = info;
        }
        return info;
    }

    public static string GetName(int code) => GetOrCreate(code).Name;
    public static Color GetCanvasColor(int code) => GetOrCreate(code).CanvasColor;
}
