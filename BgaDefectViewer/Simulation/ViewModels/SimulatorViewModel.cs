using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Simulation.Generators;
using BgaDefectViewer.Simulation.Layouts;
using BgaDefectViewer.Simulation.Models;

namespace BgaDefectViewer.Simulation.ViewModels;

public class SimulatorViewModel : ViewModelBase
{
    private const int AutoRegenPadThreshold = 100_000;

    private readonly DispatcherTimer _debounceTimer;
    private bool _generating;
    private bool _pendingRegen;

    public SimulatorViewModel()
    {
        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            _ = RunGenerateAsync();
        };

        Layouts = new ObservableCollection<IPadLayout>(LayoutRegistry.All);
        SelectedLayout = Layouts[0];

        GenerateCommand = new RelayCommand(_ => _ = RunGenerateAsync(), _ => !_generating);
        ResetViewCommand = new RelayCommand(_ => ResetViewRequested?.Invoke());
        // Pad-follow rule of thumb: Pad copper opening is normally cut 10 µm
        // larger than the nominal ball diameter (per KBGA recipe convention).
        FollowBlobDiameterCommand = new RelayCommand(
            _ => PadDiameter = Math.Round(_blobDiameterMean + 0.010, 4));

        // Kick off initial generation so canvas isn't empty
        ScheduleRegenerate();
    }

    public event Action<SimulationFrame>? FrameUpdated;
    public event Action? ResetViewRequested;

    public ObservableCollection<IPadLayout> Layouts { get; }

    private IPadLayout _selectedLayout = null!;
    public IPadLayout SelectedLayout
    {
        get => _selectedLayout;
        set { if (SetProperty(ref _selectedLayout, value)) ScheduleRegenerate(true); }
    }

    // ── Grid ───────────────────────────────────────────────────────────────
    private int _rows = 10;
    public int Rows
    {
        get => _rows;
        set { if (SetProperty(ref _rows, Math.Clamp(value, 1, 1000))) { OnPropertyChanged(nameof(TotalPadsText)); ScheduleRegenerate(true); } }
    }

    private int _cols = 10;
    public int Cols
    {
        get => _cols;
        set { if (SetProperty(ref _cols, Math.Clamp(value, 1, 1000))) { OnPropertyChanged(nameof(TotalPadsText)); ScheduleRegenerate(true); } }
    }

    private double _pitchX = 0.12;
    public double PitchX { get => _pitchX; set { if (SetProperty(ref _pitchX, value)) ScheduleRegenerate(true); } }

    private double _pitchY = 0.12;
    public double PitchY { get => _pitchY; set { if (SetProperty(ref _pitchY, value)) ScheduleRegenerate(true); } }

    private double _staggerOffsetX = 0.06;
    public double StaggerOffsetX { get => _staggerOffsetX; set { if (SetProperty(ref _staggerOffsetX, value)) ScheduleRegenerate(true); } }

    // ── Master ─────────────────────────────────────────────────────────────
    private double _masterDiameter = 0.08;
    public double MasterDiameter { get => _masterDiameter; set { if (SetProperty(ref _masterDiameter, value)) ScheduleRegenerate(); } }

    private double _masterDiameterStdDev = 0.0;
    public double MasterDiameterStdDev { get => _masterDiameterStdDev; set { if (SetProperty(ref _masterDiameterStdDev, value)) ScheduleRegenerate(); } }

    // ── Pad (copper recess) ────────────────────────────────────────────────
    private bool _padEnabled = true;
    public bool PadEnabled { get => _padEnabled; set { if (SetProperty(ref _padEnabled, value)) ScheduleRegenerate(); } }

    private double _padDiameter = 0.090;
    public double PadDiameter { get => _padDiameter; set { if (SetProperty(ref _padDiameter, value)) ScheduleRegenerate(); } }

    private int _padDepthUm = 20;
    public int PadDepthUm { get => _padDepthUm; set { if (SetProperty(ref _padDepthUm, value)) ScheduleRegenerate(); } }

    private double _padEdgeSoftness = 0.15;
    public double PadEdgeSoftness { get => _padEdgeSoftness; set { if (SetProperty(ref _padEdgeSoftness, value)) ScheduleRegenerate(); } }

    private double _padCenterDimming = 0.3;
    public double PadCenterDimming { get => _padCenterDimming; set { if (SetProperty(ref _padCenterDimming, value)) ScheduleRegenerate(); } }

    private double _padTextureAmount = 0.4;
    public double PadTextureAmount { get => _padTextureAmount; set { if (SetProperty(ref _padTextureAmount, value)) ScheduleRegenerate(); } }

    private byte _sensorReadNoise = 2;
    public byte SensorReadNoise { get => _sensorReadNoise; set { if (SetProperty(ref _sensorReadNoise, value)) ScheduleRegenerate(); } }

    private double _sensorShotNoise = 0.05;
    public double SensorShotNoise { get => _sensorShotNoise; set { if (SetProperty(ref _sensorShotNoise, value)) ScheduleRegenerate(); } }

    // ── Blob ───────────────────────────────────────────────────────────────
    private double _blobDiameterMean = 0.08;
    public double BlobDiameterMean { get => _blobDiameterMean; set { if (SetProperty(ref _blobDiameterMean, value)) ScheduleRegenerate(); } }

    private double _blobDiameterStdDev = 0.0;
    public double BlobDiameterStdDev { get => _blobDiameterStdDev; set { if (SetProperty(ref _blobDiameterStdDev, value)) ScheduleRegenerate(); } }

    private double _blobAcircularityMean = 1.2;
    public double BlobAcircularityMean { get => _blobAcircularityMean; set { if (SetProperty(ref _blobAcircularityMean, value)) ScheduleRegenerate(); } }

    private double _blobAcircularityStdDev = 0.1;
    public double BlobAcircularityStdDev { get => _blobAcircularityStdDev; set { if (SetProperty(ref _blobAcircularityStdDev, value)) ScheduleRegenerate(); } }

    private double _blobShapeDeformationMean = 0.02;
    public double BlobShapeDeformationMean { get => _blobShapeDeformationMean; set { if (SetProperty(ref _blobShapeDeformationMean, value)) ScheduleRegenerate(); } }

    private double _blobShapeDeformationStdDev = 0.01;
    public double BlobShapeDeformationStdDev { get => _blobShapeDeformationStdDev; set { if (SetProperty(ref _blobShapeDeformationStdDev, value)) ScheduleRegenerate(); } }

    private double _blobScoreMean = 1.0;
    public double BlobScoreMean { get => _blobScoreMean; set { if (SetProperty(ref _blobScoreMean, value)) ScheduleRegenerate(); } }

    private double _blobScoreStdDev = 1.0;
    public double BlobScoreStdDev { get => _blobScoreStdDev; set { if (SetProperty(ref _blobScoreStdDev, value)) ScheduleRegenerate(); } }

    private byte _blobBrightnessMean = 240;
    public byte BlobBrightnessMean { get => _blobBrightnessMean; set { if (SetProperty(ref _blobBrightnessMean, value)) ScheduleRegenerate(); } }

    private byte _blobBrightnessStdDev = 0;
    public byte BlobBrightnessStdDev { get => _blobBrightnessStdDev; set { if (SetProperty(ref _blobBrightnessStdDev, value)) ScheduleRegenerate(); } }

    private byte _backgroundBrightness = 30;
    public byte BackgroundBrightness { get => _backgroundBrightness; set { if (SetProperty(ref _backgroundBrightness, value)) ScheduleRegenerate(); } }

    // ── Mode ───────────────────────────────────────────────────────────────
    private SimulationMode _mode = SimulationMode.AllPresent;
    public SimulationMode Mode
    {
        get => _mode;
        set
        {
            if (!SetProperty(ref _mode, value)) return;
            OnPropertyChanged(nameof(IsAllPresent));
            OnPropertyChanged(nameof(IsRandomOffset));
            OnPropertyChanged(nameof(IsRandomMissing));
            ScheduleRegenerate();
        }
    }

    public bool IsAllPresent
    {
        get => _mode == SimulationMode.AllPresent;
        set { if (value) Mode = SimulationMode.AllPresent; }
    }
    public bool IsRandomOffset
    {
        get => _mode == SimulationMode.RandomOffset;
        set { if (value) Mode = SimulationMode.RandomOffset; }
    }
    public bool IsRandomMissing
    {
        get => _mode == SimulationMode.RandomMissing;
        set { if (value) Mode = SimulationMode.RandomMissing; }
    }

    // ── Offset sub-params ──────────────────────────────────────────────────
    private QuantityMode _offsetQuantityMode = QuantityMode.Probability;
    public QuantityMode OffsetQuantityMode
    {
        get => _offsetQuantityMode;
        set
        {
            if (!SetProperty(ref _offsetQuantityMode, value)) return;
            OnPropertyChanged(nameof(OffsetUseProbability));
            OnPropertyChanged(nameof(OffsetUseCount));
            ScheduleRegenerate();
        }
    }

    public bool OffsetUseProbability
    {
        get => _offsetQuantityMode == QuantityMode.Probability;
        set { if (value) OffsetQuantityMode = QuantityMode.Probability; }
    }
    public bool OffsetUseCount
    {
        get => _offsetQuantityMode == QuantityMode.AbsoluteCount;
        set { if (value) OffsetQuantityMode = QuantityMode.AbsoluteCount; }
    }

    private double _offsetProbability = 0.02;
    public double OffsetProbability { get => _offsetProbability; set { if (SetProperty(ref _offsetProbability, value)) ScheduleRegenerate(); } }

    private int _offsetCount = 100;
    public int OffsetCount { get => _offsetCount; set { if (SetProperty(ref _offsetCount, value)) ScheduleRegenerate(); } }

    private double _offsetMinMm = 0.05;
    public double OffsetMinMm { get => _offsetMinMm; set { if (SetProperty(ref _offsetMinMm, value)) ScheduleRegenerate(); } }

    private double _offsetMaxMm = 0.15;
    public double OffsetMaxMm { get => _offsetMaxMm; set { if (SetProperty(ref _offsetMaxMm, value)) ScheduleRegenerate(); } }

    private bool _enableCollision = true;
    public bool EnableCollision { get => _enableCollision; set { if (SetProperty(ref _enableCollision, value)) ScheduleRegenerate(); } }

    private double _collisionGapMean = 0.0;
    public double CollisionGapMean { get => _collisionGapMean; set { if (SetProperty(ref _collisionGapMean, value)) ScheduleRegenerate(); } }

    private double _collisionGapVariance = 0.005;
    public double CollisionGapVariance { get => _collisionGapVariance; set { if (SetProperty(ref _collisionGapVariance, value)) ScheduleRegenerate(); } }

    // ── Missing sub-params ─────────────────────────────────────────────────
    private QuantityMode _missingQuantityMode = QuantityMode.Probability;
    public QuantityMode MissingQuantityMode
    {
        get => _missingQuantityMode;
        set
        {
            if (!SetProperty(ref _missingQuantityMode, value)) return;
            OnPropertyChanged(nameof(MissingUseProbability));
            OnPropertyChanged(nameof(MissingUseCount));
            ScheduleRegenerate();
        }
    }

    public bool MissingUseProbability
    {
        get => _missingQuantityMode == QuantityMode.Probability;
        set { if (value) MissingQuantityMode = QuantityMode.Probability; }
    }
    public bool MissingUseCount
    {
        get => _missingQuantityMode == QuantityMode.AbsoluteCount;
        set { if (value) MissingQuantityMode = QuantityMode.AbsoluteCount; }
    }

    private double _missingProbability = 0.02;
    public double MissingProbability { get => _missingProbability; set { if (SetProperty(ref _missingProbability, value)) ScheduleRegenerate(); } }

    private int _missingCount = 100;
    public int MissingCount { get => _missingCount; set { if (SetProperty(ref _missingCount, value)) ScheduleRegenerate(); } }

    // ── Other ──────────────────────────────────────────────────────────────
    private int _seed = 42;
    public int Seed { get => _seed; set { if (SetProperty(ref _seed, value)) ScheduleRegenerate(); } }

    private double _mmPerPixel = 0.00666;
    public double MmPerPixel { get => _mmPerPixel; set { if (SetProperty(ref _mmPerPixel, value)) ScheduleRegenerate(); } }

    private string _statusText = "Ready";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public string TotalPadsText => $"{(_rows * _cols):N0} pads";

    public bool AutoRegenerateAllowed => _rows * _cols <= AutoRegenPadThreshold;
    public string AutoRegenerateHint => AutoRegenerateAllowed
        ? "Auto regenerates on change."
        : $"Auto-regen disabled above {AutoRegenPadThreshold:N0} pads — press Generate.";

    public ICommand GenerateCommand { get; }
    public ICommand ResetViewCommand { get; }
    public ICommand FollowBlobDiameterCommand { get; }

    public SimulationParams BuildParams() => new()
    {
        Layout = _selectedLayout?.Type ?? LayoutType.StandardMatrix,
        Rows = _rows,
        Cols = _cols,
        PitchX = _pitchX,
        PitchY = _pitchY,
        StaggerOffsetX = _staggerOffsetX,
        MasterDiameter = _masterDiameter,
        MasterDiameterStdDev = _masterDiameterStdDev,
        PadEnabled = _padEnabled,
        PadDiameter = _padDiameter,
        PadDepthUm = _padDepthUm,
        PadEdgeSoftness = _padEdgeSoftness,
        PadCenterDimming = _padCenterDimming,
        PadTextureAmount = _padTextureAmount,
        SensorReadNoise = _sensorReadNoise,
        SensorShotNoise = _sensorShotNoise,
        BlobDiameterMean = _blobDiameterMean,
        BlobDiameterStdDev = _blobDiameterStdDev,
        BlobAcircularityMean = _blobAcircularityMean,
        BlobAcircularityStdDev = _blobAcircularityStdDev,
        BlobShapeDeformationMean = _blobShapeDeformationMean,
        BlobShapeDeformationStdDev = _blobShapeDeformationStdDev,
        BlobScoreMean = _blobScoreMean,
        BlobScoreStdDev = _blobScoreStdDev,
        BlobBrightnessMean = _blobBrightnessMean,
        BlobBrightnessStdDev = _blobBrightnessStdDev,
        BackgroundBrightness = _backgroundBrightness,
        Mode = _mode,
        OffsetQuantityMode = _offsetQuantityMode,
        OffsetProbability = _offsetProbability,
        OffsetCount = _offsetCount,
        OffsetMinMm = _offsetMinMm,
        OffsetMaxMm = _offsetMaxMm,
        EnableCollision = _enableCollision,
        CollisionGapMean = _collisionGapMean,
        CollisionGapVariance = _collisionGapVariance,
        MissingQuantityMode = _missingQuantityMode,
        MissingProbability = _missingProbability,
        MissingCount = _missingCount,
        Seed = _seed,
        MmPerPixel = _mmPerPixel,
    };

    private void ScheduleRegenerate(bool boundsChanged = false)
    {
        OnPropertyChanged(nameof(AutoRegenerateAllowed));
        OnPropertyChanged(nameof(AutoRegenerateHint));
        if (!AutoRegenerateAllowed) return;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async Task RunGenerateAsync()
    {
        if (_generating)
        {
            _pendingRegen = true;
            return;
        }

        _generating = true;
        try
        {
            do
            {
                _pendingRegen = false;
                var p = BuildParams();
                StatusText = $"Generating {p.TotalPads:N0} pads ...";
                var sw = Stopwatch.StartNew();
                var frame = await Task.Run(() =>
                {
                    var masters = MasterGenerator.Generate(p);
                    var blobs = BlobGenerator.Generate(masters, p);
                    return (masters, blobs);
                });
                sw.Stop();
                var stats = BlobGenerator.ComputeStats(frame.blobs, sw.ElapsedMilliseconds);
                var simFrame = new SimulationFrame
                {
                    Masters = frame.masters,
                    Blobs = frame.blobs,
                    Params = p,
                    Stats = stats,
                };
                StatusText = $"{stats.TotalPads:N0} pads | present {stats.PresentCount:N0} | missing {stats.MissingCount:N0} | shifted {stats.ShiftedCount:N0} | gen {stats.GenMs} ms";
                FrameUpdated?.Invoke(simFrame);
            } while (_pendingRegen);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            _generating = false;
        }
    }
}
