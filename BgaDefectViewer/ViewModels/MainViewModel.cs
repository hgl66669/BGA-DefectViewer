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

    /// <summary>Session-only cache of merged virtual lots, keyed by their <c>__merged__</c> id.</summary>
    private readonly Dictionary<string, MergedLotData> _mergedSessions =
        new(StringComparer.OrdinalIgnoreCase);

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

    private string? _selectedLotNumber;
    public string? SelectedLotNumber
    {
        get => _selectedLotNumber;
        set
        {
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
                PartNumbers.Any(p => p.Name == _settings.LastPartNumber))
            {
                _selectedPartNumber = _settings.LastPartNumber;
                OnPropertyChanged(nameof(SelectedPartNumber));
                OnPartNumberChanged();

                if (!string.IsNullOrEmpty(_settings.LastLotNumber) &&
                    LotNumbers.Any(l => l.Id == _settings.LastLotNumber))
                {
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
            PartNumbers.Any(p => p.Name == analysis.PartNumber))
        {
            SelectedPartNumber = analysis.PartNumber;

            // Auto-select lot number if detected (OnPartNumberChanged already populated LotNumbers)
            if (!string.IsNullOrEmpty(analysis.LotNumber) &&
                LotNumbers.Any(l => l.Id == analysis.LotNumber))
            {
                SelectedLotNumber = analysis.LotNumber;
            }
        }
    }

    private void OnPathChanged()
    {
        var partNames = FileLocator.GetPartNumbers(AthleteSysPath, PartSortMode);
        PartNumbers = new ObservableCollection<PartListItem>(
            partNames.Select(n => new PartListItem { Name = n }));
        SelectedPartNumber = null;
        LotNumbers.Clear();
        SelectedLotNumber = null;
        Settings.Clear();
        _mergedSessions.Clear();   // path change invalidates all merged sessions
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
        PartNumbers = new ObservableCollection<PartListItem>(items);

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

    private static List<LotListItem> ApplyLotCountSort(List<LotListItem> items, FolderSortMode mode)
    {
        if (mode != FolderSortMode.CountDesc && mode != FolderSortMode.CountAsc) return items;
        var withCount = items.Where(i => i.SubstrateCount.HasValue).ToList();
        var without = items.Where(i => !i.SubstrateCount.HasValue).ToList();
        withCount = (mode == FolderSortMode.CountDesc
                        ? withCount.OrderByDescending(i => i.SubstrateCount!.Value).ThenBy(i => i.Id)
                        : withCount.OrderBy(i => i.SubstrateCount!.Value).ThenBy(i => i.Id))
                    .ToList();
        return withCount.Concat(without).ToList();
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
        var items = PartNumbers.ToList();
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
                        var sorted = ApplyPartCountSort(PartNumbers.ToList(), PartSortMode);
                        PartNumbers = new ObservableCollection<PartListItem>(sorted);
                    }
                });
            }
        }, token);
    }

    /// <summary>
    /// 背景列舉 LotNumbers 中每個 Lot 的基板筆數，依序回灌。
    /// 合併批（<c>__merged__</c>）跳過 — 其計數於 RefreshLotNumbers 已直接由快取帶入。
    /// </summary>
    private void StartLoadingLotCounts()
    {
        _lotCountCts?.Cancel();
        _lotCountCts = new CancellationTokenSource();
        var token = _lotCountCts.Token;
        var path = AthleteSysPath;
        var partNo = SelectedPartNumber;
        var items = LotNumbers.ToList();
        var dispatcher = Application.Current.Dispatcher;

        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(partNo) || items.Count == 0) return;

        Task.Run(() =>
        {
            foreach (var item in items)
            {
                if (token.IsCancellationRequested) return;
                if (item.SubstrateCount.HasValue) continue;   // 已有值（如合併批）→ 跳過
                int count;
                try { count = FileLocator.CountSubstratesForLot(path, partNo, item.Id); }
                catch { count = 0; }
                if (token.IsCancellationRequested) return;
                dispatcher.BeginInvoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    item.SubstrateCount = count;
                });
            }
            if (!token.IsCancellationRequested)
            {
                dispatcher.BeginInvoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    if (LotSortMode is FolderSortMode.CountDesc or FolderSortMode.CountAsc)
                    {
                        var current = SelectedLotNumber;
                        var sorted = ApplyLotCountSort(LotNumbers.ToList(), LotSortMode);
                        LotNumbers = new ObservableCollection<LotListItem>(sorted);
                        // 保留選取
                        if (current != null && LotNumbers.Any(l => l.Id == current))
                        {
                            _selectedLotNumber = current;
                            OnPropertyChanged(nameof(SelectedLotNumber));
                        }
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
            LotNumbers.Clear();
            RefreshCanMergeLots();
            return;
        }

        var current = SelectedLotNumber;
        var lotIds = FileLocator.GetLotNumbers(AthleteSysPath, SelectedPartNumber, LotSortMode).ToList();
        // Preserve any session-only merged lots so re-sorting doesn't drop them.
        foreach (var mergedId in _mergedSessions.Keys)
            if (!lotIds.Contains(mergedId, StringComparer.OrdinalIgnoreCase))
                lotIds.Add(mergedId);

        // 嘗試保留已載入的基板計數（避免重排序時把計數歸零）
        var existingCounts = LotNumbers.ToDictionary(li => li.Id, li => li.SubstrateCount,
                                                     StringComparer.OrdinalIgnoreCase);
        var items = lotIds.Select(id =>
        {
            var item = new LotListItem { Id = id };
            if (_mergedSessions.TryGetValue(id, out var mg))
                item.SubstrateCount = mg.Session.SubstrateCount;
            else if (existingCounts.TryGetValue(id, out var c))
                item.SubstrateCount = c;
            return item;
        }).ToList();
        items = ApplyLotCountSort(items, LotSortMode);
        LotNumbers = new ObservableCollection<LotListItem>(items);

        if (current != null && lotIds.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            _selectedLotNumber = current;
            OnPropertyChanged(nameof(SelectedLotNumber));
        }
        else
        {
            _selectedLotNumber = null;
            OnPropertyChanged(nameof(SelectedLotNumber));
        }
        RefreshCanMergeLots();
        StartLoadingLotCounts();
    }

    private async void OnPartNumberChanged()
    {
        if (string.IsNullOrEmpty(SelectedPartNumber)) return;

        // Part# change invalidates session-only merged lots from the previous Part#.
        _mergedSessions.Clear();

        var lotIds = FileLocator.GetLotNumbers(AthleteSysPath, SelectedPartNumber, LotSortMode);
        var items = lotIds.Select(id => new LotListItem { Id = id }).ToList();
        items = ApplyLotCountSort(items, LotSortMode);
        LotNumbers = new ObservableCollection<LotListItem>(items);
        SelectedLotNumber = null;
        RefreshCanMergeLots();
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

        // ── Session-only merged lot: load from in-memory cache, skip all disk I/O ──
        if (FileLocator.TryDecodeMergedLot(SelectedLotNumber!, out _) &&
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

        try
        {
            StatusText = "Loading...";
            var missing = new List<string>();
            var loaded = new List<string>();

            // --- Load Master ---
            var masterCheck = FileLocator.FindMasterCsv(AthleteSysPath, SelectedPartNumber);
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
            bool isVirtualDay = FileLocator.TryDecodeVirtualDayLot(SelectedLotNumber, out var virtualDate);
            var summaryCheck = FileLocator.FindSummaryCsv(AthleteSysPath, SelectedPartNumber, SelectedLotNumber);
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
                FileLocator.EnumerateSubstratesForLot(AthleteSysPath, SelectedPartNumber, SelectedLotNumber));

            // For virtual-day / new-format real lots, augment session.Rows with stub rows for any
            // substrate folder that has no matching summary entry yet.
            var session = loadedSession ?? new LotSession { LotName = FileLocator.FormatLotForDisplay(SelectedLotNumber) };
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
                    session.LotName = FileLocator.FormatLotForDisplay(SelectedLotNumber);
            }

            LotMonitor.Load(session);
            if (loadedSession != null)
                loaded.Add($"Summary({session.Rows.Count} rows/{session.SubstrateCount} substrates)");

            // --- Load .map files via FileLocator (handles both flat-legacy and timestamp-folder layouts) ---
            List<Models.SubstrateMap> substrateMaps = new();
            var mapFiles = await Task.Run(() =>
                FileLocator.EnumerateMapFilesForLot(AthleteSysPath, SelectedPartNumber, SelectedLotNumber));
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
                var mapData = DieMapCalculator.Calculate(substrateMaps, SelectedLotNumber, SelectedPartNumber);
                DefectMap.Load(mapData);
                if (mapData != null)
                    loaded.Add($"DefectMap(calculated from {substrateMaps.Count} .map)");
            }
            else
            {
                var mapCsvCheck = FileLocator.FindMapCsv(AthleteSysPath, SelectedLotNumber);
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
                var recurringData = RecurringDefectCalculator.CalculateFromMaps(substrateMaps, SelectedLotNumber);
                RecurringDefect.Load(recurringData, _masterBalls);

                // Phase 2: enrich with ball-level data from .afa files (background)
                if (recurringData != null)
                {
                    var partNo = SelectedPartNumber!;
                    var lotNo  = SelectedLotNumber!;
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
            Settings.UpdateFileStatuses(AthleteSysPath, SelectedPartNumber, SelectedLotNumber);

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
            // 合併批的 SubstrateCount 由快取維護，不可清掉；只清磁碟批的
            if (!IsMergedLot(l.Id))
                l.SubstrateCount = null;

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

        var dialog = new Views.LotMergeDialog(LotNumbers.Select(li => li.Id))
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
            LotNumbers.Add(new LotListItem
            {
                Id = mergedId,
                SubstrateCount = merged.Session.SubstrateCount
            });
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
    /// </summary>
    private MergedLotData BuildMergedSession(IReadOnlyList<string> lotIds)
    {
        var mergedRows = new List<SummaryRow>();
        var mergedMaps = new List<Models.SubstrateMap>();
        var sources = new List<string>();

        foreach (var lotId in lotIds)
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
}
