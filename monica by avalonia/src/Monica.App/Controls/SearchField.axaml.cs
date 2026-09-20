using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Monica.App.Controls;

public partial class SearchField : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SearchField, string?>(nameof(Text));

    public static readonly StyledProperty<string> PlaceholderTextProperty =
        AvaloniaProperty.Register<SearchField, string>(nameof(PlaceholderText), "Search");

    public static readonly StyledProperty<ICommand?> ClearCommandProperty =
        AvaloniaProperty.Register<SearchField, ICommand?>(nameof(ClearCommand));

    public static readonly StyledProperty<string> AutomationNameProperty =
        AvaloniaProperty.Register<SearchField, string>(nameof(AutomationName), "Search");

    public static readonly StyledProperty<string?> AutomationHelpTextProperty =
        AvaloniaProperty.Register<SearchField, string?>(nameof(AutomationHelpText));

    public static readonly StyledProperty<string> ClearTooltipProperty =
        AvaloniaProperty.Register<SearchField, string>(nameof(ClearTooltip), "Clear search");

    public static readonly StyledProperty<string> ClearAutomationNameProperty =
        AvaloniaProperty.Register<SearchField, string>(nameof(ClearAutomationName), "Clear search");

    public static readonly DirectProperty<SearchField, bool> HasTextProperty =
        AvaloniaProperty.RegisterDirect<SearchField, bool>(nameof(HasText), o => o.HasText);

    private bool _hasText;

    internal TextBox? InnerSearchBox => this.FindControl<TextBox>("SearchBox");

    public SearchField()
    {
        InitializeComponent();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public ICommand? ClearCommand
    {
        get => GetValue(ClearCommandProperty);
        set => SetValue(ClearCommandProperty, value);
    }

    public string AutomationName
    {
        get => GetValue(AutomationNameProperty);
        set => SetValue(AutomationNameProperty, value);
    }

    public string? AutomationHelpText
    {
        get => GetValue(AutomationHelpTextProperty);
        set => SetValue(AutomationHelpTextProperty, value);
    }

    public string ClearTooltip
    {
        get => GetValue(ClearTooltipProperty);
        set => SetValue(ClearTooltipProperty, value);
    }

    public string ClearAutomationName
    {
        get => GetValue(ClearAutomationNameProperty);
        set => SetValue(ClearAutomationNameProperty, value);
    }

    public bool HasText
    {
        get => _hasText;
        private set => SetAndRaise(HasTextProperty, ref _hasText, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            HasText = !string.IsNullOrWhiteSpace(Text);
        }
    }
}
