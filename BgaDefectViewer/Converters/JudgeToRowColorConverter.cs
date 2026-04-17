using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BgaDefectViewer.Converters;

public class JudgeToRowColorConverter : IValueConverter
{
    private static readonly SolidColorBrush GoodBrush;
    private static readonly SolidColorBrush NgBrush;

    static JudgeToRowColorConverter()
    {
        GoodBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F5E9"));
        GoodBrush.Freeze();
        NgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF3E0"));
        NgBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string judge = value as string ?? "";
        return judge == "GD" ? GoodBrush : NgBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
