using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BgaDefectViewer.Converters;

/// <summary>
/// Converts an integer recurring-defect count to a WPF SolidColorBrush.
/// ConverterParameter: "Background" (default) or "Foreground".
/// Count 0 → grey (no defect). Counts 1-10+ use a cyan→red heat scale.
/// </summary>
public class CountToColorConverter : IValueConverter
{
    // Background colors indexed by count: index 0 = count 0 (grey), 1-10 = counts 1-10
    private static readonly Color[] BackgroundColors =
    [
        Color.FromRgb(0xE0, 0xE0, 0xE0), // 0 — grey
        Color.FromRgb(0x00, 0xBC, 0xD4), // 1 — cyan
        Color.FromRgb(0x4C, 0xAF, 0x50), // 2 — green
        Color.FromRgb(0x8B, 0xC3, 0x4A), // 3 — light green
        Color.FromRgb(0xFF, 0xEB, 0x3B), // 4 — yellow
        Color.FromRgb(0xFF, 0x98, 0x00), // 5 — orange
        Color.FromRgb(0xFF, 0x57, 0x22), // 6 — deep orange
        Color.FromRgb(0xF4, 0x43, 0x36), // 7 — red
        Color.FromRgb(0xE9, 0x1E, 0x63), // 8 — pink
        Color.FromRgb(0x9C, 0x27, 0xB0), // 9 — purple
        Color.FromRgb(0x37, 0x47, 0x4F), // 10+ — dark blue-grey
    ];

    private static readonly Color[] ForegroundColors =
    [
        Color.FromRgb(0x99, 0x99, 0x99), // 0 — dark grey text
        Colors.White, // 1
        Colors.White, // 2
        Colors.White, // 3
        Color.FromRgb(0x33, 0x33, 0x33), // 4 — dark text on yellow
        Colors.White, // 5
        Colors.White, // 6
        Colors.White, // 7
        Colors.White, // 8
        Colors.White, // 9
        Colors.White, // 10+
    ];

    // Cache brushes so we don't allocate new ones on every cell render
    private static readonly SolidColorBrush[] BgBrushes =
        BackgroundColors.Select(c => Freeze(new SolidColorBrush(c))).ToArray();
    private static readonly SolidColorBrush[] FgBrushes =
        ForegroundColors.Select(c => Freeze(new SolidColorBrush(c))).ToArray();

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int count = value is int c ? c : 0;
        int index = Math.Clamp(count, 0, BackgroundColors.Length - 1);
        bool isFg = parameter is string s && s.Equals("Foreground", StringComparison.OrdinalIgnoreCase);
        return isFg ? FgBrushes[index] : BgBrushes[index];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>Return the background Color for the given count (used by DefectTypes registration).</summary>
    public static Color GetColor(int count)
        => BackgroundColors[Math.Clamp(count, 0, BackgroundColors.Length - 1)];
}
