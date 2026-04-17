using System.Windows;

namespace BgaDefectViewer.Helpers;

/// <summary>
/// Freezable-based proxy，讓 ItemsPanelTemplate 內的元素能透過 Source= 綁定到 DataContext 屬性。
/// WPF ItemsPanelTemplate 內的元素無法直接繼承 DataContext，需要此 Proxy 做橋接。
/// </summary>
public class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore() => new BindingProxy();

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy),
            new PropertyMetadata(null));

    public object Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }
}
