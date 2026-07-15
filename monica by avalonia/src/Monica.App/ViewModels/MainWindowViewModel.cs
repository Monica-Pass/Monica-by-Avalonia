using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.Styling;
using Monica.App;
using Monica.App.Services;
using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Data.Services;
using Monica.Platform.Services;
namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IMonicaRepository _repository;
    private readonly ICryptoService _cryptoService;
    private readonly IPasswordGeneratorService _passwordGenerator;
    private readonly IClipboardService _clipboardService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ILocalizationService _localization;
    private readonly IVaultSessionService _vaultSessionService;
    private readonly IWindowPrivacyService _windowPrivacyService;
    private readonly IExportAuthorizationService _exportAuthorizationService;
    private int _vaultLoadVersion;
    public MainWindowViewModel(
        IMonicaRepository repository,
        IVaultCredentialStore credentialStore,
        ICryptoService cryptoService,
        ITotpService totpService,
        IPasswordGeneratorService passwordGenerator,
        IImportExportService importExportService,
        IPlatformCapabilityService platformCapabilityService,
        IPlatformIntegrationService platformIntegrationService,
        IClipboardService clipboardService,
        IWebDavBackupService? webDavBackupService,
        IMdbxVaultService mdbxVaultService,
        IPasswordAttachmentFileService passwordAttachmentFileService,
        IPasswordEditorDialogService passwordEditorDialogService,
        IPasswordDetailDialogService passwordDetailDialogService,
        ICategoryPickerDialogService categoryPickerDialogService,
        ILegacyVaultDetector? legacyVaultDetector,
        IAppSettingsService settingsService,
        ILocalizationService localization,
        IPwnedPasswordService? pwnedPasswordService = null,
        IConfirmationDialogService? confirmationDialogService = null,
        ITotpEditorDialogService? totpEditorDialogService = null,
        IWalletItemEditorDialogService? walletItemEditorDialogService = null,
        IMasterPasswordMaintenanceService? masterPasswordMaintenanceService = null,
        IVaultUnlockCoordinator? vaultUnlockCoordinator = null,
        IExternalLinkService? externalLinkService = null,
        IFileSystemPickerService? fileSystemPickerService = null,
        IVaultSessionService? vaultSessionService = null,
        IWindowPrivacyService? windowPrivacyService = null,
        IExportAuthorizationService? exportAuthorizationService = null,
        IOneDriveBackupService? oneDriveBackupService = null,
        IKeePassVaultService? keePassVaultService = null)
    {
        _repository = repository;
        _cryptoService = cryptoService;
        _totpService = totpService;
        _passwordGenerator = passwordGenerator;
        _pwnedPasswordService = pwnedPasswordService ?? new PwnedPasswordService();
        _importExportService = importExportService;
        _clipboardService = clipboardService;
        _vaultSessionService = vaultSessionService ?? new VaultSessionService();
        _windowPrivacyService = windowPrivacyService ?? new DisabledWindowPrivacyService();
        _exportAuthorizationService = exportAuthorizationService ?? new SessionExportAuthorizationService(_cryptoService);
        _webDavBackupService = webDavBackupService ?? new DisabledWebDavBackupService();
        _oneDriveBackupService = oneDriveBackupService ?? new DisabledOneDriveBackupService();
        _keePassVaultService = keePassVaultService ?? new KeePassVaultService();
        _mdbxVaultService = mdbxVaultService;
        _passwordAttachmentFileService = passwordAttachmentFileService;
        _passwordEditorDialogService = passwordEditorDialogService;
        _passwordDetailDialogService = passwordDetailDialogService;
        _categoryPickerDialogService = categoryPickerDialogService;
        _confirmationDialogService = confirmationDialogService ?? new DisabledConfirmationDialogService();
        _totpEditorDialogService = totpEditorDialogService ?? new DisabledTotpEditorDialogService();
        _walletItemEditorDialogService = walletItemEditorDialogService ?? new DisabledWalletItemEditorDialogService();
        _masterPasswordMaintenanceService = masterPasswordMaintenanceService ?? new DisabledMasterPasswordMaintenanceService();
        _vaultUnlockCoordinator = vaultUnlockCoordinator ?? new VaultUnlockCoordinator(
            credentialStore,
            _cryptoService,
            legacyVaultDetector ?? new NoLegacyVaultDetector());
        _settingsService = settingsService;
        _localization = localization;
        _localization.PropertyChanged += (_, _) => RefreshLocalizedProperties();
        _sourceCapabilities = platformCapabilityService.GetCapabilities();
        _sourcePlatformIntegrationCapabilities = platformIntegrationService.GetCapabilities();
        _externalLinkService = externalLinkService ?? new SystemExternalLinkService(platformIntegrationService);
        _fileSystemPickerService = fileSystemPickerService ?? new CapabilityOnlyFileSystemPickerService(platformIntegrationService);
        PlatformName = platformIntegrationService.PlatformName;
        CompromisedPasswordStatus = _localization.Get("CompromisedPasswordNotChecked");
        RefreshPlatformIntegrationCapabilities();
        RefreshCapabilities();
        RefreshChoiceLabels();
        RefreshMdbxHealthItems();
        RefreshSyncHealthItems();
    }

    public ILocalizationService L => _localization;

    [ObservableProperty]
    private string _selectedSection = "Passwords";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecoverableStatusMessage))]
    private string _statusMessage = "Locked";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecoverableStatusMessage))]
    private bool _isLoadingVault;

    [ObservableProperty]
    private string _vaultLoadStageText = "";

    [ObservableProperty]
    private long _lastVaultLoadDurationMilliseconds;

    public bool IsOtherWorkspaceCompact =>
        OtherWorkspaceViewportWidth > 0 &&
        (OtherWorkspaceViewportWidth < 980 || OtherWorkspaceViewportHeight < 460);
    public Thickness TotpCodeConsolePadding => IsOtherWorkspaceCompact
        ? new Thickness(16)
        : new Thickness(24);
    public double TotpCodeFontSize => IsOtherWorkspaceCompact ? 40 : 56;
    private double _otherWorkspaceViewportWidth;

    private double _otherWorkspaceViewportHeight;

    public double OtherWorkspaceViewportWidth
    {
        get => _otherWorkspaceViewportWidth;
        set
        {
            if (SetProperty(ref _otherWorkspaceViewportWidth, Math.Max(0, value)))
            {
                RaiseOtherWorkspaceLayoutState();
            }
        }
    }

    public double OtherWorkspaceViewportHeight
    {
        get => _otherWorkspaceViewportHeight;
        set
        {
            if (SetProperty(ref _otherWorkspaceViewportHeight, Math.Max(0, value)))
            {
                RaiseOtherWorkspaceLayoutState();
            }
        }
    }

    public string SelectedSectionTitle => SectionTitle(SelectedSection);
    public string ShellVaultText => SelectedSection switch
    {
        "Mdbx" => "MDBX",
        "DatabaseManagement" => "Database",
        "Sync" => WebDavEnabled ? "WebDAV" : "Local",
        "Settings" => "Monica",
        "Archive" => "Archive",
        "RecycleBin" => "Recycle Bin",
        _ => "Monica Local"
    };
    public string ShellSyncText => SelectedSection switch
    {
        "Mdbx" => MdbxDatabases.Count > 0 ? "Vaults Ready" : "Metadata",
        "DatabaseManagement" => "Sources Ready",
        "Sync" => WebDavEnabled ? "Sync Ready" : "Local Only",
        "Settings" => "Ready",
        _ => StatusMessage
    };
    public string ShellPageText => SelectedSectionTitle;
    public string ShellPlatformText => OperatingSystem.IsWindows() ? "Windows" :
        OperatingSystem.IsMacOS() ? "macOS" :
        OperatingSystem.IsLinux() ? "Linux" :
        "Desktop";

    partial void OnSelectedSectionChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedSectionTitle));
        RaiseShellStatus();
        RefreshSecurityAnalysisIfNeeded();
        if (!string.Equals(value, "Settings", StringComparison.OrdinalIgnoreCase))
        {
            ClearTransientSettingsSecurityInputs();
        }

        if (!string.Equals(value, "Sync", StringComparison.OrdinalIgnoreCase))
        {
            ClearSensitiveImportBuffers();
            ClearSensitiveExportPreviews();
        }

        if (string.Equals(value, "Generator", StringComparison.OrdinalIgnoreCase))
        {
            EnsureGeneratedPassword();
        }
    }

    partial void OnStatusMessageChanged(string value)
    {
        RaiseShellStatus();
        OnPropertyChanged(nameof(HasUnlockStatusMessage));
    }

    private static bool IsRecoverableStatusMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("failure", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("无法", StringComparison.Ordinal) ||
            value.Contains("失败", StringComparison.Ordinal) ||
            value.Contains("错误", StringComparison.Ordinal);
    }

    private void RaiseShellStatus()
    {
        OnPropertyChanged(nameof(ShellVaultText));
        OnPropertyChanged(nameof(ShellSyncText));
        OnPropertyChanged(nameof(ShellPageText));
        OnPropertyChanged(nameof(ShellPlatformText));
    }

    private async Task LoadAfterUnlockAsync()
    {
        await Task.Yield();
        await LoadCoreAsync(deferSecurityAnalysis: true);
    }

    private static async Task YieldVaultLoadUiAsync()
    {
        await Task.Yield();
    }

    [RelayCommand]
    public Task LoadAsync() => LoadCoreAsync(deferSecurityAnalysis: false);

    private async Task LoadCoreAsync(bool deferSecurityAnalysis)
    {
        if (!IsUnlocked && _cryptoService.IsUnlocked)
        {
            IsUnlocked = true;
        }

        if (IsLoadingVault ||
            (_vaultSessionService.IsExplicitlyLocked && !_cryptoService.IsUnlocked))
        {
            AppDiagnostics.Info("Vault load skipped because the vault is locked or another load is running");
            return;
        }

        var sessionCancellationToken = _vaultSessionService.IsUnlocked
            ? _vaultSessionService.SessionCancellationToken
            : CancellationToken.None;
        var loadVersion = ++_vaultLoadVersion;
        BeginPasswordProjectionNotificationDeferral();
        IsLoadingVault = true;
        VaultLoadStageText = "准备加载保险库...";
        var loadStopwatch = Stopwatch.StartNew();
        AppDiagnostics.Info("Vault load started");
        try
        {
            StatusMessage = "正在加载保险库数据...";
            SelectedPassword = null;
            SelectedPasswordDetails = null;
            _selectedPasswordCount = 0;

            StatusMessage = "正在后台读取保险库数据...";
            VaultLoadStageText = "正在读取密码、笔记和分类...";
            if (SmokeVaultLoadDelayMilliseconds > 0)
            {
                var delay = Math.Clamp(SmokeVaultLoadDelayMilliseconds, 0, 30000);
                AppDiagnostics.Info($"Smoke UI vault load delay started. milliseconds={delay}");
                await Task.Delay(delay, sessionCancellationToken);
                AppDiagnostics.Info("Smoke UI vault load delay completed");
            }

            var snapshot = await Task.Run(async () =>
            {
                var loadedSnapshot = await VaultSnapshotLoader.LoadAsync(_repository);
                VaultPasswordPresentationPreparer.Prepare(
                    loadedSnapshot,
                    _totpService,
                    sessionCancellationToken);
                var preparedTotpItems = VaultTotpPresentationPreparer.Prepare(
                    loadedSnapshot,
                    _totpService,
                    sessionCancellationToken);
                return loadedSnapshot with { PreparedTotpItems = preparedTotpItems };
            }, sessionCancellationToken);
            sessionCancellationToken.ThrowIfCancellationRequested();
            VaultLoadStageText = "正在整理密码列表...";
            await YieldVaultLoadUiAsync();
            _passwordCustomFields = snapshot.PasswordCustomFields;
            _passwordAttachments = snapshot.PasswordAttachments;
            _passwordQuickAccessRecords = snapshot.PasswordQuickAccessRecords;

            AppDiagnostics.Measure("Track password selections", () =>
            {
                foreach (var item in snapshot.AllPasswords)
                {
                    TrackPasswordSelection(item);
                }
            });
            await YieldVaultLoadUiAsync();
            AppDiagnostics.Measure("Replace password collections", () =>
            {
                ReplaceItems(Passwords, snapshot.ActivePasswords);
                ReplaceItems(ArchivedPasswords, snapshot.ArchivedPasswords);
                ReplaceItems(DeletedPasswords, snapshot.DeletedPasswords);
                RefreshPasswordSelectionStateFromPasswords();
                RaisePasswordQuickAccessState();
            });
            await YieldVaultLoadUiAsync();

            VaultLoadStageText = "正在加载笔记和安全项目...";
            await YieldVaultLoadUiAsync();
            AppDiagnostics.Measure("Replace secure item collections", () =>
            {
                ReplaceItems(NoteItems, snapshot.NoteItems);

                foreach (var item in snapshot.WalletItems)
                {
                    item.IsSelected = false;
                    TrackWalletSelection(item);
                }

                ReplaceItems(WalletItems, snapshot.WalletItems);
            });
            await YieldVaultLoadUiAsync();

            VaultLoadStageText = "正在加载文件夹和保险库源...";
            await YieldVaultLoadUiAsync();
            AppDiagnostics.Measure("Replace folder and source collections", () =>
            {
                ReplaceItems(Categories, snapshot.Categories);
                RefreshPasswordFolderFilters();
                ReplaceItems(MdbxDatabases, snapshot.MdbxDatabases);
                RefreshMdbxVaultState();
                RefreshVaultSources();
            });
            await YieldVaultLoadUiAsync();
            VaultLoadStageText = "正在加载验证码...";
            await YieldVaultLoadUiAsync();
            AppDiagnostics.Measure("Apply TOTP collections", () => ApplyPreparedTotpItems(snapshot.PreparedTotpItems));
            AppDiagnostics.Measure("Finalize vault load UI state", () =>
            {
                ReconcileSecureItemSelectionsAfterLoad();
                RaiseAllCountState();
                RaiseFilteredPasswordsChanged();
            });
            EndPasswordProjectionNotificationDeferral();
            StatusMessage = _localization.Get(
                HasPendingLegacyBusinessData
                    ? "VaultUnlockedLegacyBusinessDataPending"
                    : "VaultUnlocked");
            VaultLoadStageText = "保险库已就绪";
            _ = LoadTimelineDeferredAsync();
            if (deferSecurityAnalysis)
            {
                InvalidateSecurityAnalysis();
            }
            else
            {
                AppDiagnostics.Measure("Refresh security analysis", RefreshSecurityAnalysis);
            }

            LastVaultLoadDurationMilliseconds = loadStopwatch.ElapsedMilliseconds;
            AppDiagnostics.Info($"Vault load completed in {LastVaultLoadDurationMilliseconds} ms. passwords={Passwords.Count}, archived={ArchivedPasswords.Count}, deleted={DeletedPasswords.Count}, notes={NoteItems.Count}, totp={TotpItems.Count}, wallet={WalletItems.Count}");
        }
        catch (OperationCanceledException) when (sessionCancellationToken.IsCancellationRequested)
        {
            AppDiagnostics.Info("Vault load canceled because the session was locked");
            ClearSensitiveSessionState();
            EndPasswordProjectionNotificationDeferral();
            VaultLoadStageText = "";
        }
        catch (Exception ex)
        {
            LastVaultLoadDurationMilliseconds = loadStopwatch.ElapsedMilliseconds;
            AppDiagnostics.Error($"Vault load failed after {loadStopwatch.ElapsedMilliseconds} ms", ex);
            IsUnlocked = false;
            ClearSensitiveSessionState();
            EndPasswordProjectionNotificationDeferral();
            VaultLoadStageText = "保险库加载失败";
            StatusMessage = _localization.Format("VaultLoadFailedFormat", ex.Message);
        }
        finally
        {
            EndPasswordProjectionNotificationDeferral();
            if (loadVersion == _vaultLoadVersion)
            {
                IsLoadingVault = false;
            }
        }
    }

    [RelayCommand]
    private void SelectSection(string? section)
    {
        if (!string.IsNullOrWhiteSpace(section))
        {
            SelectedSection = section;
        }
    }





    private static bool IsWorkspacePageSelected(string selectedPage, string expectedPage) =>
        string.Equals(selectedPage, expectedPage, StringComparison.OrdinalIgnoreCase);



















    private void RefreshLocalizedProperties()
    {
        RefreshChoiceLabels();
        RefreshPlatformIntegrationCapabilities();
        RefreshCapabilities();
        OnPropertyChanged(nameof(SelectedSectionTitle));
        OnPropertyChanged(nameof(PlatformIntegrationsTitle));
        RaisePlatformIntegrationState();
        RaiseAboutText();
        RaiseSecurityRecoveryText();
        RaiseMasterPasswordMaintenanceText();
        RaiseDangerZoneText();
        OnPropertyChanged(nameof(LoginTitle));
        OnPropertyChanged(nameof(LoginDescription));
        OnPropertyChanged(nameof(LoginButtonText));
        OnPropertyChanged(nameof(MasterPasswordPrivacyNotice));
        OnPropertyChanged(nameof(ToggleMasterPasswordVisibilityLabel));
        OnPropertyChanged(nameof(ToggleConfirmMasterPasswordVisibilityLabel));
        OnPropertyChanged(nameof(LockVaultText));
        RefreshGeneratorLocalizedState();
        OnPropertyChanged(nameof(LegacyVaultImportPromptText));
        OnPropertyChanged(nameof(WebDavBackupOptionsSummaryText));
        RaiseSyncPageState();
        RefreshVaultSources();
        RaiseWebDavBackupHistoryState();
        RaisePasswordQuickAccessState();
        RaisePasswordFilterState();
        OnPropertyChanged(nameof(ClearPasswordFiltersText));
        OnPropertyChanged(nameof(PasswordEmptyStateText));
        OnPropertyChanged(nameof(SelectPasswordItemsText));
        OnPropertyChanged(nameof(SelectAllVisiblePasswordsText));
        OnPropertyChanged(nameof(BackToPasswordListText));
        OnPropertyChanged(nameof(RetryPasswordDetailsText));
        RaisePasswordSortText();
        OnPropertyChanged(nameof(TotpScanQrText));
        OnPropertyChanged(nameof(TotpManualAddText));
        OnPropertyChanged(nameof(TotpMoreActionsText));
        OnPropertyChanged(nameof(TotpFilterTitleText));
        OnPropertyChanged(nameof(TotpIssuerGroupsText));
        OnPropertyChanged(nameof(TotpNoFilteredResultsText));
        OnPropertyChanged(nameof(TotpEmptyStateText));
        OnPropertyChanged(nameof(ClearTotpFiltersText));
        OnPropertyChanged(nameof(TotpShowHiddenText));
        OnPropertyChanged(nameof(TotpHelpText));
        if (SelectedTotpItem is not null)
        {
            SelectedTotpDetails = new TotpItemDetailsViewModel(_localization, SelectedTotpItem);
        }
        if (SelectedWalletItem is not null)
        {
            SelectedWalletDetails = new WalletItemDetailsViewModel(_localization, SelectedWalletItem);
        }
        RaiseAllCountState();
        OnPropertyChanged(nameof(SecurityIssueCountText));
        InvalidateNoteEditorProjections();
        OnPropertyChanged(nameof(NotePreviewMarkdown));
        OnPropertyChanged(nameof(NotePlainPreview));
        OnPropertyChanged(nameof(NoteFormatText));
        OnPropertyChanged(nameof(NoteEditorStatusText));
        OnPropertyChanged(nameof(NoteReferenceItems));
        if (!_hasCompromisedPasswordCheckResults)
        {
            CompromisedPasswordStatus = _localization.Get("CompromisedPasswordNotChecked");
        }
    }


    private string SectionTitle(string section)
    {
        return section switch
        {
            "Passwords" => _localization.Passwords,
            "Notes" => _localization.SecureNotes,
            "Totp" => _localization.Totp,
            "Cards" => _localization.Cards,
            "Generator" => _localization.Generator,
            "Archive" => _localization.Archive,
            "RecycleBin" => _localization.RecycleBin,
            "SecurityAnalysis" => _localization.SecurityAnalysis,
            "Timeline" => _localization.Timeline,
            "Mdbx" => _localization.Get("MdbxVaults"),
            "DatabaseManagement" => _localization.DatabaseManagement,
            "Sync" => _localization.SyncAndBackup,
            "Settings" => _localization.Settings,
            _ => section
        };
    }





    private void ReconcileSecureItemSelectionsAfterLoad()
    {
        if (SelectedNote is not null)
        {
            SelectedNote = SelectedNote.Id > 0
                ? NoteItems.FirstOrDefault(item => item.Id == SelectedNote.Id)
                : null;
        }

        if (SelectedWalletItem is not null)
        {
            SelectedWalletItem = SelectedWalletItem.Id > 0
                ? WalletItems.FirstOrDefault(item => item.Id == SelectedWalletItem.Id)
                : null;
        }

        SelectedWalletItem ??= WalletItems.FirstOrDefault();
    }

}
