using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using FluentAvalonia.Styling;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static void ApplyTheme(string theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        var normalizedTheme = NormalizeThemeValue(theme);
        var themeVariant = normalizedTheme switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            "high-contrast" => FluentAvaloniaTheme.HighContrastTheme,
            _ => ThemeVariant.Default
        };
        Application.Current.RequestedThemeVariant = themeVariant;
        if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            mainWindow.RequestedThemeVariant = themeVariant;
        }

        var useDarkTheme = themeVariant == ThemeVariant.Dark ||
            themeVariant == FluentAvaloniaTheme.HighContrastTheme ||
            themeVariant == ThemeVariant.Default && Application.Current.ActualThemeVariant == ThemeVariant.Dark;
        ApplyMonicaThemeResources(Application.Current.Resources, useDarkTheme, normalizedTheme == "high-contrast");
    }

    private static string NormalizeThemeValue(string theme) =>
        theme.Trim().ToLowerInvariant() switch
        {
            "highcontrast" or "high-contrast" or "contrast" => "high-contrast",
            "light" => "light",
            "dark" => "dark",
            _ => "system"
        };

    private static void ApplyMonicaThemeResources(
        IResourceDictionary resources,
        bool useDarkTheme,
        bool useHighContrastTheme)
    {
        var colors = useHighContrastTheme
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LayerFillColorDefaultBrush"] = "#FFFFFF",
                ["LayerFillColorAltBrush"] = "#000000",
                ["LayerFillColorSubtleBrush"] = "#F2F2F2",
                ["CardBackgroundBrush"] = "#FFFFFF",
                ["CardBorderBrush"] = "#000000",
                ["CardBackgroundFillColorDefaultBrush"] = "#FFFFFF",
                ["CardBackgroundFillColorSecondaryBrush"] = "#F2F2F2",
                ["CardStrokeColorDefaultBrush"] = "#000000",
                ["DividerStrokeColorDefaultBrush"] = "#000000",
                ["ControlFillColorDefaultBrush"] = "#FFFFFF",
                ["ControlFillColorSecondaryBrush"] = "#F2F2F2",
                ["ControlFillColorTertiaryBrush"] = "#E0E0E0",
                ["ListViewItemBackgroundPointerOver"] = "#E6F7FF",
                ["ListViewItemBackgroundSelected"] = "#FFF200",
                ["ListViewItemBackgroundSelectedPointerOver"] = "#FFE000",
                ["TextFillColorPrimaryBrush"] = "#000000",
                ["TextFillColorSecondaryBrush"] = "#000000",
                ["TextFillColorTertiaryBrush"] = "#1A1A1A",
                ["AccentFillColorDefaultBrush"] = "#FFFF00",
                ["AccentFillColorSecondaryBrush"] = "#00FFFF",
                ["AccentFillColorTertiaryBrush"] = "#E6F7FF",
                ["AccentTextFillColorPrimaryBrush"] = "#000000",
                ["SystemFillColorCautionBrush"] = "#FFFF00",
                ["SystemFillColorCriticalBrush"] = "#B00000",
                ["MutedTextBrush"] = "#CC000000",
                ["OverlayFillColorDefaultBrush"] = "#CC000000"
            }
            : useDarkTheme
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LayerFillColorDefaultBrush"] = "#202020",
                ["LayerFillColorAltBrush"] = "#1B1B1B",
                ["LayerFillColorSubtleBrush"] = "#242424",
                ["CardBackgroundBrush"] = "#2B2B2B",
                ["CardBorderBrush"] = "#3A3A3A",
                ["CardBackgroundFillColorDefaultBrush"] = "#2B2B2B",
                ["CardBackgroundFillColorSecondaryBrush"] = "#252525",
                ["CardStrokeColorDefaultBrush"] = "#3A3A3A",
                ["DividerStrokeColorDefaultBrush"] = "#343434",
                ["ControlFillColorDefaultBrush"] = "#323232",
                ["ControlFillColorSecondaryBrush"] = "#383838",
                ["ControlFillColorTertiaryBrush"] = "#424242",
                ["ListViewItemBackgroundPointerOver"] = "#343434",
                ["ListViewItemBackgroundSelected"] = "#3A3A3A",
                ["ListViewItemBackgroundSelectedPointerOver"] = "#414141",
                ["TextFillColorPrimaryBrush"] = "#F3F3F3",
                ["TextFillColorSecondaryBrush"] = "#C9C9C9",
                ["TextFillColorTertiaryBrush"] = "#9D9D9D",
                ["AccentFillColorDefaultBrush"] = "#60CDFF",
                ["AccentFillColorSecondaryBrush"] = "#3AADE2",
                ["AccentFillColorTertiaryBrush"] = "#275A70",
                ["AccentTextFillColorPrimaryBrush"] = "#9CDCFE",
                ["SystemFillColorCautionBrush"] = "#FCE100",
                ["SystemFillColorCriticalBrush"] = "#FF99A4",
                ["MutedTextBrush"] = "#99000000",
                ["OverlayFillColorDefaultBrush"] = "#A0000000"
            }
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LayerFillColorDefaultBrush"] = "#F7F7F7",
                ["LayerFillColorAltBrush"] = "#FFFFFF",
                ["LayerFillColorSubtleBrush"] = "#EFEFEF",
                ["CardBackgroundBrush"] = "#FFFFFF",
                ["CardBorderBrush"] = "#D8D8D8",
                ["CardBackgroundFillColorDefaultBrush"] = "#FFFFFF",
                ["CardBackgroundFillColorSecondaryBrush"] = "#F4F4F4",
                ["CardStrokeColorDefaultBrush"] = "#D8D8D8",
                ["DividerStrokeColorDefaultBrush"] = "#E0E0E0",
                ["ControlFillColorDefaultBrush"] = "#FFFFFF",
                ["ControlFillColorSecondaryBrush"] = "#F4F4F4",
                ["ControlFillColorTertiaryBrush"] = "#EAEAEA",
                ["ListViewItemBackgroundPointerOver"] = "#F0F6FC",
                ["ListViewItemBackgroundSelected"] = "#E7F2FF",
                ["ListViewItemBackgroundSelectedPointerOver"] = "#DCEEFF",
                ["TextFillColorPrimaryBrush"] = "#1A1A1A",
                ["TextFillColorSecondaryBrush"] = "#5C5C5C",
                ["TextFillColorTertiaryBrush"] = "#767676",
                ["AccentFillColorDefaultBrush"] = "#0078D4",
                ["AccentFillColorSecondaryBrush"] = "#106EBE",
                ["AccentFillColorTertiaryBrush"] = "#D7EBF8",
                ["AccentTextFillColorPrimaryBrush"] = "#005A9E",
                ["SystemFillColorCautionBrush"] = "#FCE100",
                ["SystemFillColorCriticalBrush"] = "#C42B1C",
                ["MutedTextBrush"] = "#66000000",
                ["OverlayFillColorDefaultBrush"] = "#66000000"
            };

        foreach (var (key, color) in colors)
        {
            resources[key] = new SolidColorBrush(Color.Parse(color));
        }
    }
}
