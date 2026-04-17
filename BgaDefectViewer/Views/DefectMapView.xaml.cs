using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BgaDefectViewer.Models;
using BgaDefectViewer.ViewModels;

namespace BgaDefectViewer.Views;

public partial class DefectMapView : UserControl
{
    public DefectMapView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is DefectMapViewModel oldVm)
            oldVm.PropertyChanged -= Vm_PropertyChanged;

        if (e.NewValue is DefectMapViewModel vm)
        {
            vm.PropertyChanged += Vm_PropertyChanged;
            RebuildGrids(vm);
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DefectMapViewModel.MapData))
        {
            if (sender is DefectMapViewModel vm)
                RebuildGrids(vm);
        }
    }

    private void RebuildGrids(DefectMapViewModel vm)
    {
        PreRepairPanel.Children.Clear();
        PostRepairPanel.Children.Clear();

        if (vm.MapData == null) return;

        foreach (var pair in vm.MapData.DefectMaps)
        {
            PreRepairPanel.Children.Add(BuildDieGrid(pair.PreRepair));
            PostRepairPanel.Children.Add(BuildDieGrid(pair.PostRepair));
        }
    }

    private static UIElement BuildDieGrid(DieMatrix matrix)
    {
        var panel = new StackPanel { Margin = new Thickness(4, 4, 4, 12) };

        // Header
        var header = new TextBlock
        {
            Text = $"{matrix.DefectName}  [{matrix.TotalCount}]",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        panel.Children.Add(header);

        if (matrix.Rows == 0 || matrix.Cols == 0) return panel;

        // Grid with row/col headers
        int maxVal = matrix.MaxValue;
        var grid = new Grid();

        // Header column (40px) + data columns (60px each)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        for (int c = 0; c < matrix.Cols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

        // Header row (30px) + data rows (40px each)
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        for (int r = 0; r < matrix.Rows; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });

        // Helper: create a cyan header cell
        Border MakeHeader(string text)
        {
            var hb = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0xAC, 0xC1)),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(1)
            };
            hb.Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return hb;
        }

        // Corner "V" at [0, 0]
        var corner = MakeHeader("V");
        Grid.SetRow(corner, 0); Grid.SetColumn(corner, 0); grid.Children.Add(corner);

        // Column headers at row=0, col=1..Cols
        for (int c = 0; c < matrix.Cols; c++)
        {
            var h = MakeHeader($"{c + 1:D2}");
            Grid.SetRow(h, 0); Grid.SetColumn(h, c + 1); grid.Children.Add(h);
        }

        // Row headers at row=1..Rows, col=0
        for (int r = 0; r < matrix.Rows; r++)
        {
            var h = MakeHeader($"{r + 1:D2}");
            Grid.SetRow(h, r + 1); Grid.SetColumn(h, 0); grid.Children.Add(h);
        }

        // Data cells at [r+1, c+1]
        for (int r = 0; r < matrix.Rows; r++)
        {
            for (int c = 0; c < matrix.Cols; c++)
            {
                int val = matrix.Values[r, c];
                var bg = GetHeatColor(val, maxVal);
                var brush = new SolidColorBrush(bg);
                brush.Freeze();

                var border = new Border
                {
                    Background = brush,
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(1)
                };
                var text = new TextBlock
                {
                    Text = val.ToString(),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = val > 0 ? Brushes.White : Brushes.Black
                };
                border.Child = text;
                Grid.SetRow(border, r + 1);
                Grid.SetColumn(border, c + 1);
                grid.Children.Add(border);
            }
        }

        panel.Children.Add(grid);
        return panel;
    }

    private static Color GetHeatColor(int value, int maxValue)
    {
        if (value == 0)
            return (Color)ColorConverter.ConvertFromString("#E8F5E9"); // light green

        if (maxValue <= 0) maxValue = 1;
        double ratio = Math.Min(1.0, (double)value / maxValue);

        // Gradient: orange (#FFA500) → red (#FF0000)
        byte r = 255;
        byte g = (byte)(165 * (1 - ratio));
        byte b = 0;
        return Color.FromRgb(r, g, b);
    }
}
