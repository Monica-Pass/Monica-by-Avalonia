using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Services;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class GeneratorWorkspaceViewModelTests
{
    [Fact]
    public void Independent_workspaces_do_not_share_options_or_secrets()
    {
        var first = Create();
        var second = Create();
        first.GeneratorTemplate = "pin";
        first.GeneratePasswordCommand.Execute(null);
        var retained = Assert.Single(first.GeneratedPasswordHistory);
        retained.IsRevealed = true;

        Assert.Empty(second.GeneratedPassword);
        Assert.Empty(second.GeneratedPasswordHistory);
        Assert.Equal("random", second.GeneratorMode);

        first.ClearSensitiveState();

        Assert.Empty(first.GeneratedPassword);
        Assert.Empty(first.GeneratedPasswordHistory);
        Assert.Empty(retained.Value);
        Assert.Empty(retained.DisplayValue);
        Assert.False(retained.IsRevealed);
        Assert.False(first.CopyGeneratedPasswordCommand.CanExecute(null));
        Assert.False(first.ClearGeneratedPasswordHistoryCommand.CanExecute(null));
    }

    [Fact]
    public void Initial_result_is_lazy_and_history_eviction_clears_retained_items()
    {
        var generator = Create();
        Assert.Empty(generator.GeneratedPassword);
        generator.EnsureGeneratedPassword();
        Assert.NotEmpty(generator.GeneratedPassword);
        Assert.Empty(generator.GeneratedPasswordHistory);
        generator.GeneratePasswordCommand.Execute(null);
        var oldest = Assert.Single(generator.GeneratedPasswordHistory);
        for (var index = 0; index < 8; index++)
        {
            generator.GeneratePasswordCommand.Execute(null);
        }

        Assert.Equal(8, generator.GeneratedPasswordHistory.Count);
        Assert.Empty(oldest.Value);
        Assert.Empty(oldest.DisplayValue);
    }

    [Fact]
    public void Relocalizing_preserves_option_identity_selection_and_generated_secret()
    {
        var localization = new LocalizationService();
        var generator = Create(localization: localization);
        generator.GeneratorTemplate = "memorable";
        generator.GeneratePasswordCommand.Execute(null);
        var option = generator.SelectedGeneratorTemplateOption;
        var secret = generator.GeneratedPassword;
        var language = localization.SelectedLanguage;
        try
        {
            localization.SetLanguage("zh-CN");
            generator.RefreshLocalization();

            Assert.Same(option, generator.SelectedGeneratorTemplateOption);
            Assert.Equal(localization.Get("GeneratorTemplateMemorable"), option!.Label);
            Assert.Equal(secret, generator.GeneratedPassword);
            Assert.Single(generator.GeneratedPasswordHistory);
        }
        finally
        {
            localization.SetLanguage(language);
        }
    }

    [Fact]
    public void Compact_layout_notifies_without_changing_generator_options()
    {
        var generator = Create();
        var changes = new List<string?>();
        generator.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        generator.IsCompact = true;

        Assert.Equal(96, generator.GeneratorPasswordBoxMinHeight);
        Assert.False(generator.ShowGeneratorStrengthSummaryCard);
        Assert.Contains(nameof(generator.GeneratorPasswordBoxMinHeight), changes);
        Assert.Contains(nameof(generator.ShowGeneratorStrengthSummaryCard), changes);
        Assert.Equal(24, generator.GeneratorLength);
    }

    [Fact]
    public async Task Copy_uses_sensitive_clipboard_and_save_delegates_to_the_host()
    {
        var clipboard = new GeneratorClipboard();
        var notices = new List<string>();
        var saved = 0;
        var save = new AsyncRelayCommand(() => { saved++; return Task.CompletedTask; });
        var generator = new GeneratorWorkspaceViewModel(new PasswordGeneratorService(),
            clipboard, new LocalizationService(), notices.Add, save);
        generator.GeneratePasswordCommand.Execute(null);
        await generator.CopyGeneratedPasswordCommand.ExecuteAsync(null);
        await generator.AddPasswordCommand.ExecuteAsync(null);

        Assert.Equal(generator.GeneratedPassword, clipboard.Text);
        Assert.Equal(1, saved);
        Assert.Same(save, generator.AddPasswordCommand);
        Assert.Equal(["GeneratedPassword", "CopiedGeneratedPassword"], notices);
    }

    private static GeneratorWorkspaceViewModel Create(ILocalizationService? localization = null) =>
        new(new PasswordGeneratorService(), new GeneratorClipboard(), localization ?? new LocalizationService(),
            _ => { }, new AsyncRelayCommand(() => Task.CompletedTask));

    private sealed class GeneratorClipboard : IClipboardService
    {
        public string Text { get; private set; } = "";
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Use the sensitive clipboard path.");

        public Task SetSensitiveTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }
}
