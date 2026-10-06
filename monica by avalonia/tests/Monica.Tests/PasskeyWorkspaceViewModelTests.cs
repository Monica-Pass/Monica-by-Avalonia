using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Core.Passkeys;
using Monica.Data.Passkeys;

namespace Monica.Tests;

public sealed class PasskeyWorkspaceViewModelTests
{
    [Fact]
    public async Task Locked_workspace_never_reads_storage_and_exposes_no_commands()
    {
        var fixture = new Fixture { Unlocked = false };
        await fixture.ViewModel.RefreshAsync();
        Assert.Equal(0, fixture.Store.ListCalls);
        Assert.False(fixture.ViewModel.RefreshCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.DeleteSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task List_search_and_details_are_public_projections_without_private_key_references()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.RefreshAsync();
        var first = fixture.ViewModel.Items[0];
        Assert.Null(typeof(PasskeyListItem).GetProperty("PrivateKeyAlias"));
        Assert.Equal(0, fixture.Store.PrivateKeyReads);
        fixture.ViewModel.SelectedItem = first;
        fixture.ViewModel.SearchText = "ALICE";
        Assert.Single(fixture.ViewModel.Items);
        Assert.Same(first, fixture.ViewModel.SelectedItem);
        fixture.ViewModel.SearchText = "second.test";
        Assert.Equal(2, Assert.Single(fixture.ViewModel.Items).Id);
        Assert.Null(fixture.ViewModel.SelectedItem);
        fixture.ViewModel.SearchText = "no-match";
        Assert.True(fixture.ViewModel.IsEmpty);
        Assert.Equal(fixture.Localization.Get("PasskeyNoMatches"), fixture.ViewModel.EmptyText);
    }

    [Fact]
    public async Task Lock_clears_existing_rows_selection_search_and_account_text()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.RefreshAsync();
        var held = fixture.ViewModel.Items[0];
        fixture.ViewModel.SelectedItem = held;
        fixture.ViewModel.SearchText = "alice";
        fixture.Lock();
        Assert.Empty(fixture.ViewModel.Items);
        Assert.Null(fixture.ViewModel.SelectedItem);
        Assert.Empty(fixture.ViewModel.SearchText);
        Assert.Empty(held.UserName);
        Assert.Empty(held.RpId);
        Assert.Empty(held.CredentialId);
    }

    [Fact]
    public async Task Old_load_cannot_overwrite_a_new_session_or_end_its_busy_state()
    {
        var fixture = new Fixture();
        var oldRows = new TaskCompletionSource<IReadOnlyList<PasskeyEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newRows = new TaskCompletionSource<IReadOnlyList<PasskeyEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.ListHandler = _ => oldRows.Task;
        var oldLoad = fixture.ViewModel.RefreshAsync();
        fixture.Lock();
        fixture.Unlocked = true;
        fixture.Store.ListHandler = _ => newRows.Task;
        var newLoad = fixture.ViewModel.RefreshAsync();
        oldRows.SetResult([Entry(77, "old.test", "stale-account")]);
        await oldLoad;
        Assert.True(fixture.ViewModel.IsBusy);
        Assert.Empty(fixture.ViewModel.Items);
        newRows.SetResult([Entry(88, "current.test", "current-account")]);
        await newLoad;
        Assert.False(fixture.ViewModel.IsBusy);
        Assert.Equal("current-account", Assert.Single(fixture.ViewModel.Items).UserName);
    }

    [Fact]
    public async Task Old_delete_confirmation_cannot_delete_after_lock_and_reunlock()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.RefreshAsync();
        fixture.ViewModel.SelectedItem = fixture.ViewModel.Items[0];
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Confirmation.Result = confirmation.Task;
        var deletion = fixture.ViewModel.DeleteSelectedAsync();
        fixture.Lock();
        fixture.Unlocked = true;
        confirmation.SetResult(true);
        await deletion;
        Assert.Equal(0, fixture.Store.DeleteCalls);
        Assert.Empty(fixture.ViewModel.Items);
        Assert.False(fixture.ViewModel.HasError);
    }

    [Fact]
    public async Task Platform_removal_explains_that_the_system_key_remains_and_does_not_resolve_a_key()
    {
        var fixture = new Fixture();
        fixture.Store.Rows = [Entry(3, "platform.test", "user", PasskeyModes.WindowsHello)];
        await fixture.ViewModel.RefreshAsync();
        fixture.ViewModel.SelectedItem = fixture.ViewModel.Items[0];
        Assert.Equal(fixture.Localization.Get("PasskeyRemoveRecord"), fixture.ViewModel.DeleteText);
        await fixture.ViewModel.DeleteSelectedAsync();
        Assert.Contains("system passkey", fixture.Confirmation.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, fixture.Store.DeleteCalls);
        Assert.Equal(0, fixture.Store.PrivateKeyReads);
        Assert.Empty(fixture.ViewModel.Items);
    }

    [Fact]
    public async Task Deletion_failure_preserves_the_row_and_uses_localized_secret_free_errors()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.RefreshAsync();
        var item = fixture.ViewModel.Items[0];
        fixture.ViewModel.SelectedItem = item;
        fixture.Store.DeleteHandler = _ => Task.FromException<bool>(new InvalidOperationException("private-key-sentinel"));
        await fixture.ViewModel.DeleteSelectedAsync();
        Assert.Contains(item, fixture.ViewModel.Items);
        Assert.Same(item, fixture.ViewModel.SelectedItem);
        Assert.Equal(fixture.Localization.Get("PasskeyDeleteFailed"), fixture.ViewModel.ErrorText);
        Assert.DoesNotContain("private-key-sentinel", fixture.ViewModel.ErrorText);
        fixture.Localization.SetLanguage("zh-CN");
        fixture.ViewModel.RefreshLocalization();
        Assert.Equal(fixture.Localization.Get("PasskeyDeleteFailed"), fixture.ViewModel.ErrorText);
    }

    [Fact]
    public async Task Cancelled_confirmation_keeps_the_credential_and_metadata()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.RefreshAsync();
        fixture.ViewModel.SelectedItem = fixture.ViewModel.Items[0];
        fixture.Confirmation.Result = Task.FromResult(false);
        await fixture.ViewModel.DeleteSelectedAsync();
        Assert.Equal(0, fixture.Store.DeleteCalls);
        Assert.Equal(2, fixture.ViewModel.Items.Count);
        Assert.NotNull(fixture.ViewModel.SelectedItem);
    }

    [Fact]
    public async Task Selection_changed_while_deleting_is_preserved_for_the_surviving_account()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.RefreshAsync();
        fixture.ViewModel.SelectedItem = fixture.ViewModel.Items[0];
        var survivor = fixture.ViewModel.Items[1];
        var deleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.DeleteHandler = _ => deleted.Task;
        var operation = fixture.ViewModel.DeleteSelectedAsync();
        fixture.ViewModel.SelectedItem = survivor;
        deleted.SetResult(true);
        await operation;
        Assert.Same(survivor, fixture.ViewModel.SelectedItem);
        Assert.Same(survivor, Assert.Single(fixture.ViewModel.Items));
    }

    private static PasskeyEntry Entry(long id, string rp, string user, string mode = PasskeyModes.BitwardenCompatible) => new()
    {
        Id = id,
        RpId = rp,
        RpName = rp,
        UserName = user,
        UserDisplayName = user,
        CredentialId = $"credential-{id}",
        PasskeyMode = mode,
        PrivateKeyAlias = "private-key-sentinel"
    };

    private sealed class Fixture
    {
        public Fixture()
        {
            Localization.SetLanguage("en-US");
            ViewModel = new(Store, Confirmation, Localization, () => Unlocked, () => CancellationToken.None);
        }
        public bool Unlocked { get; set; } = true;
        public FakeStore Store { get; } = new();
        public FakeConfirmation Confirmation { get; } = new();
        public LocalizationService Localization { get; } = new();
        public PasskeyWorkspaceViewModel ViewModel { get; }
        public void Lock() { Unlocked = false; ViewModel.ClearSensitiveState(); }
    }

    private sealed class FakeConfirmation : IConfirmationDialogService
    {
        public Task<bool> Result { get; set; } = Task.FromResult(true);
        public string Message { get; private set; } = "";
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText, string? closeButtonText = null, CancellationToken cancellationToken = default)
        { Message = message; return Result; }
        public Task<bool> ConfirmTypedAsync(string title, string message, string requiredPhrase, string instruction, string primaryButtonText, string? closeButtonText = null, CancellationToken cancellationToken = default) => Result;
    }

    private sealed class FakeStore : IPasskeyStore
    {
        public IReadOnlyList<PasskeyEntry> Rows { get; set; } = [Entry(1, "first.test", "Alice"), Entry(2, "second.test", "Bob")];
        public Func<CancellationToken, Task<IReadOnlyList<PasskeyEntry>>>? ListHandler { get; set; }
        public Func<CancellationToken, Task<bool>>? DeleteHandler { get; set; }
        public int ListCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int PrivateKeyReads { get; private set; }
        public Task<IReadOnlyList<PasskeyEntry>> ListAllAsync(CancellationToken cancellationToken = default)
        { ListCalls++; return ListHandler?.Invoke(cancellationToken) ?? Task.FromResult(Rows); }
        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
        { DeleteCalls++; return DeleteHandler?.Invoke(cancellationToken) ?? Task.FromResult(true); }
        public Task<string?> ResolvePrivateKeyAsync(PasskeyEntry entry, CancellationToken cancellationToken = default)
        { PrivateKeyReads++; return Task.FromResult<string?>("private-key-sentinel"); }
        public Task<long> SaveAsync(PasskeyEntry entry, string? privateKeyPkcs8Base64 = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PasskeyEntry?> GetAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PasskeyEntry?> FindAsync(string credentialId, string? rpId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PasskeyEntry>> ListByRpIdAsync(string rpId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MarkUsedAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
