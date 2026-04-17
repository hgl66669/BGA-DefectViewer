using System.ComponentModel;

namespace BgaDefectViewer.Models;

/// <summary>單輪 INSPECTION 資料（Die 矩陣 + 球級統計）</summary>
public class MapInspection
{
    public int InspectionNumber { get; set; }

    // 球級統計
    public int OK { get; set; }
    public int Miss { get; set; }
    public int Shift { get; set; }
    public int SD { get; set; }
    public int LD { get; set; }
    public int ETC { get; set; }
    public int Bridge { get; set; }
    public int Extra { get; set; }
    public int EO { get; set; }
    public int GDie { get; set; }
    public int NGDie { get; set; }
    public double PPM { get; set; }

    // Die 矩陣
    public int Rows { get; set; }
    public int Cols { get; set; }
    public char[,] DieGrid { get; set; } = new char[0, 0];

    public char GetDie(int row, int col) => DieGrid[row, col];
    public bool IsDiePresent(int row, int col) => DieGrid[row, col] != '1';
    public bool IsDieGood(int row, int col) => DieGrid[row, col] == 'G';
    public bool IsInspectionFailed(int row, int col) => DieGrid[row, col] == 'F';
}

/// <summary>完整 .map 檔案（含多輪 INSPECTION）</summary>
public class SubstrateMap : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string SubstrateId { get; set; } = "";
    public List<MapInspection> Inspections { get; set; } = new();

    // ── 目前顯示的 Inspection（由 ViewModel 設定）────────────────────────
    private int _displayInspectionNumber;
    public int DisplayInspectionNumber
    {
        get => _displayInspectionNumber;
        set
        {
            if (_displayInspectionNumber == value) return;
            _displayInspectionNumber = value;
            Raise(nameof(PrimaryDefectChar));
            Raise(nameof(TotalMiss));
            Raise(nameof(TotalShift));
            Raise(nameof(TotalExtra));
            Raise(nameof(IsNotFirstInspection));
            Raise(nameof(IsInspectionFallback));
            Raise(nameof(InspectionLabel));
        }
    }

    /// <summary>目前應顯示的 MapInspection（找不到時退回最後一輪）</summary>
    private MapInspection? DisplayInspection =>
        Inspections.FirstOrDefault(i => i.InspectionNumber == _displayInspectionNumber)
        ?? LastInspection;

    /// <summary>預設顯示最後一輪</summary>
    public MapInspection? LastInspection => Inspections.LastOrDefault();

    /// <summary>是否為多 Die 產品（矩陣大於 1×1）</summary>
    public bool IsMultiDie =>
        LastInspection is { Rows: > 1 } or { Cols: > 1 };

    /// <summary>單 Die 產品用：最後一輪第一個 Die 的判定字母</summary>
    public char SingleDieChar
    {
        get
        {
            var insp = LastInspection;
            if (insp == null || insp.Rows == 0 || insp.Cols == 0) return '?';
            return insp.DieGrid[0, 0];
        }
    }

    // Priority string: worst (index 0) → best. Used by WorstDieChar.
    private static readonly string Severity = "FDdBbEeCcSsMmUuG1";

    /// <summary>最差 Die 判定字母（Last Inspection，用於列表縮圖）</summary>
    public char WorstDieChar
    {
        get
        {
            var insp = LastInspection;
            if (insp == null || insp.Rows == 0 || insp.Cols == 0) return '?';
            char worst = 'G';
            int worstPri = Severity.IndexOf('G');
            for (int r = 0; r < insp.Rows; r++)
                for (int c = 0; c < insp.Cols; c++)
                {
                    char ch = insp.DieGrid[r, c];
                    int pri = Severity.IndexOf(ch);
                    if (pri >= 0 && pri < worstPri) { worst = ch; worstPri = pri; }
                }
            return worst;
        }
    }

    /// <summary>目前顯示輪次中最常見缺陷字母（用於列表縮圖）</summary>
    public char PrimaryDefectChar
    {
        get
        {
            var insp = DisplayInspection;
            if (insp == null) return 'G';
            var counts = new Dictionary<char, int>();
            for (int r = 0; r < insp.Rows; r++)
                for (int c = 0; c < insp.Cols; c++)
                {
                    char ch = insp.DieGrid[r, c];
                    if (ch != 'G' && ch != '1')
                        counts[ch] = counts.GetValueOrDefault(ch) + 1;
                }
            if (counts.Count == 0) return 'G';
            return counts.MaxBy(kv => kv.Value).Key;
        }
    }

    /// <summary>目前顯示輪次 Miss 數</summary>
    public int TotalMiss => DisplayInspection?.Miss ?? 0;
    /// <summary>目前顯示輪次 Shift 數</summary>
    public int TotalShift => DisplayInspection?.Shift ?? 0;
    /// <summary>目前顯示輪次 Extra 數</summary>
    public int TotalExtra => DisplayInspection?.Extra ?? 0;

    /// <summary>實際顯示的輪次號碼（Fallback 時為 LastInspection 的號碼）</summary>
    private int EffectiveInspectionNumber =>
        DisplayInspection?.InspectionNumber ?? _displayInspectionNumber;

    /// <summary>目前顯示輪次是否非第一輪（用於顯示 INSP 標籤）</summary>
    public bool IsNotFirstInspection => EffectiveInspectionNumber > 1;
    /// <summary>實際顯示的輪次低於要求的輪次（Fallback 發生）</summary>
    public bool IsInspectionFallback =>
        _displayInspectionNumber > 1 && EffectiveInspectionNumber != _displayInspectionNumber;
    /// <summary>顯示目前瀏覽輪次的標籤文字（使用實際顯示的號碼）</summary>
    public string InspectionLabel => $"INSP: {EffectiveInspectionNumber}";

    /// <summary>多 Die 產品列表用：彙總判定（最後一輪 NGDie > 0 → 主要缺陷字母，否則 G）</summary>
    public string JudgeSummary
    {
        get
        {
            var insp = LastInspection;
            if (insp == null) return "?";
            return insp.NGDie > 0 ? "NG" : "GD";
        }
    }
}
