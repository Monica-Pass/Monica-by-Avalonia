using System.Globalization;
using Avalonia.Data.Converters;
using FluentIcons.Common;

namespace Monica.App;

public static class StringConverters
{
    public static IValueConverter IsBankCard { get; } = new SectionConverter("BankCard");
    public static IValueConverter IsDocument { get; } = new SectionConverter("Document");
    public static IValueConverter IsGenerator { get; } = new SectionConverter("Generator");
    public static IValueConverter IsArchive { get; } = new SectionConverter("Archive");
    public static IValueConverter IsRecycleBin { get; } = new SectionConverter("RecycleBin");
    public static IValueConverter IsSecurityAnalysis { get; } = new SectionConverter("SecurityAnalysis");
    public static IValueConverter IsTimeline { get; } = new SectionConverter("Timeline");
    public static IValueConverter IsMdbx { get; } = new SectionConverter("Mdbx");
    public static IValueConverter IsDatabaseManagement { get; } = new SectionConverter("DatabaseManagement");
    public static IValueConverter IsSettings { get; } = new SectionConverter("Settings");
    public static IValueConverter IsSync { get; } = new SectionConverter("Sync");

    private sealed class SectionConverter(string section) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            string.Equals(value?.ToString(), section, StringComparison.OrdinalIgnoreCase);

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

public static class IconConverters
{
    public static IValueConverter FromSymbol { get; } = new SymbolToIconConverter();

    // FluentIcon.Icon is a struct that only converts to and from Symbol explicitly, and a binding
    // applies no cast: without this bridge every dynamic glyph silently keeps the control default.
    private sealed class SymbolToIconConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            (Icon)(Symbol)value!;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
