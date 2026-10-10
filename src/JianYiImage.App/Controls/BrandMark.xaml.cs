using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JianYiImage.App.Controls;

/// <summary>
/// 矢量品牌标。<see cref="Compact"/> 为真时换成 24 像素以下专用的简化版（排布对齐整像素，
/// 叠放的照片只露一张），标题栏的 16 DIP 用它；其余尺寸用完整版。
/// 两版图形都由 tools/build-app-icon.py 与图标文件一同生成，不要手改 BrandMark.xaml。
/// </summary>
public sealed partial class BrandMark : UserControl
{
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(BrandMark), new PropertyMetadata(false, OnCompactChanged));

    public BrandMark() => InitializeComponent();

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    private static void OnCompactChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var mark = (BrandMark)sender;
        var compact = (bool)e.NewValue;
        mark.FullArt.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        mark.CompactArt.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }
}
