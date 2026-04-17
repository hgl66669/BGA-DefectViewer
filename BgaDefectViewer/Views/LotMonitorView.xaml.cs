using System.Windows.Controls;
using System.Windows.Input;
using BgaDefectViewer.ViewModels;

namespace BgaDefectViewer.Views;

public partial class LotMonitorView : UserControl
{
    public LotMonitorView()
    {
        InitializeComponent();
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is LotMonitorViewModel vm && vm.SelectedRow != null)
        {
            e.Handled = true;
            var row = vm.SelectedRow;  // capture before deferred execution
            Dispatcher.InvokeAsync(() => vm.OnRowDoubleClick(row));
        }
    }
}
