using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Monica.App;

public static class StringConverters
{
    public static IValueConverter IsPasswords { get; } = new SectionConverter("Passwords");
    public static IValueConverter IsNotPasswords { get; } = new NotSectionConverter("Passwords");
    public static IValueConverter IsContentHeaderVisible { get; } = new NotSectionSetConverter(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Passwords",
            "Notes",
            "Totp",
            "Cards",
            "Generator",
            "Archive",
            "RecycleBin",
            "SecurityAnalysis",
            "Timeline",
            "Mdbx",
            "DatabaseManagement",
            "Sync",
            "Settings"
        });
    public static IValueConverter IsTotp { get; } = new SectionConverter("Totp");
    public static IValueConverter IsCards { get; } = new SectionConverter("Cards");
    public static IValueConverter IsBankCard { get; } = new SectionConverter("BankCard");
    public static IValueConverter IsDocument { get; } = new SectionConverter("Document");
    public static IValueConverter IsNotes { get; } = new SectionConverter("Notes");
    public static IValueConverter IsGenerator { get; } = new SectionConverter("Generator");
    public static IValueConverter IsArchive { get; } = new SectionConverter("Archive");
    public static IValueConverter IsRecycleBin { get; } = new SectionConverter("RecycleBin");
    public static IValueConverter IsSecurityAnalysis { get; } = new SectionConverter("SecurityAnalysis");
    public static IValueConverter IsTimeline { get; } = new SectionConverter("Timeline");
    public static IValueConverter IsMdbx { get; } = new SectionConverter("Mdbx");
    public static IValueConverter IsDatabaseManagement { get; } = new SectionConverter("DatabaseManagement");
    public static IValueConverter IsSettings { get; } = new SectionConverter("Settings");
    public static IValueConverter IsSync { get; } = new SectionConverter("Sync");
    public static IValueConverter IsSettingsOrSync { get; } = new SectionSetConverter(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Settings", "Sync" });
    public static IValueConverter ManagementDetailWidth { get; } = new AvailableWidthConverter(reservedWidth: 570d, minWidth: 260d, maxWidth: 760d);

    private sealed class SectionConverter(string section) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            string.Equals(value?.ToString(), section, StringComparison.OrdinalIgnoreCase);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class NotSectionConverter(string section) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            !string.Equals(value?.ToString(), section, StringComparison.OrdinalIgnoreCase);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class SectionSetConverter(IReadOnlySet<string> sections) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is not null && sections.Contains(value.ToString() ?? "");

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class NotSectionSetConverter(IReadOnlySet<string> sections) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is null || !sections.Contains(value.ToString() ?? "");

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class AvailableWidthConverter(double reservedWidth, double minWidth, double maxWidth) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var width = value switch
            {
                double doubleValue => doubleValue,
                decimal decimalValue => (double)decimalValue,
                int intValue => intValue,
                Rect rectValue => rectValue.Width,
                Size sizeValue => sizeValue.Width,
                _ => reservedWidth + 390d
            };

            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
            {
                return 390d;
            }

            return Math.Clamp(width - reservedWidth, minWidth, maxWidth);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

public static class BoolConverters
{
    public static IValueConverter Not { get; } = new NotConverter();
    public static IValueConverter ToOpacity { get; } = new BoolToOpacityConverter();

    private sealed class NotConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is bool boolValue && !boolValue;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is bool boolValue && boolValue ? 1d : 0.18d;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
