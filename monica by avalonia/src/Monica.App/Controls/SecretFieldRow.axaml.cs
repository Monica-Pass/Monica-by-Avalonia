using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Monica.App.Controls;

public partial class SecretFieldRow : UserControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<SecretFieldRow, string?>(nameof(Label));

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<SecretFieldRow, string?>(nameof(Value));

    public static readonly StyledProperty<char> MaskCharProperty =
        AvaloniaProperty.Register<SecretFieldRow, char>(nameof(MaskChar), '\0');

    public static readonly StyledProperty<string> PlaceholderTextProperty =
        AvaloniaProperty.Register<SecretFieldRow, string>(nameof(PlaceholderText));

    public static readonly StyledProperty<bool> IsMaskedProperty =
        AvaloniaProperty.Register<SecretFieldRow, bool>(nameof(IsMasked), true);

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<SecretFieldRow, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<ICommand?> ToggleVisibilityCommandProperty =
        AvaloniaProperty.Register<SecretFieldRow, ICommand?>(nameof(ToggleVisibilityCommand));

    public static readonly StyledProperty<ICommand?> CopyCommandProperty =
        AvaloniaProperty.Register<SecretFieldRow, ICommand?>(nameof(CopyCommand));

    public static readonly StyledProperty<string> ColumnDefinitionsProperty =
        AvaloniaProperty.Register<SecretFieldRow, string>(nameof(ColumnDefinitions), "*,Auto");

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<SecretFieldRow, double>(nameof(ColumnSpacing), 6);

    public static readonly StyledProperty<int> ValueColumnIndexProperty =
        AvaloniaProperty.Register<SecretFieldRow, int>(nameof(ValueColumnIndex), 0);

    public static readonly StyledProperty<int> ToggleColumnIndexProperty =
        AvaloniaProperty.Register<SecretFieldRow, int>(nameof(ToggleColumnIndex), 1);

    public static readonly StyledProperty<int> CopyColumnIndexProperty =
        AvaloniaProperty.Register<SecretFieldRow, int>(nameof(CopyColumnIndex), 2);

    public static readonly StyledProperty<string> AutomationNameProperty =
        AvaloniaProperty.Register<SecretFieldRow, string>(nameof(AutomationName), "Secret field");

    public static readonly StyledProperty<string> ToggleVisibilityTooltipProperty =
        AvaloniaProperty.Register<SecretFieldRow, string>(nameof(ToggleVisibilityTooltip), "Toggle visibility");

    public static readonly StyledProperty<string> ToggleVisibilityAutomationNameProperty =
        AvaloniaProperty.Register<SecretFieldRow, string>(nameof(ToggleVisibilityAutomationName), "Toggle visibility");

    public static readonly StyledProperty<string> CopyTooltipProperty =
        AvaloniaProperty.Register<SecretFieldRow, string>(nameof(CopyTooltip), "Copy");

    public static readonly StyledProperty<string> CopyAutomationNameProperty =
        AvaloniaProperty.Register<SecretFieldRow, string>(nameof(CopyAutomationName), "Copy");

    public SecretFieldRow()
    {
        InitializeComponent();
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public char MaskChar
    {
        get => GetValue(MaskCharProperty);
        set => SetValue(MaskCharProperty, value);
    }

    public string PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public bool IsMasked
    {
        get => GetValue(IsMaskedProperty);
        set => SetValue(IsMaskedProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public ICommand? ToggleVisibilityCommand
    {
        get => GetValue(ToggleVisibilityCommandProperty);
        set => SetValue(ToggleVisibilityCommandProperty, value);
    }

    public ICommand? CopyCommand
    {
        get => GetValue(CopyCommandProperty);
        set => SetValue(CopyCommandProperty, value);
    }

    public string ColumnDefinitions
    {
        get => GetValue(ColumnDefinitionsProperty);
        set => SetValue(ColumnDefinitionsProperty, value);
    }

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public int ValueColumnIndex
    {
        get => GetValue(ValueColumnIndexProperty);
        set => SetValue(ValueColumnIndexProperty, value);
    }

    public int ToggleColumnIndex
    {
        get => GetValue(ToggleColumnIndexProperty);
        set => SetValue(ToggleColumnIndexProperty, value);
    }

    public int CopyColumnIndex
    {
        get => GetValue(CopyColumnIndexProperty);
        set => SetValue(CopyColumnIndexProperty, value);
    }

    public string AutomationName
    {
        get => GetValue(AutomationNameProperty);
        set => SetValue(AutomationNameProperty, value);
    }

    public string ToggleVisibilityTooltip
    {
        get => GetValue(ToggleVisibilityTooltipProperty);
        set => SetValue(ToggleVisibilityTooltipProperty, value);
    }

    public string ToggleVisibilityAutomationName
    {
        get => GetValue(ToggleVisibilityAutomationNameProperty);
        set => SetValue(ToggleVisibilityAutomationNameProperty, value);
    }

    public string CopyTooltip
    {
        get => GetValue(CopyTooltipProperty);
        set => SetValue(CopyTooltipProperty, value);
    }

    public string CopyAutomationName
    {
        get => GetValue(CopyAutomationNameProperty);
        set => SetValue(CopyAutomationNameProperty, value);
    }

    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    public bool ShowToggleVisibility => ToggleVisibilityCommand is not null;

    public bool ShowCopy => CopyCommand is not null;

    private Grid? _rootGrid;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _rootGrid = this.FindControl<Grid>("RootGrid");
        var valueBox = this.FindControl<TextBox>("ValueBox");
        var toggleButton = this.FindControl<Button>("ToggleVisibilityButton");
        var copyButton = this.FindControl<Button>("CopyButton");

        if (_rootGrid is not null)
        {
            _rootGrid.ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse(ColumnDefinitions);
            _rootGrid.ColumnSpacing = ColumnSpacing;
        }

        if (valueBox is not null)
        {
            Grid.SetColumn(valueBox, ValueColumnIndex);
        }

        if (toggleButton is not null)
        {
            Grid.SetColumn(toggleButton, ToggleColumnIndex);
        }

        if (copyButton is not null)
        {
            Grid.SetColumn(copyButton, CopyColumnIndex);
        }
    }
}
