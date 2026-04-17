using System.Collections.ObjectModel;
using BgaDefectViewer.Controls;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;
using BgaDefectViewer.Parsers;

namespace BgaDefectViewer.ViewModels;

public class SubstrateViewerViewModel : ViewModelBase
{
    private MasterBall[]? _masterBalls;
    private AfaFile? _afaFile;
    private CoordinateTransform? _transform;

    // 多 Die 模式：過濾至特定 Die 的欄/列識別字（如 "2C", "1R"）
    private string? _filterDieCol;
    private string? _filterDieRow;

    private string _waferId = "";
    public string WaferId
    {
        get => _waferId;
        set => SetProperty(ref _waferId, value);
    }

    private string _recipe = "";
    public string Recipe
    {
        get => _recipe;
        set => SetProperty(ref _recipe, value);
    }

    private ObservableCollection<int> _inspectionNumbers = new();
    public ObservableCollection<int> InspectionNumbers
    {
        get => _inspectionNumbers;
        set => SetProperty(ref _inspectionNumbers, value);
    }

    private int _selectedInspection;
    public int SelectedInspection
    {
        get => _selectedInspection;
        set
        {
            if (SetProperty(ref _selectedInspection, value))
                OnInspectionChanged();
        }
    }

    private ObservableCollection<DefectBall> _defects = new();
    public ObservableCollection<DefectBall> Defects
    {
        get => _defects;
        set => SetProperty(ref _defects, value);
    }

    private DefectBall? _selectedDefect;
    public DefectBall? SelectedDefect
    {
        get => _selectedDefect;
        set
        {
            if (SetProperty(ref _selectedDefect, value))
                Canvas?.HighlightBall(value, _transform);
        }
    }

    private string _summaryText = "";
    public string SummaryText
    {
        get => _summaryText;
        set => SetProperty(ref _summaryText, value);
    }

    private string _deviceJudge = "";
    public string DeviceJudge
    {
        get => _deviceJudge;
        set => SetProperty(ref _deviceJudge, value);
    }

    public BallMapCanvas? Canvas { get; set; }
    public CoordinateTransform? Transform => _transform;

    public void LoadMaster(MasterBall[] balls)
    {
        _masterBalls = balls;
        var (minX, maxX, minY, maxY) = MasterCsvParser.GetBounds(balls);
        _transform = new CoordinateTransform();
        _transform.SetBounds(minX, maxX, minY, maxY);

        // Clear AFA state so viewer shows master-only coordinate map
        _afaFile = null;
        _filterDieCol = null;
        _filterDieRow = null;
        WaferId = "";
        Recipe = "";
        InspectionNumbers = new ObservableCollection<int>();
        _selectedInspection = 0;
        OnPropertyChanged(nameof(SelectedInspection));
        Defects = new ObservableCollection<DefectBall>();
        DeviceJudge = "";
        SummaryText = $"Master: {balls.Length} balls";

        RequestRender();
    }

    public void LoadAfa(AfaFile afa, int? inspectionNumber = null,
                        bool isStale = false,
                        string? dieCol = null, string? dieRow = null)
    {
        _afaFile = afa;
        _filterDieCol = dieCol;
        _filterDieRow = dieRow;

        WaferId = afa.WaferId;
        Recipe = afa.Recipe;

        var filtered = GetFilteredInspections().ToList();

        InspectionNumbers = new ObservableCollection<int>(
            filtered.Select(i => i.InspectionNumber).Distinct().OrderBy(n => n));

        if (isStale)
        {
            // 此 CSV 列已被後續同 Stage 的檢驗覆蓋，.afa 中無對應資料 → 顯示空白
            Defects = new ObservableCollection<DefectBall>();
            DeviceJudge = "";
            SummaryText = "";
            _selectedInspection = 0;
            OnPropertyChanged(nameof(SelectedInspection));
            RequestRender();
            return;
        }

        if (!filtered.Any()) return;

        // 重置 backing field，確保即使目標 InspectionNumber 與目前值相同，
        // SetProperty 仍會觸發 OnInspectionChanged（切換不同基板但同 Inspection 號碼時的場景）。
        _selectedInspection = 0;

        if (inspectionNumber.HasValue)
        {
            var target = filtered.FirstOrDefault(i => i.InspectionNumber == inspectionNumber.Value);
            SelectedInspection = target?.InspectionNumber ?? filtered.Last().InspectionNumber;
        }
        else
        {
            SelectedInspection = filtered.Last().InspectionNumber;
        }
    }

    /// <summary>根據 _filterDieCol/_filterDieRow 過濾 InspectionResult 清單</summary>
    private IEnumerable<InspectionResult> GetFilteredInspections()
    {
        if (_afaFile == null) return Enumerable.Empty<InspectionResult>();
        var q = _afaFile.Inspections.AsEnumerable();
        if (_filterDieCol != null) q = q.Where(i => i.DieCol == _filterDieCol);
        if (_filterDieRow != null) q = q.Where(i => i.DieRow == _filterDieRow);
        return q;
    }

    private void OnInspectionChanged()
    {
        // Collect all InspectionResult entries for this inspection number (one per defective die)
        var matches = GetFilteredInspections()
            .Where(i => i.InspectionNumber == SelectedInspection)
            .ToList();
        if (!matches.Any()) return;

        var allDefects = matches.SelectMany(i => i.Defects).ToList();
        Defects = new ObservableCollection<DefectBall>(allDefects);

        var worstName = matches.FirstOrDefault(i => !string.IsNullOrEmpty(i.WorstName))?.WorstName;
        DeviceJudge = worstName ?? (allDefects.Count == 0 ? "Good" : "—");

        BuildSummaryText(matches);
        RequestRender();
    }

    private void BuildSummaryText(IList<InspectionResult> inspList)
    {
        int totalMaster = _masterBalls?.Length ?? 0;
        var allDefects  = inspList.SelectMany(i => i.Defects).ToList();
        int defectOnMaster = allDefects.Count(d => d.BallId != -1);
        int ok    = totalMaster - defectOnMaster;
        int extra = allDefects.Count(d => d.BallId == -1);

        var counts = allDefects.Where(d => d.BallId != -1)
            .GroupBy(d => d.DefectCode)
            .ToDictionary(g => g.Key, g => g.Count());

        int miss   = counts.GetValueOrDefault(2);
        int shift  = counts.GetValueOrDefault(3);
        int sd     = counts.GetValueOrDefault(21);
        int ld     = counts.GetValueOrDefault(22);
        int etc    = counts.GetValueOrDefault(30);
        int bridge = counts.GetValueOrDefault(11);

        double ppm = totalMaster > 0 ? (defectOnMaster * 1_000_000.0 / totalMaster) : 0;

        SummaryText = $"OK={ok}  Miss={miss}  Shift={shift}  SD={sd}  LD={ld}  ETC={etc}  Bridge={bridge}  Extra={extra}    PPM={ppm:F1}";
    }

    public void RequestRender()
    {
        if (Canvas == null || _masterBalls == null || _transform == null) return;
        _transform.SetCanvasSize(Canvas.ActualWidth, Canvas.ActualHeight);
        Canvas.RenderAll(_masterBalls, Defects?.ToList(), _transform);
    }

    public void OnCanvasDefectClicked(DefectBall defect)
    {
        SelectedDefect = defect;
    }

    /// <summary>使用者點擊畫布空白處 → 取消選取高亮</summary>
    public void OnCanvasBlankClicked() => SelectedDefect = null;

    /// <summary>FIT 按鈕：重置縮放至顯示整個裝置</summary>
    public void FitView()
    {
        _transform?.ResetToFit();
        RequestRender();
    }

    /// <summary>雙擊右側缺陷清單 → 畫面跳至該缺陷並自動縮放</summary>
    public void JumpToDefect(DefectBall defect)
    {
        if (_transform == null || Canvas == null) return;
        // Target ball radius ~15px for comfortable viewing
        double curR = _transform.BallRadiusPixels(defect.Diameter);
        double targetZoom = curR > 0 ? _transform.Zoom * 15.0 / curR : 5.0;
        _transform.CenterOn(defect.X, defect.Y, Canvas.ActualWidth, Canvas.ActualHeight, targetZoom);
        // If SelectedDefect is already this instance, SetProperty returns false → force re-highlight
        if (!SetProperty(ref _selectedDefect, defect, nameof(SelectedDefect)))
            Canvas.HighlightBall(defect, _transform);
        RequestRender();
    }
}
