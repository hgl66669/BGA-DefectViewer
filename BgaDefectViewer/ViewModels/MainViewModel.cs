using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;
using BgaDefectViewer.Parsers;

namespace BgaDefectViewer.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private MasterBall[]? _masterBalls;
    private bool _programmaticNav;

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

    public ICommand BrowsePathCommand { get; }

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

        BrowsePathCommand = new RelayCommand(BrowsePath);

        // ── 事件訂閱 ──────────────────────────────────────────────────
        LotMonitor.RowDoubleClicked += OnSubstrateRowDoubleClicked;
        LotMonitor.RowSingleClicked += OnLotMonitorRowSingleClicked;

        SubstrateMap.SubstrateSelected += OnSubstrateMapSelected;
        SubstrateMap.SubstrateDoubleClicked += OnSubstrateMapDoubleClicked;
        SubstrateMap.DieDoubleClicked += OnSubstrateMapDieDoubleClicked;

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
        var parts = FileLocator.GetPartNumbers(AthleteSysPath);
        PartNumbers = new ObservableCollection<string>(parts);
        SelectedPartNumber = null;
        LotNumbers.Clear();
        SelectedLotNumber = null;
        Settings.Clear();
    }

    private async void OnPartNumberChanged()
    {
        if (string.IsNullOrEmpty(SelectedPartNumber)) return;

        var lots = FileLocator.GetLotNumbers(AthleteSysPath, SelectedPartNumber);
        LotNumbers = new ObservableCollection<string>(lots);
        SelectedLotNumber = null;

        // Load master CSV immediately so Substrate Viewer shows coordinate map
        var masterCheck = FileLocator.FindMasterCsv(AthleteSysPath, SelectedPartNumber);
        if (masterCheck.Found)
        {
            _masterBalls = await Task.Run(() => MasterCsvParser.Parse(masterCheck.ActualPath!));
            SubstrateViewer.LoadMaster(_masterBalls);
            OverlapInspection.LoadMaster(_masterBalls);
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
                SubstrateViewer.LoadMaster(_masterBalls);
                OverlapInspection.LoadMaster(_masterBalls);
                loaded.Add($"Master({_masterBalls.Length} balls)");
            }
            else
            {
                _masterBalls = null;
                missing.Add($"Master: {masterCheck.ExpectedPath}");
            }

            // --- Load Summary ---
            var summaryCheck = FileLocator.FindSummaryCsv(AthleteSysPath, SelectedPartNumber, SelectedLotNumber);
            if (summaryCheck.Found)
            {
                var session = await Task.Run(() => SummaryCsvParser.ParseFirstLot(summaryCheck.ActualPath!));
                LotMonitor.Load(session);
                loaded.Add($"Summary({session.Rows.Count} rows/{session.SubstrateCount} substrates)");
            }
            else
            {
                LotMonitor.Load(new LotSession());
                missing.Add($"Summary: {summaryCheck.ExpectedPath}");
            }

            // --- Load .map files (Substrate Map tab + Defect Map primary source) ---
            List<Models.SubstrateMap> substrateMaps = new();
            var resultDir = FileLocator.GetResultDir(AthleteSysPath, SelectedPartNumber, SelectedLotNumber);
            if (Directory.Exists(resultDir))
            {
                var mapFiles = Directory.GetFiles(resultDir, "*.map");
                if (mapFiles.Length > 0)
                {
                    substrateMaps = await Task.Run(() =>
                        mapFiles.Select(f => SubstrateMapParser.Parse(f)).ToList());
                    SubstrateMap.Load(substrateMaps);
                    loaded.Add($"SubstrateMap({mapFiles.Length} maps)");
                }
                else
                {
                    SubstrateMap.Load(Enumerable.Empty<Models.SubstrateMap>());
                }
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

}
