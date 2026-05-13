using System.Collections.ObjectModel;
using System.Text;
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

    /// <summary>Session-only cache of merged virtual lots, keyed by their <c>__merged__</c> id.</summary>
    private readonly Dictionary<string, LotSession> _mergedSessions =
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

    private ObservableCollection<string> _partNumbers = new();
    public ObservableCollection<string> PartNumbers
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

    private ObservableCollection<string> _lotNumbers = new();
    public ObservableCollection<string> LotNumbers
    {
        get => _lotNumbers;
        set => SetProperty(ref _lotNumbers, value);
    }

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

            if (!string.IsNullOrEmpty(_settings.LastPartNumber) && PartNumbers.Contains(_settings.LastPartNumber))
            {
                _selectedPartNumber = _settings.LastPartNumber;
                OnPropertyChanged(nameof(SelectedPartNumber));
                OnPartNumberChanged();

                if (!string.IsNullOrEmpty(_settings.LastLotNumber) && LotNumbers.Contains(_settings.LastLotNumber))
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
        if (!string.IsNullOrEmpty(analysis.PartNumber) && PartNumbers.Contains(analysis.PartNumber))
        {
            SelectedPartNumber = analysis.PartNumber;

            // Auto-select lot number if detected (OnPartNumberChanged already populated LotNumbers)
            if (!string.IsNullOrEmpty(analysis.LotNumber) && LotNumbers.Contains(analysis.LotNumber))
            {
                SelectedLotNumber = analysis.LotNumber;
            }
        }
    }

    private void OnPathChanged()
    {
        var parts = FileLocator.GetPartNumbers(AthleteSysPath, PartSortMode);
        PartNumbers = new ObservableCollection<string>(parts);
        SelectedPartNumber = null;
        LotNumbers.Clear();
        SelectedLotNumber = null;
        Settings.Clear();
        _mergedSessions.Clear();   // path change invalidates all merged sessions
        RefreshCanMergeLots();
    }

    /// <summary>
    /// Re-sort the Part# list without losing the current selection or triggering a
    /// data reload. Called when the user picks a different Part sort mode.
    /// </summary>
    private void RefreshPartNumbers()
    {
        var current = SelectedPartNumber;
        var parts = FileLocator.GetPartNumbers(AthleteSysPath, PartSortMode);
        PartNumbers = new ObservableCollection<string>(parts);

        // Preserve selection without re-firing OnPartNumberChanged (which would reload Master/Lot data)
        if (current != null && parts.Contains(current))
        {
            _selectedPartNumber = current;
            OnPropertyChanged(nameof(SelectedPartNumber));
        }
        else
        {
            _selectedPartNumber = null;
            OnPropertyChanged(nameof(SelectedPartNumber));
        }
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
        var lots = FileLocator.GetLotNumbers(AthleteSysPath, SelectedPartNumber, LotSortMode).ToList();
        // Preserve any session-only merged lots so re-sorting doesn't drop them.
        foreach (var mergedId in _mergedSessions.Keys)
            if (!lots.Contains(mergedId, StringComparer.OrdinalIgnoreCase))
                lots.Add(mergedId);
        LotNumbers = new ObservableCollection<string>(lots);

        if (current != null && lots.Contains(current, StringComparer.OrdinalIgnoreCase))
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
    }

    private async void OnPartNumberChanged()
    {
        if (string.IsNullOrEmpty(SelectedPartNumber)) return;

        // Part# change invalidates session-only merged lots from the previous Part#.
        _mergedSessions.Clear();

        var lots = FileLocator.GetLotNumbers(AthleteSysPath, SelectedPartNumber, LotSortMode);
        LotNumbers = new ObservableCollection<string>(lots);
        SelectedLotNumber = null;
        RefreshCanMergeLots();

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
            _mergedSessions.TryGetValue(SelectedLotNumber!, out var mergedSession))
        {
            try
            {
                StatusText = "Loading merged lot...";
                LotMonitor.Load(mergedSession);
                SubstrateMap.Load(Enumerable.Empty<Models.SubstrateMap>());
                DefectMap.Load(null);
                RecurringDefect.Load(null, _masterBalls);
                Settings.UpdateFileStatuses(AthleteSysPath, SelectedPartNumber, SelectedLotNumber);
                StatusText = $"Merged lot loaded: {mergedSession.Rows.Count} rows / {mergedSession.SubstrateCount} substrates (in-memory)";
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
                session.SubstrateCount = session.Rows.Select(r => r.SubstrateId).Distinct().Count();
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

            var afaCheck = FileLocator.FindAfaFile(AthleteSysPath, SelectedPartNumber, SelectedLotNumber, map.SubstrateId);

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
            StatusText = $"Loaded {Path.GetFileName(afaCheck.ActualPath)}";
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

            var afaCheck = FileLocator.FindAfaFile(AthleteSysPath, SelectedPartNumber, SelectedLotNumber, map.SubstrateId);

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
    /// </summary>
    private void Reload()
    {
        if (string.IsNullOrEmpty(AthleteSysPath)) return;

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
        LotMonitor.CanMergeLots = LotNumbers.Count(id => !IsMergedLot(id)) >= 2;
    }

    /// <summary>Opens the merge dialog and, on OK, builds + selects a session-only virtual lot.</summary>
    private void OnMergeLotsRequested()
    {
        if (string.IsNullOrEmpty(SelectedPartNumber))
        {
            MessageBox.Show("請先選擇 Part# 後再合併。", "合併批號", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Views.LotMergeDialog(LotNumbers) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;
        var picks = dialog.SelectedLotIds;
        if (picks.Count < 2) return;

        try
        {
            var merged = BuildMergedSession(picks);
            if (merged.Rows.Count == 0)
            {
                MessageBox.Show("所選 Lot 皆無法讀取資料，已取消合併。", "合併批號",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var mergedId = FileLocator.EncodeMergedLot(DateTime.Now);
            _mergedSessions[mergedId] = merged;
            LotNumbers.Add(mergedId);
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
    /// Reads each source lot's <c>.summary.csv</c> via the existing parser path and concatenates
    /// the Rows into a single <see cref="LotSession"/>. No files are modified.
    /// </summary>
    private LotSession BuildMergedSession(IReadOnlyList<string> lotIds)
    {
        var merged = new List<SummaryRow>();
        var sources = new List<string>();

        foreach (var lotId in lotIds)
        {
            var chk = FileLocator.FindSummaryCsv(AthleteSysPath, SelectedPartNumber!, lotId);
            if (!chk.Found) continue;

            LotSession part;
            try
            {
                part = FileLocator.TryDecodeVirtualDayLot(lotId, out var d)
                    ? SummaryCsvParser.ParseFilteredByDate(chk.ActualPath!, d)
                    : SummaryCsvParser.ParseFirstLot(chk.ActualPath!);
            }
            catch
            {
                continue;
            }

            if (part.Rows.Count == 0) continue;
            merged.AddRange(part.Rows);
            sources.Add(FileLocator.FormatLotForDisplay(lotId));
        }

        for (int i = 0; i < merged.Count; i++)
            merged[i].RowIndex = i;

        var now = DateTime.Now;
        var session = new LotSession
        {
            LotName = $"{FileLocator.MergedLotDisplayPrefix}{now:yyyy-MM-dd HH:mm}" +
                      (sources.Count > 0 ? $" ({sources.Count} lots: {string.Join(", ", sources)})" : ""),
            SubstrateCount = merged.Select(r => r.SubstrateId).Distinct().Count(),
            Rows = merged,
            Summary = LotSummaryCalculator.Calculate(merged)
        };
        return session;
    }
}
