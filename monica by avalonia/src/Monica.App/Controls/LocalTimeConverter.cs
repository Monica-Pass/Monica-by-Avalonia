using System.Globalization;
using Avalonia.Data.Converters;

namespace Monica.App.Controls;

/// <summary>
/// Shows a stored timestamp the way the rest of the window reads: in local time and in the UI
/// language. A plain <c>StringFormat='{}{0:g}'</c> printed the stored offset (UTC for vault rows,
/// hours off for most people) in the operating system's format rather than the chosen language.
/// </summary>
public sealed class LocalTimeConverter(string format) : IValueConverter
{
    public static readonly LocalTimeConverter General = new("g");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DateTimeOffset offset => offset.ToLocalTime().ToString(format, CultureInfo.CurrentUICulture),
        DateTime { Kind: DateTimeKind.Utc } utc => utc.ToLocalTime().ToString(format, CultureInfo.CurrentUICulture),
        DateTime local => local.ToString(format, CultureInfo.CurrentUICulture),
        _ => ""
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
