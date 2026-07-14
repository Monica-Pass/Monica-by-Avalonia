using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Monica.App.Features.Generator;

public partial class GeneratorWorkspaceView : UserControl
{
    public const double NarrowLayoutBreakpoint = 760;
    public const double WideLayoutBreakpoint = 1100;
    public const double WideHeaderBreakpoint = 850;

    public GeneratorWorkspaceView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateResponsiveLayoutForWidth(e.NewSize.Width);
    }

    public bool IsNarrowLayout { get; private set; }

    public bool IsMediumLayout { get; private set; }

    public bool IsGeneratedPasswordSource(object? source) =>
        ReferenceEquals(source, GeneratedPasswordBox) ||
        source is Control control && control.GetVisualAncestors().Contains(GeneratedPasswordBox);

    public void FocusGeneratedPassword() => Dispatcher.UIThread.Post(() =>
    {
        GeneratedPasswordBox.Focus();
        GeneratedPasswordBox.SelectAll();
    });

    public void UpdateResponsiveLayoutForWidth(double width)
    {
        IsNarrowLayout = width > 0 && width < NarrowLayoutBreakpoint;
        IsMediumLayout = width >= NarrowLayoutBreakpoint && width < WideLayoutBreakpoint;
        ApplyContentLayout();
        ApplyHeaderLayout(width);
    }

    private void ApplyContentLayout()
    {
        GeneratorContentGrid.ColumnDefinitions.Clear();
        GeneratorContentGrid.RowDefinitions.Clear();
        if (IsNarrowLayout)
        {
            GeneratorContentGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            GeneratorContentGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            GeneratorContentGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(GeneratorOptionsRegion, 0);
            Grid.SetRow(GeneratorOptionsRegion, 1);
            return;
        }

        GeneratorContentGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        GeneratorContentGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(IsMediumLayout ? 300 : 340)));
        GeneratorContentGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetColumn(GeneratorOptionsRegion, 1);
        Grid.SetRow(GeneratorOptionsRegion, 0);
    }

    private void ApplyHeaderLayout(double width)
    {
        var compact = width > 0 && width < WideHeaderBreakpoint;
        Grid.SetRow(GeneratorHeaderCommands, compact ? 1 : 0);
        Grid.SetColumn(GeneratorHeaderCommands, compact ? 0 : 1);
        Grid.SetColumnSpan(GeneratorHeaderCommands, compact ? 2 : 1);
        GeneratorHeaderCommands.Margin = compact ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        GeneratorHeaderCommands.HorizontalAlignment = compact
            ? Avalonia.Layout.HorizontalAlignment.Left
            : Avalonia.Layout.HorizontalAlignment.Right;
    }
}
