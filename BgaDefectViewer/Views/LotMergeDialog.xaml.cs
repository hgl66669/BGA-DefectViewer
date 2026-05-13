using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using BgaDefectViewer.Helpers;

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

    public LotMergeDialog(IEnumerable<string> availableLotIds)
    {
        InitializeComponent();
        DataContext = this;

        foreach (var id in availableLotIds)
        {
            // Exclude already-merged session lots from the picker — merging merges is confusing.
            if (id.StartsWith(FileLocator.MergedLotPrefix, StringComparison.Ordinal)) continue;
            var pick = new LotPick { LotId = id, DisplayName = FileLocator.FormatLotForDisplay(id) };
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
