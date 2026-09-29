namespace Monica.Tests;

public sealed partial class PasswordManagementTests
{
    [Fact]
    public void ViewModel_generator_history_clear_wipes_retained_secret_state()
    {
        var harness = CreateHarness();
        harness.ViewModel.Generator.GeneratePasswordCommand.Execute(null);
        var historyItem = Assert.Single(harness.ViewModel.Generator.GeneratedPasswordHistory);
        historyItem.ToggleVisibilityCommand.Execute(null);

        harness.ViewModel.Generator.ClearGeneratedPasswordHistoryCommand.Execute(null);

        Assert.Empty(harness.ViewModel.Generator.GeneratedPasswordHistory);
        Assert.Empty(historyItem.Value);
        Assert.Empty(historyItem.DisplayValue);
        Assert.False(historyItem.IsRevealed);
    }

    [Fact]
    public void ViewModel_background_hibernation_wipes_retained_generator_history_state()
    {
        var harness = CreateHarness();
        harness.ViewModel.IsUnlocked = true;
        harness.ViewModel.Generator.GeneratePasswordCommand.Execute(null);
        var historyItem = Assert.Single(harness.ViewModel.Generator.GeneratedPasswordHistory);
        historyItem.ToggleVisibilityCommand.Execute(null);

        harness.ViewModel.SetShellHibernatedByWindow(true);

        Assert.Empty(harness.ViewModel.Generator.GeneratedPassword);
        Assert.Empty(harness.ViewModel.Generator.GeneratedPasswordHistory);
        Assert.Empty(historyItem.Value);
        Assert.Empty(historyItem.DisplayValue);
        Assert.False(historyItem.IsRevealed);
    }
}
