using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
    }

    private static string NormalizeThemeValue(string theme) =>
        theme.Trim().ToLowerInvariant() switch
        {
            "highcontrast" or "high-contrast" or "contrast" => "high-contrast",
            "light" => "light",
            "dark" => "dark",
            _ => "system"
        };
}
