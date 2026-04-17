using System.Collections.ObjectModel;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.ViewModels;

public class LotMonitorViewModel : ViewModelBase
{
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
        set => SetProperty(ref _substrateCount, value);
    }

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

    public void Load(LotSession session)
    {
        LotName = session.LotName;
        SubstrateCount = session.SubstrateCount;
        Rows = new ObservableCollection<SummaryRow>(session.Rows);
        LotSummaryRows = new ObservableCollection<LotSummaryLine>(session.Summary.Lines);
        IsSummaryCalculated = session.Summary.IsCalculated;
    }

    public void OnRowDoubleClick(SummaryRow row)
    {
        RowDoubleClicked?.Invoke(row);
    }
}
