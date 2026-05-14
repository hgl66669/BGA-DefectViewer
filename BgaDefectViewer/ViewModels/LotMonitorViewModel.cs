using System.Collections.ObjectModel;
using System.Windows.Input;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.ViewModels;

public class LotMonitorViewModel : ViewModelBase
{
    // ── Source-of-truth snapshot from the last Load() ────────────────────
    private LotSession? _currentSession;
    private List<SummaryRow> _allRows = new();
    /// <summary>After applying <see cref="ActiveMountFilter"/>; drives Rows + summaries.</summary>
    private List<SummaryRow> _filteredRows = new();

    public LotMonitorViewModel()
    {
        MergeLotsCommand = new RelayCommand(
            _ => MergeLotsRequested?.Invoke(),
            _ => CanMergeLots);
        MountFilterCommand = new RelayCommand(_ => MountFilterRequested?.Invoke());
    }

    // ── Existing observable state ────────────────────────────────────────

    private ObservableCollection<SummaryRow> _rows = new();
    public ObservableCollection<SummaryRow> Rows
    {
        get => _rows;
        set => SetProperty(ref _rows, value);
    }

    private ObservableCollection<LotSummaryLine> _lotSummaryRows = new();
    public ObservableCollection<LotSummaryLine> LotSummaryRows
    {
        get => _lotSummaryRows;
        set => SetProperty(ref _lotSummaryRows, value);
    }

    private int _substrateCount;
    public int SubstrateCount
    {
        get => _substrateCount;
        set
        {
            if (SetProperty(ref _substrateCount, value))
                OnPropertyChanged(nameof(TotalDieCount));
        }
    }

    /// <summary>所有 Die 數 = SubstrateCount × DIE/Sub (effective)。</summary>
    public int TotalDieCount => SubstrateCount * DieBaseDieCountEffective;

    private string _lotName = "";
    public string LotName
    {
        get => _lotName;
        set => SetProperty(ref _lotName, value);
    }

    private SummaryRow? _selectedRow;
    public SummaryRow? SelectedRow
    {
        get => _selectedRow;
        set => SetProperty(ref _selectedRow, value);
    }

    private bool _isSummaryCalculated;
    /// <summary>true = LOT Summary was calculated by app; false = read from CSV</summary>
    public bool IsSummaryCalculated
    {
        get => _isSummaryCalculated;
        set => SetProperty(ref _isSummaryCalculated, value);
    }

    // ── New: Top-N filter ────────────────────────────────────────────────

    private bool _topNEnabled;
    public bool TopNEnabled
    {
        get => _topNEnabled;
        set
        {
            if (SetProperty(ref _topNEnabled, value))
            {
                OnPropertyChanged(nameof(IsTopNVisible));
                RecalculateSummaries();
            }
        }
    }

    private int _topN = 25;
    public int TopN
    {
        get => _topN;
        set { if (SetProperty(ref _topN, value)) RecalculateSummaries(); }
    }

    /// <summary>True when the Top-N summary block should be visible.</summary>
    public bool IsTopNVisible => TopNEnabled;

    private ObservableCollection<LotSummaryLine> _topNSummaryRows = new();
    public ObservableCollection<LotSummaryLine> TopNSummaryRows
    {
        get => _topNSummaryRows;
        set => SetProperty(ref _topNSummaryRows, value);
    }

    // ── New: Yield mode + CountETC + DIE/Sub ─────────────────────────────

    /// <summary>Static list for the Yield-mode ComboBox.</summary>
    public IReadOnlyList<YieldMode> YieldModes { get; } = new[] { YieldMode.Default, YieldMode.DieBase };

    private YieldMode _yieldMode = YieldMode.Default;
    public YieldMode YieldMode
    {
        get => _yieldMode;
        set { if (SetProperty(ref _yieldMode, value)) RecalculateSummaries(); }
    }

    private bool _countETC = true;
    public bool CountETC
    {
        get => _countETC;
        set { if (SetProperty(ref _countETC, value)) RecalculateSummaries(); }
    }

    private int _dieBaseDieCount;
    /// <summary>User-visible DIE/Sub value. Setting this from UI marks <see cref="DieBaseDieCountIsManual"/>.</summary>
    public int DieBaseDieCount
    {
        get => _dieBaseDieCount;
        set
        {
            if (SetProperty(ref _dieBaseDieCount, value))
            {
                // Only set the manual flag when the new value differs from auto;
                // this lets `Load()` reset to auto without flipping the flag.
                if (value != DieBaseDieCountAuto)
                {
                    DieBaseDieCountIsManual = true;
                }
                OnPropertyChanged(nameof(TotalDieCount));
                RecalculateSummaries();
            }
        }
    }

    private int _dieBaseDieCountAuto;
    /// <summary>Auto-derived DIE/Sub from <see cref="DieCountInference"/>; 0 when no clean row exists.</summary>
    public int DieBaseDieCountAuto
    {
        get => _dieBaseDieCountAuto;
        private set
        {
            if (SetProperty(ref _dieBaseDieCountAuto, value))
            {
                OnPropertyChanged(nameof(DieBaseHasAutoValue));
                OnPropertyChanged(nameof(TotalDieCount));
            }
        }
    }

    private bool _dieBaseDieCountIsManual;
    public bool DieBaseDieCountIsManual
    {
        get => _dieBaseDieCountIsManual;
        private set
        {
            if (SetProperty(ref _dieBaseDieCountIsManual, value))
                OnPropertyChanged(nameof(TotalDieCount));
        }
    }

    public bool DieBaseHasAutoValue => DieBaseDieCountAuto > 0;

    private string _topNWarning = "";
    /// <summary>當 Top-N 超過實際基板數時，此屬性帶有提示文字；否則為空字串。</summary>
    public string TopNWarning
    {
        get => _topNWarning;
        private set => SetProperty(ref _topNWarning, value);
    }

    // ── New: Cycle Time ──────────────────────────────────────────────────

    private CycleTimeResult _cycleTimeOverall = CycleTimeResult.Empty;
    /// <summary>整批 Cycle Time 結果（平均秒 + 計入樣本數）。</summary>
    public CycleTimeResult CycleTimeOverall
    {
        get => _cycleTimeOverall;
        private set
        {
            if (SetProperty(ref _cycleTimeOverall, value))
            {
                OnPropertyChanged(nameof(CycleTimeOverallDisplay));
                OnPropertyChanged(nameof(CycleTimeOverallSampleDisplay));
                OnPropertyChanged(nameof(CycleTimeSeconds));
            }
        }
    }

    private CycleTimeResult _cycleTimeTopN = CycleTimeResult.Empty;
    /// <summary>最新 N 片 Cycle Time 結果；未啟用 Top-N 時為 Empty。</summary>
    public CycleTimeResult CycleTimeTopN
    {
        get => _cycleTimeTopN;
        private set
        {
            if (SetProperty(ref _cycleTimeTopN, value))
            {
                OnPropertyChanged(nameof(CycleTimeTopNDisplay));
                OnPropertyChanged(nameof(CycleTimeTopNSampleDisplay));
                OnPropertyChanged(nameof(TopNCycleTimeSeconds));
            }
        }
    }

    /// <summary>整批 Cycle Time（秒），無資料時為 <c>null</c>。給外部相容性保留。</summary>
    public double? CycleTimeSeconds => CycleTimeOverall.AverageSeconds;

    /// <summary>最新 N 片 Cycle Time（秒），未啟用 Top-N 或無資料時為 <c>null</c>。</summary>
    public double? TopNCycleTimeSeconds => CycleTimeTopN.AverageSeconds;

    /// <summary>整批 Cycle Time UI 顯示，例如 <c>"31.91s"</c> 或 <c>"—"</c>。</summary>
    public string CycleTimeOverallDisplay => CycleTimeCalculator.Format(CycleTimeOverall.AverageSeconds);

    /// <summary>最新 N 片 Cycle Time UI 顯示，例如 <c>"25.50s"</c> 或 <c>"—"</c>。</summary>
    public string CycleTimeTopNDisplay => CycleTimeCalculator.Format(CycleTimeTopN.AverageSeconds);

    /// <summary>整批 Cycle Time 的樣本數標示，例如 <c>"依 8 個間隔平均"</c>；無樣本時為空字串。</summary>
    public string CycleTimeOverallSampleDisplay =>
        CycleTimeOverall.SampleCount > 0
            ? $"依 {CycleTimeOverall.SampleCount} 個間隔平均"
            : "";

    /// <summary>最新 N 片 Cycle Time 的樣本數標示。</summary>
    public string CycleTimeTopNSampleDisplay =>
        CycleTimeTopN.SampleCount > 0
            ? $"依 {CycleTimeTopN.SampleCount} 個間隔平均"
            : "";

    private bool _cycleTimeStage1Only = true;
    /// <summary>
    /// <c>true</c>（預設）= 僅計算 Stage=1 row 的 cycle time（實際生產節拍）；
    /// <c>false</c> = 包含所有 row（含 Stage&gt;1 的修補後重檢）。
    /// </summary>
    public bool CycleTimeStage1Only
    {
        get => _cycleTimeStage1Only;
        set { if (SetProperty(ref _cycleTimeStage1Only, value)) RecalculateSummaries(); }
    }

    private int _cycleTimeMaxGapSeconds = CycleTimeCalculator.DefaultMaxGapSeconds;
    /// <summary>視為「非連續生產」的間隔秒數上限；預設 300。</summary>
    public int CycleTimeMaxGapSeconds
    {
        get => _cycleTimeMaxGapSeconds;
        set { if (SetProperty(ref _cycleTimeMaxGapSeconds, value)) RecalculateSummaries(); }
    }

    /// <summary>Effective DIE/Sub used in recalculation (manual override wins when set).</summary>
    private int DieBaseDieCountEffective
        => DieBaseDieCountIsManual ? DieBaseDieCount : DieBaseDieCountAuto;

    // ── New: Merge command ───────────────────────────────────────────────

    public ICommand MergeLotsCommand { get; }
    public event Action? MergeLotsRequested;

    private bool _canMergeLots;
    /// <summary>Pushed from MainViewModel whenever LotNumbers changes.</summary>
    public bool CanMergeLots
    {
        get => _canMergeLots;
        set
        {
            if (SetProperty(ref _canMergeLots, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    // ── New: 特殊統計規則 (Mount filter) ────────────────────────────────

    public ICommand MountFilterCommand { get; }
    /// <summary>MainViewModel 訂閱以開啟 MountFilterDialog。</summary>
    public event Action? MountFilterRequested;

    private MountFilter _activeMountFilter = new();
    /// <summary>當前的 mount 過濾規則。設值後自動重算 (透過 ApplyMountFilter)。</summary>
    public MountFilter ActiveMountFilter
    {
        get => _activeMountFilter;
        set
        {
            if (SetProperty(ref _activeMountFilter, value ?? new MountFilter()))
            {
                OnPropertyChanged(nameof(IsMountFilterActive));
                OnPropertyChanged(nameof(MountFilterButtonText));
                ApplyMountFilter();
            }
        }
    }

    public bool IsMountFilterActive => ActiveMountFilter.Mode != MountMode.Off;

    /// <summary>按鈕文字：未啟用 = <c>"特殊統計"</c>；啟用 = <c>"特殊統計 [雙植球: M1+M2]"</c>。</summary>
    public string MountFilterButtonText =>
        IsMountFilterActive
            ? $"特殊統計  [{ActiveMountFilter.Describe()}]"
            : "特殊統計";

    // ── Existing events ──────────────────────────────────────────────────

    public event Action<SummaryRow>? RowDoubleClicked;
    public event Action<SummaryRow>? RowSingleClicked;

    public void OnRowSingleClick(SummaryRow row) => RowSingleClicked?.Invoke(row);

    /// <summary>由 SubstrateMap 單擊觸發，同步選中 Lot Monitor 對應行</summary>
    public void SelectBySubstrateId(string substrateId)
    {
        var target = Rows.FirstOrDefault(r =>
            r.SubstrateId.Equals(substrateId, StringComparison.OrdinalIgnoreCase)
            || substrateId.EndsWith(r.SubstrateId, StringComparison.OrdinalIgnoreCase)
            || r.SubstrateId.EndsWith(substrateId, StringComparison.OrdinalIgnoreCase));

        if (target != null)
            SelectedRow = target;
    }

    // ── Loading ──────────────────────────────────────────────────────────

    public void Load(LotSession session)
    {
        _currentSession = session;
        _allRows = session.Rows.ToList();

        LotName = session.LotName;

        // Reset DIE/Sub: prefer fresh auto-derivation; drop any prior manual override.
        DieBaseDieCountIsManual = false;
        DieBaseDieCountAuto = DieCountInference.InferDieCountPerSubstrate(_allRows);
        // _dieBaseDieCount mirrors the effective value so the TextBox shows auto by default.
        SetProperty(ref _dieBaseDieCount, DieBaseDieCountAuto, nameof(DieBaseDieCount));

        // ApplyMountFilter() rebuilds _filteredRows / Rows / SubstrateCount / TotalDieCount,
        // then calls RecalculateSummaries() for the summary block.
        ApplyMountFilter();
    }

    /// <summary>
    /// 套用當前 <see cref="ActiveMountFilter"/> 至 <c>_allRows</c>，更新主 DataGrid (Rows)、
    /// SubstrateCount，並觸發 summary 重算。
    /// <para>先呼叫 <see cref="MountFilter.RebuildAssignment"/> 依時間順序計算每個基板的 mount
    /// 索引，再依 Mount1/2/3Enabled 過濾。這樣即使基板名稱不規則也能正確分組。</para>
    /// </summary>
    private void ApplyMountFilter()
    {
        // 先依 _allRows 的時間順序 rebuild 一次「基板 → mount」映射
        ActiveMountFilter.RebuildAssignment(_allRows);

        _filteredRows = ActiveMountFilter.Mode == MountMode.Off
            ? _allRows.ToList()
            : _allRows.Where(r => ActiveMountFilter.IsAccepted(r.Name)).ToList();

        Rows = new ObservableCollection<SummaryRow>(_filteredRows);
        // Count by Name (full unique identifier) so merged-lot substrates aren't collapsed
        // when their short SubstrateIds collide across source lots (e.g., "Leg1-1" vs "Leg2-1").
        SubstrateCount = _filteredRows.Select(r => r.Name)
                                      .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        OnPropertyChanged(nameof(TotalDieCount));

        RecalculateSummaries();
    }

    public void OnRowDoubleClick(SummaryRow row)
    {
        RowDoubleClicked?.Invoke(row);
    }

    // ── Recalculation ────────────────────────────────────────────────────

    /// <summary>
    /// Bottom summary 與 CSV 原始 LotSummary 等價的條件：
    /// <list type="bullet">
    /// <item>未啟用 Mount filter（過濾改變了行集合）。</item>
    /// <item>YieldMode 必須是 Default。</item>
    /// <item>CountETC=true；或 CountETC=false 但資料中沒有任何「ETC 類 only 且 NGDie&gt;0」的 row。</item>
    /// </list>
    /// </summary>
    private bool BottomMatchesCsv
    {
        get
        {
            if (IsMountFilterActive) return false;
            if (YieldMode != YieldMode.Default) return false;
            if (CountETC) return true;
            return !_filteredRows.Any(LotSummaryCalculator.WouldCountETCAffect);
        }
    }

    private void RecalculateSummaries()
    {
        var opts = new LotSummaryOptions
        {
            Mode = YieldMode,
            CountETC = CountETC,
            DieBaseDieCount = DieBaseDieCountEffective
        };

        // 整批 summary：若 CSV 有原生 LOT Summary 區塊且使用者未動任何選項，
        // 直接顯示 CSV 原文（指示燈保持綠色）；否則重算（指示燈轉橘色）。
        bool useCsvVerbatim = _currentSession != null
                           && !_currentSession.Summary.IsCalculated
                           && BottomMatchesCsv;

        if (useCsvVerbatim)
        {
            LotSummaryRows = new ObservableCollection<LotSummaryLine>(_currentSession!.Summary.Lines);
            IsSummaryCalculated = false;
        }
        else
        {
            LotSummaryRows = new ObservableCollection<LotSummaryLine>(
                LotSummaryCalculator.Calculate(_filteredRows, opts).Lines);
            IsSummaryCalculated = true;
        }

        // 整批 Cycle Time（永遠重算）— 使用使用者選擇的 Stage1Only 與 MaxGap 選項
        int maxGap = Math.Max(1, CycleTimeMaxGapSeconds);
        CycleTimeOverall = CycleTimeCalculator.Calculate(_filteredRows, CycleTimeStage1Only, maxGap);

        // Top-N summary 與超界提示
        if (TopNEnabled)
        {
            int distinct = _filteredRows.Select(r => r.Name)
                                        .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int upper = Math.Max(1, distinct);
            int n = Math.Max(1, Math.Min(TopN, upper));

            TopNWarning = (TopN > distinct && distinct > 0)
                ? $"超過實際基板數 ({distinct})，已採用 {n} 片"
                : (TopN <= 0 ? $"N 須 ≥ 1，已採用 {n} 片" : "");

            var slice = TopNFilter.SelectLatestNSubstrates(_filteredRows, n);
            TopNSummaryRows = new ObservableCollection<LotSummaryLine>(
                LotSummaryCalculator.Calculate(slice, opts).Lines);
            CycleTimeTopN = CycleTimeCalculator.Calculate(slice, CycleTimeStage1Only, maxGap);
        }
        else
        {
            TopNWarning = "";
            TopNSummaryRows = new ObservableCollection<LotSummaryLine>();
            CycleTimeTopN = CycleTimeResult.Empty;
        }
    }
}
