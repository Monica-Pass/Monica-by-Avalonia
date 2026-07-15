using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.ViewModels;
using Monica.Core.Models;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class BackgroundTransientSecretUiTests
{
    public BackgroundTransientSecretUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Minimize_releases_transient_security_transfer_and_generator_secrets()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var sourcePassword = new PasswordEntry
        {
            Id = 2001,
            Title = "Preserved source",
            Password = "preserved-vault-secret"
        };
        viewModel.Passwords.Add(sourcePassword);
        viewModel.SelectedSection = "Generator";
        PopulateTransientSecrets(viewModel);
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;

            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();

            AssertSettingsSecretsCleared(viewModel);
            AssertTransferSecretsCleared(viewModel);
            Assert.Equal("", viewModel.GeneratedPassword);
            Assert.Empty(viewModel.GeneratedPasswordHistory);
            Assert.Same(sourcePassword, Assert.Single(viewModel.Passwords));
            Assert.Equal("preserved-vault-secret", sourcePassword.Password);

            window.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            Assert.NotEmpty(viewModel.GeneratedPassword);
            Assert.NotEqual("retained-generator-secret", viewModel.GeneratedPassword);
            Assert.Empty(viewModel.GeneratedPasswordHistory);
        }
        finally
        {
            window.Close();
        }
    }

    private static void PopulateTransientSecrets(MainWindowViewModel viewModel)
    {
        viewModel.CurrentMasterPassword = "current-master";
        viewModel.NewMasterPassword = "new-master";
        viewModel.ConfirmNewMasterPassword = "new-master";
        viewModel.SecurityQuestion1Answer = "answer-one";
        viewModel.SecurityQuestion2Answer = "answer-two";
        viewModel.SecurityRecoveryAnswer1 = "recovery-one";
        viewModel.SecurityRecoveryAnswer2 = "recovery-two";
        viewModel.RecoveryNewMasterPassword = "recovered-master";
        viewModel.RecoveryConfirmNewMasterPassword = "recovered-master";
        viewModel.DangerZoneConfirmationText = "DELETE";

        viewModel.ImportJsonText = "json-import-secret";
        viewModel.ImportCsvText = "csv-import-secret";
        viewModel.ImportNoteCsvText = "note-import-secret";
        viewModel.ImportAegisJsonText = "aegis-import-secret";
        viewModel.AegisImportPassword = "aegis-password";
        viewModel.IsAegisImportPasswordRequired = true;
        viewModel.ImportTotpCsvText = "totp-import-secret";
        viewModel.KeePassImportPassword = "keepass-password";
        viewModel.ExportPreview = "json-export-secret";
        viewModel.ExportCsvPreview = "csv-export-secret";
        viewModel.ExportNoteCsvPreview = "note-export-secret";
        viewModel.ExportTotpCsvPreview = "totp-export-secret";
        viewModel.ExportAegisPreview = "aegis-export-secret";
        viewModel.ExportWalletCsvPreview = "wallet-export-secret";
        viewModel.ExportTimelinePreview = "timeline-export-secret";

        viewModel.GeneratedPassword = "retained-generator-secret";
        viewModel.GeneratedPasswordHistory.Add(new GeneratorHistoryItem(
            "retained-history-secret",
            "Random",
            "Strong",
            "Now"));
    }

    private static void AssertSettingsSecretsCleared(MainWindowViewModel viewModel)
    {
        Assert.Equal("", viewModel.CurrentMasterPassword);
        Assert.Equal("", viewModel.NewMasterPassword);
        Assert.Equal("", viewModel.ConfirmNewMasterPassword);
        Assert.Equal("", viewModel.SecurityQuestion1Answer);
        Assert.Equal("", viewModel.SecurityQuestion2Answer);
        Assert.Equal("", viewModel.SecurityRecoveryAnswer1);
        Assert.Equal("", viewModel.SecurityRecoveryAnswer2);
        Assert.Equal("", viewModel.RecoveryNewMasterPassword);
        Assert.Equal("", viewModel.RecoveryConfirmNewMasterPassword);
        Assert.Equal("", viewModel.DangerZoneConfirmationText);
    }

    private static void AssertTransferSecretsCleared(MainWindowViewModel viewModel)
    {
        Assert.Equal("", viewModel.ImportJsonText);
        Assert.Equal("", viewModel.ImportCsvText);
        Assert.Equal("", viewModel.ImportNoteCsvText);
        Assert.Equal("", viewModel.ImportAegisJsonText);
        Assert.Equal("", viewModel.AegisImportPassword);
        Assert.False(viewModel.IsAegisImportPasswordRequired);
        Assert.Equal("", viewModel.ImportTotpCsvText);
        Assert.Equal("", viewModel.KeePassImportPassword);
        Assert.Equal("", viewModel.ExportPreview);
        Assert.Equal("", viewModel.ExportCsvPreview);
        Assert.Equal("", viewModel.ExportNoteCsvPreview);
        Assert.Equal("", viewModel.ExportTotpCsvPreview);
        Assert.Equal("", viewModel.ExportAegisPreview);
        Assert.Equal("", viewModel.ExportWalletCsvPreview);
        Assert.Equal("", viewModel.ExportTimelinePreview);
    }
}
