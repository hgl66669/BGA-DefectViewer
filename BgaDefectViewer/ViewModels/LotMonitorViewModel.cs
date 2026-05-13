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

    public LotMonitorViewModel()
    {
        MergeLotsCommand = new RelayCommand(
            _ => MergeLotsRequested?.Invoke(),
            _ => CanMergeLots);
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
        SubstrateCount = session.SubstrateCount;
        Rows = new ObservableCollection<SummaryRow>(session.Rows);

        // Reset DIE/Sub: prefer fresh auto-derivation; drop any prior manual override.
        DieBaseDieCountIsManual = false;
        DieBaseDieCountAuto = DieCountInference.InferDieCountPerSubstrate(_allRows);
        // _dieBaseDieCount mirrors the effective value so the TextBox shows auto by default.
        SetProperty(ref _dieBaseDieCount, DieBaseDieCountAuto, nameof(DieBaseDieCount));
        OnPropertyChanged(nameof(TotalDieCount));

        // RecalculateSummaries() handles IsSummaryCalculated based on current options +
        // whether session has a CSV-original summary.
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
    /// <item>YieldMode 必須是 Default。</item>
    /// <item>CountETC=true；或 CountETC=false 但資料中沒有任何「ETC 類 only 且 NGDie&gt;0」的 row
    ///   （此時 toggle 對結果無影響）。</item>
    /// </list>
    /// 第二條讓使用者在「批次內無 ETC 類缺陷實際造成 NG die」的情況下，
    /// 切換 CountETC 不會誤觸發指示燈轉橘色。
    /// </summary>
    private bool BottomMatchesCsv
    {
        get
        {
            if (YieldMode != YieldMode.Default) return false;
            if (CountETC) return true;
            return !_allRows.Any(LotSummaryCalculator.WouldCountETCAffect);
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
                LotSummaryCalculator.Calculate(_allRows, opts).Lines);
            IsSummaryCalculated = true;
        }

        // Top-N summary 與超界提示
        if (TopNEnabled)
        {
            int distinct = _allRows.Select(r => r.SubstrateId).Distinct().Count();
            int upper = Math.Max(1, distinct);
            int n = Math.Max(1, Math.Min(TopN, upper));

            TopNWarning = (TopN > distinct && distinct > 0)
                ? $"超過實際基板數 ({distinct})，已採用 {n} 片"
                : (TopN <= 0 ? $"N 須 ≥ 1，已採用 {n} 片" : "");

            var slice = TopNFilter.SelectLatestNSubstrates(_allRows, n);
            TopNSummaryRows = new ObservableCollection<LotSummaryLine>(
                LotSummaryCalculator.Calculate(slice, opts).Lines);
        }
        else
        {
            TopNWarning = "";
            TopNSummaryRows = new ObservableCollection<LotSummaryLine>();
        }
    }
}
