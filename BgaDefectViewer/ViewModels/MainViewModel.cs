using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;
using BgaDefectViewer.Parsers;
using BgaDefectViewer.Simulation.ViewModels;

namespace BgaDefectViewer.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private MasterBall[]? _masterBalls;
    private bool _programmaticNav;

    /// <summary>
    /// Loaded payload for a session-only merged virtual lot. Includes the aggregated
    /// LotSession plus the SubstrateMaps gathered from each source lot (with
    /// <c>SourceLotId</c> tagged on each), so downstream tabs (Substrate Map,
    /// SubstrateViewer, DefectMap, RecurringDefects) stay functional.
    /// </summary>
    private sealed class MergedLotData
    {
        public LotSession Session { get; set; } = new();
        public List<Models.SubstrateMap> SubstrateMaps { get; set; } = new();
    }

    /// <summary>Session-only cache of merged virtual lots, keyed by <c>__merged__</c> or
    /// <c>__umkf__</c> id. Both flavors share the cache; OnLotNumberChanged routes through
    /// the same in-memory load path.</summary>
    private readonly Dictionary<string, MergedLotData> _mergedSessions =
        new(StringComparer.OrdinalIgnoreCase);

    // ── UMKF auto-merge state ────────────────────────────────────────
    /// <summary>Raw lot ids returned by <c>FileLocator.GetLotNumbers</c> for the current
    /// Part#. Cached so toggling <see cref="IsUmkfMergeEnabled"/> / <see cref="IncludeRBatches"/>
    /// can re-group without re-reading the disk.</summary>
    private List<string>? _rawLotIdsForCurrentPart;

    /// <summary>UMKF master id (<c>__umkf__...</c>) → list of raw child lot ids it represents.
    /// Populated by <see cref="RebuildLotsForUmkfMode"/>, consumed by
    /// <see cref="OnLotNumberChanged"/> for lazy session build and by
    /// <see cref="BuildMergedSession"/> for expansion.</summary>
    private readonly Dictionary<string, List<string>> _umkfMasterToChildren =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raw lot id → 是否有 .afa/.map 詳細檔。背景任務填入；UMKF 切換 / 排序變化時
    /// <see cref="RebuildLotsForUmkfMode"/> 從此 cache 回算母批與 singleton 的 IsSummaryOnly，
    /// 不需重新跑背景任務。Path / Part# 變化時清空。</summary>
    private readonly Dictionary<string, bool> _lotDetailCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Suppress the rebuild side-effects of the UMKF property setters during the
    /// part-number-change transition (we batch-set several flags then call rebuild once).</summary>
    private bool _suppressUmkfRebuild;

    private string _athleteSysPath = "";
    public string AthleteSysPath
    {
        get => _athleteSysPath;
        set
        {
            if (SetProperty(ref _athleteSysPath, value))
            {
                _settings.AthleteSysPath = value;
                _settings.Save();
                OnPathChanged();
            }
        }
    }

    private ObservableCollection<PartListItem> _partNumbers = new();
    public ObservableCollection<PartListItem> PartNumbers
    {
        get => _partNumbers;
        set => SetProperty(ref _partNumbers, value);
    }

    public IReadOnlyList<FolderSortOption> SortOptions { get; } = new[]
    {
        new FolderSortOption { Mode = FolderSortMode.NameAsc,    Display = "名稱 ↑" },
        new FolderSortOption { Mode = FolderSortMode.NameDesc,   Display = "名稱 ↓" },
        new FolderSortOption { Mode = FolderSortMode.TimeNewest, Display = "時間 (新→舊)" },
        new FolderSortOption { Mode = FolderSortMode.TimeOldest, Display = "時間 (舊→新)" },
        new FolderSortOption { Mode = FolderSortMode.CountDesc,  Display = "筆數 ↓" },
        new FolderSortOption { Mode = FolderSortMode.CountAsc,   Display = "筆數 ↑" },
    };

    private FolderSortMode _partSortMode = FolderSortMode.NameAsc;
    public FolderSortMode PartSortMode
    {
        get => _partSortMode;
        set { if (SetProperty(ref _partSortMode, value)) RefreshPartNumbers(); }
    }

    private FolderSortMode _lotSortMode = FolderSortMode.NameAsc;
    public FolderSortMode LotSortMode
    {
        get => _lotSortMode;
        set { if (SetProperty(ref _lotSortMode, value)) RefreshLotNumbers(); }
    }

    private string? _selectedPartNumber;
    public string? SelectedPartNumber
    {
        get => _selectedPartNumber;
        set
        {
            // 「載入更多」哨兵被點擊：展開分頁、把目前選取值 push 回 ComboBox（避免哨兵停留為選取狀態）
            if (value == LoadMoreSentinel.Id)
            {
                LoadMorePartNumbers();
                OnPropertyChanged(nameof(SelectedPartNumber));
                return;
            }
            if (SetProperty(ref _selectedPartNumber, value))
            {
                _settings.LastPartNumber = value ?? "";
                _settings.Save();
                OnPartNumberChanged();
            }
        }
    }

    private ObservableCollection<LotListItem> _lotNumbers = new();
    public ObservableCollection<LotListItem> LotNumbers
    {
        get => _lotNumbers;
        set => SetProperty(ref _lotNumbers, value);
    }

    /// <summary>
    /// 背景計數任務的取消來源。每次 OnPartNumberChanged / OnPathChanged 切換時取消上一個任務，
    /// 避免舊任務在新 lot 列表載入後寫入過時的計數。
    /// </summary>
    private CancellationTokenSource? _partCountCts;
    private CancellationTokenSource? _lotCountCts;

    // ── 下拉選單分頁載入 ────────────────────────────────────────────────
    // PartNumbers / LotNumbers 是 UI 真正綁定的可見集合；資料量大時 ComboBox
    // 首次展開會卡頓，因此採「先顯示 PageSize 筆 + 末尾哨兵『載入更多』」策略，
    // 由使用者按需展開。_all* 為完整來源（背景計數任務、選擇查找等以這份為主）。
    private const int PartPageSize = 128;
    private const int LotPageSize = 128;
    private List<PartListItem> _allPartNumbers = new();
    private List<LotListItem> _allLotNumbers = new();
    private bool _partsExpanded;
    private bool _lotsExpanded;

    private string? _selectedLotNumber;
    public string? SelectedLotNumber
    {
        get => _selectedLotNumber;
        set
        {
            // 「載入更多」哨兵被點擊：展開分頁、把目前選取值 push 回 ComboBox
            if (value == LoadMoreSentinel.Id)
            {
                LoadMoreLotNumbers();
                OnPropertyChanged(nameof(SelectedLotNumber));
                return;
            }
            if (SetProperty(ref _selectedLotNumber, value))
            {
                _settings.LastLotNumber = value ?? "";
                _settings.Save();
                OnLotNumberChanged();
            }
        }
    }

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // ── UMKF Mode (PSPREP_/PSPUBL_ 自動合併) ─────────────────────────
    private bool _isUmkfModeAvailable;
    /// <summary>True 表示目前 Part# 符合 UMKF 自動合併條件 (前綴 + 第6碼變動規律)。
    /// 控制上方配置列的 UMKF CheckBox 顯示。</summary>
    public bool IsUmkfModeAvailable
    {
        get => _isUmkfModeAvailable;
        set => SetProperty(ref _isUmkfModeAvailable, value);
    }

    private bool _isUmkfMergeEnabled;
    /// <summary>UMKF 自動合併模式開關。勾選後 LotNumbers 以母批形式呈現。</summary>
    public bool IsUmkfMergeEnabled
    {
        get => _isUmkfMergeEnabled;
        set
        {
            if (SetProperty(ref _isUmkfMergeEnabled, value) && !_suppressUmkfRebuild)
                RebuildLotsForUmkfMode();
        }
    }

    private bool _includeRBatches = true;
    /// <summary>UMKF 模式下是否將 <c>-R</c> 結尾的子批納入合併。預設納入，使用者可取消。</summary>
    public bool IncludeRBatches
    {
        get => _includeRBatches;
        set
        {
            if (SetProperty(ref _includeRBatches, value) && !_suppressUmkfRebuild)
                RebuildLotsForUmkfMode();
        }
    }

    private string _umkfStatusText = "";
    /// <summary>UMKF 模式下的精簡狀態提示 (例：<c>"12→4 批"</c>)，顯示於 CheckBox 旁。</summary>
    public string UmkfStatusText
    {
        get => _umkfStatusText;
        set => SetProperty(ref _umkfStatusText, value);
    }

    // ── 子 ViewModels ────────────────────────────────────────────────
    public SettingsViewModel Settings { get; }
    public LotMonitorViewModel LotMonitor { get; }
    public SubstrateMapViewModel SubstrateMap { get; }
    public SubstrateViewerViewModel SubstrateViewer { get; }
    public DefectMapViewModel DefectMap { get; }
    public RecurringDefectViewModel RecurringDefect { get; }
    public OverlapInspectionViewModel OverlapInspection { get; }
    public SimulatorViewModel Simulator { get; }

    public ICommand BrowsePathCommand { get; }
    public ICommand ReloadLotCommand { get; }

    public MainViewModel()
    {
        _settings = AppSettings.Load();

        Settings = new SettingsViewModel();
        LotMonitor = new LotMonitorViewModel();
        SubstrateMap = new SubstrateMapViewModel();
        SubstrateViewer = new SubstrateViewerViewModel();
        DefectMap = new DefectMapViewModel();
        RecurringDefect = new RecurringDefectViewModel();
        OverlapInspection = new OverlapInspectionViewModel();
        Simulator = new SimulatorViewModel();

        BrowsePathCommand = new RelayCommand(BrowsePath);
        ReloadLotCommand = new RelayCommand(
            _ => Reload(),
            _ => !string.IsNullOrEmpty(AthleteSysPath));

        // ── 事件訂閱 ──────────────────────────────────────────────────
        LotMonitor.RowDoubleClicked += OnSubstrateRowDoubleClicked;
        LotMonitor.RowSingleClicked += OnLotMonitorRowSingleClicked;
        LotMonitor.MergeLotsRequested += OnMergeLotsRequested;
        LotMonitor.MountFilterRequested += OnMountFilterRequested;
        LotMonitor.ExportReportRequested += OnExportReportRequested;

        SubstrateMap.SubstrateSelected += OnSubstrateMapSelected;
        SubstrateMap.SubstrateDoubleClicked += OnSubstrateMapDoubleClicked;
        SubstrateMap.DieDoubleClicked += OnSubstrateMapDieDoubleClicked;

        RefreshCanMergeLots();

        // ── 還原設定 ──────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(_settings.AthleteSysPath))
        {
            _athleteSysPath = _settings.AthleteSysPath;
            OnPropertyChanged(nameof(AthleteSysPath));
            OnPathChanged();

            if (!string.IsNullOrEmpty(_settings.LastPartNumber) &&
                _allPartNumbers.Any(p => p.Name == _settings.LastPartNumber))
            {
                // 若上次的料號落在分頁外，自動展開讓 ComboBox 顯示之
                if (!PartNumbers.Any(p => p.Name == _settings.LastPartNumber))
                    LoadMorePartNumbers();
                _selectedPartNumber = _settings.LastPartNumber;
                OnPropertyChanged(nameof(SelectedPartNumber));
                OnPartNumberChanged();

                if (!string.IsNullOrEmpty(_settings.LastLotNumber) &&
                    _allLotNumbers.Any(l => l.Id == _settings.LastLotNumber))
                {
                    if (!LotNumbers.Any(l => l.Id == _settings.LastLotNumber))
                        LoadMoreLotNumbers();
                    _selectedLotNumber = _settings.LastLotNumber;
                    OnPropertyChanged(nameof(SelectedLotNumber));
                    OnLotNumberChanged();
                }
            }
        }
    }

    private void BrowsePath(object? _)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "選擇資料來源目錄"
        };
        if (dialog.ShowDialog() != true) return;

        var analysis = FileLocator.AnalyzeSelectedFolder(dialog.FolderName);

        // Set root path (triggers OnPathChanged → populates PartNumbers)
        AthleteSysPath = analysis.RootPath;

        // Auto-select part number if detected
        if (!string.IsNullOrEmpty(analysis.PartNumber) &&
            _allPartNumbers.Any(p => p.Name == analysis.PartNumber))
        {
            if (!PartNumbers.Any(p => p.Name == analysis.PartNumber))
                LoadMorePartNumbers();
            SelectedPartNumber = analysis.PartNumber;

            // Auto-select lot number if detected (OnPartNumberChanged already populated LotNumbers)
            if (!string.IsNullOrEmpty(analysis.LotNumber) &&
                _allLotNumbers.Any(l => l.Id == analysis.LotNumber))
            {
                if (!LotNumbers.Any(l => l.Id == analysis.LotNumber))
                    LoadMoreLotNumbers();
                SelectedLotNumber = analysis.LotNumber;
            }
        }
    }

    private void OnPathChanged()
    {
        var partNames = FileLocator.GetPartNumbers(AthleteSysPath, PartSortMode);
        SetAllPartNumbers(partNames.Select(n => new PartListItem { Name = n }));
        SelectedPartNumber = null;
        SetAllLotNumbers(Enumerable.Empty<LotListItem>());
        SelectedLotNumber = null;
        Settings.Clear();
        _mergedSessions.Clear();   // path change invalidates all merged sessions
        _umkfMasterToChildren.Clear();
        _lotDetailCache.Clear();
        _rawLotIdsForCurrentPart = null;
        _suppressUmkfRebuild = true;
        try { IsUmkfModeAvailable = false; IsUmkfMergeEnabled = false; UmkfStatusText = ""; }
        finally { _suppressUmkfRebuild = false; }
        RefreshCanMergeLots();
        StartLoadingPartCounts();
    }

    /// <summary>
    /// Re-sort the Part# list without losing the current selection or triggering a
    /// data reload. Called when the user picks a different Part sort mode.
    /// </summary>
    private void RefreshPartNumbers()
    {
        var current = SelectedPartNumber;
        var partNames = FileLocator.GetPartNumbers(AthleteSysPath, PartSortMode);
        // CountDesc / CountAsc 在 FileLocator 內部會 fall back 至 NameAsc，因為計數要等背景任務；
        // 此處將已知計數套用後再排序，可達到「目前可見計數的排序」效果。
        var items = partNames.Select(n => new PartListItem { Name = n }).ToList();
        items = ApplyPartCountSort(items, PartSortMode);
        // 排序變化保留展開狀態：使用者若已按過「載入更多」，重新排序後仍應看到全部
        SetAllPartNumbers(items, resetExpanded: false);

        // Preserve selection without re-firing OnPartNumberChanged (which would reload Master/Lot data)
        if (current != null && partNames.Contains(current))
        {
            _selectedPartNumber = current;
            OnPropertyChanged(nameof(SelectedPartNumber));
        }
        else
        {
            _selectedPartNumber = null;
            OnPropertyChanged(nameof(SelectedPartNumber));
        }
        StartLoadingPartCounts();
    }

    /// <summary>
    /// 套用 CountDesc / CountAsc 排序到 PartListItem 列表；其他排序模式保留原順序。
    /// 計數尚未載入（LotCount == null）的項目永遠排在最後。
    /// </summary>
    private static List<PartListItem> ApplyPartCountSort(List<PartListItem> items, FolderSortMode mode)
    {
        if (mode != FolderSortMode.CountDesc && mode != FolderSortMode.CountAsc) return items;
        var withCount = items.Where(i => i.LotCount.HasValue).ToList();
        var without = items.Where(i => !i.LotCount.HasValue).ToList();
        withCount = (mode == FolderSortMode.CountDesc
                        ? withCount.OrderByDescending(i => i.LotCount!.Value).ThenBy(i => i.Name)
                        : withCount.OrderBy(i => i.LotCount!.Value).ThenBy(i => i.Name))
                    .ToList();
        return withCount.Concat(without).ToList();
    }

    /// <summary>
    /// 依當前 LotSortMode 決定次要排序，主要排序固定為「CreatedAt 日期降序」(group header 顯示用)。
    /// 未知日期的項目排到最後。
    /// </summary>
    private static List<LotListItem> SortLotItems(IEnumerable<LotListItem> items, FolderSortMode mode)
    {
        // 主鍵：日期降序，null 排到最後 (用 DateTime.MinValue.Ticks 並反轉)
        var byDate = items.OrderByDescending(i => i.CreatedAt?.Date ?? DateTime.MinValue);

        IOrderedEnumerable<LotListItem> withSecondary = mode switch
        {
            FolderSortMode.NameAsc    => byDate.ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase),
            FolderSortMode.NameDesc   => byDate.ThenByDescending(i => i.Id, StringComparer.OrdinalIgnoreCase),
            FolderSortMode.TimeNewest => byDate.ThenByDescending(i => i.CreatedAt ?? DateTime.MinValue),
            FolderSortMode.TimeOldest => byDate.ThenBy(i => i.CreatedAt ?? DateTime.MaxValue),
            FolderSortMode.CountDesc  => byDate
                .ThenByDescending(i => i.SubstrateCount ?? -1)
                .ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase),
            FolderSortMode.CountAsc   => byDate
                .ThenBy(i => i.SubstrateCount ?? int.MaxValue)
                .ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase),
            _ => byDate.ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase),
        };

        return withSecondary.ToList();
    }

    /// <summary>解析某 lot id 的 CreatedAt：合併批 / 虛擬日批 / 一般批分別走不同來源。</summary>
    private static DateTime? ResolveLotCreatedAt(
        string id, Dictionary<string, DateTime>? folderTimes,
        Dictionary<string, MergedLotData> mergedSessions)
    {
        if (FileLocator.TryDecodeMergedLot(id, out var mts)) return mts;
        if (FileLocator.TryDecodeUmkfMaster(id, out _)) return null;  // 由 RebuildLotsForUmkfMode 直接帶入
        if (FileLocator.TryDecodeVirtualDayLot(id, out var dDate)) return dDate;
        if (mergedSessions.ContainsKey(id)) return null;  // 已在上方處理；fallback safety
        return folderTimes != null && folderTimes.TryGetValue(id, out var t) ? t : null;
    }

    /// <summary>
    /// 依目前的 <see cref="IsUmkfMergeEnabled"/> 與 <see cref="IncludeRBatches"/> 旗標重建
    /// <see cref="LotNumbers"/>。UMKF 模式下將原始批號依第 6 碼分組，每組 ≥2 個的合併為單一
    /// <c>__umkf__</c> 母批；session-only 手動合併批 (<c>__merged__</c>) 一律保留。
    /// 由 <see cref="OnPartNumberChanged"/>、UMKF 屬性 setter、<see cref="RefreshLotNumbers"/> 共用。
    /// </summary>
    private void RebuildLotsForUmkfMode(bool resetExpanded = true)
    {
        if (_rawLotIdsForCurrentPart == null || string.IsNullOrEmpty(SelectedPartNumber))
        {
            SetAllLotNumbers(Enumerable.Empty<LotListItem>());
            UmkfStatusText = "";
            RefreshCanMergeLots();
            return;
        }

        var folderTimes = FileLocator.GetLotTimestamps(AthleteSysPath, SelectedPartNumber);
        // 從完整來源保留計數，避免分頁可見視窗外的 item 因 rebuild 丟失已載入的 SubstrateCount
        var existingCounts = _allLotNumbers.ToDictionary(
            li => li.Id, li => li.SubstrateCount, StringComparer.OrdinalIgnoreCase);

        // Invalidate any cached UMKF master sessions — toggling IncludeRBatches (or UMKF mode)
        // can change which children belong to a master, so stale aggregates must be rebuilt.
        // Manual __merged__ entries are NOT invalidated (those are explicit user snapshots).
        var staleUmkfKeys = _mergedSessions.Keys
            .Where(k => FileLocator.TryDecodeUmkfMaster(k, out _))
            .ToList();
        foreach (var k in staleUmkfKeys) _mergedSessions.Remove(k);

        _umkfMasterToChildren.Clear();
        var items = new List<LotListItem>();

        if (IsUmkfMergeEnabled)
        {
            var groups = UmkfBatchMerger.GroupLots(_rawLotIdsForCurrentPart, IncludeRBatches);
            int rawCount = groups.Sum(kv => kv.Value.Count);

            foreach (var g in groups)
            {
                if (g.Value.Count >= 2)
                {
                    var master = UmkfBatchMerger.FindMasterBatch(g.Value);
                    var encoded = FileLocator.EncodeUmkfMaster(master);
                    _umkfMasterToChildren[encoded] = g.Value;
                    // CreatedAt left null on purpose — folder mtimes are unreliable on backup drives
                    // (every folder bears the backup timestamp). StartLoadingLotCounts fills the
                    // date from each child's .summary.csv first-row Date/Time, then takes the max.
                    items.Add(new LotListItem
                    {
                        Id = encoded,
                        UmkfChildCount = g.Value.Count,
                        CreatedAt = null,
                        // 母批 IsSummaryOnly = 全部子批皆已快取且皆無 detail；任一未知或有 detail → 不標註
                        IsSummaryOnly = g.Value.All(c =>
                            _lotDetailCache.TryGetValue(c, out var d) && !d),
                    });
                }
                else
                {
                    items.Add(BuildPlainLotItem(g.Value[0], folderTimes, existingCounts));
                }
            }
            UmkfStatusText = $"{rawCount}→{items.Count} 批";
        }
        else
        {
            foreach (var id in _rawLotIdsForCurrentPart)
                items.Add(BuildPlainLotItem(id, folderTimes, existingCounts));
            UmkfStatusText = "";
        }

        // Preserve session-only manual-merge lots (created via 合併批號 dialog).
        foreach (var kv in _mergedSessions)
        {
            if (FileLocator.TryDecodeUmkfMaster(kv.Key, out _)) continue;  // UMKF masters already in items
            if (items.Any(i => string.Equals(i.Id, kv.Key, StringComparison.OrdinalIgnoreCase))) continue;
            items.Add(new LotListItem
            {
                Id = kv.Key,
                SubstrateCount = kv.Value.Session.SubstrateCount,
                CreatedAt = ResolveLotCreatedAt(kv.Key, folderTimes, _mergedSessions),
            });
        }

        items = SortLotItems(items, LotSortMode);
        // 預設視為「新一批 lot」→ 重置展開；排序變化路徑 (RefreshLotNumbers) 會傳 false 保留
        SetAllLotNumbers(items, resetExpanded);

        // If the previously-selected lot is no longer in the rebuilt list (e.g. its raw id got
        // absorbed into a UMKF master, or UMKF master disappeared after toggling off), clear it.
        if (!string.IsNullOrEmpty(SelectedLotNumber) &&
            !items.Any(i => string.Equals(i.Id, SelectedLotNumber, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedLotNumber = null;
            OnPropertyChanged(nameof(SelectedLotNumber));
        }
        RefreshCanMergeLots();
    }

    /// <summary>Helper for <see cref="RebuildLotsForUmkfMode"/>: build a normal LotListItem,
    /// preserving any already-loaded substrate count and resolving CreatedAt from folder times.</summary>
    private LotListItem BuildPlainLotItem(
        string id,
        Dictionary<string, DateTime>? folderTimes,
        Dictionary<string, int?> existingCounts)
    {
        var item = new LotListItem
        {
            Id = id,
            CreatedAt = ResolveLotCreatedAt(id, folderTimes, _mergedSessions),
        };
        if (_mergedSessions.TryGetValue(id, out var mg))
            item.SubstrateCount = mg.Session.SubstrateCount;
        else if (existingCounts.TryGetValue(id, out var c) && c.HasValue)
            item.SubstrateCount = c;
        // 從 detail cache 回填 IsSummaryOnly，rebuild 時不需等背景任務再跑一次
        if (_lotDetailCache.TryGetValue(id, out var hasDetail))
            item.IsSummaryOnly = !hasDetail;
        return item;
    }

    // ── 背景計數任務 ──────────────────────────────────────────────────

    /// <summary>
    /// 背景列舉 PartNumbers 中每個 Part 的 lot 筆數，依序回灌至 UI。
    /// 每次呼叫會取消前一個未完成的任務，避免舊任務寫入過時的計數。
    /// </summary>
    private void StartLoadingPartCounts()
    {
        _partCountCts?.Cancel();
        _partCountCts = new CancellationTokenSource();
        var token = _partCountCts.Token;
        var path = AthleteSysPath;
        // 從完整來源 snapshot，避免哨兵被當成料號 + 確保未展開的項目也能拿到計數
        var items = _allPartNumbers.ToList();
        var dispatcher = Application.Current.Dispatcher;

        if (string.IsNullOrEmpty(path) || items.Count == 0) return;

        Task.Run(() =>
        {
            foreach (var item in items)
            {
                if (token.IsCancellationRequested) return;
                int count;
                try { count = FileLocator.CountLotsForPart(path, item.Name); }
                catch { count = 0; }
                if (token.IsCancellationRequested) return;
                dispatcher.BeginInvoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    item.LotCount = count;
                });
            }
            // 全部計數載入完成後，若使用者目前是依筆數排序則重新排序一次
            if (!token.IsCancellationRequested)
            {
                dispatcher.BeginInvoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    if (PartSortMode is FolderSortMode.CountDesc or FolderSortMode.CountAsc)
                    {
                        var sorted = ApplyPartCountSort(_allPartNumbers, PartSortMode);
                        SetAllPartNumbers(sorted, resetExpanded: false);
                    }
                });
            }
        }, token);
    }

    /// <summary>
    /// 背景對 LotNumbers 中每個 Lot 串流其 <c>.summary.csv</c>，取得真實的「基板數」與
    /// 「首次檢驗日期」，依序回灌到 UI。比 folder enumeration / folder mtime 更精確：
    /// <list type="bullet">
    /// <item>能正確算出「只有 .summary.csv 沒有 .afa」的 legacy lot 基板數。</item>
    /// <item>日期取 CSV 內第一個 row 的 Date/Time，不再受 AFABackup 等 mtime 變動影響。</item>
    /// </list>
    /// 合併批跳過 — count 和 date 都已在加入清單時帶入。
    /// </summary>
    private void StartLoadingLotCounts()
    {
        _lotCountCts?.Cancel();
        _lotCountCts = new CancellationTokenSource();
        var token = _lotCountCts.Token;
        var path = AthleteSysPath;
        var partNo = SelectedPartNumber;
        var rawIds = _rawLotIdsForCurrentPart?.ToList() ?? new List<string>();
        // Snapshot child→master mapping so the background task is independent of subsequent mutations.
        var childToMaster = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _umkfMasterToChildren)
            foreach (var child in kv.Value)
                childToMaster[child] = kv.Key;
        var dispatcher = Application.Current.Dispatcher;

        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(partNo) || rawIds.Count == 0) return;

        // Running per-master tallies (accessed only on UI thread).
        var masterCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var masterMaxDate = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        // 母批是否含 detail：任一子批有 detail 即 true；無紀錄等同 false（暫顯示「僅摘要」直到某子批回報有 detail）
        var masterAnyDetail = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        Task.Run(() =>
        {
            foreach (var rawId in rawIds)
            {
                if (token.IsCancellationRequested) return;

                (int count, DateTime? date, bool hasDetail) info;
                try { info = FileLocator.GetLotInfo(path, partNo, rawId); }
                catch { info = (0, null, true); }  // 失敗時不顯示誤導性註記

                if (token.IsCancellationRequested) return;
                dispatcher.BeginInvoke(() =>
                {
                    if (token.IsCancellationRequested) return;

                    // 寫入 detail cache：供 UMKF 切換 / 排序變化時 RebuildLotsForUmkfMode 重算註記
                    _lotDetailCache[rawId] = info.hasDetail;

                    if (childToMaster.TryGetValue(rawId, out var masterId))
                    {
                        // UMKF master: accumulate child counts (and pick the newest date).
                        masterCount[masterId] = (masterCount.TryGetValue(masterId, out var c) ? c : 0) + info.count;
                        if (info.date.HasValue &&
                            (!masterMaxDate.TryGetValue(masterId, out var prevDate) || info.date.Value > prevDate))
                            masterMaxDate[masterId] = info.date.Value;
                        // 任一子批有 detail → 母批整體視為有 detail
                        if (info.hasDetail) masterAnyDetail[masterId] = true;

                        // 查找完整來源，讓分頁外的項目也能收到更新；可見視窗內的 item 同實例會自動 PropertyChanged。
                        var item = _allLotNumbers.FirstOrDefault(li => string.Equals(li.Id, masterId, StringComparison.OrdinalIgnoreCase));
                        if (item != null)
                        {
                            item.SubstrateCount = masterCount[masterId];
                            if (masterMaxDate.TryGetValue(masterId, out var d)) item.CreatedAt = d;
                            item.IsSummaryOnly = !(masterAnyDetail.TryGetValue(masterId, out var anyDetail) && anyDetail);
                        }
                    }
                    else
                    {
                        // Singleton (UMKF-disabled or solo group): update the lot item directly.
                        var item = _allLotNumbers.FirstOrDefault(li => string.Equals(li.Id, rawId, StringComparison.OrdinalIgnoreCase));
                        if (item != null)
                        {
                            item.SubstrateCount = info.count;
                            if (info.date.HasValue) item.CreatedAt = info.date;
                            item.IsSummaryOnly = !info.hasDetail;
                        }
                    }
                });
            }
            if (!token.IsCancellationRequested)
            {
                dispatcher.BeginInvoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    // 背景全部讀完後重新排序一次（主鍵：CreatedAt 日期降序；次鍵：使用者選的 SortMode）
                    var current = SelectedLotNumber;
                    var sorted = SortLotItems(_allLotNumbers, LotSortMode);
                    SetAllLotNumbers(sorted, resetExpanded: false);
                    if (current != null && _allLotNumbers.Any(l => l.Id == current))
                    {
                        _selectedLotNumber = current;
                        OnPropertyChanged(nameof(SelectedLotNumber));
                    }
                });
            }
        }, token);
    }

    /// <summary>
    /// Re-sort the Lot# list without losing the current selection or triggering a
    /// data reload. Called when the user picks a different Lot sort mode.
    /// </summary>
    private void RefreshLotNumbers()
    {
        if (string.IsNullOrEmpty(SelectedPartNumber))
        {
            SetAllLotNumbers(Enumerable.Empty<LotListItem>());
            RefreshCanMergeLots();
            return;
        }

        var current = SelectedLotNumber;
        // Refresh raw lot ids from disk so any new/removed folders are picked up.
        _rawLotIdsForCurrentPart = FileLocator.GetLotNumbers(AthleteSysPath, SelectedPartNumber, LotSortMode).ToList();
        // 排序變化視為「同一批 lot」→ 保留使用者已展開狀態，不重置回分頁
        RebuildLotsForUmkfMode(resetExpanded: false);

        if (current != null && _allLotNumbers.Any(li => string.Equals(li.Id, current, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedLotNumber = current;
            OnPropertyChanged(nameof(SelectedLotNumber));
        }
        else
        {
            _selectedLotNumber = null;
            OnPropertyChanged(nameof(SelectedLotNumber));
        }
        StartLoadingLotCounts();
    }

    private async void OnPartNumberChanged()
    {
        if (string.IsNullOrEmpty(SelectedPartNumber)) return;

        // Part# change invalidates session-only merged lots + UMKF state from the previous Part#.
        _mergedSessions.Clear();
        _umkfMasterToChildren.Clear();
        _lotDetailCache.Clear();

        var lotIds = FileLocator.GetLotNumbers(AthleteSysPath, SelectedPartNumber, LotSortMode);
        _rawLotIdsForCurrentPart = lotIds.ToList();

        // UMKF auto-detection: both prefix rule AND batch-pattern rule must hold.
        _suppressUmkfRebuild = true;
        try
        {
            IsUmkfModeAvailable =
                UmkfBatchMerger.IsUmkfPartNumber(SelectedPartNumber) &&
                UmkfBatchMerger.HasSpecialBatchPattern(_rawLotIdsForCurrentPart);
            IsUmkfMergeEnabled = IsUmkfModeAvailable;  // auto-tick when eligible
            IncludeRBatches = true;                    // reset to default per part
        }
        finally { _suppressUmkfRebuild = false; }

        RebuildLotsForUmkfMode();
        SelectedLotNumber = null;
        StartLoadingLotCounts();

        // Load master CSV immediately so Substrate Viewer shows coordinate map
        var masterCheck = FileLocator.FindMasterCsv(AthleteSysPath, SelectedPartNumber);
        if (masterCheck.Found)
        {
            _masterBalls = await Task.Run(() => MasterCsvParser.Parse(masterCheck.ActualPath!));
            var datPath = Path.ChangeExtension(masterCheck.ActualPath!, ".dat");
            var metadata = await Task.Run(() => MasterDatParser.Parse(datPath));
            SubstrateViewer.LoadMaster(_masterBalls);
            OverlapInspection.LoadMaster(_masterBalls, metadata);
            StatusText = $"Master({_masterBalls.Length} balls) | Select a Lot# to load inspection data";
        }
        else
        {
            _masterBalls = null;
            StatusText = "Ready";
        }
    }

    private async void OnLotNumberChanged()
    {
        if (string.IsNullOrEmpty(SelectedPartNumber) || string.IsNullOrEmpty(SelectedLotNumber)) return;

        // ── UMKF master: lazy-build merged session on first selection ──
        // Capture the id BEFORE the await — SelectedLotNumber may be mutated by concurrent
        // events while BuildMergedSession is running, and we must cache under the same key
        // that was selected at entry.
        if (FileLocator.TryDecodeUmkfMaster(SelectedLotNumber!, out var umkfMasterName) &&
            !_mergedSessions.ContainsKey(SelectedLotNumber!) &&
            _umkfMasterToChildren.TryGetValue(SelectedLotNumber!, out var umkfChildren))
        {
            var umkfCacheKey = SelectedLotNumber!;
            try
            {
                StatusText = $"Building UMKF master '{umkfMasterName}'...";
                var built = await Task.Run(() => BuildMergedSession(umkfChildren));
                built.Session.LotName = $"{umkfMasterName} (UMKF合併 {umkfChildren.Count} 批)";
                _mergedSessions[umkfCacheKey] = built;
            }
            catch (Exception ex)
            {
                StatusText = $"UMKF合併失敗: {ex.Message}";
                MessageBox.Show(ex.ToString(), "UMKF合併", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        // ── Session-only merged lot OR UMKF master (now cached): load from in-memory, skip disk I/O ──
        bool isVirtualMerged = FileLocator.TryDecodeMergedLot(SelectedLotNumber!, out _) ||
                               FileLocator.TryDecodeUmkfMaster(SelectedLotNumber!, out _);
        if (isVirtualMerged &&
            _mergedSessions.TryGetValue(SelectedLotNumber!, out var merged))
        {
            try
            {
                StatusText = "Loading merged lot...";

                LotMonitor.Load(merged.Session);

                // Substrate Map: load combined maps (each tagged with SourceLotId so AFA
                // file lookups in OnSubstrateMap*Clicked resolve through the original lot).
                SubstrateMap.Load(merged.SubstrateMaps);

                // Defect Map: aggregate INSP=1 maps from all source lots into one heatmap.
                if (merged.SubstrateMaps.Count > 0)
                {
                    var mapData = DieMapCalculator.Calculate(
                        merged.SubstrateMaps, SelectedLotNumber!, SelectedPartNumber!);
                    DefectMap.Load(mapData);
                }
                else
                {
                    DefectMap.Load(null);
                }

                // Recurring Defects: same aggregation.
                if (merged.SubstrateMaps.Count > 0)
                {
                    var recurringData = RecurringDefectCalculator.CalculateFromMaps(
                        merged.SubstrateMaps, SelectedLotNumber!);
                    RecurringDefect.Load(recurringData, _masterBalls);

                    // Phase 2: enrich with ball-level .afa data (background) — resolve each
                    // substrate's .afa through its SourceLotId, not the merged-lot id.
                    if (recurringData != null)
                    {
                        var partNo = SelectedPartNumber!;
                        var maps = merged.SubstrateMaps.ToList();
                        _ = Task.Run(() =>
                        {
                            var afas = new List<AfaFile>();
                            foreach (var m in maps)
                            {
                                var lotForAfa = m.SourceLotId ?? SelectedLotNumber!;
                                var chk = FileLocator.FindAfaFile(AthleteSysPath, partNo, lotForAfa, m.SubstrateId);
                                if (chk.Found) afas.Add(AfaFileParser.Parse(chk.ActualPath!));
                            }
                            RecurringDefectCalculator.EnrichWithAfaData(recurringData, afas);
                        }).ContinueWith(_ =>
                            Application.Current.Dispatcher.Invoke(() => RecurringDefect.RefreshBallData()));
                    }
                }
                else
                {
                    RecurringDefect.Load(null, _masterBalls);
                }

                Settings.UpdateFileStatuses(AthleteSysPath, SelectedPartNumber, SelectedLotNumber);
                StatusText = $"Merged lot loaded: {merged.Session.Rows.Count} rows / " +
                             $"{merged.Session.SubstrateCount} substrates / " +
                             $"{merged.SubstrateMaps.Count} maps (in-memory)";
            }
            catch (Exception ex)
            {
                StatusText = $"Error loading merged lot: {ex.Message}";
                MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }

        // ── Guard: virtual lot ids (__umkf__ / __merged__) must NEVER reach the disk-loading
        // branch — their data lives in _mergedSessions or nowhere. Falling through with such an
        // id would feed it to FindSummaryCsv → Path.Combine(partDir, lotNo) which is harmless
        // (lotNo is non-null) but would synthesize a bogus filesystem path. More critically,
        // we also catch the case where SelectedLotNumber was mutated to null by a concurrent
        // event (e.g. background sort replacing LotNumbers transiently clears ComboBox value)
        // during the awaits above — Path.Combine throws ArgumentNullException on a null path2.
        if (string.IsNullOrEmpty(SelectedLotNumber) || string.IsNullOrEmpty(SelectedPartNumber))
        {
            StatusText = "";
            return;
        }
        if (FileLocator.TryDecodeMergedLot(SelectedLotNumber!, out _) ||
            FileLocator.TryDecodeUmkfMaster(SelectedLotNumber!, out _))
        {
            StatusText = "虛擬批號暫存資料不存在 — 請取消勾選 UMKF 合併或重新整理後再試。";
            return;
        }

        // Snapshot lot/part to locals so the rest of this branch is immune to property
        // mutations across the disk-load awaits.
        var lotIdLocal = SelectedLotNumber!;
        var partNoLocal = SelectedPartNumber!;

        try
        {
            StatusText = "Loading...";
            var missing = new List<string>();
            var loaded = new List<string>();

            // --- Load Master ---
            var masterCheck = FileLocator.FindMasterCsv(AthleteSysPath, partNoLocal);
            if (masterCheck.Found)
            {
                _masterBalls = await Task.Run(() => MasterCsvParser.Parse(masterCheck.ActualPath!));
                var datPath = Path.ChangeExtension(masterCheck.ActualPath!, ".dat");
                var metadata = await Task.Run(() => MasterDatParser.Parse(datPath));
                SubstrateViewer.LoadMaster(_masterBalls);
                OverlapInspection.LoadMaster(_masterBalls, metadata);
                loaded.Add($"Master({_masterBalls.Length} balls)");
            }
            else
            {
                _masterBalls = null;
                missing.Add($"Master: {masterCheck.ExpectedPath}");
            }

            // --- Load Summary (with virtual-day-lot branch) ---
            bool isVirtualDay = FileLocator.TryDecodeVirtualDayLot(lotIdLocal, out var virtualDate);
            var summaryCheck = FileLocator.FindSummaryCsv(AthleteSysPath, partNoLocal, lotIdLocal);
            LotSession? loadedSession = null;
            if (summaryCheck.Found)
            {
                loadedSession = await Task.Run(() => isVirtualDay
                    ? SummaryCsvParser.ParseFilteredByDate(summaryCheck.ActualPath!, virtualDate)
                    : SummaryCsvParser.ParseFirstLot(summaryCheck.ActualPath!));
            }
            else
            {
                missing.Add($"Summary: {summaryCheck.ExpectedPath}");
            }

            // --- Substrate folders (LEFT JOIN source-of-truth for new format) ---
            var substrateFolders = await Task.Run(() =>
                FileLocator.EnumerateSubstratesForLot(AthleteSysPath, partNoLocal, lotIdLocal));

            // For virtual-day / new-format real lots, augment session.Rows with stub rows for any
            // substrate folder that has no matching summary entry yet.
            var session = loadedSession ?? new LotSession { LotName = FileLocator.FormatLotForDisplay(lotIdLocal) };
            if (substrateFolders.Count > 0)
            {
                var existing = session.Rows.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                int rowIndex = session.Rows.Count;
                foreach (var f in substrateFolders.Where(f => !existing.Contains(f.SubstrateId)))
                {
                    session.Rows.Add(new SummaryRow
                    {
                        Name = f.SubstrateId,
                        Stage = 1,
                        Judge = "處理中",
                        DateTime = "",
                        RowIndex = rowIndex++,
                    });
                }
                session.SubstrateCount = session.Rows.Select(r => r.Name)
                                                     .Distinct(StringComparer.OrdinalIgnoreCase).Count();
                if (session.LotName.Length == 0)
                    session.LotName = FileLocator.FormatLotForDisplay(lotIdLocal);
            }

            LotMonitor.Load(session);
            if (loadedSession != null)
                loaded.Add($"Summary({session.Rows.Count} rows/{session.SubstrateCount} substrates)");

            // --- Load .map files via FileLocator (handles both flat-legacy and timestamp-folder layouts) ---
            List<Models.SubstrateMap> substrateMaps = new();
            var mapFiles = await Task.Run(() =>
                FileLocator.EnumerateMapFilesForLot(AthleteSysPath, partNoLocal, lotIdLocal));
            if (mapFiles.Count > 0)
            {
                substrateMaps = await Task.Run(() =>
                    mapFiles.Select(f => SubstrateMapParser.Parse(f)).ToList());
                SubstrateMap.Load(substrateMaps);
                loaded.Add($"SubstrateMap({mapFiles.Count} maps)");
            }
            else
            {
                SubstrateMap.Load(Enumerable.Empty<Models.SubstrateMap>());
            }

            // --- Load Defect Map: calculate from .map files (INSPECTION=1 only); fall back to map.csv ---
            if (substrateMaps.Count > 0)
            {
                var mapData = DieMapCalculator.Calculate(substrateMaps, lotIdLocal, partNoLocal);
                DefectMap.Load(mapData);
                if (mapData != null)
                    loaded.Add($"DefectMap(calculated from {substrateMaps.Count} .map)");
            }
            else
            {
                var mapCsvCheck = FileLocator.FindMapCsv(AthleteSysPath, lotIdLocal);
                if (mapCsvCheck.Found)
                {
                    var mapData = await Task.Run(() => MapCsvParser.Parse(mapCsvCheck.ActualPath!));
                    DefectMap.Load(mapData);
                    loaded.Add("DefectMap(map.csv)");
                }
                else
                {
                    DefectMap.Load(null);
                }
            }

            // --- Load Recurring Defects: Phase 1 from .map (fast) ---
            if (substrateMaps.Count > 0)
            {
                var recurringData = RecurringDefectCalculator.CalculateFromMaps(substrateMaps, lotIdLocal);
                RecurringDefect.Load(recurringData, _masterBalls);

                // Phase 2: enrich with ball-level data from .afa files (background)
                if (recurringData != null)
                {
                    var partNo = partNoLocal;
                    var lotNo  = lotIdLocal;
                    var maps   = substrateMaps.ToList();
                    _ = Task.Run(() =>
                    {
                        var afas = new List<AfaFile>();
                        foreach (var m in maps)
                        {
                            var chk = FileLocator.FindAfaFile(AthleteSysPath, partNo, lotNo, m.SubstrateId);
                            if (chk.Found) afas.Add(AfaFileParser.Parse(chk.ActualPath!));
                        }
                        RecurringDefectCalculator.EnrichWithAfaData(recurringData, afas);
                    }).ContinueWith(_ =>
                        Application.Current.Dispatcher.Invoke(() => RecurringDefect.RefreshBallData()));
                }
            }
            else
            {
                RecurringDefect.Load(null, _masterBalls);
            }

            // --- Update Settings file status ---
            Settings.UpdateFileStatuses(AthleteSysPath, partNoLocal, lotIdLocal);

            // --- Build status text ---
            var sb = new StringBuilder();
            if (loaded.Count > 0)
                sb.Append(string.Join(" | ", loaded));

            if (missing.Count > 0)
            {
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append("MISSING: " + string.Join(", ", missing.Select(m => m.Split(':')[0])));
            }
            else
            {
                sb.Append(" | Ready");
            }

            StatusText = sb.ToString();

            // Show popup for missing required files
            if (missing.Count > 0)
            {
                var msg = new StringBuilder();
                msg.AppendLine("The following files were not found:\n");
                foreach (var m in missing)
                    msg.AppendLine($"  - {m}");
                msg.AppendLine();
                msg.AppendLine("Please check the directory structure:");
                msg.AppendLine($"  Master:  kbgadata (or KBGA Data)/{{PartNo}}/{{PartNo}}.csv");
                msg.AppendLine($"  Summary: kbgaresults/{{PartNo}}/{{LotNo}}/{{LotNo}}.summary.csv");
                msg.AppendLine($"  DefectMap: calculated from .map files in kbgaresults/{{PartNo}}/{{LotNo}}/");

                MessageBox.Show(msg.ToString(), "Missing Files", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── 跨 Tab 同步 ──────────────────────────────────────────────────

    /// <summary>Lot Monitor 單擊 → 高亮 Substrate Map</summary>
    private void OnLotMonitorRowSingleClicked(SummaryRow row)
    {
        SubstrateMap.HighlightSubstrate(row.SubstrateId);
    }

    /// <summary>Substrate Map 單擊 → 同步 Lot Monitor 選擇</summary>
    private void OnSubstrateMapSelected(string substrateId)
    {
        if (_programmaticNav) return;   // don't sync back during programmatic navigation
        LotMonitor.SelectBySubstrateId(substrateId);
    }

    /// <summary>Lot Monitor 雙擊 → 切到 Substrate Map (Tab 2) 並自動選取基板與對應 INSP</summary>
    private void OnSubstrateRowDoubleClicked(SummaryRow row)
    {
        // Use same fuzzy matching as HighlightSubstrate (EndsWith handles prefix/suffix differences)
        var map = SubstrateMap.SubstrateMaps.FirstOrDefault(m =>
            m.SubstrateId.EndsWith(row.SubstrateId, StringComparison.OrdinalIgnoreCase)
            || row.SubstrateId.EndsWith(m.SubstrateId, StringComparison.OrdinalIgnoreCase)
            || m.SubstrateId == row.SubstrateId);

        if (map == null)
        {
            StatusText = $"Substrate not found in map: {row.SubstrateId}";
            return;
        }

        _programmaticNav = true;
        SelectedTabIndex = 2;
        SubstrateMap.SelectedInspection = row.Stage;
        SubstrateMap.SelectedSubstrateMap = map;
        _programmaticNav = false;
        StatusText = $"Navigated to {row.SubstrateId} — INSP {row.Stage}";
    }

    /// <summary>Substrate Map 雙擊 → 載入 .afa → 切到 Substrate Viewer (Tab 3)</summary>
    private async void OnSubstrateMapDoubleClicked(Models.SubstrateMap map)
    {
        if (string.IsNullOrEmpty(SelectedPartNumber) || string.IsNullOrEmpty(SelectedLotNumber)) return;

        try
        {
            StatusText = "Loading substrate from map...";

            // In merged-lot sessions, each SubstrateMap carries its original SourceLotId
            // so AFA lookup walks the right folder. For regular lots SourceLotId is null
            // and we fall back to the currently-selected lot.
            var lotForAfa = map.SourceLotId ?? SelectedLotNumber;
            var afaCheck = FileLocator.FindAfaFile(AthleteSysPath, SelectedPartNumber, lotForAfa, map.SubstrateId);

            if (!afaCheck.Found)
            {
                StatusText = $"AFA not found: {afaCheck.ExpectedPath}";
                MessageBox.Show(
                    $"AFA file not found for substrate: {map.SubstrateId}\n\nSearched in:\n{afaCheck.ExpectedPath}",
                    "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var afa = await Task.Run(() => AfaFileParser.Parse(afaCheck.ActualPath!));
            SubstrateViewer.LoadAfa(afa, inspectionNumber: SubstrateMap.SelectedInspection);
            SelectedTabIndex = 3;  // Substrate Viewer is now tab index 3
            StatusText = afa.Inspections.Count == 0
                ? $"Loaded {Path.GetFileName(afaCheck.ActualPath)} — ⚠ AFA 無 INSPECTION 區塊（檔案可能仍在寫入）"
                : $"Loaded {Path.GetFileName(afaCheck.ActualPath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>多 Die 基板中雙擊特定 Die → 載入 .afa 並過濾至該 Die → 切到 Substrate Viewer</summary>
    private async void OnSubstrateMapDieDoubleClicked(Models.SubstrateMap map, int row, int col)
    {
        if (string.IsNullOrEmpty(SelectedPartNumber) || string.IsNullOrEmpty(SelectedLotNumber)) return;

        try
        {
            StatusText = "Loading die from map...";

            // Same merged-lot handling as OnSubstrateMapDoubleClicked.
            var lotForAfa = map.SourceLotId ?? SelectedLotNumber;
            var afaCheck = FileLocator.FindAfaFile(AthleteSysPath, SelectedPartNumber, lotForAfa, map.SubstrateId);

            if (!afaCheck.Found)
            {
                StatusText = $"AFA not found: {afaCheck.ExpectedPath}";
                MessageBox.Show(
                    $"AFA file not found for substrate: {map.SubstrateId}\n\nSearched in:\n{afaCheck.ExpectedPath}",
                    "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var afa = await Task.Run(() => AfaFileParser.Parse(afaCheck.ActualPath!));
            SubstrateViewer.LoadAfa(afa, inspectionNumber: SubstrateMap.SelectedInspection,
                                    dieCol: $"{col + 1}C", dieRow: $"{row + 1}R");
            SelectedTabIndex = 3;
            StatusText = $"Loaded {Path.GetFileName(afaCheck.ActualPath)} — Die [{row + 1},{col + 1}]";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Reload (rescan folders + re-read files) ───────────────────────

    /// <summary>
    /// 重新讀取資料 — 不只重讀檔案內容，也會 rescan 磁碟上是否新增了 Part# 或 Lot# 資料夾。
    /// 生產中按下時，若新批號剛產生，會立即出現在 ComboBox 中。
    /// <para>同時清掉 Lot/Part dropdown 的快取計數（生產中基板數量會變動），讓背景重新計算。</para>
    /// </summary>
    private void Reload()
    {
        if (string.IsNullOrEmpty(AthleteSysPath)) return;

        // 生產中磁碟上的基板數量會變動：清掉計數快取讓背景重算
        // (Refresh*Numbers 預設會把舊計數帶到新 item，避免排序時閃爍；Reload 場景需要強制清除)
        foreach (var p in PartNumbers) p.LotCount = null;
        foreach (var l in LotNumbers)
        {
            // 合併批的 SubstrateCount / CreatedAt 由快取維護，不可清掉；只清磁碟批的
            if (IsMergedLot(l.Id)) continue;
            l.SubstrateCount = null;
            l.CreatedAt = null;   // 讓背景重新從 CSV 取最新的 FirstDate
        }

        // Re-enumerate Part# folders (catches newly-created Parts)
        RefreshPartNumbers();

        // Re-enumerate Lot# folders for the current Part# (catches newly-created Lots)
        if (!string.IsNullOrEmpty(SelectedPartNumber))
            RefreshLotNumbers();

        // If still have a valid Part#/Lot#, re-read all files for them.
        if (!string.IsNullOrEmpty(SelectedPartNumber) && !string.IsNullOrEmpty(SelectedLotNumber))
            OnLotNumberChanged();
        else
            StatusText = "Reload: 已重新掃描資料夾。" +
                         (string.IsNullOrEmpty(SelectedPartNumber)
                             ? " 請選擇 Part#。"
                             : " 請選擇 Lot#。");
    }

    // ── 合併批號（Session-only 虛擬批）────────────────────────────────

    private static bool IsMergedLot(string id) =>
        !string.IsNullOrEmpty(id) && id.StartsWith(FileLocator.MergedLotPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Updates <c>LotMonitor.CanMergeLots</c>. Called after every replacement / mutation
    /// of <see cref="LotNumbers"/>. Merged-virtual-lot ids are excluded from the count.
    /// </summary>
    private void RefreshCanMergeLots()
    {
        LotMonitor.CanMergeLots = LotNumbers.Count(li => !IsMergedLot(li.Id)) >= 2;
    }

    /// <summary>
    /// 開啟另存新檔對話框，把 LotMonitor 目前可見狀態（已套用合併批 / 特殊統計 /
    /// Yield 選項 / Cycle Time）匯出成 CSV 報表。
    /// </summary>
    private void OnExportReportRequested()
    {
        var input = LotMonitor.BuildExportInput();
        if (input.Rows.Count == 0)
        {
            MessageBox.Show("目前沒有可匯出的 row。", "輸出報表",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "輸出 Lot Monitor 報表",
            Filter = "CSV 檔 (*.csv)|*.csv|所有檔案 (*.*)|*.*",
            DefaultExt = ".csv",
            FileName = LotReportExporter.SuggestedFileName(input.LotDisplayName),
            OverwritePrompt = true,
            AddExtension = true,
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            LotReportExporter.WriteCsv(dialog.FileName, input);
            StatusText = $"報表已輸出: {dialog.FileName}  ({input.Rows.Count} rows / {input.SubstrateCount} substrates)";
        }
        catch (Exception ex)
        {
            StatusText = $"輸出報表失敗: {ex.Message}";
            MessageBox.Show(ex.ToString(), "輸出報表失敗",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Opens the Mount filter (special statistics rule) dialog. On OK, push the new
    /// <see cref="MountFilter"/> into <c>LotMonitor.ActiveMountFilter</c> which triggers
    /// re-filtering of Rows and recalculation of the summaries.
    /// </summary>
    private void OnMountFilterRequested()
    {
        var dialog = new Views.MountFilterDialog(LotMonitor.ActiveMountFilter)
            { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;
        LotMonitor.ActiveMountFilter = dialog.Result;
    }

    /// <summary>Opens the merge dialog and, on OK, builds + selects a session-only virtual lot.</summary>
    private void OnMergeLotsRequested()
    {
        if (string.IsNullOrEmpty(SelectedPartNumber))
        {
            MessageBox.Show("請先選擇 Part# 後再合併。", "合併批號", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 傳完整來源（_allLotNumbers），而非分頁過的 LotNumbers，讓使用者在對話框內也能看到全部批號
        var dialog = new Views.LotMergeDialog(_allLotNumbers)
            { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;
        var picks = dialog.SelectedLotIds;
        if (picks.Count < 2) return;

        try
        {
            var merged = BuildMergedSession(picks);
            if (merged.Session.Rows.Count == 0)
            {
                MessageBox.Show("所選 Lot 皆無法讀取資料，已取消合併。", "合併批號",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var mergedId = FileLocator.EncodeMergedLot(DateTime.Now);
            _mergedSessions[mergedId] = merged;
            // Append directly (without re-sorting the whole list — preserve current order;
            // user can re-sort to bring the merged lot into its date group if desired).
            _allLotNumbers.Add(new LotListItem
            {
                Id = mergedId,
                SubstrateCount = merged.Session.SubstrateCount,
                CreatedAt = FileLocator.TryDecodeMergedLot(mergedId, out var mergeTs) ? mergeTs : DateTime.Now,
            });
            // 合併批是附加在末尾，若 _allLotNumbers 超過 page size 且尚未展開，它會落在分頁
            // 隱藏區。隨後 SelectedLotNumber=mergedId 時 ComboBox 在可見項找不到它，TwoWay
            // 綁定會把選取回退為原批號 → 自動展開讓新批可見並可被選取。
            if (!_lotsExpanded && _allLotNumbers.Count > LotPageSize)
                _lotsExpanded = true;
            RefreshLotNumbersView();
            RefreshCanMergeLots();
            SelectedLotNumber = mergedId;     // triggers OnLotNumberChanged → merged branch
        }
        catch (Exception ex)
        {
            StatusText = $"Merge failed: {ex.Message}";
            MessageBox.Show(ex.ToString(), "合併批號失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Reads each source lot's <c>.summary.csv</c> + <c>.map</c> files via the existing parsers
    /// and bundles them into a <see cref="MergedLotData"/>. Each loaded <c>SubstrateMap</c>
    /// is tagged with <c>SourceLotId</c> so downstream AFA lookups can resolve through the
    /// original lot's folder. No files are modified.
    /// <para>If <paramref name="lotIds"/> contains UMKF master ids (<c>__umkf__</c>), they are
    /// expanded to their child lot ids first — this is what makes 「合併批號」 dialog able to
    /// further merge already-auto-merged masters.</para>
    /// </summary>
    private MergedLotData BuildMergedSession(IReadOnlyList<string> lotIds)
    {
        // Expand UMKF masters to their constituent children before any disk I/O.
        var expanded = new List<string>();
        foreach (var id in lotIds)
        {
            if (FileLocator.TryDecodeUmkfMaster(id, out _) &&
                _umkfMasterToChildren.TryGetValue(id, out var kids))
            {
                expanded.AddRange(kids);
            }
            else
            {
                expanded.Add(id);
            }
        }

        var mergedRows = new List<SummaryRow>();
        var mergedMaps = new List<Models.SubstrateMap>();
        var sources = new List<string>();

        foreach (var lotId in expanded)
        {
            // --- Summary rows ---
            var chk = FileLocator.FindSummaryCsv(AthleteSysPath, SelectedPartNumber!, lotId);
            LotSession? part = null;
            if (chk.Found)
            {
                try
                {
                    part = FileLocator.TryDecodeVirtualDayLot(lotId, out var d)
                        ? SummaryCsvParser.ParseFilteredByDate(chk.ActualPath!, d)
                        : SummaryCsvParser.ParseFirstLot(chk.ActualPath!);
                }
                catch
                {
                    part = null;
                }
            }

            // --- Substrate .map files (one per substrate folder) ---
            List<Models.SubstrateMap> partMaps = new();
            try
            {
                var mapFiles = FileLocator.EnumerateMapFilesForLot(AthleteSysPath, SelectedPartNumber!, lotId);
                foreach (var f in mapFiles)
                {
                    try
                    {
                        var m = SubstrateMapParser.Parse(f);
                        m.SourceLotId = lotId;   // remember which lot this map came from
                        partMaps.Add(m);
                    }
                    catch { /* skip unparseable maps */ }
                }
            }
            catch { /* enumeration failure: skip */ }

            bool gotSomething = (part?.Rows.Count ?? 0) > 0 || partMaps.Count > 0;
            if (!gotSomething) continue;

            if (part != null && part.Rows.Count > 0)
                mergedRows.AddRange(part.Rows);
            mergedMaps.AddRange(partMaps);
            sources.Add(FileLocator.FormatLotForDisplay(lotId));
        }

        for (int i = 0; i < mergedRows.Count; i++)
            mergedRows[i].RowIndex = i;

        var now = DateTime.Now;
        var session = new LotSession
        {
            LotName = $"{FileLocator.MergedLotDisplayPrefix}{now:yyyy-MM-dd HH:mm}" +
                      (sources.Count > 0 ? $" ({sources.Count} lots: {string.Join(", ", sources)})" : ""),
            // Count by Name (unique full identifier, e.g., "0514Leg1-1" vs "0514Leg2-1")
            // not SubstrateId, which strips the lot prefix and collides across merged source lots.
            SubstrateCount = mergedRows.Select(r => r.Name)
                                       .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Rows = mergedRows,
            Summary = LotSummaryCalculator.Calculate(mergedRows)
        };
        return new MergedLotData { Session = session, SubstrateMaps = mergedMaps };
    }

    // ── 分頁載入 helpers ────────────────────────────────────────────────

    /// <summary>
    /// 設定 Part# 完整來源並重建可見視圖。<paramref name="resetExpanded"/>=true 表示是「新一批
    /// 資料」（換 source 路徑），需要從頭分頁；排序變化等情況傳 false 保留使用者已展開狀態。
    /// </summary>
    private void SetAllPartNumbers(IEnumerable<PartListItem> items, bool resetExpanded = true)
    {
        _allPartNumbers = items.ToList();
        if (resetExpanded) _partsExpanded = false;
        RefreshPartNumbersView();
    }

    private void RefreshPartNumbersView()
    {
        var capped = _partsExpanded || _allPartNumbers.Count <= PartPageSize
            ? new List<PartListItem>(_allPartNumbers)
            : _allPartNumbers.Take(PartPageSize).ToList();
        if (!_partsExpanded && _allPartNumbers.Count > PartPageSize)
        {
            capped.Add(new PartListItem
            {
                Name = LoadMoreSentinel.Id,
                IsLoadMore = true,
                RemainingCount = _allPartNumbers.Count - PartPageSize,
            });
        }
        PartNumbers = new ObservableCollection<PartListItem>(capped);
    }

    /// <summary>使用者點擊 Part# 的「載入更多」哨兵；展開後不再顯示哨兵。</summary>
    public void LoadMorePartNumbers()
    {
        if (_partsExpanded) return;
        _partsExpanded = true;
        RefreshPartNumbersView();
    }

    /// <summary>同 <see cref="SetAllPartNumbers"/>，作用於 Lot# 列表。</summary>
    private void SetAllLotNumbers(IEnumerable<LotListItem> items, bool resetExpanded = true)
    {
        _allLotNumbers = items.ToList();
        if (resetExpanded) _lotsExpanded = false;
        RefreshLotNumbersView();
    }

    private void RefreshLotNumbersView()
    {
        var capped = _lotsExpanded || _allLotNumbers.Count <= LotPageSize
            ? new List<LotListItem>(_allLotNumbers)
            : _allLotNumbers.Take(LotPageSize).ToList();
        if (!_lotsExpanded && _allLotNumbers.Count > LotPageSize)
        {
            capped.Add(new LotListItem
            {
                Id = LoadMoreSentinel.Id,
                IsLoadMore = true,
                RemainingCount = _allLotNumbers.Count - LotPageSize,
            });
        }
        LotNumbers = new ObservableCollection<LotListItem>(capped);
    }

    /// <summary>使用者點擊 Lot# 的「載入更多」哨兵。</summary>
    public void LoadMoreLotNumbers()
    {
        if (_lotsExpanded) return;
        _lotsExpanded = true;
        RefreshLotNumbersView();
    }
}
