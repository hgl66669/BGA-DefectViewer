using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Views;

public partial class LotMergeDialog : Window
{
    public class LotPick : ViewModelBase
    {
        public string LotId { get; init; } = "";
        public string DisplayName { get; init; } = "";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public ObservableCollection<LotPick> Items { get; } = new();

    /// <summary>List of LotIds the user checked. Computed on demand.</summary>
    public IReadOnlyList<string> SelectedLotIds =>
        Items.Where(i => i.IsSelected).Select(i => i.LotId).ToList();

    /// <summary>
    /// Build the picker from the live LotNumbers items. UMKF masters (<c>__umkf__</c>) are
    /// shown using <c>LotListItem.DisplayName</c> so users see <c>"ABC123 (3 子批 / 56 片)"</c>
    /// rather than the synthetic id. Session-only manual merged lots (<c>__merged__</c>) are
    /// excluded to avoid merging-of-merges confusion.
    /// </summary>
    public LotMergeDialog(IEnumerable<LotListItem> availableLots)
    {
        InitializeComponent();
        DataContext = this;

        foreach (var lot in availableLots)
        {
            if (lot.Id.StartsWith(FileLocator.MergedLotPrefix, StringComparison.Ordinal)) continue;
            var pick = new LotPick { LotId = lot.Id, DisplayName = lot.DisplayName };
            pick.PropertyChanged += OnPickChanged;
            Items.Add(pick);
        }
        UpdateOkEnabled();
    }

    private void OnPickChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LotPick.IsSelected))
            UpdateOkEnabled();
    }

    private void UpdateOkEnabled()
    {
        OkButton.IsEnabled = Items.Count(i => i.IsSelected) >= 2;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
