using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using FluentIcons.Avalonia;
using Monica.App.Controls;
using Monica.App.Features.Authenticator;
using Monica.App.Features.DatabaseManagement;
using Monica.App.Features.Generator;
using Monica.App.Features.Mdbx;
using Monica.App.Features.Notes;
using Monica.App.Features.SecurityAnalysis;
using Monica.App.Features.Sync;
using Monica.App.Features.Timeline;
using Monica.App.Features.Wallet;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class CommandBarIconUiTests
{
    public static TheoryData<string> CommandBarHosts() =>
    [
        nameof(AuthenticatorWorkspaceView),
        nameof(WalletWorkspaceView),
        nameof(DatabaseCommandBarView),
        nameof(GeneratorResultView),
        nameof(MdbxCommandBarView),
        nameof(TimelineCommandBarView),
        nameof(NoteEditorToolbarView),
        nameof(SyncCommandBarView),
        nameof(SecurityAnalysisCommandBarView)
    ];

    private static UserControl CreateHost(string name) => name switch
    {
        nameof(AuthenticatorWorkspaceView) => new AuthenticatorWorkspaceView(),
        nameof(WalletWorkspaceView) => new WalletWorkspaceView(),
        nameof(DatabaseCommandBarView) => new DatabaseCommandBarView(),
        nameof(GeneratorResultView) => new GeneratorResultView(),
        nameof(MdbxCommandBarView) => new MdbxCommandBarView(),
        nameof(TimelineCommandBarView) => new TimelineCommandBarView(),
        nameof(NoteEditorToolbarView) => new NoteEditorToolbarView(),
        nameof(SyncCommandBarView) => new SyncCommandBarView(),
        nameof(SecurityAnalysisCommandBarView) => new SecurityAnalysisCommandBarView(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown command bar host.")
    };

    // FluentAvalonia resolves an unknown IconSource="Name" into an FAFontIconSource whose glyph is
    // the literal name, so a typo ships as visible text such as "QrCode" instead of an icon.
    [Theory]
    [MemberData(nameof(CommandBarHosts))]
    public void Command_bar_buttons_never_fall_back_to_literal_glyph_names(string hostName)
    {
        var host = CreateHost(hostName);

        var textFallbacks = CollectCommandBarButtons(host)
            .Where(button => button.IconSource is FAFontIconSource)
            .Select(button => $"{button.Label}:{((FAFontIconSource)button.IconSource!).Glyph}")
            .ToArray();

        Assert.Empty(textFallbacks);
    }

    [Theory]
    [MemberData(nameof(CommandBarHosts))]
    public void Command_bar_buttons_render_fluent_symbol_icons(string hostName)
    {
        var host = CreateHost(hostName);

        var buttons = CollectCommandBarButtons(host);

        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.IsType<FluentSymbolIconSource>(button.IconSource));
    }

    [Fact]
    public void Materialized_command_bar_button_renders_an_icon_instead_of_glyph_text()
    {
        var window = new Window { Width = 800, Height = 200 };
        var host = new MdbxCommandBarView();
        window.Content = host;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            var buttons = CollectCommandBarButtons(host);

            // Named icons such as SubItemChevron belong to FluentAvalonia's own button template;
            // the icon materialized from IconSource is the anonymous one.
            var icons = buttons
                .SelectMany(button => button.GetVisualDescendants().OfType<FAIconElement>())
                .Where(icon => icon.Name is null)
                .ToArray();

            Assert.NotEmpty(icons);
            Assert.All(
                icons,
                icon => Assert.IsType<SymbolImage>(Assert.IsType<FAImageIcon>(icon).Source));

            var labels = buttons.Select(button => button.Label).ToArray();
            var glyphText = buttons
                .SelectMany(button => button.GetVisualDescendants().OfType<TextBlock>())
                .Select(text => text.Text ?? "")
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Except(labels)
                .ToArray();
            Assert.Empty(glyphText);
        }
        finally
        {
            window.Close();
        }
    }

    private static IReadOnlyList<FACommandBarButton> CollectCommandBarButtons(UserControl host)
    {
        return host.GetSelfAndLogicalDescendants()
            .OfType<FACommandBar>()
            .SelectMany(bar => bar.PrimaryCommands.Concat(bar.SecondaryCommands))
            .OfType<FACommandBarButton>()
            .ToArray();
    }
}
