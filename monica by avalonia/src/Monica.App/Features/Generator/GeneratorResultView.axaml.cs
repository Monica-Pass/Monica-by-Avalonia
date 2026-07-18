using Avalonia.Controls;

namespace Monica.App.Features.Generator;

public partial class GeneratorResultView : UserControl
{
    public GeneratorResultView()
    {
        InitializeComponent();
    }

    public TextBox GeneratedPasswordBox => GeneratedPasswordInput;
}
