using System.Windows.Media;

namespace BgaDefectViewer.Helpers;

/// <summary>Die 判定字母工具類（規格第 7 節）</summary>
public static class DieJudge
{
    public struct DieInfo
    {
        public char Letter;
        public string DefectName;
        public bool IsGood;
        public bool IsEmpty;
        public bool HasMixedDefects;  // 小寫字母 = true
        public Color BackColor;
        public Color ForeColor;
    }

    private static readonly Dictionary<char, (string Name, string Hex)> Map =
        new()
        {
            { 'F', ("Failed",     "#F44336") },
            { 'M', ("Missing",    "#00BCD4") }, { 'm', ("Missing+",   "#00BCD4") },
            { 'E', ("Extra",      "#EF9A9A") }, { 'e', ("Extra+",     "#EF9A9A") },
            { 'B', ("Bridge",     "#E91E63") }, { 'b', ("Bridge+",    "#E91E63") },
            { 'D', ("Diameter",   "#9C27B0") }, { 'd', ("Diameter+",  "#9C27B0") },
            { 'C', ("ETC",        "#FF9800") }, { 'c', ("ETC+",       "#FF9800") },
            { 'S', ("Shift",      "#FFC107") }, { 's', ("Shift+",     "#FFC107") },
            { 'U', ("Unknown",    "#2196F3") }, { 'u', ("Unknown+",   "#2196F3") },
            { 'G', ("Good",       "#4CAF50") },
            { 'g', ("Good(Op)",   "#A5D6A7") },
            { '1', ("NoDie",      "#424242") },
        };

    public static DieInfo GetInfo(char letter)
    {
        var info = new DieInfo
        {
            Letter = letter,
            IsEmpty = letter == '1',
            IsGood = letter == 'G',
            HasMixedDefects = char.IsLower(letter)
        };

        if (Map.TryGetValue(letter, out var def))
        {
            info.DefectName = def.Name;
            info.BackColor = (Color)ColorConverter.ConvertFromString(def.Hex);
        }
        else
        {
            info.DefectName = $"Unknown({letter})";
            info.BackColor = Colors.Gray;
        }

        // Light backgrounds → black text; dark backgrounds → white text
        info.ForeColor = letter is 'S' or 's' or 'E' or 'e' or 'g'
            ? Colors.Black
            : Colors.White;

        return info;
    }
}
