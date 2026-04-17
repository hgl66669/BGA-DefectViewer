using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void FileStatusDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileStatusDataGrid.SelectedItem is not FilePathConfig cfg || !cfg.Found) return;

        var path = cfg.DisplayPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path))
                Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch { /* 忽略路徑無效等錯誤 */ }
    }
}
