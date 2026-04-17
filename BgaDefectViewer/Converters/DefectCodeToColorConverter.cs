using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Converters;

public class DefectCodeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int code)
        {
            var color = DefectTypes.GetOrCreate(code).GridColor;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
