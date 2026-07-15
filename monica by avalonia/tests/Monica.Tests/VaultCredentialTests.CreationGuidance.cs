using Monica.Core.Services;

namespace Monica.Tests;

public sealed partial class VaultCredentialTests
{
    [Theory]
    [InlineData("")]
    [InlineData("1234567")]
    public void Vault_access_creation_guidance_policy_rejects_passwords_below_the_minimum(string password)
    {
        Assert.False(VaultMasterPasswordPolicy.MeetsMinimumLength(password));
    }

    [Fact]
    public void Vault_access_creation_guidance_policy_accepts_the_minimum_length()
    {
        Assert.Equal(8, VaultMasterPasswordPolicy.MinimumLength);
        Assert.True(VaultMasterPasswordPolicy.MeetsMinimumLength("12345678"));
    }

    [Fact]
    public async Task Vault_access_creation_guidance_blocks_invalid_input_and_enables_valid_matching_input()
    {
        var viewModel = CreateViewModel(GetTempDatabasePath());
        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsVaultInitialized);
        Assert.True(viewModel.ShowCreateVaultPasswordGuidance);

        viewModel.MasterPassword = "short";
        viewModel.ConfirmMasterPassword = "short";

        Assert.False(viewModel.IsCreateVaultPasswordLengthValid);
        Assert.True(viewModel.IsCreateVaultPasswordConfirmationValid);
        Assert.False(viewModel.IsCreateVaultPasswordInputValid);
        Assert.False(viewModel.UnlockCommand.CanExecute(null));
        Assert.Contains("8", viewModel.CreateVaultPasswordLengthStatusText, StringComparison.Ordinal);

        viewModel.MasterPassword = "valid-password";
        viewModel.ConfirmMasterPassword = "different-password";

        Assert.True(viewModel.IsCreateVaultPasswordLengthValid);
        Assert.False(viewModel.IsCreateVaultPasswordConfirmationValid);
        Assert.False(viewModel.UnlockCommand.CanExecute(null));

        viewModel.ConfirmMasterPassword = "valid-password";

        Assert.True(viewModel.IsCreateVaultPasswordInputValid);
        Assert.True(viewModel.UnlockCommand.CanExecute(null));
        Assert.Equal(
            viewModel.L.Get("MasterPasswordConfirmationMatches"),
            viewModel.CreateVaultPasswordConfirmationStatusText);
    }

    [Fact]
    public async Task Vault_access_creation_guidance_preserves_existing_vault_single_password_submission()
    {
        var viewModel = CreateViewModel(GetTempDatabasePath());
        await viewModel.InitializeAsync();
        viewModel.IsVaultInitialized = true;
        viewModel.MasterPassword = "x";
        viewModel.ConfirmMasterPassword = "";

        Assert.False(viewModel.ShowCreateVaultPasswordGuidance);
        Assert.True(viewModel.UnlockCommand.CanExecute(null));
    }
}
