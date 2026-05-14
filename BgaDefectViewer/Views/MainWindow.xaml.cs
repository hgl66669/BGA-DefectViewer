using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BgaDefectViewer.ViewModels;

namespace BgaDefectViewer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Double-click the Source path TextBox → open the folder in File Explorer.
    /// Reads from the bound <see cref="MainViewModel.AthleteSysPath"/> via DataContext.
    /// </summary>
    private void SourcePathTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var path = vm.AthleteSysPath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open"
            });
            e.Handled = true;
        }
        catch
        {
            // best-effort: ignore failures (path could be a UNC offline, permissions, etc.)
        }
    }
}
