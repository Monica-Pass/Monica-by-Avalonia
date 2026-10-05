using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.Sync.Bitwarden;
using Monica.App.Features.Vault;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Data.Bitwarden;
using Monica.Data.Repositories;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class BitwardenSyncWorkflowUiTests
{
    public BitwardenSyncWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Bitwarden_source_surface_exposes_secure_desktop_controls()
    {
        var view = new BitwardenSyncSourceView();

        Assert.NotNull(view.FindControl<ComboBox>("BitwardenAccountSelector"));
        Assert.NotNull(view.FindControl<Border>("BitwardenAccountStatusCard"));
        Assert.NotNull(view.FindControl<Button>("BitwardenSyncNowButton"));
        Assert.NotNull(view.FindControl<Button>("BitwardenReconnectButton"));
        Assert.NotNull(view.FindControl<Button>("BitwardenDisconnectButton"));
        Assert.NotNull(view.FindControl<StackPanel>("BitwardenConnectionForm"));
        Assert.NotNull(view.FindControl<StackPanel>("BitwardenConflictSection"));
        Assert.NotNull(view.FindControl<StackPanel>("BitwardenStuckEraseSection"));
        Assert.NotNull(view.FindControl<Button>("BitwardenAuthenticateButton"));
        Assert.NotNull(view.FindControl<Button>("BitwardenCancelConnectionButton"));
        Assert.Equal('*', view.FindControl<TextBox>("BitwardenMasterPasswordBox")!.PasswordChar);
    }

    [Fact]
    public async Task Bitwarden_two_factor_challenge_preserves_primary_credentials_until_continued()
    {
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            false,
            null,
            null,
            BitwardenLoginChallengeKind.TwoFactor,
            Factors: [new BitwardenLoginFactor(0, "Authenticator app")]));
        using var fixture = CreateFixture(authentication);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = "person@example.com";
        viewModel.BitwardenMasterPassword = "correct horse battery staple";

        await viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsBitwardenTwoFactorChallenge);
        Assert.Equal("correct horse battery staple", viewModel.BitwardenMasterPassword);
        Assert.Single(viewModel.BitwardenLoginFactors);
        Assert.NotNull(viewModel.SelectedBitwardenLoginFactor);
        Assert.False(viewModel.CanAuthenticateBitwarden);

        viewModel.BitwardenTwoFactorToken = "123456";
        Assert.True(viewModel.CanAuthenticateBitwarden);
        viewModel.SelectedSyncPage = "Sources";

        var view = new BitwardenSyncSourceView { DataContext = viewModel };
        var host = new Window { Width = 1000, Height = 760, Content = view };
        host.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<StackPanel>("BitwardenTwoFactorChallengePanel")!.IsVisible);
            Assert.False(view.FindControl<StackPanel>("BitwardenCaptchaChallengePanel")!.IsVisible);
            Assert.False(view.FindControl<StackPanel>("BitwardenNewDeviceChallengePanel")!.IsVisible);
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public async Task Bitwarden_success_and_vault_lock_clear_transient_authentication_secrets()
    {
        var account = CreateAccount(id: 0, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        using var fixture = CreateFixture(authentication);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";
        viewModel.BitwardenTwoFactorToken = "123456";
        viewModel.BitwardenCaptchaResponse = "captcha-token";
        viewModel.BitwardenNewDeviceOtp = "654321";

        await viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null);

        Assert.Equal("", viewModel.BitwardenMasterPassword);
        Assert.Equal("", viewModel.BitwardenTwoFactorToken);
        Assert.Equal("", viewModel.BitwardenCaptchaResponse);
        Assert.Equal("", viewModel.BitwardenNewDeviceOtp);
        Assert.False(viewModel.IsBitwardenConnectionEditorVisible);
        Assert.Single(fixture.AccountStore.Accounts);
        Assert.Equal(41, fixture.SessionManager.AccountId);

        viewModel.BitwardenMasterPassword = "temporary";
        viewModel.BitwardenTwoFactorToken = "temporary-code";
        viewModel.IsUnlocked = false;

        Assert.Equal("", viewModel.BitwardenMasterPassword);
        Assert.Equal("", viewModel.BitwardenTwoFactorToken);
        Assert.Equal("", viewModel.BitwardenEmail);
        Assert.Empty(viewModel.BitwardenAccounts);
        Assert.False(viewModel.IsBitwardenSyncActive);
    }

    // The connect button is where the transport rule meets the user, so the allowance is checked there and
    // not only in the validator: a vault served on this machine may be plain HTTP, anything else on the
    // network still has to bring TLS.
    [Fact]
    public void Bitwarden_connect_accepts_a_loopback_http_vault_and_refuses_a_plain_http_host_elsewhere()
    {
        using var fixture = CreateFixture(new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            false,
            null,
            null,
            BitwardenLoginChallengeKind.None,
            Factors: [])));
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = "person@example.com";
        viewModel.BitwardenMasterPassword = "correct horse battery staple";

        viewModel.BitwardenServerUrl = "http://localhost:8080/";
        Assert.True(viewModel.CanAuthenticateBitwarden);

        viewModel.BitwardenServerUrl = "http://192.168.1.20:8080/";
        Assert.False(viewModel.CanAuthenticateBitwarden);

        viewModel.BitwardenServerUrl = "https://vault.bitwarden.eu/";
        Assert.True(viewModel.CanAuthenticateBitwarden);
    }

    [Fact]
    public async Task Bitwarden_initial_sync_failure_keeps_the_account_connected_and_exposes_retry_feedback()
    {
        var account = CreateAccount(id: 0, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        using var fixture = CreateFixture(authentication, failSynchronization: true);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";

        await viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null);

        Assert.Single(fixture.AccountStore.Accounts);
        Assert.True(fixture.AccountStore.Accounts[0].IsConnected);
        Assert.True(viewModel.HasBitwardenOperationError);
        Assert.Contains("Sync now", viewModel.BitwardenOperationError, StringComparison.OrdinalIgnoreCase);
    }

    // The editor's single-entry offer is a note's only door into a vault, and the command behind it makes
    // three ordered commitments: save the draft first, refuse a shape the encoder cannot carry, and
    // otherwise stamp the vault id and hand the upload to the ordinary drift scan. Run against a recording
    // repository and the sync double, so the stamp is observed without a database or a server.
    [Fact]
    public async Task Note_publish_stamps_only_a_note_the_encoder_can_carry()
    {
        var repository = DispatchProxy.Create<IMonicaRepository, NotePublishRepositoryProxy>();
        var vault = (NotePublishRepositoryProxy)(object)repository;
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            false,
            null,
            null,
            BitwardenLoginChallengeKind.None,
            Factors: []));
        using var fixture = CreateFixture(authentication, repository: repository);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenAccounts.Add(new BitwardenAccountDisplayItem(
            CreateAccount(id: 7, connected: true),
            "Personal Bitwarden",
            "https://vault.bitwarden.com",
            "Connected",
            "Last sync just now",
            "",
            "",
            "",
            0,
            0));
        Assert.True(viewModel.BitwardenNotePublishOffered);

        viewModel.AddNoteCommand.Execute(null);
        Assert.NotNull(viewModel.SelectedNoteTab);

        // Markdown has no field on the server side, so publishing it would mean the next pull silently
        // rewriting the note. The draft still gets saved - it is the stamp that is refused.
        viewModel.NoteIsMarkdown = true;
        await viewModel.PublishCurrentNoteToBitwardenCommand.ExecuteAsync(null);

        Assert.NotEmpty(vault.WrittenSecureItems);
        Assert.All(vault.StampedVaultIds, vaultId => Assert.Null(vaultId));
        // The refusal is a failure-tone status line; its wording is localized, so the tone is the claim.
        Assert.True(viewModel.IsStatusMessageFailure);

        viewModel.NoteIsMarkdown = false;
        await viewModel.PublishCurrentNoteToBitwardenCommand.ExecuteAsync(null);

        Assert.Equal(7, vault.StampedVaultIds.Last());
        Assert.Equal(7, viewModel.SelectedNoteTab!.Source!.BitwardenVaultId);
        var coordinator = Assert.IsType<FakeSyncCoordinator>(
            fixture.Services.GetRequiredService<IBitwardenSyncCoordinator>());
        Assert.Equal(BitwardenSyncPhase.Completed, coordinator.GetState(7).Phase);
    }

    [Fact]
    public async Task Bitwarden_conflict_surface_lists_the_overwritten_edit_and_uploads_a_restore()
    {
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        var conflicts = new FakeConflictRestoreService();
        using var fixture = CreateFixture(authentication, conflictRestore: conflicts);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";

        await viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        // Selecting the account is what opens the list; nothing else asks for it.
        var row = Assert.Single(viewModel.BitwardenConflicts);
        Assert.Equal("Renamed on this device", row.Title);
        Assert.True(viewModel.HasBitwardenConflicts);
        Assert.True(viewModel.CanResolveBitwardenConflicts);

        await viewModel.RestoreBitwardenConflictCommand.ExecuteAsync(row);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([(7L, row.BackupId)], conflicts.Restored);
        Assert.Empty(viewModel.BitwardenConflicts);
    }

    // A row lives in an ItemsControl's DataTemplate, where the commands are reached through the
    // UserControl's name rather than its own binding source. Only a materialized template proves that
    // hop resolves; a broken name would ship as a list of dead buttons.
    [Fact]
    public async Task A_conflict_row_resolves_the_command_declared_outside_its_template()
    {
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        var conflicts = new FakeConflictRestoreService();
        using var fixture = CreateFixture(authentication, conflictRestore: conflicts);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";
        await viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null);
        viewModel.SelectedSyncPage = "Sources";

        var view = new BitwardenSyncSourceView { DataContext = viewModel };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            // Read the row here, not earlier: reloading the account list republishes the conflict rows,
            // so only the instance standing after the last dispatcher pass can be the one the
            // materialized template is holding.
            var row = Assert.Single(viewModel.BitwardenConflicts);
            var buttons = view.GetSelfAndVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Name is "BitwardenConflictRestoreButton" or "BitwardenConflictDiscardButton")
                .ToArray();

            Assert.Equal(2, buttons.Length);
            var restore = buttons.Single(button => button.Name == "BitwardenConflictRestoreButton");
            var discard = buttons.Single(button => button.Name == "BitwardenConflictDiscardButton");

            // Raising Button.ClickEvent was measured not to run a Command-bound button - only the
            // pointer pipeline calls OnClick - so what is asserted here is the hop the template has to
            // get right on its own: the command object, the row it carries, and that the call is live.
            Assert.Same(viewModel.RestoreBitwardenConflictCommand, restore.Command);
            Assert.Same(viewModel.DiscardBitwardenConflictCommand, discard.Command);
            Assert.Same(row, restore.CommandParameter);
            Assert.True(restore.IsEnabled);
            Assert.True(restore.Command!.CanExecute(restore.CommandParameter));

            restore.Command.Execute(restore.CommandParameter);
            // Execute returns before the resolve has run to the end, and a restore now reloads the
            // vault, so the dispatcher has to move several passes before the list is refreshed. One
            // RunJobs pass was only ever enough while restoring wrote nothing the screen showed.
            RunJobsUntil(
                () => viewModel.BitwardenConflicts.Count == 0,
                "the resolved conflict never left the list");

            Assert.Equal([(7L, row.BackupId)], conflicts.Restored);
            Assert.Empty(viewModel.BitwardenConflicts);
        }
        finally
        {
            window.Close();
        }
    }

    // The list is read without being awaited, so switching accounts while a read is still on disk lets
    // the previous vault's rows land afterwards: they would be listed under the account now selected,
    // and restoring one would ask that account for a backup it does not own.
    [Fact]
    public async Task A_conflict_read_that_lands_after_the_account_changed_is_dropped()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var conflicts = new FakeConflictRestoreService { Gate = gate.Task };
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        using var fixture = CreateFixture(authentication, conflictRestore: conflicts);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";
        await viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null);
        Assert.Equal(1, conflicts.Reads);

        fixture.AccountStore.Accounts.Add(CreateAccount(id: 8, connected: true));
        await viewModel.LoadBitwardenAccountsCommand.ExecuteAsync(null);
        viewModel.SelectedBitwardenAccount = viewModel.BitwardenAccounts.Single(item => item.Id == 8);
        Assert.True(conflicts.Reads >= 2, $"expected the new account to be read too, saw {conflicts.Reads} reads");

        gate.SetResult();
        await Task.WhenAll(conflicts.Landed);
        Dispatcher.UIThread.RunJobs();

        var row = Assert.Single(viewModel.BitwardenConflicts);
        Assert.Equal("Renamed on the other device", row.Title);
    }

    // Giving up is the branch that throws local content away, so the row it names matters: the discard
    // must address the vault now selected and leave the remote version the merge wrote.
    [Fact]
    public async Task Discarding_a_row_removes_it_and_leaves_the_rest_alone()
    {
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        var conflicts = new FakeConflictRestoreService();
        using var fixture = CreateFixture(authentication, conflictRestore: conflicts);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";
        await viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var row = Assert.Single(viewModel.BitwardenConflicts);
        await viewModel.DiscardBitwardenConflictCommand.ExecuteAsync(row);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([(7L, row.BackupId)], conflicts.Discarded);
        Assert.Empty(conflicts.Restored);
        Assert.Empty(viewModel.BitwardenConflicts);
        Assert.False(viewModel.HasBitwardenConflicts);
        Assert.Empty(viewModel.BitwardenOperationError);
    }

    // A delete the server would not take is the one sync outcome nobody can undo on their own: the queue has
    // stopped retrying, the entry is gone here and still alive there, and the pull keeps honouring the debt.
    // The list has to name each stuck erase separately and act on exactly the row it was given - the conflicts
    // next to it are a different decision about different content and must stay put.
    [Fact]
    public async Task Abandoning_a_stuck_erase_drops_only_that_row()
    {
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        var conflicts = new FakeConflictRestoreService();
        var erasures = new FakeStuckEraseService();
        using var fixture = CreateFixture(
            authentication,
            conflictRestore: conflicts,
            stuckErasures: erasures);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";

        RunOnUiThread(() => viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null));
        RunJobsUntil(
            () => viewModel.BitwardenStuckErasures.Count == 2 && viewModel.BitwardenConflicts.Count == 1,
            "the stuck-erase list never filled once the account was selected");

        Assert.True(viewModel.HasBitwardenStuckErasures);
        Assert.True(viewModel.CanResolveBitwardenStuckErasures);
        var refused = Assert.Single(
            viewModel.BitwardenStuckErasures,
            row => row.ReasonText == viewModel.L.Get("BitwardenStuckEraseReasonFailed"));
        var raced = Assert.Single(
            viewModel.BitwardenStuckErasures,
            row => row.ReasonText == viewModel.L.Get("BitwardenStuckEraseReasonConflict"));
        Assert.Contains("cipher-erase-refused", refused.CipherText, StringComparison.Ordinal);
        Assert.Contains("20", refused.LastAttemptText, StringComparison.Ordinal);
        var conflictRow = Assert.Single(viewModel.BitwardenConflicts);

        RunOnUiThread(() => viewModel.AbandonBitwardenStuckEraseCommand.ExecuteAsync(refused));
        RunJobsUntil(
            () => viewModel.BitwardenStuckErasures.Count == 1,
            "the abandoned erase never left the list");

        Assert.Equal([(7L, refused.OperationId)], erasures.Abandoned);
        Assert.Equal([raced], viewModel.BitwardenStuckErasures);
        Assert.Empty(conflicts.Restored);
        Assert.Empty(conflicts.Discarded);
        Assert.Equal([conflictRow], viewModel.BitwardenConflicts);
        Assert.Empty(viewModel.BitwardenOperationError);
    }

    // Two rows are materialized on purpose. A template that bound the command parameter to the wrong
    // instance would render a list where abandoning one erase quietly retires another, and only the rendered
    // hop - not the view model call above - can show which row a button is actually carrying.
    [Fact]
    public async Task A_stuck_erase_row_resolves_the_command_declared_outside_its_template()
    {
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        var erasures = new FakeStuckEraseService();
        using var fixture = CreateFixture(authentication, stuckErasures: erasures);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";
        await viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null);
        viewModel.SelectedSyncPage = "Sources";

        var view = new BitwardenSyncSourceView { DataContext = viewModel };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        try
        {
            Button[] Buttons() => view.GetSelfAndVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Name == "BitwardenStuckEraseAbandonButton")
                .ToArray();
            RunJobsUntil(
                () => Buttons().Length == 2 && viewModel.BitwardenStuckErasures.Count == 2,
                "the stuck-erase rows never materialized in the template");
            var buttons = Buttons();
            var rows = viewModel.BitwardenStuckErasures.ToArray();

            Assert.All(
                buttons,
                button => Assert.Same(viewModel.AbandonBitwardenStuckEraseCommand, button.Command));
            Assert.Equal(
                rows.Select(row => row.OperationId).ToArray(),
                buttons.Select(button =>
                    ((BitwardenStuckEraseDisplayItem)button.CommandParameter!).OperationId).ToArray());
            Assert.All(buttons, button => Assert.True(button.IsEnabled));

            var first = buttons[0];
            Assert.True(first.Command!.CanExecute(first.CommandParameter));
            first.Command.Execute(first.CommandParameter);
            RunJobsUntil(
                () => viewModel.BitwardenStuckErasures.Count == 1,
                "the abandoned erase never left the list");

            Assert.Equal([(7L, rows[0].OperationId)], erasures.Abandoned);
            Assert.Equal([rows[1]], viewModel.BitwardenStuckErasures);
        }
        finally
        {
            window.Close();
        }
    }

    // A pull writes straight into the database while every on-screen collection keeps its old copies,
    // so a change from another device used to stay invisible until the vault was locked and unlocked
    // again. This runs the user's own path: the library is open, one entry is up in the editor, "Sync
    // now" is pressed - and the renamed row has to read differently without another click.
    [Fact]
    public async Task A_pull_lands_in_the_library_the_user_is_reading()
    {
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        var repository = DispatchProxy.Create<IMonicaRepository, PulledVaultRepositoryProxy>();
        var vault = (PulledVaultRepositoryProxy)(object)repository;
        vault.Write(
            new PasswordEntry { Id = 901, Title = "Bank", Username = "person" });
        var pull = new FakePull(
            new BitwardenPullMergeResult(1, 1, 0, 0, 0, 0, 0),
            () => vault.Write(
                new PasswordEntry { Id = 901, Title = "Bank (work)", Username = "person" },
                new PasswordEntry { Id = 902, Title = "Written elsewhere", Username = "other" }));
        using var fixture = CreateFixture(authentication, repository: repository, pull: pull);
        var viewModel = fixture.ViewModel;
        fixture.Window.Show();
        fixture.Window.DataContext = viewModel;
        viewModel.IsUnlocked = true;
        viewModel.SelectSectionCommand.Execute(VaultPresets.LibrarySection);
        Dispatcher.UIThread.RunJobs();
        fixture.AccountStore.Accounts.Add(account);

        // The load has to run the way the shell runs it: LoadAsync yields back to the dispatcher
        // between passes, and those continuations only move when the queue is drained.
        RunOnUiThread(() => viewModel.LoadAsync());
        await viewModel.LoadBitwardenAccountsCommand.ExecuteAsync(null);
        var entryRow = Assert.Single(viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>());
        viewModel.SelectedVaultRow = entryRow;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Bank", viewModel.SelectedPassword?.Title);
        var readsBeforeSync = vault.PasswordReads;

        RunOnUiThread(() => viewModel.SyncBitwardenAccountCommand.ExecuteAsync(null));

        Assert.Equal(
            ["Bank (work)", "Written elsewhere"],
            viewModel.Passwords.Select(item => item.Title).ToArray());
        // Every entry object was replaced under the editor, so this is a fresh instance carrying the
        // remote title - the entry the user was reading, not the one they were reading before.
        Assert.Equal(901, viewModel.SelectedPassword?.Id);
        Assert.Equal("Bank (work)", viewModel.SelectedPassword?.Title);
        Assert.True(vault.PasswordReads > readsBeforeSync, "the library was not re-read");
        Assert.Equal(
            viewModel.L.Format("BitwardenPullAppliedFormat", 1, 1, 0),
            viewModel.StatusMessage);
    }

    // The reload is a full vault read with a visible cost, so an idle sync must not pay for it. The
    // counts come from the merge, and a sync that found nothing to write leaves the screen alone.
    [Fact]
    public async Task A_pull_that_changed_nothing_leaves_the_loaded_vault_alone()
    {
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        var repository = DispatchProxy.Create<IMonicaRepository, PulledVaultRepositoryProxy>();
        var vault = (PulledVaultRepositoryProxy)(object)repository;
        vault.Write(new PasswordEntry { Id = 901, Title = "Bank", Username = "person" });
        using var fixture = CreateFixture(authentication, repository: repository);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        fixture.AccountStore.Accounts.Add(account);
        RunOnUiThread(() => viewModel.LoadAsync());
        await viewModel.LoadBitwardenAccountsCommand.ExecuteAsync(null);
        var readsBeforeSync = vault.PasswordReads;

        RunOnUiThread(() => viewModel.SyncBitwardenAccountCommand.ExecuteAsync(null));

        Assert.Equal(readsBeforeSync, vault.PasswordReads);
        Assert.Equal(
            viewModel.L.Format("BitwardenSyncedFormat", viewModel.BitwardenAccounts.Single().DisplayName),
            viewModel.StatusMessage);
    }

    // A local edit Bitwarden cannot encode is refused on every round, and the pull rewrites the baseline
    // each time, so the same row is declined forever. Until the refusal list existed the count was
    // computed, returned and dropped: the vault kept the edit, the queue booked nothing, and the screen
    // said 已同步. The rows must carry the entry the queue actually declined and the code its encoder
    // gave, and the status line must contradict "已同步" rather than sit under it.
    [Fact]
    public async Task A_local_change_bitwarden_cannot_take_is_listed_with_the_reason_it_gave()
    {
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        var pull = new FakePull(
            new BitwardenPullMergeResult(0, 0, 0, 0, 0, 0, 0),
            () => { },
            [
                new BitwardenUnsyncableLocalChange(
                    "Bank",
                    true,
                    BitwardenPayloadRefusal.UnsupportedShape),
                new BitwardenUnsyncableLocalChange(
                    "",
                    false,
                    BitwardenPayloadRefusal.MissingRemoteRevision)
            ]);
        using var fixture = CreateFixture(authentication, pull: pull);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        fixture.AccountStore.Accounts.Add(account);
        await viewModel.LoadBitwardenAccountsCommand.ExecuteAsync(null);

        RunOnUiThread(() => viewModel.SyncBitwardenAccountCommand.ExecuteAsync(null));

        Assert.True(viewModel.HasBitwardenUnsyncableChanges);
        Assert.Equal(
            [
                ("Bank", viewModel.L.Get("BitwardenUnsyncableKindLogin"),
                    viewModel.L.Get("BitwardenUnsyncableReasonShape")),
                (viewModel.L.Get("BitwardenUnsyncableUntitledEntry"),
                    viewModel.L.Get("BitwardenUnsyncableKindSecureItem"),
                    viewModel.L.Get("BitwardenUnsyncableReasonNoRevision"))
            ],
            viewModel.BitwardenUnsyncableChanges
                .Select(row => (row.Title, row.KindText, row.ReasonText))
                .ToArray());
        Assert.Equal(
            viewModel.L.Format("BitwardenUnsyncableChangesFormat", 2),
            viewModel.StatusMessage);

        // The list describes the vault that is on screen; locking it away has to take the accusation
        // with it, because the next unlock opens a vault nobody has offered anything to.
        viewModel.IsUnlocked = false;
        Assert.False(viewModel.HasBitwardenUnsyncableChanges);
        Assert.Empty(viewModel.BitwardenUnsyncableChanges);
    }

    // The gate runs both ways, and only the rendered item can be asked: an expander row that stayed up
    // with nothing in it would tell every connected user their vault is broken, and a row that vanished
    // because something unrelated touched the account list would put the silent failure back again.
    [Fact]
    public async Task The_refusal_section_shows_only_while_a_change_is_standing_behind()
    {
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        var pull = new FakePull(
            new BitwardenPullMergeResult(0, 0, 0, 0, 0, 0, 0),
            () => { },
            [new BitwardenUnsyncableLocalChange("Bank", true, BitwardenPayloadRefusal.HasAttachments)]);
        using var fixture = CreateFixture(authentication, pull: pull);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        fixture.AccountStore.Accounts.Add(account);
        await viewModel.LoadBitwardenAccountsCommand.ExecuteAsync(null);
        viewModel.SelectedSyncPage = "Sources";

        var view = new BitwardenSyncSourceView { DataContext = viewModel };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        try
        {
            var item = view.FindControl<FASettingsExpanderItem>("BitwardenUnsyncableItem");
            Assert.NotNull(item);
            string[] Texts() => view.GetSelfAndVisualDescendants()
                .OfType<TextBlock>()
                .Select(text => text.Text ?? "")
                .ToArray();
            Dispatcher.UIThread.RunJobs();
            Assert.False(item.IsVisible);
            Assert.DoesNotContain("Bank", Texts());

            RunOnUiThread(() => viewModel.SyncBitwardenAccountCommand.ExecuteAsync(null));
            RunJobsUntil(
                () => item.IsVisible,
                "the refusal section never appeared once a change was refused");
            RunJobsUntil(
                () => Texts().Contains("Bank"),
                "the refused entry was never rendered as a row");
            Assert.Contains(viewModel.L.Get("BitwardenUnsyncableReasonAttachments"), Texts());

            // A synchronization refreshes the account list underneath itself, and the refresh used to run
            // the selection through null - which retired the warning a beat after it appeared.
            await viewModel.LoadBitwardenAccountsCommand.ExecuteAsync(null);
            Assert.True(viewModel.HasBitwardenUnsyncableChanges);
            Assert.True(item.IsVisible);

            // The refusal is one vault's business: another account on screen must not inherit it, and
            // coming back must not need another synchronization to say it again.
            fixture.AccountStore.Accounts.Add(CreateAccount(id: 8, connected: true));
            await viewModel.LoadBitwardenAccountsCommand.ExecuteAsync(null);
            viewModel.SelectedBitwardenAccount = viewModel.BitwardenAccounts.Single(row => row.Id == 8);
            RunJobsUntil(
                () => !item.IsVisible,
                "another account's vault inherited a refusal it was never offered");
            Assert.Single(viewModel.BitwardenUnsyncableChanges);
            viewModel.SelectedBitwardenAccount = viewModel.BitwardenAccounts.Single(row => row.Id == 7);
            RunJobsUntil(
                () => item.IsVisible,
                "the refusal did not come back with the account it belongs to");

            viewModel.IsUnlocked = false;
            RunJobsUntil(
                () => !item.IsVisible,
                "the refusal section survived the vault being locked");
            Assert.DoesNotContain("Bank", Texts());
        }
        finally
        {
            window.Close();
        }
    }

    // Connecting for the first time is the biggest pull there is, and the vault the account owns has
    // to be the one on screen right after - not one the user only sees after locking and unlocking.
    [Fact]
    public void The_first_pull_of_a_new_connection_shows_up_in_the_vault()
    {
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        var repository = DispatchProxy.Create<IMonicaRepository, PulledVaultRepositoryProxy>();
        var vault = (PulledVaultRepositoryProxy)(object)repository;
        var pull = new FakePull(
            new BitwardenPullMergeResult(3, 0, 0, 0, 0, 0, 0),
            () => vault.Write(new PasswordEntry { Id = 903, Title = "From the server", Username = "person" }));
        using var fixture = CreateFixture(authentication, repository: repository, pull: pull);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";
        Assert.Empty(viewModel.Passwords);

        RunOnUiThread(() => viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null));

        var pulled = Assert.Single(viewModel.Passwords);
        Assert.Equal("From the server", pulled.Title);
        Assert.Equal(
            viewModel.L.Format("BitwardenPullAppliedFormat", 3, 0, 0),
            viewModel.StatusMessage);
    }

    // Restoring is the conflict action that changes an entry the user can see, and the write goes to the
    // database: without reading the vault back the row keeps the title the pull overwrote theirs with, so
    // the button would report "restored" over a screen that still says otherwise.
    [Fact]
    public void Restoring_a_conflict_brings_the_local_edit_back_on_screen()
    {
        var account = CreateAccount(id: 7, connected: true);
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            true,
            account,
            CreateSecrets(),
            BitwardenLoginChallengeKind.None));
        var repository = DispatchProxy.Create<IMonicaRepository, PulledVaultRepositoryProxy>();
        var vault = (PulledVaultRepositoryProxy)(object)repository;
        vault.Write(new PasswordEntry { Id = 901, Title = "Renamed by the remote", Username = "person" });
        var conflicts = new FakeConflictRestoreService
        {
            OnRestore = () => vault.Write(
                new PasswordEntry { Id = 901, Title = "Renamed on this device", Username = "person" })
        };
        using var fixture = CreateFixture(authentication, conflictRestore: conflicts, repository: repository);
        var viewModel = fixture.ViewModel;
        viewModel.IsUnlocked = true;
        viewModel.BitwardenEmail = account.Email;
        viewModel.BitwardenMasterPassword = "master password";
        RunOnUiThread(() => viewModel.AuthenticateBitwardenCommand.ExecuteAsync(null));
        RunOnUiThread(() => viewModel.LoadAsync());
        var row = Assert.Single(viewModel.BitwardenConflicts);
        Assert.Equal("Renamed by the remote", Assert.Single(viewModel.Passwords).Title);

        RunOnUiThread(() => viewModel.RestoreBitwardenConflictCommand.ExecuteAsync(row));

        Assert.Equal("Renamed on this device", Assert.Single(viewModel.Passwords).Title);
        Assert.Empty(viewModel.BitwardenConflicts);
    }

    // Both halves of a permanent delete are wired by registration alone, and the view model takes its
    // queue as an optional parameter. Delete either line in the composition root and the app still
    // builds, still starts, and still clears the row locally - it just stops booking the erase and
    // stops honouring the booking on the next pull, so the entry the user threw away grows back. This
    // resolves from the same container that built the view model above, which is the only place the
    // production graph is reachable from a test.
    [Fact]
    public void The_production_graph_holds_both_halves_of_a_permanent_delete()
    {
        var authentication = new FakeAuthenticationService(_ => new BitwardenAuthenticationResult(
            false,
            null,
            null,
            BitwardenLoginChallengeKind.None));
        using var fixture = CreateFixture(authentication);

        Assert.IsType<BitwardenPurgeQueue>(fixture.Services.GetRequiredService<IBitwardenPurgeQueue>());
        Assert.IsType<BitwardenPullMergeService>(
            fixture.Services.GetRequiredService<IBitwardenPullMergeService>());
        Assert.IsType<BitwardenPendingOperationStore>(
            fixture.Services.GetRequiredService<IBitwardenPendingOperationStore>());
        // The decision that reads the same queue row is registered next to the two that wrote it, and a
        // missing line here costs nothing but the section: the view model takes its erasure service as an
        // optional parameter, so the list would simply never fill and the stuck delete would stay invisible.
        Assert.IsType<BitwardenStuckEraseService>(
            fixture.Services.GetRequiredService<IBitwardenStuckEraseService>());
    }

    private static void RunOnUiThread(Func<Task> work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await work();
                done.TrySetResult();
            }
            catch (Exception exception)
            {
                done.TrySetException(exception);
            }
        });

        var timeout = Stopwatch.StartNew();
        while (!done.Task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(20))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.True(done.Task.IsCompleted, "The work did not finish before the pump timeout.");
        done.Task.GetAwaiter().GetResult();
    }

    private static void RunJobsUntil(Func<bool> condition, string because)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(20))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.True(condition(), because);
    }

    private static Fixture CreateFixture(
        IBitwardenAuthenticationService authentication,
        bool failSynchronization = false,
        IBitwardenConflictRestoreService? conflictRestore = null,
        IMonicaRepository? repository = null,
        FakePull? pull = null,
        IBitwardenStuckEraseService? stuckErasures = null)
    {
        var accountStore = new FakeAccountStore();
        var sessionManager = new FakeSessionManager();
        var coordinator = new FakeSyncCoordinator(accountStore, failSynchronization, pull);
        var window = new Monica.App.MainWindow();
        var services = Monica.App.App.ConfigureServices(window, collection =>
        {
            collection.AddSingleton<IBitwardenAccountStore>(accountStore);
            collection.AddSingleton(authentication);
            collection.AddSingleton<IBitwardenSyncCoordinator>(coordinator);
            collection.AddSingleton<IBitwardenSessionManager>(sessionManager);
            collection.AddSingleton<IBitwardenDeviceIdentityProvider>(new FakeDeviceIdentityProvider());
            if (repository is not null)
            {
                collection.AddSingleton(repository);
            }

            if (conflictRestore is not null)
            {
                collection.AddSingleton(conflictRestore);
            }

            if (stuckErasures is not null)
            {
                collection.AddSingleton(stuckErasures);
            }
        });
        return new Fixture(
            window,
            services,
            services.GetRequiredService<MainWindowViewModel>(),
            accountStore,
            sessionManager);
    }

    private static BitwardenAccount CreateAccount(long id, bool connected) => new()
    {
        Id = id,
        Email = "person@example.com",
        DisplayName = "Personal Bitwarden",
        AccountKey = "bw:v1:test-account",
        Endpoints = BitwardenEndpointSet.UnitedStates,
        Kdf = BitwardenKdfParameters.Pbkdf2(),
        IsConnected = connected,
        IsDefault = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static BitwardenAccountSecrets CreateSecrets() => new(
        new byte[] { 1 },
        new byte[] { 2 },
        new byte[32],
        new byte[32],
        new byte[32]);

    private sealed record Fixture(
        Window Window,
        ServiceProvider Services,
        MainWindowViewModel ViewModel,
        FakeAccountStore AccountStore,
        FakeSessionManager SessionManager) : IDisposable
    {
        public void Dispose()
        {
            Window.Close();
            Services.Dispose();
        }
    }

    private sealed class FakeAuthenticationService(
        Func<BitwardenAuthenticationRequest, BitwardenAuthenticationResult> authenticate) :
        IBitwardenAuthenticationService
    {
        public Task<BitwardenKdfParameters> PreloginAsync(
            string email,
            BitwardenEndpointSet endpoints,
            BitwardenTlsOptions tls,
            string? clientCertificatePassword = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(BitwardenKdfParameters.Pbkdf2());

        public Task<BitwardenAuthenticationResult> AuthenticateAsync(
            BitwardenAuthenticationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(authenticate(request));

        public Task<(BitwardenAccount Account, BitwardenAccountSecrets Secrets)> RefreshAsync(
            BitwardenAccount account,
            BitwardenAccountSecrets currentSecrets,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAccountStore : IBitwardenAccountStore
    {
        public List<BitwardenAccount> Accounts { get; } = [];

        public Task<IReadOnlyList<BitwardenAccount>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BitwardenAccount>>(Accounts.ToArray());

        public Task<BitwardenAccount?> GetAsync(long accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Accounts.FirstOrDefault(account => account.Id == accountId));

        public Task<BitwardenAccount> SaveConnectedAsync(
            BitwardenAccount account,
            BitwardenAccountSecrets secrets,
            CancellationToken cancellationToken = default)
        {
            var saved = account with { Id = account.Id == 0 ? 41 : account.Id, IsConnected = true };
            Accounts.RemoveAll(existing => existing.Id == saved.Id);
            Accounts.Add(saved);
            return Task.FromResult(saved);
        }

        public Task<BitwardenAccountSecrets?> LoadSecretsAsync(
            long accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BitwardenAccountSecrets?>(null);

        public Task DisconnectAsync(long accountId, CancellationToken cancellationToken = default)
        {
            var index = Accounts.FindIndex(account => account.Id == accountId);
            if (index >= 0)
            {
                Accounts[index] = Accounts[index] with { IsConnected = false };
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(long accountId, CancellationToken cancellationToken = default)
        {
            Accounts.RemoveAll(account => account.Id == accountId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessionManager : IBitwardenSessionManager
    {
        public long? AccountId { get; private set; }

        public bool HasSession(long accountId) => AccountId == accountId;

        public void Open(long accountId, BitwardenAccountSecrets secrets, DateTimeOffset? accessTokenExpiresAt) =>
            AccountId = accountId;

        public bool TryCreateLease(long accountId, out BitwardenSessionLease? lease)
        {
            lease = null;
            return false;
        }

        public void Clear() => AccountId = null;
    }

    private sealed class FakeSyncCoordinator(
        FakeAccountStore accountStore,
        bool failSynchronization,
        FakePull? pull) : IBitwardenSyncCoordinator
    {
        private BitwardenSyncState _state = new(
            0,
            BitwardenSyncPhase.Idle,
            BitwardenSyncTrigger.Manual,
            DateTimeOffset.UtcNow);

        public event EventHandler<BitwardenSyncState>? StateChanged;

        public BitwardenSyncState GetState(long accountId) => _state with { AccountId = accountId };

        public Task<BitwardenSyncResult> SyncAsync(
            long accountId,
            BitwardenSyncTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            if (failSynchronization)
            {
                throw new HttpRequestException("Simulated synchronization failure.");
            }

            _state = new BitwardenSyncState(
                accountId,
                BitwardenSyncPhase.Completed,
                trigger,
                DateTimeOffset.UtcNow);
            StateChanged?.Invoke(this, _state);
            var account = accountStore.Accounts.Single(item => item.Id == accountId);
            pull?.Apply();
            return Task.FromResult(new BitwardenSyncResult(
                account,
                new BitwardenMutationBatchResult(0, 0, 0, 0, 0),
                pull?.Merge ?? new BitwardenPullMergeResult(0, 0, 0, 0, 0, 0, 0),
                pull?.Unsyncable ?? []));
        }
    }

    /// Stands in for a merge that reached the database: the write itself is covered by the pull
    /// service's own tests, so what the fake owes here is the counts and a repository that answers
    /// differently once the sync has run.
    private sealed record FakePull(
        BitwardenPullMergeResult Merge,
        Action Apply,
        IReadOnlyList<BitwardenUnsyncableLocalChange>? Unsyncable = null);

    private class PulledVaultRepositoryProxy : DispatchProxy
    {
        private IReadOnlyList<PasswordEntry> _passwordItems = [];
        private int _passwordReads;

        public int PasswordReads => _passwordReads;

        public void Write(params PasswordEntry[] entries) => _passwordItems = entries;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            return targetMethod.Name switch
            {
                nameof(IMonicaRepository.GetPasswordsAsync) => Task.FromResult(ReadPasswords()),
                nameof(IMonicaRepository.GetCustomFieldsByEntryIdsAsync) =>
                    Task.FromResult<IReadOnlyDictionary<long, IReadOnlyList<CustomField>>>(
                        new Dictionary<long, IReadOnlyList<CustomField>>()),
                nameof(IMonicaRepository.GetAttachmentsByOwnerIdsAsync) =>
                    Task.FromResult<IReadOnlyDictionary<long, IReadOnlyList<Attachment>>>(
                        new Dictionary<long, IReadOnlyList<Attachment>>()),
                nameof(IMonicaRepository.GetAttachmentOwnerIdsAsync) =>
                    Task.FromResult<IReadOnlyList<long>>([]),
                nameof(IMonicaRepository.GetSecureItemsAsync) =>
                    Task.FromResult<IReadOnlyList<SecureItem>>([]),
                nameof(IMonicaRepository.GetCategoriesAsync) =>
                    Task.FromResult<IReadOnlyList<Category>>([]),
                nameof(IMonicaRepository.GetPasswordQuickAccessRecordsAsync) =>
                    Task.FromResult<IReadOnlyList<PasswordQuickAccessRecord>>([]),
                nameof(IMonicaRepository.GetMdbxDatabasesAsync) =>
                    Task.FromResult<IReadOnlyList<LocalMdbxDatabase>>([]),
                nameof(IMonicaRepository.GetOperationLogsAsync) =>
                    Task.FromResult<IReadOnlyList<OperationLog>>([]),
                _ => throw new NotSupportedException($"Unexpected repository call: {targetMethod.Name}")
            };
        }

        private IReadOnlyList<PasswordEntry> ReadPasswords()
        {
            Interlocked.Increment(ref _passwordReads);
            return _passwordItems;
        }
    }

    /// Reads answer from an in-memory note list and writes are kept, including the vault id the row carried
    /// at that exact moment - the instance is mutated in place afterwards, so a snapshot is the only way to
    /// see whether a write was stamped or only saved.
    private class NotePublishRepositoryProxy : DispatchProxy
    {
        private readonly List<SecureItem> _items = [];

        public List<long?> StampedVaultIds { get; } = [];

        public IReadOnlyList<SecureItem> WrittenSecureItems => _items;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            return targetMethod.Name switch
            {
                nameof(IMonicaRepository.GetSecureItemsAsync) => Task.FromResult(
                    (IReadOnlyList<SecureItem>)_items
                        .Where(item => args is { Length: > 0 } && Equals(args[0], VaultItemType.Note) ||
                                        item.ItemType == VaultItemType.Note)
                        .ToList()),
                nameof(IMonicaRepository.SaveSecureItemAsync) => Save(args),
                nameof(IMonicaRepository.GetPasswordsAsync) => Task.FromResult<IReadOnlyList<PasswordEntry>>([]),
                nameof(IMonicaRepository.GetCustomFieldsByEntryIdsAsync) =>
                    Task.FromResult<IReadOnlyDictionary<long, IReadOnlyList<CustomField>>>(
                        new Dictionary<long, IReadOnlyList<CustomField>>()),
                nameof(IMonicaRepository.GetAttachmentsByOwnerIdsAsync) =>
                    Task.FromResult<IReadOnlyDictionary<long, IReadOnlyList<Attachment>>>(
                        new Dictionary<long, IReadOnlyList<Attachment>>()),
                nameof(IMonicaRepository.GetAttachmentOwnerIdsAsync) =>
                    Task.FromResult<IReadOnlyList<long>>([]),
                nameof(IMonicaRepository.GetCategoriesAsync) => Task.FromResult<IReadOnlyList<Category>>([]),
                nameof(IMonicaRepository.GetPasswordQuickAccessRecordsAsync) =>
                    Task.FromResult<IReadOnlyList<PasswordQuickAccessRecord>>([]),
                nameof(IMonicaRepository.GetMdbxDatabasesAsync) =>
                    Task.FromResult<IReadOnlyList<LocalMdbxDatabase>>([]),
                nameof(IMonicaRepository.GetOperationLogsAsync) =>
                    Task.FromResult<IReadOnlyList<OperationLog>>([]),
                nameof(IMonicaRepository.LogAsync) => Task.CompletedTask,
                _ => throw new NotSupportedException($"Unexpected repository call: {targetMethod.Name}")
            };
        }

        private Task<long> Save(object?[]? args)
        {
            var item = (SecureItem)(args?[0] ?? throw new ArgumentException("Save without a payload."));
            if (item.Id == 0)
            {
                item.Id = 4_000 + _items.Count;
            }

            StampedVaultIds.Add(item.BitwardenVaultId);
            _items.RemoveAll(existing => existing.Id == item.Id);
            _items.Add(item);
            return Task.FromResult(item.Id);
        }
    }

    private sealed class FakeDeviceIdentityProvider : IBitwardenDeviceIdentityProvider
    {
        public string DeviceIdentifier => "0123456789abcdef0123456789abcdef";
        public string DeviceName => "Monica test desktop";
    }

    private sealed class FakeStuckEraseService : IBitwardenStuckEraseService
    {
        private static readonly BitwardenStuckErase EditedElsewhere = new(
            OperationId: 700,
            CipherId: "cipher-erase-edited-elsewhere",
            Status: BitwardenMutationStatus.Conflict,
            LastAttemptAt: new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero));

        private static readonly BitwardenStuckErase Refused = new(
            OperationId: 701,
            CipherId: "cipher-erase-refused",
            Status: BitwardenMutationStatus.Failed,
            LastAttemptAt: new DateTimeOffset(2026, 9, 21, 9, 0, 0, TimeSpan.Zero));

        private readonly List<BitwardenStuckErase> stuck = [EditedElsewhere, Refused];

        public List<(long VaultId, long OperationId)> Abandoned { get; } = [];

        public Task<IReadOnlyList<BitwardenStuckErase>> GetStuckAsync(
            long vaultId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BitwardenStuckErase>>(vaultId == 7 ? stuck.ToArray() : []);

        public Task<IReadOnlyList<BitwardenStuckErase>> GetSuppressedAsync(
            long vaultId,
            IReadOnlySet<string> suppressedCipherIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BitwardenStuckErase>>(
                vaultId == 7
                    ? stuck
                        .Select(row => row with
                        {
                            SuppressedThisRound = suppressedCipherIds.Contains(row.CipherId)
                        })
                        .ToArray()
                    : []);

        public Task AbandonAsync(long vaultId, long operationId, CancellationToken cancellationToken = default)
        {
            Abandoned.Add((vaultId, operationId));
            stuck.RemoveAll(row => row.OperationId == operationId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConflictRestoreService : IBitwardenConflictRestoreService
    {
        private static readonly BitwardenConflictSummary VaultSevenRow = new(
            BackupId: 900,
            CipherId: "cipher-overwritten-by-pull",
            IsPassword: true,
            Title: "Renamed on this device",
            Reason: "Local and remote changed at the same revision.",
            CreatedAt: new DateTimeOffset(2026, 9, 20, 8, 30, 0, TimeSpan.Zero));

        private static readonly BitwardenConflictSummary VaultEightRow = new(
            BackupId: 901,
            CipherId: "cipher-overwritten-in-the-other-vault",
            IsPassword: true,
            Title: "Renamed on the other device",
            Reason: "Local and remote changed at the same revision.",
            CreatedAt: new DateTimeOffset(2026, 9, 21, 8, 30, 0, TimeSpan.Zero));

        private readonly Dictionary<long, List<BitwardenConflictSummary>> unresolved = new()
        {
            [7] = [VaultSevenRow],
            [8] = [VaultEightRow]
        };

        public List<(long VaultId, long BackupId)> Restored { get; } = [];

        public List<(long VaultId, long BackupId)> Discarded { get; } = [];

        public int Reads { get; private set; }

        public Task? Gate { get; set; }

        /// Stands in for the write the real service makes against the repository, so a test can see the
        /// entry change value the way the database does.
        public Action? OnRestore { get; set; }

        public List<Task> Landed { get; } = [];

        public async Task<IReadOnlyList<BitwardenConflictSummary>> GetSummariesAsync(
            long vaultId,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            var landed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Landed.Add(landed.Task);
            if (Gate is { } gate)
            {
                await gate;
            }

            var result = unresolved.TryGetValue(vaultId, out var rows)
                ? rows.ToArray()
                : [];
            landed.SetResult();
            return result;
        }

        public Task RestoreAsync(long vaultId, long backupId, CancellationToken cancellationToken = default)
        {
            Restored.Add((vaultId, backupId));
            Remove(backupId);
            OnRestore?.Invoke();
            return Task.CompletedTask;
        }

        public Task DiscardAsync(long vaultId, long backupId, CancellationToken cancellationToken = default)
        {
            Discarded.Add((vaultId, backupId));
            Remove(backupId);
            return Task.CompletedTask;
        }

        private void Remove(long backupId)
        {
            foreach (var rows in unresolved.Values)
            {
                rows.RemoveAll(summary => summary.BackupId == backupId);
            }
        }
    }
}
