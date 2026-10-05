using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.Data.Mdbx;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly ObservableRangeCollection<MdbxUnknownEntryDescriptor> _mdbxUnknownEntryItems = [];
    private int _mdbxUnknownEntriesVersion;
    private int _mdbxUnknownEntryDetailsVersion;
    private CancellationTokenSource? _mdbxUnknownEntryDetailsCts;

    public ObservableCollection<MdbxUnknownEntryDescriptor> MdbxUnknownEntries => _mdbxUnknownEntryItems;
    public ObservableCollection<PasswordDetailField> MdbxUnknownEntryFields { get; } = [];
    public bool HasMdbxUnknownEntries => MdbxUnknownEntries.Count > 0;
    public bool HasMdbxUnknownEntryDetails => SelectedMdbxUnknownEntry is not null;
    public bool HasMdbxUnknownEntryDetailsError => !string.IsNullOrEmpty(MdbxUnknownEntryDetailsError);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MdbxUnknownEntrySummaryText))]
    private bool _isLoadingMdbxUnknownEntries;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MdbxUnknownEntrySummaryText))]
    private bool _hasLoadedMdbxUnknownEntries;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MdbxUnknownEntrySummaryText))]
    private bool _isMdbxUnknownEntryDiscoveryFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMdbxUnknownEntryDetails))]
    private MdbxUnknownEntryDescriptor? _selectedMdbxUnknownEntry;

    [ObservableProperty]
    private bool _isLoadingMdbxUnknownEntryDetails;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMdbxUnknownEntryDetailsError))]
    private string _mdbxUnknownEntryDetailsError = "";

    private bool CanReadMdbxUnknownEntries(CancellationToken cancellationToken) =>
        IsUnlocked && !_isUnlockedShellHibernated &&
        _vaultSessionService.IsUnlocked && !cancellationToken.IsCancellationRequested;

    private bool CanShowMdbxUnknownEntryDetails(CancellationToken cancellationToken) =>
        CanReadMdbxUnknownEntries(cancellationToken) && _isWindowActive && !IsMdbxBusy &&
        string.Equals(SelectedSection, "Mdbx", StringComparison.OrdinalIgnoreCase) &&
        IsMdbxHealthSelected;

    private async Task RefreshMdbxUnknownEntryCountAsync(CancellationToken cancellationToken)
    {
        if (!CanReadMdbxUnknownEntries(cancellationToken))
        {
            return;
        }

        var version = Interlocked.Increment(ref _mdbxUnknownEntriesVersion);
        ClearMdbxUnknownEntryDetails();
        IsLoadingMdbxUnknownEntries = true;
        IsMdbxUnknownEntryDiscoveryFailed = false;
        try
        {
            if (_mdbxUnknownEntryDiagnostics is null)
            {
                ReplaceMdbxUnknownEntries([]);
                HasLoadedMdbxUnknownEntries = false;
                return;
            }

            var entries = await _mdbxUnknownEntryDiagnostics.GetUnknownMdbxEntriesAsync(
                includeDeleted: false,
                cancellationToken);
            if (version != Volatile.Read(ref _mdbxUnknownEntriesVersion) ||
                !CanReadMdbxUnknownEntries(cancellationToken))
            {
                return;
            }

            ReplaceMdbxUnknownEntries(entries);
            HasLoadedMdbxUnknownEntries = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _mdbxUnknownEntriesVersion) &&
                CanReadMdbxUnknownEntries(cancellationToken))
            {
                ReplaceMdbxUnknownEntries([]);
                HasLoadedMdbxUnknownEntries = false;
                IsMdbxUnknownEntryDiscoveryFailed = true;
            }
        }
        finally
        {
            if (version == Volatile.Read(ref _mdbxUnknownEntriesVersion))
            {
                IsLoadingMdbxUnknownEntries = false;
                RefreshMdbxHealthItems();
            }
        }
    }

    private void ReplaceMdbxUnknownEntries(IEnumerable<MdbxUnknownEntryDescriptor> entries)
    {
        _mdbxUnknownEntryItems.ReplaceRange(entries);
        MdbxUnknownEntryCount = MdbxUnknownEntries.Count;
        OnPropertyChanged(nameof(MdbxUnknownEntryCount));
        OnPropertyChanged(nameof(HasMdbxUnknownEntries));
        OnPropertyChanged(nameof(MdbxUnknownEntrySummaryText));
    }

    [RelayCommand]
    private async Task RefreshMdbxUnknownEntriesAsync()
    {
        var cancellationToken = _vaultSessionService.SessionCancellationToken;
        if (IsLoadingMdbxUnknownEntries || !CanShowMdbxUnknownEntryDetails(cancellationToken))
        {
            return;
        }

        await RefreshMdbxUnknownEntryCountAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task ShowMdbxUnknownEntryDetailsAsync(MdbxUnknownEntryDescriptor? entry)
    {
        var sessionCancellationToken = _vaultSessionService.SessionCancellationToken;
        if (entry is null || _mdbxUnknownEntryDiagnostics is null ||
            !MdbxUnknownEntries.Contains(entry) ||
            !CanShowMdbxUnknownEntryDetails(sessionCancellationToken))
        {
            return;
        }

        ClearMdbxUnknownEntryDetails();
        var version = Volatile.Read(ref _mdbxUnknownEntryDetailsVersion);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellationToken);
        _mdbxUnknownEntryDetailsCts = cts;
        SelectedMdbxUnknownEntry = entry;
        IsLoadingMdbxUnknownEntryDetails = true;
        try
        {
            var detail = await _mdbxUnknownEntryDiagnostics.ReadUnknownMdbxEntryAsync(
                entry.EntryId, entry.ProjectId, cts.Token);
            if (!IsCurrentMdbxUnknownEntryDetails(version, entry, cts.Token))
            {
                return;
            }

            if (detail is null)
            {
                MdbxUnknownEntryDetailsError = _localization.Get("MdbxUnknownEntryUnavailable");
                return;
            }

            SelectedMdbxUnknownEntry = detail.Descriptor;
            foreach (var field in CreateMdbxUnknownEntryFields(detail))
            {
                MdbxUnknownEntryFields.Add(field);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (MdbxObjectDisclosureException ex)
        {
            if (IsCurrentMdbxUnknownEntryDetails(version, entry, cts.Token))
            {
                MdbxUnknownEntryDetailsError = _localization.Get(ex.Outcome switch
                {
                    "read-only-session" => "MdbxUnknownEntryReadOnlySession",
                    "constraints-unsupported" => "MdbxUnknownEntryConstraintsUnsupported",
                    "payload-too-large" => "MdbxUnknownEntryPayloadTooLarge",
                    _ => "MdbxUnknownEntryDetailsFailed"
                });
            }
        }
        catch (Exception)
        {
            if (IsCurrentMdbxUnknownEntryDetails(version, entry, cts.Token))
            {
                MdbxUnknownEntryDetailsError = _localization.Get("MdbxUnknownEntryDetailsFailed");
            }
        }
        finally
        {
            if (version == Volatile.Read(ref _mdbxUnknownEntryDetailsVersion))
            {
                IsLoadingMdbxUnknownEntryDetails = false;
            }

            if (ReferenceEquals(_mdbxUnknownEntryDetailsCts, cts))
            {
                _mdbxUnknownEntryDetailsCts = null;
            }

            cts.Dispose();
        }
    }

    private bool IsCurrentMdbxUnknownEntryDetails(
        int version, MdbxUnknownEntryDescriptor entry, CancellationToken cancellationToken) =>
        version == Volatile.Read(ref _mdbxUnknownEntryDetailsVersion) &&
        CanShowMdbxUnknownEntryDetails(cancellationToken) &&
        SelectedMdbxUnknownEntry is { } selected &&
        selected.EntryId == entry.EntryId && selected.ProjectId == entry.ProjectId;

    [RelayCommand]
    private void ToggleMdbxUnknownEntryFieldVisibility(PasswordDetailField? field)
    {
        if (field is not null && field.CanToggleVisibility && MdbxUnknownEntryFields.Contains(field) &&
            CanShowMdbxUnknownEntryDetails(_vaultSessionService.SessionCancellationToken))
        {
            field.IsVisible = !field.IsVisible;
        }
    }

    [RelayCommand]
    private async Task CopyMdbxUnknownEntryFieldAsync(PasswordDetailField? field)
    {
        var cancellationToken = _vaultSessionService.SessionCancellationToken;
        if (field is null || !field.CanCopy || !MdbxUnknownEntryFields.Contains(field) ||
            !CanShowMdbxUnknownEntryDetails(cancellationToken))
        {
            return;
        }

        var version = Volatile.Read(ref _mdbxUnknownEntryDetailsVersion);
        try
        {
            await _clipboardService.SetSensitiveTextAsync(field.CopyValue);
            if (version != Volatile.Read(ref _mdbxUnknownEntryDetailsVersion) ||
                !CanShowMdbxUnknownEntryDetails(cancellationToken))
            {
                await _clipboardService.ClearOwnedContentAsync();
                return;
            }

            SetStatusMessage("CopiedToClipboard");
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _mdbxUnknownEntryDetailsVersion) &&
                CanShowMdbxUnknownEntryDetails(cancellationToken))
            {
                SetStatusFailure("MdbxUnknownEntryCopyFailed");
            }
        }
    }

    [RelayCommand]
    private void CloseMdbxUnknownEntryDetails() => ClearMdbxUnknownEntryDetails();

    public void ClearMdbxUnknownEntryDetails()
    {
        Interlocked.Increment(ref _mdbxUnknownEntryDetailsVersion);
        var cts = _mdbxUnknownEntryDetailsCts;
        _mdbxUnknownEntryDetailsCts = null;
        cts?.Cancel();
        foreach (var field in MdbxUnknownEntryFields)
        {
            field.ClearSensitiveState();
        }

        MdbxUnknownEntryFields.Clear();
        SelectedMdbxUnknownEntry = null;
        IsLoadingMdbxUnknownEntryDetails = false;
        MdbxUnknownEntryDetailsError = "";
    }

    public void ClearMdbxUnknownEntries()
    {
        Interlocked.Increment(ref _mdbxUnknownEntriesVersion);
        ClearMdbxUnknownEntryDetails();
        ReplaceMdbxUnknownEntries([]);
        IsLoadingMdbxUnknownEntries = false;
        HasLoadedMdbxUnknownEntries = false;
        IsMdbxUnknownEntryDiscoveryFailed = false;
        RefreshMdbxHealthItems();
    }
}
