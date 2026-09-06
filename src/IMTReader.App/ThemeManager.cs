using System.Windows;
using System.Windows.Media;
using IMTReader.Core.Models;

namespace IMTReader.App;

/// <summary>根据设置切换阅读区配色与字体（资源以 DynamicResource 引用）。</summary>
public static class ThemeManager
{
    public static void Apply(AppSettings s)
    {
        var res = Application.Current.Resources;
        (Color bg, Color fg, Color block, Color blockSelected, Color border) = s.Theme switch
        {
            AppTheme.Dark => (Hex("#1F2421"), Hex("#E6E1D6"), Hex("#2A312C"), Hex("#2F4A3B"), Hex("#3B443E")),
            AppTheme.Light => (Hex("#FFFFFF"), Hex("#222222"), Hex("#FAFAFA"), Hex("#DDF3D6"), Hex("#E2E2E2")),
            _ => (Hex("#F7F3E8"), Hex("#2B2620"), Hex("#FDFBF5"), Hex("#DCF3D2"), Hex("#E6E0D0"))
        };
        res["ReadingBackgroundBrush"] = Freeze(new SolidColorBrush(bg));
        res["ReadingForegroundBrush"] = Freeze(new SolidColorBrush(fg));
        res["BlockBackgroundBrush"] = Freeze(new SolidColorBrush(block));
        res["BlockSelectedBrush"] = Freeze(new SolidColorBrush(blockSelected));
        res["BlockBorderBrush"] = Freeze(new SolidColorBrush(border));
        res["ReadingFontFamily"] = new FontFamily(string.IsNullOrWhiteSpace(s.FontFamily) ? "楷体" : s.FontFamily);
        res["ReadingFontSize"] = Math.Clamp(s.FontSize, 9, 48);
    }

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}
