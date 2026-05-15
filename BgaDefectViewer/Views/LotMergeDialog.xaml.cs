using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Views;

public partial class LotMergeDialog : Window
{
    public class LotPick : ViewModelBase
    {
        public string LotId { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public DateTime? CreatedAt { get; init; }

        /// <summary>True 時此項目為「載入更多」哨兵；分組鍵改走特殊路徑，<see cref="IsSelected"/> 不受勾選影響。</summary>
        public bool IsLoadMore { get; init; }

        /// <summary>哨兵剩餘可載入筆數。</summary>
        public int RemainingCount { get; init; }

        /// <summary>分組鍵，與 <see cref="LotListItem.DateGroupKey"/> 同規則；哨兵採 <see cref="LoadMoreSentinel.GroupKey"/>。</summary>
        public string DateGroupKey =>
            IsLoadMore ? LoadMoreSentinel.GroupKey :
            CreatedAt.HasValue ? CreatedAt.Value.ToString("yyyy/MM/dd") : "(未知日期)";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    // ── 分頁載入 ────────────────────────────────────────────────────────
    // 與 MainViewModel 同樣策略：先顯示首批 PageSize 筆 + 末尾哨兵；使用者
    // 點哨兵後一次展開全部。資料量大時 ListBox 不會卡頓於對話框開啟。
    private const int PageSize = 128;
    private readonly List<LotPick> _allItems = new();
    private bool _isExpanded;

    public ObservableCollection<LotPick> Items { get; } = new();

    /// <summary>List of LotIds the user checked. 跳過哨兵以避免被誤算入。</summary>
    public IReadOnlyList<string> SelectedLotIds =>
        _allItems.Where(i => i.IsSelected && !i.IsLoadMore).Select(i => i.LotId).ToList();

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
            var pick = new LotPick
            {
                LotId = lot.Id,
                DisplayName = lot.DisplayName,
                CreatedAt = lot.CreatedAt,
            };
            pick.PropertyChanged += OnPickChanged;
            _allItems.Add(pick);
        }
        RefreshItemsView();
        UpdateOkEnabled();
    }

    /// <summary>依 <see cref="_isExpanded"/> 重建可見 <see cref="Items"/>：首批 <see cref="PageSize"/> 筆 +
    /// （若仍有剩餘）哨兵；展開後一次顯示全部。已勾選的 LotPick 不受重建影響（_allItems 持有原始參考）。</summary>
    private void RefreshItemsView()
    {
        Items.Clear();
        var visible = _isExpanded || _allItems.Count <= PageSize
            ? _allItems.AsEnumerable()
            : _allItems.Take(PageSize);
        foreach (var p in visible) Items.Add(p);
        if (!_isExpanded && _allItems.Count > PageSize)
        {
            Items.Add(new LotPick
            {
                LotId = LoadMoreSentinel.Id,
                DisplayName = $"▼ 載入更多 (還有 {_allItems.Count - PageSize} 筆)",
                IsLoadMore = true,
                RemainingCount = _allItems.Count - PageSize,
            });
        }
    }

    private void OnPickChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LotPick.IsSelected))
            UpdateOkEnabled();
    }

    private void UpdateOkEnabled()
    {
        OkButton.IsEnabled = _allItems.Count(i => i.IsSelected && !i.IsLoadMore) >= 2;
    }

    /// <summary>
    /// 哨兵被點擊時觸發（透過 ListBox.SelectionChanged）。展開後刷新 Items；
    /// 並清除 ListBox 的選取狀態（哨兵不該停留為「選中」）。
    /// </summary>
    private void LotList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb) return;
        if (lb.SelectedItem is LotPick pick && pick.IsLoadMore)
        {
            if (!_isExpanded)
            {
                _isExpanded = true;
                RefreshItemsView();
            }
            // 哨兵不該停留為選中狀態（避免使用者後續用鍵盤跳出 token）
            lb.SelectedIndex = -1;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
