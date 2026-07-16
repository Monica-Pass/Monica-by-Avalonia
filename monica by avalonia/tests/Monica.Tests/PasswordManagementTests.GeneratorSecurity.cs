namespace Monica.Tests;

public sealed partial class PasswordManagementTests
{
    [Fact]
    public void ViewModel_generator_history_clear_wipes_retained_secret_state()
    {
        var harness = CreateHarness();
        harness.ViewModel.GeneratePasswordCommand.Execute(null);
        var historyItem = Assert.Single(harness.ViewModel.GeneratedPasswordHistory);
        historyItem.ToggleVisibilityCommand.Execute(null);

        harness.ViewModel.ClearGeneratedPasswordHistoryCommand.Execute(null);

        Assert.Empty(harness.ViewModel.GeneratedPasswordHistory);
        Assert.Empty(historyItem.Value);
        Assert.Empty(historyItem.DisplayValue);
        Assert.False(historyItem.IsRevealed);
    }

    [Fact]
    public void ViewModel_background_hibernation_wipes_retained_generator_history_state()
    {
        var harness = CreateHarness();
        harness.ViewModel.IsUnlocked = true;
        harness.ViewModel.GeneratePasswordCommand.Execute(null);
        var historyItem = Assert.Single(harness.ViewModel.GeneratedPasswordHistory);
        historyItem.ToggleVisibilityCommand.Execute(null);

        harness.ViewModel.SetUnlockedShellHibernated(true);

        Assert.Empty(harness.ViewModel.GeneratedPassword);
        Assert.Empty(harness.ViewModel.GeneratedPasswordHistory);
        Assert.Empty(historyItem.Value);
        Assert.Empty(historyItem.DisplayValue);
        Assert.False(historyItem.IsRevealed);
    }
}
