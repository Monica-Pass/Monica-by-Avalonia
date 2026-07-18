using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Monica.App.Features.Generator;

public partial class GeneratorWorkspaceView : UserControl
{
    public const double NarrowLayoutBreakpoint = 760;
    public const double WideLayoutBreakpoint = 1100;
    public GeneratorWorkspaceView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateResponsiveLayoutForWidth(e.NewSize.Width);
    }

    public bool IsNarrowLayout { get; private set; }

    public bool IsMediumLayout { get; private set; }

    public bool IsGeneratedPasswordSource(object? source) =>
        ReferenceEquals(source, GeneratorResultView.GeneratedPasswordBox) ||
        source is Control control && control.GetVisualAncestors().Contains(GeneratorResultView.GeneratedPasswordBox);

    public void FocusGeneratedPassword() => Dispatcher.UIThread.Post(() =>
    {
        GeneratorResultView.GeneratedPasswordBox.Focus();
        GeneratorResultView.GeneratedPasswordBox.SelectAll();
    });

    public void UpdateResponsiveLayoutForWidth(double width)
    {
        IsNarrowLayout = width > 0 && width < NarrowLayoutBreakpoint;
        IsMediumLayout = width >= NarrowLayoutBreakpoint && width < WideLayoutBreakpoint;
        ApplyContentLayout();
    }

    private void ApplyContentLayout()
    {
        GeneratorContentGrid.ColumnDefinitions.Clear();
        GeneratorContentGrid.RowDefinitions.Clear();
        if (IsNarrowLayout)
        {
            GeneratorContentGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            GeneratorContentGrid.RowDefinitions.Add(new RowDefinition(new GridLength(3, GridUnitType.Star)));
            GeneratorContentGrid.RowDefinitions.Add(new RowDefinition(new GridLength(2, GridUnitType.Star)));
            Grid.SetColumn(GeneratorResultView, 0);
            Grid.SetRow(GeneratorResultView, 0);
            Grid.SetColumn(GeneratorOptionsView, 0);
            Grid.SetRow(GeneratorOptionsView, 1);
            return;
        }

        GeneratorContentGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        GeneratorContentGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(IsMediumLayout ? 300 : 340)));
        GeneratorContentGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        Grid.SetColumn(GeneratorResultView, 0);
        Grid.SetRow(GeneratorResultView, 0);
        Grid.SetColumn(GeneratorOptionsView, 1);
        Grid.SetRow(GeneratorOptionsView, 0);
    }
}
