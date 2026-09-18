using Avalonia;
using Avalonia.Data;
using FluentAvalonia.UI.Controls;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Monica.App.Controls;

// FluentAvalonia resolves IconSource="Name" against its own small FASymbol set and silently
// degrades unknown names into literal text, so command bars need the FluentIcons set the rest
// of the app already uses.
public sealed class FluentSymbolIconSource : FAIconSource
{
    public static readonly StyledProperty<Symbol> SymbolProperty =
        AvaloniaProperty.Register<FluentSymbolIconSource, Symbol>(nameof(Symbol));

    public static readonly StyledProperty<IconVariant> IconVariantProperty =
        AvaloniaProperty.Register<FluentSymbolIconSource, IconVariant>(nameof(IconVariant), IconVariant.Regular);

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<FluentSymbolIconSource, double>(nameof(FontSize), 16d);

    static FluentSymbolIconSource() => FAIconHelpers.RegisterCustomIconSourceFactory(
        typeof(FluentSymbolIconSource),
        static source => CreateIcon((FluentSymbolIconSource)source));

    public Symbol Symbol
    {
        get => GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public IconVariant IconVariant
    {
        get => GetValue(IconVariantProperty);
        set => SetValue(IconVariantProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    private static FAIconElement CreateIcon(FluentSymbolIconSource source)
    {
        var image = new SymbolImage
        {
            Symbol = source.Symbol,
            IconVariant = source.IconVariant,
            FontSize = source.FontSize
        };

        var icon = new FAImageIcon { Source = image };

        // SymbolImage bakes its glyph with its own Foreground, which defaults to black and
        // disappears on the dark rail. Following the hosting element keeps the glyph in step
        // with the navigation item's themed colour and with light/dark switches.
        image.Bind(
            SymbolImage.ForegroundProperty,
            new Binding
            {
                Source = icon,
                Path = nameof(FAIconElement.Foreground),
                FallbackValue = source.Foreground
            });

        return icon;
    }
}

public sealed class FluentSymbolExtension
{
    public FluentSymbolExtension()
    {
    }

    public FluentSymbolExtension(Symbol symbol) => Symbol = symbol;

    public Symbol Symbol { get; set; }

    public IconVariant IconVariant { get; set; } = IconVariant.Regular;

    public double FontSize { get; set; } = 16d;

    public FluentSymbolIconSource ProvideValue() => new()
    {
        Symbol = Symbol,
        IconVariant = IconVariant,
        FontSize = FontSize
    };
}
