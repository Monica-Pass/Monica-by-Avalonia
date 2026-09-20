using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Monica.App.Controls;

public partial class WorkspaceHeader : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<WorkspaceHeader, string>(nameof(Title));

    public static readonly StyledProperty<object?> FilterContentProperty =
        AvaloniaProperty.Register<WorkspaceHeader, object?>(nameof(FilterContent));

    public static readonly StyledProperty<object?> ActionContentProperty =
        AvaloniaProperty.Register<WorkspaceHeader, object?>(nameof(ActionContent));

    public static readonly StyledProperty<string> ColumnDefinitionsProperty =
        AvaloniaProperty.Register<WorkspaceHeader, string>(nameof(ColumnDefinitions), "Auto,*,Auto");

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<WorkspaceHeader, double>(nameof(ColumnSpacing), 12);

    public static readonly StyledProperty<string> BorderClassesProperty =
        AvaloniaProperty.Register<WorkspaceHeader, string>(nameof(BorderClasses), "workspaceToolbar");

    public WorkspaceHeader()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? FilterContent
    {
        get => GetValue(FilterContentProperty);
        set => SetValue(FilterContentProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
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

    public string BorderClasses
    {
        get => GetValue(BorderClassesProperty);
        set
        {
            SetValue(BorderClassesProperty, value);
            if (_rootBorder is not null)
            {
                _rootBorder.Classes.Replace(value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }
    }

    public bool HasFilterContent => FilterContent is not null;

    public bool HasActionContent => ActionContent is not null;

    private Border? _rootBorder;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _rootBorder = this.FindControl<Border>("RootBorder");
        var rootGrid = this.FindControl<Grid>("RootGrid");
        
        if (_rootBorder is not null && !string.IsNullOrWhiteSpace(BorderClasses))
        {
            _rootBorder.Classes.Replace(BorderClasses.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        
        if (rootGrid is not null)
        {
            rootGrid.ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse(ColumnDefinitions);
            rootGrid.ColumnSpacing = ColumnSpacing;
        }
    }
}
