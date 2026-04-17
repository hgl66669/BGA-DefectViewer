using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BgaDefectViewer.Helpers;

namespace BgaDefectViewer.Converters;

/// <summary>
/// 將 Die 判定字母（char 或 string）轉換為對應的背景/前景色。
/// ConverterParameter = "Background" | "Foreground"
/// </summary>
[ValueConversion(typeof(char), typeof(Brush))]
public class DieLetterToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        char letter = value switch
        {
            char c => c,
            string s when s.Length > 0 => s[0],
            _ => 'G'
        };

        var info = DieJudge.GetInfo(letter);
        var color = parameter is string p && p == "Foreground"
            ? info.ForeColor
            : info.BackColor;

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
