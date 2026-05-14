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
    // Background colors indexed by count: index 0 = count 0 (grey), 1-10 = counts 1-10.
    // Cool → warm → hot progression so the visual signal scales monotonically with
    // defect frequency (replaces the previous palette that wrapped through pink/purple).
    private static readonly Color[] BackgroundColors =
    [
        Color.FromRgb(0xE0, 0xE0, 0xE0), // 0  — grey         (empty cell)
        Color.FromRgb(0x42, 0xA5, 0xF5), // 1  — Blue         偶發，極低關注度
        Color.FromRgb(0x26, 0xC6, 0xDA), // 2  — Light Blue   低頻率
        Color.FromRgb(0x26, 0xA6, 0x9A), // 3  — Teal         低頻率，開始過渡
        Color.FromRgb(0x66, 0xBB, 0x6A), // 4  — Green        正常範圍內的累積
        Color.FromRgb(0xD4, 0xE1, 0x57), // 5  — Yellow Green 中等頻率，值得留意
        Color.FromRgb(0xFF, 0xEE, 0x58), // 6  — Yellow       中等頻率，達到警戒
        Color.FromRgb(0xFF, 0xCA, 0x28), // 7  — Amber        偏高頻率，需注意
        Color.FromRgb(0xFF, 0xA7, 0x26), // 8  — Orange       高頻率警告
        Color.FromRgb(0xFF, 0x70, 0x43), // 9  — Deep Orange  嚴重警告
        Color.FromRgb(0xE5, 0x39, 0x35), // 10 — Red          極度嚴重的固定缺陷
    ];

    // Foreground (text) colors paired with each background. WCAG contrast pass:
    // dark text on the light yellow/amber/orange band (5–8) keeps the digit legible
    // at 36×36 px cell size; white on the deep blues/greens/reds reads cleanly.
    private static readonly Color[] ForegroundColors =
    [
        Color.FromRgb(0x99, 0x99, 0x99), // 0  — dark grey on empty
        Colors.White,                    // 1  — Blue
        Colors.White,                    // 2  — Light Blue
        Colors.White,                    // 3  — Teal
        Colors.White,                    // 4  — Green
        Color.FromRgb(0x33, 0x33, 0x33), // 5  — Yellow Green (dark)
        Color.FromRgb(0x33, 0x33, 0x33), // 6  — Yellow (dark)
        Color.FromRgb(0x33, 0x33, 0x33), // 7  — Amber (dark)
        Color.FromRgb(0x33, 0x33, 0x33), // 8  — Orange (dark)
        Colors.White,                    // 9  — Deep Orange
        Colors.White,                    // 10 — Red
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
