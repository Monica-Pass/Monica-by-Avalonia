using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Monica.App.Services;

public interface ILocalizationService : INotifyPropertyChanged
{
    string this[string key] { get; }
    string SelectedLanguage { get; }
    CultureInfo Culture { get; }
    string Get(string key);
    string Format(string key, params object[] args);
    string GetLanguageName(string language);
    void SetLanguage(string language);

    string Passwords { get; }
    string SecureNotes { get; }
    string Totp { get; }
    string Cards { get; }
    string Generator { get; }
    string Archive { get; }
    string RecycleBin { get; }
    string ArchiveEmptyHint { get; }
    string RecycleBinEmptyHint { get; }
    string Timeline { get; }
    string TimelineEmptyHint { get; }
    string TimelineEmptySelectionHint { get; }
    string SecurityAnalysis { get; }
    string SecurityAnalysisSubtitle { get; }
    string SecurityScore { get; }
    string WeakPasswords { get; }
    string DuplicatePasswords { get; }
    string DuplicateWebsites { get; }
    string MissingTwoFactor { get; }
    string StalePasswords { get; }
    string CompromisedPasswords { get; }
    string CheckCompromisedPasswords { get; }
    string SyncAndBackup { get; }
    string DatabaseManagement { get; }
    string DataManagement { get; }
    string DataManagementDescription { get; }
    string Settings { get; }
    string Folders { get; }
    string Personal { get; }
    string AllFolders { get; }
    string FolderScopes { get; }
    string NewFolder { get; }
    string CreateFolder { get; }
    string RenameFolder { get; }
    string DeleteFolder { get; }
    string Refresh { get; }
    string Export { get; }
    string UnlockMonica { get; }
    string CreateMonicaVault { get; }
    string LegacyVaultDetected { get; }
    string UnlockDescription { get; }
    string CreateVaultDescription { get; }
    string MasterPasswordWatermark { get; }
    string ConfirmMasterPasswordWatermark { get; }
    string Unlock { get; }
    string CreateVault { get; }
    string PasswordManager { get; }
    string DeletedPasswords { get; }
    string Search { get; }
    string AddPassword { get; }
    string EditPassword { get; }
    string PasswordDetails { get; }
    string LoadingPasswordDetails { get; }
    string Details { get; }
    string PasswordHistory { get; }
    string PasswordHistoryDescription { get; }
    string PasswordHistoryLatest { get; }
    string ClearPasswordHistory { get; }
    string Favorite { get; }
    string Copy { get; }
    string CopyPassword { get; }
    string CopyUsername { get; }
    string CopyWebsite { get; }
    string BatchFavorite { get; }
    string BatchArchive { get; }
    string BatchDelete { get; }
    string MoveToFolder { get; }
    string Move { get; }
    string MoveSelectedPasswordsDescription { get; }
    string StackSelectedPasswords { get; }
    string ArchivePassword { get; }
    string UnarchivePassword { get; }
    string MoveToRecycleBin { get; }
    string QuickFilterFavorite { get; }
    string QuickFilter2Fa { get; }
    string QuickFilterNotes { get; }
    string QuickFilterPasskey { get; }
    string QuickFilterBoundNote { get; }
    string QuickFilterUncategorized { get; }
    string QuickFilterLocalOnly { get; }
    string QuickFilterAttachments { get; }
    string QuickAccessRecent { get; }
    string QuickAccessFrequent { get; }
    string SortPasswords { get; }
    string MoreOptions { get; }
    string RestorePassword { get; }
    string DeletePermanently { get; }
    string EmptyRecycleBin { get; }
    string Delete { get; }
    string Select { get; }
    string Save { get; }
    string Cancel { get; }
    string NoFolder { get; }
    string NewPassword { get; }
    string PasswordTitleRequired { get; }
    string PasswordValueRequired { get; }
    string PasswordTitle { get; }
    string Website { get; }
    string Username { get; }
    string Password { get; }
    string Category { get; }
    string BoundNote { get; }
    string SecurityVerification { get; }
    string AuthenticatorKey { get; }
    string AuthenticatorKeyHint { get; }
    string TotpCode { get; }
    string RemainingTime { get; }
    string Issuer { get; }
    string Account { get; }
    string TotpSecret { get; }
    string AppBinding { get; }
    string AppName { get; }
    string AppPackageName { get; }
    string NoBoundNote { get; }
    string Untitled { get; }
    string PersonalInfo { get; }
    string Email { get; }
    string Phone { get; }
    string AddressLine { get; }
    string City { get; }
    string State { get; }
    string ZipCode { get; }
    string Country { get; }
    string CardInfo { get; }
    string CreditCardNumber { get; }
    string CreditCardHolder { get; }
    string CreditCardExpiry { get; }
    string CreditCardCvv { get; }
    string AdvancedLogin { get; }
    string LoginType { get; }
    string LoginTypePassword { get; }
    string LoginTypeSso { get; }
    string LoginTypeWifi { get; }
    string LoginTypeSshKey { get; }
    string SsoProvider { get; }
    string PasskeyBindings { get; }
    string WifiMetadata { get; }
    string SshKeyData { get; }
    string CustomIcon { get; }
    string CustomIconType { get; }
    string CustomIconValue { get; }
    string CustomIconDescription { get; }
    string CustomIconUseDefault { get; }
    string CustomIconSimple { get; }
    string CustomIconUploaded { get; }
    string CustomIconSimpleHint { get; }
    string CustomIconUploadedHint { get; }
    string CustomFields { get; }
    string CustomFieldsHint { get; }
    string Attachments { get; }
    string Attachment { get; }
    string AddAttachment { get; }
    string NoAttachments { get; }
    string SelectAttachment { get; }
    string Notes { get; }
    string SourceMetadata { get; }
    string CreatedAt { get; }
    string UpdatedAt { get; }
    string Close { get; }
    string TwoStepVerification { get; }
    string AddAuthenticator { get; }
    string EditAuthenticator { get; }
    string TotpPageDescription { get; }
    string AdvancedTotpOptions { get; }
    string TotpSecretHint { get; }
    string CopyCode { get; }
    string Wallet { get; }
    string AddItem { get; }
    string AddWalletItem { get; }
    string EditWalletItem { get; }
    string WalletPageDescription { get; }
    string Document { get; }
    string BankCard { get; }
    string DocumentNumber { get; }
    string FullName { get; }
    string IssuedDate { get; }
    string ExpiryDate { get; }
    string IssuedBy { get; }
    string Nationality { get; }
    string AdditionalInfo { get; }
    string CardNumber { get; }
    string CardholderName { get; }
    string Expiry { get; }
    string ExpiryMonth { get; }
    string ExpiryYear { get; }
    string BankName { get; }
    string BillingAddress { get; }
    string CardBrand { get; }
    string DocumentPhotos { get; }
    string NoDocumentPhotos { get; }
    string ImagePathsWatermark { get; }
    string ImagePathsDescription { get; }
    string DesktopEquivalents { get; }
    string DesktopEquivalentsMessage { get; }
    string CreateMdbxMetadata { get; }
    string MdbxVaults { get; }
    string MdbxVaultsDescription { get; }
    string MdbxLocalSource { get; }
    string MdbxWebDavSource { get; }
    string MdbxOneDriveSource { get; }
    string CreateLocalMdbxVault { get; }
    string RegisterMdbxSource { get; }
    string Configure { get; }
    string MdbxSourcesSection { get; }
    string MdbxWorkingCopiesSection { get; }
    string MdbxHealthSection { get; }
    string MdbxDiagnostics { get; }
    string MdbxRemotePath { get; }
    string MdbxLastSynced { get; }
    string RegisteredMdbxVaults { get; }
    string NoMdbxVaults { get; }
    string MdbxEmptyHint { get; }
    string Default { get; }
    string LocalPath { get; }
    string SetDefault { get; }
    string Open { get; }
    string MdbxRuntime { get; }
    string MdbxSecurity { get; }
    string MdbxAndroidParity { get; }
    string MdbxAndroidParityDescription { get; }
    string MdbxAndroidParityLocal { get; }
    string MdbxAndroidParityRemote { get; }
    string LocalDatabase { get; }
    string LocalDatabaseDescription { get; }
    string ExternalDatabases { get; }
    string ExternalDatabasesDescription { get; }
    string MdbxDatabaseCount { get; }
    string RegisteredDatabases { get; }
    string DatabaseSourcesEmptyHint { get; }
    string WebDavConnection { get; }
    string SyncOverview { get; }
    string SyncConfiguration { get; }
    string TestConnection { get; }
    string FeatureParityMap { get; }
    string FeatureParityMapDescription { get; }
    string ExportPreview { get; }
    string ImportMonicaJson { get; }
    string ImportMonicaJsonDescription { get; }
    string ImportJsonWatermark { get; }
    string ImportAegisJson { get; }
    string ImportAegisJsonDescription { get; }
    string ImportAegisJsonWatermark { get; }
    string ImportTotpCsv { get; }
    string ImportTotpCsvDescription { get; }
    string ImportTotpCsvWatermark { get; }
    string ImportNoteCsv { get; }
    string ImportNoteCsvDescription { get; }
    string ImportNoteCsvWatermark { get; }
    string ImportPasswordCsv { get; }
    string ImportPasswordCsvDescription { get; }
    string ImportCsvWatermark { get; }
    string ExportPasswordCsv { get; }
    string ExportCsvPreview { get; }
    string ExportTotpCsv { get; }
    string ExportTotpCsvDescription { get; }
    string ExportTotpCsvPreview { get; }
    string ExportNoteCsv { get; }
    string ExportNoteCsvDescription { get; }
    string ExportNoteCsvPreview { get; }
    string ExportAegisJson { get; }
    string ExportAegisJsonDescription { get; }
    string ExportAegisPreview { get; }
    string Import { get; }
    string ImportFromFile { get; }
    string SaveJsonExport { get; }
    string SaveCsvExport { get; }
    string SaveTotpCsvExport { get; }
    string SaveNoteCsvExport { get; }
    string SaveAegisExport { get; }
    string PasswordGenerator { get; }
    string GeneratedPassword { get; }
    string GeneratedPasswordLabel { get; }
    string GeneratedPasswordPlaceholder { get; }
    string Generate { get; }
    string SaveAsLogin { get; }
    string GeneratorLength { get; }
    string GeneratorMode { get; }
    string GeneratorTemplate { get; }
    string GeneratorWordCount { get; }
    string ExcludeSimilarCharacters { get; }
    string RecentGeneratedPasswords { get; }
    string NoGeneratedPasswordHistory { get; }
    string UsePassword { get; }
    string Reset { get; }
    string ShowPassword { get; }
    string HidePassword { get; }
    string AddPasswordRow { get; }
    string IncludeUppercase { get; }
    string IncludeLowercase { get; }
    string IncludeNumbers { get; }
    string IncludeSymbols { get; }
    string PasswordStrength { get; }
    string SecureNotesDescription { get; }
    string CreateSecureItem { get; }
    string NewSecureNote { get; }
    string NoteTitleWatermark { get; }
    string NoteTagsWatermark { get; }
    string NoteContentWatermark { get; }
    string PlainText { get; }
    string Edit { get; }
    string Preview { get; }
    string SaveNote { get; }
    string SettingsSubtitle { get; }
    string General { get; }
    string GeneralSettingsDescription { get; }
    string Language { get; }
    string LanguageDescription { get; }
    string Theme { get; }
    string ThemeDescription { get; }
    string StartupView { get; }
    string StartupViewDescription { get; }
    string Security { get; }
    string SecuritySettingsDescription { get; }
    string AutoLock { get; }
    string AutoLockDescription { get; }
    string AutoLockAfter { get; }
    string AutoLockAfterDescription { get; }
    string ClearClipboard { get; }
    string ClearClipboardDescription { get; }
    string ClearClipboardAfter { get; }
    string ClearClipboardAfterDescription { get; }
    string RequirePasswordBeforeExport { get; }
    string RequirePasswordBeforeExportDescription { get; }
    string ChangeMasterPassword { get; }
    string ChangeMasterPasswordDescription { get; }
    string CurrentMasterPassword { get; }
    string NewMasterPassword { get; }
    string ConfirmNewMasterPassword { get; }
    string ChangeMasterPasswordAction { get; }
    string ResetMasterPassword { get; }
    string ResetMasterPasswordDescription { get; }
    string ResetMasterPasswordAction { get; }
    string Desktop { get; }
    string DesktopSettingsDescription { get; }
    string MinimizeToTray { get; }
    string MinimizeToTrayDescription { get; }
    string QuickSearch { get; }
    string QuickSearchDescription { get; }
    string QuickSearchHotkey { get; }
    string QuickSearchHotkeyDescription { get; }
    string BrowserIntegration { get; }
    string BrowserIntegrationDescription { get; }
    string BrowserIntegrationPort { get; }
    string BrowserIntegrationPortDescription { get; }
    string CompactPasswordList { get; }
    string CompactPasswordListDescription { get; }
    string SyncSubtitle { get; }
    string RemoteSync { get; }
    string RemoteSyncDescription { get; }
    string WebDav { get; }
    string EnableWebDav { get; }
    string EnableWebDavDescription { get; }
    string WebDavServerUrl { get; }
    string WebDavServerUrlDescription { get; }
    string WebDavUsername { get; }
    string WebDavUsernameDescription { get; }
    string WebDavPassword { get; }
    string WebDavPasswordDescription { get; }
    string WebDavRemotePath { get; }
    string WebDavRemotePathDescription { get; }
    string WebDavBackupOptions { get; }
    string WebDavBackupOptionsDescription { get; }
    string BackupNow { get; }
    string RestoreLatest { get; }
    string IncludePasswords { get; }
    string IncludeTotp { get; }
    string IncludeNotes { get; }
    string IncludeCards { get; }
    string IncludeDocuments { get; }
    string IncludeImages { get; }
    string IncludeCategories { get; }
    string EncryptBackup { get; }
    string EncryptBackupDescription { get; }
    string BackupEncryptionPassword { get; }
    string BackupEncryptionPasswordDescription { get; }
    string SyncOnStartup { get; }
    string SyncOnStartupDescription { get; }
    string SyncAfterChanges { get; }
    string SyncAfterChangesDescription { get; }
    string ConflictStrategy { get; }
    string ConflictStrategyDescription { get; }
    string CloudAndLocalVaults { get; }
    string CloudAndLocalVaultsDescription { get; }
    string OneDrive { get; }
    string EnableOneDrive { get; }
    string EnableOneDriveDescription { get; }
    string MdbxLocalCache { get; }
    string MdbxLocalCacheDescription { get; }
    string CreateMdbxMetadataDescription { get; }
    string ImportData { get; }
    string ImportDataDescription { get; }
    string ExportData { get; }
    string ExportDataDescription { get; }
    string BackupHistory { get; }
    string NoBackupsFound { get; }
    string Available { get; }
    string DesktopEquivalent { get; }
    string PlatformLimited { get; }
    string Unsupported { get; }
    string Planned { get; }
    string FeatureEnabled { get; }
    string FeatureDisabled { get; }
    string OperationCreate { get; }
    string OperationUpdate { get; }
    string OperationDelete { get; }
    string OperationRestore { get; }
    string OperationPurge { get; }
    string OperationFavorite { get; }
    string OperationMoveCategory { get; }
    string OperationStack { get; }
    string OperationAttachment { get; }
    string OperationArchive { get; }
    string OperationUnarchive { get; }
    string OperationImport { get; }
}

public sealed class LocalizationService : ILocalizationService
{
    private const string SystemLanguage = "system";
    private string _selectedLanguage = SystemLanguage;
    private Dictionary<string, string> _strings = English;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SelectedLanguage => _selectedLanguage;
    public CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    public void SetLanguage(string language)
    {
        _selectedLanguage = NormalizeLanguage(language);
        Culture = ResolveCulture(_selectedLanguage);
        CultureInfo.CurrentUICulture = Culture;
        _strings = Culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? Chinese : English;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public string this[string key] => Get(key);

    public string Get(string key) => _strings.TryGetValue(key, out var value)
        ? value
        : English.TryGetValue(key, out var english)
            ? english
            : key;

    public string Format(string key, params object[] args) => string.Format(Culture, Get(key), args);

    public string GetLanguageName(string language)
    {
        return NormalizeLanguage(language) switch
        {
            "en-US" => Get("English"),
            "zh-CN" => Get("SimplifiedChinese"),
            _ => Get("SystemDefault")
        };
    }

    public string Passwords => Text();
    public string SecureNotes => Text();
    public string Totp => Text();
    public string Cards => Text();
    public string Generator => Text();
    public string Archive => Text();
    public string RecycleBin => Text();
    public string ArchiveEmptyHint => Text();
    public string RecycleBinEmptyHint => Text();
    public string Timeline => Text();
    public string TimelineEmptyHint => Text();
    public string TimelineEmptySelectionHint => Text();
    public string SecurityAnalysis => Text();
    public string SecurityAnalysisSubtitle => Text();
    public string SecurityScore => Text();
    public string WeakPasswords => Text();
    public string DuplicatePasswords => Text();
    public string DuplicateWebsites => Text();
    public string MissingTwoFactor => Text();
    public string StalePasswords => Text();
    public string CompromisedPasswords => Text();
    public string CheckCompromisedPasswords => Text();
    public string SyncAndBackup => Text();
    public string DatabaseManagement => Text();
    public string DataManagement => Text();
    public string DataManagementDescription => Text();
    public string Settings => Text();
    public string Folders => Text();
    public string Personal => Text();
    public string AllFolders => Text();
    public string FolderScopes => Text();
    public string NewFolder => Text();
    public string CreateFolder => Text();
    public string RenameFolder => Text();
    public string DeleteFolder => Text();
    public string Refresh => Text();
    public string Export => Text();
    public string UnlockMonica => Text();
    public string CreateMonicaVault => Text();
    public string LegacyVaultDetected => Text();
    public string UnlockDescription => Text();
    public string CreateVaultDescription => Text();
    public string MasterPasswordWatermark => Text();
    public string ConfirmMasterPasswordWatermark => Text();
    public string Unlock => Text();
    public string CreateVault => Text();
    public string PasswordManager => Text();
    public string DeletedPasswords => Text();
    public string Search => Text();
    public string AddPassword => Text();
    public string EditPassword => Text();
    public string PasswordDetails => Text();
    public string LoadingPasswordDetails => Text();
    public string Details => Text();
    public string PasswordHistory => Text();
    public string PasswordHistoryDescription => Text();
    public string PasswordHistoryLatest => Text();
    public string ClearPasswordHistory => Text();
    public string Favorite => Text();
    public string Copy => Text();
    public string CopyPassword => Text();
    public string CopyUsername => Text();
    public string CopyWebsite => Text();
    public string BatchFavorite => Text();
    public string BatchArchive => Text();
    public string BatchDelete => Text();
    public string MoveToFolder => Text();
    public string Move => Text();
    public string MoveSelectedPasswordsDescription => Text();
    public string StackSelectedPasswords => Text();
    public string ArchivePassword => Text();
    public string UnarchivePassword => Text();
    public string MoveToRecycleBin => Text();
    public string QuickFilterFavorite => Text();
    public string QuickFilter2Fa => Text();
    public string QuickFilterNotes => Text();
    public string QuickFilterPasskey => Text();
    public string QuickFilterBoundNote => Text();
    public string QuickFilterUncategorized => Text();
    public string QuickFilterLocalOnly => Text();
    public string QuickFilterAttachments => Text();
    public string QuickAccessRecent => Text();
    public string QuickAccessFrequent => Text();
    public string SortPasswords => Text();
    public string MoreOptions => Text();
    public string RestorePassword => Text();
    public string DeletePermanently => Text();
    public string EmptyRecycleBin => Text();
    public string Delete => Text();
    public string Select => Text();
    public string Save => Text();
    public string Cancel => Text();
    public string NoFolder => Text();
    public string NewPassword => Text();
    public string PasswordTitleRequired => Text();
    public string PasswordValueRequired => Text();
    public string PasswordTitle => Text();
    public string Website => Text();
    public string Username => Text();
    public string Password => Text();
    public string Category => Text();
    public string BoundNote => Text();
    public string SecurityVerification => Text();
    public string AuthenticatorKey => Text();
    public string AuthenticatorKeyHint => Text();
    public string TotpCode => Text();
    public string RemainingTime => Text();
    public string Issuer => Text();
    public string Account => Text();
    public string TotpSecret => Text();
    public string AppBinding => Text();
    public string AppName => Text();
    public string AppPackageName => Text();
    public string NoBoundNote => Text();
    public string Untitled => Text();
    public string PersonalInfo => Text();
    public string Email => Text();
    public string Phone => Text();
    public string AddressLine => Text();
    public string City => Text();
    public string State => Text();
    public string ZipCode => Text();
    public string Country => Text();
    public string CardInfo => Text();
    public string CreditCardNumber => Text();
    public string CreditCardHolder => Text();
    public string CreditCardExpiry => Text();
    public string CreditCardCvv => Text();
    public string AdvancedLogin => Text();
    public string LoginType => Text();
    public string LoginTypePassword => Text();
    public string LoginTypeSso => Text();
    public string LoginTypeWifi => Text();
    public string LoginTypeSshKey => Text();
    public string SsoProvider => Text();
    public string PasskeyBindings => Text();
    public string WifiMetadata => Text();
    public string SshKeyData => Text();
    public string CustomIcon => Text();
    public string CustomIconType => Text();
    public string CustomIconValue => Text();
    public string CustomIconDescription => Text();
    public string CustomIconUseDefault => Text();
    public string CustomIconSimple => Text();
    public string CustomIconUploaded => Text();
    public string CustomIconSimpleHint => Text();
    public string CustomIconUploadedHint => Text();
    public string CustomFields => Text();
    public string CustomFieldsHint => Text();
    public string Attachments => Text();
    public string Attachment => Text();
    public string AddAttachment => Text();
    public string NoAttachments => Text();
    public string SelectAttachment => Text();
    public string Notes => Text();
    public string SourceMetadata => Text();
    public string CreatedAt => Text();
    public string UpdatedAt => Text();
    public string Close => Text();
    public string TwoStepVerification => Text();
    public string AddAuthenticator => Text();
    public string EditAuthenticator => Text();
    public string TotpPageDescription => Text();
    public string AdvancedTotpOptions => Text();
    public string TotpSecretHint => Text();
    public string CopyCode => Text();
    public string Wallet => Text();
    public string AddItem => Text();
    public string AddWalletItem => Text();
    public string EditWalletItem => Text();
    public string WalletPageDescription => Text();
    public string Document => Text();
    public string BankCard => Text();
    public string DocumentNumber => Text();
    public string FullName => Text();
    public string IssuedDate => Text();
    public string ExpiryDate => Text();
    public string IssuedBy => Text();
    public string Nationality => Text();
    public string AdditionalInfo => Text();
    public string CardNumber => Text();
    public string CardholderName => Text();
    public string Expiry => Text();
    public string ExpiryMonth => Text();
    public string ExpiryYear => Text();
    public string BankName => Text();
    public string BillingAddress => Text();
    public string CardBrand => Text();
    public string DocumentPhotos => Text();
    public string NoDocumentPhotos => Text();
    public string ImagePathsWatermark => Text();
    public string ImagePathsDescription => Text();
    public string DesktopEquivalents => Text();
    public string DesktopEquivalentsMessage => Text();
    public string CreateMdbxMetadata => Text();
    public string MdbxVaults => Text();
    public string MdbxVaultsDescription => Text();
    public string MdbxLocalSource => Text();
    public string MdbxWebDavSource => Text();
    public string MdbxOneDriveSource => Text();
    public string CreateLocalMdbxVault => Text();
    public string RegisterMdbxSource => Text();
    public string Configure => Text();
    public string MdbxSourcesSection => Text();
    public string MdbxWorkingCopiesSection => Text();
    public string MdbxHealthSection => Text();
    public string MdbxDiagnostics => Text();
    public string MdbxRemotePath => Text();
    public string MdbxLastSynced => Text();
    public string RegisteredMdbxVaults => Text();
    public string NoMdbxVaults => Text();
    public string MdbxEmptyHint => Text();
    public string Default => Text();
    public string LocalPath => Text();
    public string SetDefault => Text();
    public string Open => Text();
    public string MdbxRuntime => Text();
    public string MdbxSecurity => Text();
    public string MdbxAndroidParity => Text();
    public string MdbxAndroidParityDescription => Text();
    public string MdbxAndroidParityLocal => Text();
    public string MdbxAndroidParityRemote => Text();
    public string LocalDatabase => Text();
    public string LocalDatabaseDescription => Text();
    public string ExternalDatabases => Text();
    public string ExternalDatabasesDescription => Text();
    public string MdbxDatabaseCount => Text();
    public string RegisteredDatabases => Text();
    public string DatabaseSourcesEmptyHint => Text();
    public string WebDavConnection => Text();
    public string SyncOverview => Text();
    public string SyncConfiguration => Text();
    public string TestConnection => Text();
    public string FeatureParityMap => Text();
    public string FeatureParityMapDescription => Text();
    public string ExportPreview => Text();
    public string ImportMonicaJson => Text();
    public string ImportMonicaJsonDescription => Text();
    public string ImportJsonWatermark => Text();
    public string ImportAegisJson => Text();
    public string ImportAegisJsonDescription => Text();
    public string ImportAegisJsonWatermark => Text();
    public string ImportTotpCsv => Text();
    public string ImportTotpCsvDescription => Text();
    public string ImportTotpCsvWatermark => Text();
    public string ImportNoteCsv => Text();
    public string ImportNoteCsvDescription => Text();
    public string ImportNoteCsvWatermark => Text();
    public string ImportPasswordCsv => Text();
    public string ImportPasswordCsvDescription => Text();
    public string ImportCsvWatermark => Text();
    public string ExportPasswordCsv => Text();
    public string ExportCsvPreview => Text();
    public string ExportTotpCsv => Text();
    public string ExportTotpCsvDescription => Text();
    public string ExportTotpCsvPreview => Text();
    public string ExportNoteCsv => Text();
    public string ExportNoteCsvDescription => Text();
    public string ExportNoteCsvPreview => Text();
    public string ExportAegisJson => Text();
    public string ExportAegisJsonDescription => Text();
    public string ExportAegisPreview => Text();
    public string Import => Text();
    public string ImportFromFile => Text();
    public string SaveJsonExport => Text();
    public string SaveCsvExport => Text();
    public string SaveTotpCsvExport => Text();
    public string SaveNoteCsvExport => Text();
    public string SaveAegisExport => Text();
    public string PasswordGenerator => Text();
    public string GeneratedPassword => Text();
    public string GeneratedPasswordLabel => Text();
    public string GeneratedPasswordPlaceholder => Text();
    public string Generate => Text();
    public string SaveAsLogin => Text();
    public string GeneratorLength => Text();
    public string GeneratorMode => Text();
    public string GeneratorTemplate => Text();
    public string GeneratorWordCount => Text();
    public string ExcludeSimilarCharacters => Text();
    public string RecentGeneratedPasswords => Text();
    public string NoGeneratedPasswordHistory => Text();
    public string UsePassword => Text();
    public string Reset => Text();
    public string ShowPassword => Text();
    public string HidePassword => Text();
    public string AddPasswordRow => Text();
    public string IncludeUppercase => Text();
    public string IncludeLowercase => Text();
    public string IncludeNumbers => Text();
    public string IncludeSymbols => Text();
    public string PasswordStrength => Text();
    public string SecureNotesDescription => Text();
    public string CreateSecureItem => Text();
    public string NewSecureNote => Text();
    public string NoteTitleWatermark => Text();
    public string NoteTagsWatermark => Text();
    public string NoteContentWatermark => Text();
    public string PlainText => Text();
    public string Edit => Text();
    public string Preview => Text();
    public string SaveNote => Text();
    public string SettingsSubtitle => Text();
    public string General => Text();
    public string GeneralSettingsDescription => Text();
    public string Language => Text();
    public string LanguageDescription => Text();
    public string Theme => Text();
    public string ThemeDescription => Text();
    public string StartupView => Text();
    public string StartupViewDescription => Text();
    public string Security => Text();
    public string SecuritySettingsDescription => Text();
    public string AutoLock => Text();
    public string AutoLockDescription => Text();
    public string AutoLockAfter => Text();
    public string AutoLockAfterDescription => Text();
    public string ClearClipboard => Text();
    public string ClearClipboardDescription => Text();
    public string ClearClipboardAfter => Text();
    public string ClearClipboardAfterDescription => Text();
    public string RequirePasswordBeforeExport => Text();
    public string RequirePasswordBeforeExportDescription => Text();
    public string ChangeMasterPassword => Text();
    public string ChangeMasterPasswordDescription => Text();
    public string CurrentMasterPassword => Text();
    public string NewMasterPassword => Text();
    public string ConfirmNewMasterPassword => Text();
    public string ChangeMasterPasswordAction => Text();
    public string ResetMasterPassword => Text();
    public string ResetMasterPasswordDescription => Text();
    public string ResetMasterPasswordAction => Text();
    public string Desktop => Text();
    public string DesktopSettingsDescription => Text();
    public string MinimizeToTray => Text();
    public string MinimizeToTrayDescription => Text();
    public string QuickSearch => Text();
    public string QuickSearchDescription => Text();
    public string QuickSearchHotkey => Text();
    public string QuickSearchHotkeyDescription => Text();
    public string BrowserIntegration => Text();
    public string BrowserIntegrationDescription => Text();
    public string BrowserIntegrationPort => Text();
    public string BrowserIntegrationPortDescription => Text();
    public string CompactPasswordList => Text();
    public string CompactPasswordListDescription => Text();
    public string SyncSubtitle => Text();
    public string RemoteSync => Text();
    public string RemoteSyncDescription => Text();
    public string WebDav => Text();
    public string EnableWebDav => Text();
    public string EnableWebDavDescription => Text();
    public string WebDavServerUrl => Text();
    public string WebDavServerUrlDescription => Text();
    public string WebDavUsername => Text();
    public string WebDavUsernameDescription => Text();
    public string WebDavPassword => Text();
    public string WebDavPasswordDescription => Text();
    public string WebDavRemotePath => Text();
    public string WebDavRemotePathDescription => Text();
    public string WebDavBackupOptions => Text();
    public string WebDavBackupOptionsDescription => Text();
    public string BackupNow => Text();
    public string RestoreLatest => Text();
    public string IncludePasswords => Text();
    public string IncludeTotp => Text();
    public string IncludeNotes => Text();
    public string IncludeCards => Text();
    public string IncludeDocuments => Text();
    public string IncludeImages => Text();
    public string IncludeCategories => Text();
    public string EncryptBackup => Text();
    public string EncryptBackupDescription => Text();
    public string BackupEncryptionPassword => Text();
    public string BackupEncryptionPasswordDescription => Text();
    public string SyncOnStartup => Text();
    public string SyncOnStartupDescription => Text();
    public string SyncAfterChanges => Text();
    public string SyncAfterChangesDescription => Text();
    public string ConflictStrategy => Text();
    public string ConflictStrategyDescription => Text();
    public string CloudAndLocalVaults => Text();
    public string CloudAndLocalVaultsDescription => Text();
    public string OneDrive => Text();
    public string EnableOneDrive => Text();
    public string EnableOneDriveDescription => Text();
    public string MdbxLocalCache => Text();
    public string MdbxLocalCacheDescription => Text();
    public string CreateMdbxMetadataDescription => Text();
    public string ImportData => Text();
    public string ImportDataDescription => Text();
    public string ExportData => Text();
    public string ExportDataDescription => Text();
    public string BackupHistory => Text();
    public string NoBackupsFound => Text();
    public string Available => Text();
    public string DesktopEquivalent => Text();
    public string PlatformLimited => Text();
    public string Unsupported => Text();
    public string Planned => Text();
    public string FeatureEnabled => Text();
    public string FeatureDisabled => Text();
    public string OperationCreate => Text();
    public string OperationUpdate => Text();
    public string OperationDelete => Text();
    public string OperationRestore => Text();
    public string OperationPurge => Text();
    public string OperationFavorite => Text();
    public string OperationMoveCategory => Text();
    public string OperationStack => Text();
    public string OperationAttachment => Text();
    public string OperationArchive => Text();
    public string OperationUnarchive => Text();
    public string OperationImport => Text();

    private string Text([CallerMemberName] string key = "") => Get(key);

    private static string NormalizeLanguage(string? language)
    {
        return language switch
        {
            "en-US" or "zh-CN" => language,
            _ => SystemLanguage
        };
    }

    private static CultureInfo ResolveCulture(string language)
    {
        if (language == SystemLanguage)
        {
            return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? CultureInfo.GetCultureInfo("zh-CN")
                : CultureInfo.GetCultureInfo("en-US");
        }

        return CultureInfo.GetCultureInfo(language);
    }

    private static readonly Dictionary<string, string> English = new()
    {
        ["Passwords"] = "Passwords",
        ["SecureNotes"] = "Secure Notes",
        ["Totp"] = "TOTP",
        ["Cards"] = "Cards",
        ["Generator"] = "Generator",
        ["Archive"] = "Archive",
        ["RecycleBin"] = "Recycle Bin",
        ["ArchiveEmptyHint"] = "Archived passwords will appear here. Return to Passwords when you want to archive an item first.",
        ["RecycleBinEmptyHint"] = "Deleted passwords will appear here before permanent removal. Return to Passwords to manage active items.",
        ["Timeline"] = "Timeline",
        ["TimelineEmptyHint"] = "Activity will appear after vault changes such as create, restore, import, sync, or permanent delete.",
        ["TimelineEmptySelectionHint"] = "Select an event to inspect its timestamp, item type, and operation details.",
        ["SecurityAnalysis"] = "Security Analysis",
        ["SecurityAnalysisSubtitle"] = "Local checks for weak, reused, duplicate, stale, unprotected, and known-compromised password records.",
        ["SecurityIssueCountFormat"] = "{0} issue(s) found",
        ["SecurityScore"] = "Security score",
        ["SecurityScoreFormat"] = "{0}/100",
        ["SecurityAnalyzedPasswordCountFormat"] = "{0} active password(s) analyzed",
        ["WeakPasswords"] = "Weak passwords",
        ["WeakPasswordsSummary"] = "Passwords scoring weak or very weak.",
        ["DuplicatePasswords"] = "Duplicate passwords",
        ["DuplicatePasswordsSummary"] = "The same secret is reused by more than one login.",
        ["DuplicateWebsites"] = "Duplicate websites",
        ["MissingTwoFactor"] = "Missing 2FA",
        ["MissingTwoFactorSummary"] = "Known 2FA-capable sites without a stored authenticator or passkey binding.",
        ["StalePasswords"] = "Stale passwords",
        ["CompromisedPasswords"] = "Compromised passwords",
        ["CompromisedPasswordsSummary"] = "Passwords found in known breach corpuses.",
        ["CheckCompromisedPasswords"] = "Check compromised passwords",
        ["CompromisedPasswordNotChecked"] = "Compromised password check has not run in this session.",
        ["CompromisedPasswordCheckingFormat"] = "Checking {0} password(s) with k-anonymity range queries...",
        ["CompromisedPasswordCheckCompleteFormat"] = "Checked {0} active password(s); {1} compromised password(s) found.",
        ["CompromisedPasswordCheckUnavailableFormat"] = "Compromised password check failed: {0}",
        ["CompromisedPasswordIssueFormat"] = "Found in breach data {0} time(s). Change it immediately.",
        ["WeakPasswordIssueFormat"] = "Password strength is {0}. Replace it with a generated password.",
        ["DuplicatePasswordIssueFormat"] = "This secret is reused by {0} entries, including {1}.",
        ["DuplicateWebsiteIssueFormat"] = "{0} appears in {1} password entries.",
        ["MissingTwoFactorIssueFormat"] = "{0} usually supports two-factor authentication.",
        ["StalePasswordIssueFormat"] = "Last updated on {0}. Consider rotating it.",
        ["HighSeverity"] = "High",
        ["MediumSeverity"] = "Medium",
        ["LowSeverity"] = "Low",
        ["SyncAndBackup"] = "Sync and Backup",
        ["DatabaseManagement"] = "Database Management",
        ["DataManagement"] = "Data Management",
        ["DataManagementDescription"] = "File import and export actions aligned with the Monica for Windows settings page.",
        ["Settings"] = "Settings",
        ["Folders"] = "Folders",
        ["Personal"] = "Personal",
        ["AllFolders"] = "All folders",
        ["FolderScopes"] = "Scopes",
        ["NewFolder"] = "New folder",
        ["CreateFolder"] = "Create folder",
        ["RenameFolder"] = "Rename folder",
        ["DeleteFolder"] = "Delete folder",
        ["Refresh"] = "Refresh",
        ["Export"] = "Export",
        ["UnlockMonica"] = "Unlock Monica",
        ["CreateMonicaVault"] = "Create Monica Vault",
        ["LegacyVaultDetected"] = "Monica for Windows vault detected",
        ["UnlockDescription"] = "Use your master password to open the Avalonia desktop vault.",
        ["CreateVaultDescription"] = "Choose a master password. It will be required every time this desktop vault opens.",
        ["MasterPasswordWatermark"] = "Master password",
        ["ConfirmMasterPasswordWatermark"] = "Confirm master password",
        ["Unlock"] = "Unlock",
        ["CreateVault"] = "Create Vault",
        ["UnlockingVault"] = "Unlocking...",
        ["CreatingVault"] = "Creating vault...",
        ["MasterPasswordPrivacyNotice"] = "Your master password is used only to unlock this vault and is never stored as plain text.",
        ["UnsupportedMasterPasswordCharactersRemoved"] = "Unsupported control characters were removed from the master password.",
        ["VaultUnlockedLoadingFormat"] = "{0}. Loading vault data...",
        ["PasswordManager"] = "Password Manager",
        ["PasswordEmptyHint"] = "No passwords yet. Create your first password to start using this vault.",
        ["PasswordNoFilteredResults"] = "No passwords match the current search or filters.",
        ["SelectPasswordItems"] = "Select items",
        ["SelectAllVisiblePasswords"] = "Select all visible passwords",
        ["DeletedPasswords"] = "Deleted Passwords",
        ["Search"] = "Search...",
        ["AddPassword"] = "Add Password",
        ["EditPassword"] = "Edit Password",
        ["PasswordDetails"] = "Password Details",
        ["LoadingPasswordDetails"] = "Loading password details...",
        ["BackToPasswordList"] = "Back to password list",
        ["RetryPasswordDetails"] = "Retry loading details",
        ["Details"] = "Details",
        ["PasswordDetailsLoadFailedFormat"] = "Failed to load password details: {0}",
        ["PasswordHistory"] = "Password History",
        ["PasswordHistoryDescription"] = "Stored locally in this vault. Monica keeps the 10 most recent previous passwords for this entry.",
        ["PasswordHistoryLatest"] = "Latest",
        ["ClearPasswordHistory"] = "Clear password history",
        ["Favorite"] = "Favorite",
        ["Copy"] = "Copy",
        ["CopyPassword"] = "Copy password",
        ["CopyUsername"] = "Copy username",
        ["CopyWebsite"] = "Copy website",
        ["BatchFavorite"] = "Favorite selected",
        ["BatchArchive"] = "Archive selected",
        ["BatchDelete"] = "Delete selected",
        ["MoveToFolder"] = "Move to folder",
        ["Move"] = "Move",
        ["MoveSelectedPasswordsDescription"] = "Choose the folder/category that should own the selected password records.",
        ["StackSelectedPasswords"] = "Stack selected passwords",
        ["ArchivePassword"] = "Archive password",
        ["UnarchivePassword"] = "Unarchive password",
        ["MoveToRecycleBin"] = "Move to recycle bin",
        ["DeletePasswordConfirmationTitle"] = "Move password to recycle bin?",
        ["DeletePasswordConfirmationMessageFormat"] = "Move \"{0}\" to the recycle bin? You can restore it later from Recycle Bin.",
        ["DeleteSelectedPasswordsConfirmationTitle"] = "Move selected passwords?",
        ["DeleteSelectedPasswordsConfirmationMessageFormat"] = "Move {0} selected password(s) to the recycle bin? You can restore them later from Recycle Bin.",
        ["DeleteItemConfirmationTitle"] = "Move item to recycle bin?",
        ["DeleteItemConfirmationMessageFormat"] = "Move \"{0}\" to the recycle bin? You can restore it later from Recycle Bin.",
        ["DeleteSelectedItemsConfirmationTitle"] = "Move selected items?",
        ["DeleteSelectedItemsConfirmationMessageFormat"] = "Move {0} selected item(s) to the recycle bin? You can restore them later from Recycle Bin.",
        ["DeletePermanentlyConfirmationTitle"] = "Delete permanently?",
        ["DeletePermanentlyConfirmationMessageFormat"] = "Permanently delete \"{0}\"? This cannot be undone.",
        ["PermanentDeleteConfirmationPhrase"] = "DELETE PERMANENTLY",
        ["PermanentDeleteConfirmationInstructionFormat"] = "Type \"{0}\" to enable permanent deletion.",
        ["EmptyRecycleBin"] = "Empty recycle bin",
        ["EmptyRecycleBinConfirmationTitle"] = "Empty recycle bin?",
        ["EmptyRecycleBinConfirmationMessageFormat"] = "Permanently delete all {0} item(s) in the recycle bin? This cannot be undone.",
        ["EmptyRecycleBinConfirmationPhrase"] = "EMPTY RECYCLE BIN",
        ["EmptyRecycleBinConfirmationInstructionFormat"] = "Type \"{0}\" to permanently delete every item in the recycle bin.",
        ["DeleteWebDavBackupConfirmationTitle"] = "Delete WebDAV backup?",
        ["DeleteWebDavBackupConfirmationMessageFormat"] = "Delete remote backup \"{0}\"? This cannot be undone.",
        ["DeleteWebDavBackupConfirmationPhrase"] = "DELETE REMOTE BACKUP",
        ["DeleteWebDavBackupConfirmationInstructionFormat"] = "Type \"{0}\" to delete this remote backup.",
        ["DeleteFolderConfirmationTitle"] = "Delete folder?",
        ["DeleteFolderConfirmationMessageFormat"] = "Delete folder \"{0}\"? {1} password(s) will be moved to No folder.",
        ["DeleteAttachmentConfirmationTitle"] = "Delete attachment?",
        ["DeleteAttachmentConfirmationMessageFormat"] = "Delete attachment \"{0}\"? This removes the stored file from the vault.",
        ["DeletePasswordHistoryConfirmationTitle"] = "Delete password history?",
        ["DeletePasswordHistoryConfirmationMessage"] = "Delete this password history entry? This cannot be undone.",
        ["ClearPasswordHistoryConfirmationTitle"] = "Clear password history?",
        ["ClearPasswordHistoryConfirmationMessage"] = "Clear all password history for this login? This cannot be undone.",
        ["QuickFilterFavorite"] = "Favorites",
        ["QuickFilter2Fa"] = "2FA",
        ["QuickFilterNotes"] = "Notes",
        ["QuickFilterPasskey"] = "Passkeys",
        ["QuickFilterBoundNote"] = "Bound note",
        ["QuickFilterUncategorized"] = "Uncategorized",
        ["QuickFilterLocalOnly"] = "Local only",
        ["QuickFilterAttachments"] = "Attachments",
        ["ClearPasswordFilters"] = "Clear filters",
        ["ClearedPasswordFilters"] = "Cleared password filters",
        ["QuickAccessRecent"] = "Recently opened",
        ["QuickAccessFrequent"] = "Frequently opened",
        ["SortPasswords"] = "Sort passwords",
        ["MoreOptions"] = "More options",
        ["SortUpdated"] = "Recently updated",
        ["SortTitle"] = "Title",
        ["SortWebsite"] = "Website",
        ["SortUsername"] = "Username",
        ["SortCreated"] = "Recently created",
        ["SortFavorites"] = "Favorites first",
        ["RestorePassword"] = "Restore password",
        ["DeletePermanently"] = "Delete permanently",
        ["Delete"] = "Delete",
        ["Select"] = "Select",
        ["Save"] = "Save",
        ["Cancel"] = "Cancel",
        ["Discard"] = "Discard",
        ["NoteVault"] = "Vault",
        ["NoteSearchWatermark"] = "Search notes, tags, and content",
        ["ClearNoteSearch"] = "Clear note search",
        ["NoteHome"] = "Home",
        ["NoteTagTree"] = "Tag tree",
        ["NoteNoMatchingItems"] = "No notes match the current search.",
        ["NoteEmptyTitle"] = "No note open",
        ["NoteEmptyDescription"] = "Create a secure note or select one from the list.",
        ["BackToNoteList"] = "Back to note list",
        ["PreviousNoteTabs"] = "Scroll tabs left",
        ["NextNoteTabs"] = "Scroll tabs right",
        ["CloseNoteTab"] = "Close note tab",
        ["SaveAllNotes"] = "Save all notes",
        ["ImportMarkdown"] = "Import Markdown",
        ["ExportMarkdown"] = "Export Markdown",
        ["InsertImage"] = "Insert image",
        ["NoteEditMode"] = "Edit mode",
        ["NotePreviewMode"] = "Preview mode",
        ["NoteSplitMode"] = "Split mode",
        ["NoteFind"] = "Find",
        ["NoteReplaceWith"] = "Replace with",
        ["NotePreviousMatch"] = "Previous match",
        ["NoteNextMatch"] = "Next match",
        ["NoteReplaceCurrentMatch"] = "Replace current match",
        ["NoteReplaceAllMatches"] = "Replace all matches",
        ["NoteReplace"] = "Replace",
        ["NoteReplaceAll"] = "Replace all",
        ["NoteMatchCase"] = "Match case",
        ["NoteCloseFind"] = "Close find",
        ["NoteNoMatches"] = "No matches",
        ["NoteReplacedMatchesFormat"] = "Replaced {0} occurrence(s)",
        ["NoteView"] = "View",
        ["NoteFormat"] = "Format",
        ["NoteMode"] = "Mode",
        ["NoteLayout"] = "Layout",
        ["NoteSinglePane"] = "Single pane",
        ["NoteSplitPane"] = "Split view",
        ["NoteInformation"] = "Information",
        ["NoteLineCountLabel"] = "Lines",
        ["NoteWordCountLabel"] = "Words",
        ["NoteCharacterCountLabel"] = "Characters",
        ["NoteProperties"] = "Properties",
        ["NoteTagsLabel"] = "Tags",
        ["NoteOutline"] = "Outline",
        ["NoteNoOutline"] = "No Markdown headings",
        ["NoteReferences"] = "References",
        ["NoteNoReferences"] = "No link or image references",
        ["NoteToolbarUndo"] = "Undo",
        ["NoteToolbarRedo"] = "Redo",
        ["NoteToolbarFindReplace"] = "Find and replace",
        ["NoteToolbarHeading1"] = "Heading 1",
        ["NoteToolbarHeading2"] = "Heading 2",
        ["NoteToolbarHeading3"] = "Heading 3",
        ["NoteToolbarBold"] = "Bold",
        ["NoteToolbarItalic"] = "Italic",
        ["NoteToolbarStrikethrough"] = "Strikethrough",
        ["NoteToolbarInlineCode"] = "Inline code",
        ["NoteToolbarQuote"] = "Quote",
        ["NoteToolbarCodeBlock"] = "Code block",
        ["NoteToolbarUnorderedList"] = "Bulleted list",
        ["NoteToolbarOrderedList"] = "Numbered list",
        ["NoteToolbarTaskList"] = "Task list",
        ["NoteToolbarTable"] = "Table",
        ["NoteToolbarLink"] = "Link",
        ["NoteToolbarHorizontalRule"] = "Horizontal rule",
        ["NoteUntagged"] = "Untagged",
        ["InsertedNoteImageFormat"] = "Inserted image {0}",
        ["InsertNoteImageFailedFormat"] = "Insert image failed: {0}",
        ["NoNotesToSave"] = "There are no note changes to save.",
        ["SavedNotesFormat"] = "Saved {0} note(s)",
        ["SavedNotesWithSkippedFormat"] = "Saved {0} note(s); skipped {1} empty note(s)",
        ["ImportedMarkdownDraftFormat"] = "Imported Markdown draft {0}",
        ["ImportMarkdownFailedFormat"] = "Import Markdown failed: {0}",
        ["NoteEditorStatusFormat"] = "Line {0}, column {1} · {2} lines · {3} words · {4} characters",
        ["NoteEditorSelectionStatusFormat"] = "Line {0}, column {1} · {2} selected · {3} lines · {4} words · {5} characters",
        ["ReferenceCannotOpen"] = "This reference cannot be opened.",
        ["OpenedReferenceFormat"] = "Opened {0}",
        ["OpenReferenceFailedFormat"] = "Open reference failed: {0}",
        ["CopiedReference"] = "Copied reference",
        ["SaveNoteChangesTitle"] = "Save changes to this note?",
        ["UnsavedNoteMessageFormat"] = "\"{0}\" has unsaved changes. Save before closing?",
        ["SaveUnsavedNotesTitle"] = "Save unsaved notes?",
        ["UnsavedNotesMessageFormat"] = "{0} note tab(s) contain unsaved changes. Save all before closing Monica?",
        ["NoteImageAttachment"] = "Image attachment",
        ["NoteImageAttachmentFormat"] = "Image attachment: {0}",
        ["NoteImage"] = "Image",
        ["NoteImageNumberFormat"] = "Image {0}",
        ["NoFolder"] = "No folder",
        ["NewPassword"] = "New Password",
        ["PasswordTitleRequired"] = "Enter a title for this password.",
        ["PasswordValueRequired"] = "Enter a password value.",
        ["PasswordTitle"] = "Title",
        ["Website"] = "Website",
        ["Username"] = "Username",
        ["Password"] = "Password",
        ["Category"] = "Category",
        ["BoundNote"] = "Bound note",
        ["SecurityVerification"] = "Security verification",
        ["AuthenticatorKey"] = "Authenticator secret",
        ["AuthenticatorKeyHint"] = "Optional TOTP secret from the Android authenticator field. QR import and multi-password storage will be layered onto this same model.",
        ["TotpCode"] = "TOTP code",
        ["RemainingTime"] = "Remaining time",
        ["Issuer"] = "Issuer",
        ["Account"] = "Account",
        ["TotpSecret"] = "TOTP secret",
        ["AppBinding"] = "App binding",
        ["AppName"] = "App name",
        ["AppPackageName"] = "App package or bundle id",
        ["NoBoundNote"] = "No bound note",
        ["Untitled"] = "Untitled",
        ["PersonalInfo"] = "Personal information",
        ["Email"] = "Email",
        ["Phone"] = "Phone",
        ["AddressLine"] = "Address",
        ["City"] = "City",
        ["State"] = "State or province",
        ["ZipCode"] = "ZIP or postal code",
        ["Country"] = "Country",
        ["CardInfo"] = "Card information",
        ["CreditCardNumber"] = "Card number",
        ["CreditCardHolder"] = "Cardholder name",
        ["CreditCardExpiry"] = "Expiry",
        ["CreditCardCvv"] = "CVV",
        ["AdvancedLogin"] = "Advanced login",
        ["LoginType"] = "Login type",
        ["LoginTypePassword"] = "Password",
        ["LoginTypeSso"] = "SSO",
        ["LoginTypeWifi"] = "Wi-Fi",
        ["LoginTypeSshKey"] = "SSH key",
        ["SsoProvider"] = "SSO provider",
        ["PasskeyBindings"] = "Passkey bindings",
        ["WifiMetadata"] = "Wi-Fi metadata",
        ["SshKeyData"] = "SSH key data",
        ["CustomIcon"] = "Custom icon",
        ["CustomIconType"] = "Icon type",
        ["CustomIconValue"] = "Icon value",
        ["CustomIconDescription"] = "Matches Android custom icon metadata: simple icon slug or uploaded icon file/path.",
        ["CustomIconUseDefault"] = "Use website/default icon",
        ["CustomIconSimple"] = "Simple icon slug",
        ["CustomIconUploaded"] = "Uploaded icon file",
        ["CustomIconSimpleHint"] = "github, microsoft, bank, mail...",
        ["CustomIconUploadedHint"] = "Local icon file name or path",
        ["CustomFields"] = "Custom fields",
        ["CustomFieldsHint"] = "One field per line. Use Title=Value, and prefix the title with ! for protected fields.",
        ["Attachments"] = "Attachments",
        ["Attachment"] = "Attachment",
        ["AddAttachment"] = "Add attachment",
        ["NoAttachments"] = "No attachments",
        ["SelectAttachment"] = "Select attachment",
        ["Notes"] = "Notes",
        ["SourceMetadata"] = "Source metadata",
        ["BitwardenVault"] = "Bitwarden vault",
        ["BitwardenCipher"] = "Bitwarden cipher",
        ["KeePassDatabase"] = "KeePass database",
        ["KeePassGroup"] = "KeePass group",
        ["MdbxDatabase"] = "MDBX database",
        ["MdbxFolder"] = "MDBX folder",
        ["CreatedAt"] = "Created",
        ["UpdatedAt"] = "Updated",
        ["Close"] = "Close",
        ["TwoStepVerification"] = "Two-Step Verification",
        ["AddAuthenticator"] = "Add Authenticator",
        ["EditAuthenticator"] = "Edit Authenticator",
        ["TotpPageDescription"] = "TOTP authenticators with copy, edit, favorite, delete, context menu, and batch actions.",
        ["TotpEmptyHint"] = "Add an authenticator by scanning a QR code or entering a Base32 secret manually.",
        ["TotpConsoleStatusFormat"] = "{0} authenticators · {1} expiring soon",
        ["TotpFilteredStatusFormat"] = "{0} visible · {1} total",
        ["TotpScanQr"] = "Scan QR",
        ["TotpManualAdd"] = "Manual add",
        ["TotpScanQrFallback"] = "QR scanning will open the authenticator entry dialog on this desktop build.",
        ["TotpFilterTitle"] = "Groups",
        ["TotpIssuerGroups"] = "Issuers",
        ["TotpFilterAll"] = "All",
        ["TotpFilterExpiringSoon"] = "Expiring soon",
        ["TotpFilterUnbound"] = "Unbound password",
        ["TotpNoFilteredResults"] = "No authenticators match the current search or group.",
        ["ClearTotpFilters"] = "Clear filters",
        ["ClearedTotpFilters"] = "Cleared authenticator filters",
        ["MoreActions"] = "More actions",
        ["ShowHidden"] = "Show hidden",
        ["Help"] = "Help",
        ["AdvancedTotpOptions"] = "Advanced options",
        ["TotpSecretHint"] = "Paste a Base32 secret or otpauth URI. Monica stores the normalized TOTP metadata in the local vault.",
        ["TotpTypeTotp"] = "TOTP (time based)",
        ["TotpTypeHotp"] = "HOTP (counter based)",
        ["TotpTypeSteam"] = "Steam Guard",
        ["CopyCode"] = "Copy code",
        ["Wallet"] = "Wallet",
        ["AddItem"] = "Add Item",
        ["AddWalletItem"] = "Add wallet item",
        ["EditWalletItem"] = "Edit wallet item",
        ["WalletPageDescription"] = "Cards and identity documents with edit, details, context menu, image paths, and batch delete.",
        ["Document"] = "Document",
        ["BankCard"] = "Bank card",
        ["DocumentNumber"] = "Document number",
        ["FullName"] = "Full name",
        ["IssuedDate"] = "Issued date",
        ["ExpiryDate"] = "Expiry date",
        ["IssuedBy"] = "Issued by",
        ["Nationality"] = "Nationality",
        ["AdditionalInfo"] = "Additional info",
        ["CardNumber"] = "Card number",
        ["CardholderName"] = "Cardholder name",
        ["Expiry"] = "Expiry",
        ["ExpiryMonth"] = "Expiry month",
        ["ExpiryYear"] = "Expiry year",
        ["BankName"] = "Bank name",
        ["BillingAddress"] = "Billing address",
        ["CardBrand"] = "Brand",
        ["DocumentPhotos"] = "Document photos",
        ["NoDocumentPhotos"] = "No document photos",
        ["ImagePathsWatermark"] = "Front image path\nBack image path",
        ["ImagePathsDescription"] = "Enter one local image path per line. File picking and encrypted image storage will use this same imagePaths schema.",
        ["DocumentTypeIdCard"] = "ID card",
        ["DocumentTypePassport"] = "Passport",
        ["DocumentTypeDriverLicense"] = "Driver license",
        ["DocumentTypeSocialSecurity"] = "Social security card",
        ["DocumentTypeOther"] = "Other document",
        ["CardTypeDebit"] = "Debit card",
        ["CardTypeCredit"] = "Credit card",
        ["CardTypePrepaid"] = "Prepaid card",
        ["DesktopEquivalents"] = "Desktop equivalents",
        ["DesktopEquivalentsMessage"] = "Android Autofill, IME, Accessibility and Credential Provider features are represented through quick search, clipboard, tray/browser extension boundaries, or platform-limited status.",
        ["CreateMdbxMetadata"] = "Create MDBX Metadata",
        ["MdbxVaults"] = "MDBX Vaults",
        ["MdbxVaultsDescription"] = "Manage local, WebDAV and OneDrive MDBX vault metadata from one page. Local vaults can be opened on desktop now; remote MDBX entries are registered for the upcoming sync engine.",
        ["MdbxLocalSource"] = "Local MDBX",
        ["MdbxWebDavSource"] = "WebDAV MDBX",
        ["MdbxOneDriveSource"] = "OneDrive MDBX",
        ["CreateLocalMdbxVault"] = "Create local MDBX",
        ["RegisterMdbxSource"] = "Register source",
        ["Configure"] = "Configure",
        ["MdbxSourcesSection"] = "Sources",
        ["MdbxWorkingCopiesSection"] = "Working copies",
        ["MdbxHealthSection"] = "Health and diagnostics",
        ["MdbxDiagnostics"] = "Diagnostics",
        ["MdbxRemotePath"] = "Remote path",
        ["MdbxLastSynced"] = "Last synced",
        ["RegisteredMdbxVaults"] = "Registered MDBX vaults",
        ["NoMdbxVaults"] = "No MDBX vault metadata has been registered yet.",
        ["MdbxEmptyHint"] = "Create a local MDBX working copy or configure a remote MDBX source to start using the encrypted business store.",
        ["Default"] = "Default",
        ["LocalPath"] = "Local path",
        ["SetDefault"] = "Set default",
        ["Open"] = "Open",
        ["MdbxRuntime"] = "Runtime",
        ["MdbxSecurity"] = "Security",
        ["MdbxAndroidParity"] = "Android parity",
        ["MdbxAndroidParityDescription"] = "Desktop MDBX now has a dedicated manager page matching the Android source hub shape.",
        ["MdbxAndroidParityLocal"] = "Local MDBX metadata and working-copy opening are available on desktop.",
        ["MdbxAndroidParityRemote"] = "WebDAV and OneDrive MDBX sources now create local MDBX working copies for business storage; full remote sync/commit/conflict handling still requires the Android MDBX engine port.",
        ["LocalDatabase"] = "Local database",
        ["LocalDatabaseDescription"] = "Avalonia keeps SQLite for app settings, local cache and migration indexes while MDBX working copies carry sensitive business data.",
        ["ExternalDatabases"] = "External databases",
        ["ExternalDatabasesDescription"] = "KeePass KDBX, MDBX, Bitwarden and WebDAV sources are exposed through platform-neutral services.",
        ["MdbxDatabaseCount"] = "MDBX vault metadata",
        ["RegisteredDatabases"] = "Registered databases",
        ["DatabaseSourcesEmptyHint"] = "Database source records will appear here after local MDBX, WebDAV, OneDrive, or migration metadata is registered.",
        ["WebDavConnection"] = "WebDAV connection",
        ["SyncOverview"] = "Sync overview",
        ["SyncConfiguration"] = "Sync configuration",
        ["TestConnection"] = "Test connection",
        ["FeatureParityMap"] = "Feature parity map",
        ["DangerZone"] = "Danger zone",
        ["About"] = "About",
        ["AboutDescription"] = "Version and project links from the Monica desktop settings surface.",
        ["AppVersion"] = "App version",
        ["GitHubRepository"] = "GitHub Repository",
        ["OpenRepository"] = "Open repository",
        ["GitHubRepositoryOpened"] = "Opened the GitHub repository.",
        ["GitHubRepositoryOpenFailedFormat"] = "Could not open GitHub repository: {0}",
        ["DangerZoneDescription"] = "Destructive vault maintenance actions copied from the WinUI desktop settings surface.",
        ["ClearVaultData"] = "Clear vault data",
        ["ClearVaultDataDescription"] = "Delete passwords, secure items, or the full local Avalonia v69 vault data set. The master password record is kept.",
        ["ClearPasswordsOnly"] = "Clear passwords",
        ["ClearSecureItemsOnly"] = "Clear secure items",
        ["ClearAllVaultData"] = "Clear all vault data",
        ["ClearVaultConfirmationPhrase"] = "CLEAR MONICA DATA",
        ["ClearVaultConfirmationInstructionFormat"] = "Type \"{0}\" before using these destructive actions.",
        ["ClearVaultConfirmationFailedFormat"] = "Type \"{0}\" to confirm clearing vault data.",
        ["ClearedVaultDataFormat"] = "Cleared {0}.",
        ["ExportPreview"] = "Export Preview",
        ["ImportMonicaJson"] = "Import Monica JSON",
        ["ImportMonicaJsonDescription"] = "Paste a Monica JSON export package and import its passwords, notes, wallet items and authenticators into this vault.",
        ["ImportJsonWatermark"] = "Paste Monica JSON export here",
        ["ImportAegisJson"] = "Import Aegis JSON",
        ["ImportAegisJsonDescription"] = "Import unencrypted Aegis JSON authenticator exports into the TOTP vault. Encrypted Aegis files are reported as unsupported for now.",
        ["ImportAegisJsonWatermark"] = "Paste unencrypted Aegis JSON here",
        ["ImportTotpCsv"] = "Import TOTP CSV",
        ["ImportTotpCsvDescription"] = "Import Monica for Windows compatible TOTP secure-item CSV rows.",
        ["ImportTotpCsvWatermark"] = "Paste TOTP CSV here",
        ["ImportNoteCsv"] = "Import Notes CSV",
        ["ImportNoteCsvDescription"] = "Import Monica for Windows compatible NOTE secure-item CSV rows.",
        ["ImportNoteCsvWatermark"] = "Paste Notes CSV here",
        ["ImportPasswordCsv"] = "Import Password CSV",
        ["ImportPasswordCsvDescription"] = "Paste a password CSV from Monica, Bitwarden-style exports or another manager. Passwords are encrypted before they are saved.",
        ["ImportCsvWatermark"] = "Paste password CSV here",
        ["ExportPasswordCsv"] = "Export Password CSV",
        ["ExportCsvPreview"] = "Password CSV Preview",
        ["ExportTotpCsv"] = "Export TOTP CSV",
        ["ExportTotpCsvDescription"] = "Export authenticators as Monica for Windows compatible secure-item CSV rows.",
        ["ExportTotpCsvPreview"] = "TOTP CSV Preview",
        ["ExportNoteCsv"] = "Export Notes CSV",
        ["ExportNoteCsvDescription"] = "Export secure notes as Monica for Windows compatible NOTE secure-item CSV rows.",
        ["ExportNoteCsvPreview"] = "Notes CSV Preview",
        ["ExportAegisJson"] = "Export Aegis JSON",
        ["ExportAegisJsonDescription"] = "Export authenticators as unencrypted Aegis JSON. The file contains plaintext TOTP secrets.",
        ["ExportAegisPreview"] = "Aegis JSON Preview",
        ["Import"] = "Import",
        ["ImportFromFile"] = "Import from file",
        ["SaveJsonExport"] = "Save JSON export",
        ["SaveCsvExport"] = "Save CSV export",
        ["SaveTotpCsvExport"] = "Save TOTP CSV",
        ["SaveNoteCsvExport"] = "Save Notes CSV",
        ["SaveAegisExport"] = "Save Aegis JSON",
        ["PasswordGenerator"] = "Password Generator",
        ["GeneratedPasswordLabel"] = "Generated password",
        ["GeneratedPasswordPlaceholder"] = "Generate or type a password.",
        ["Generate"] = "Generate",
        ["SaveAsLogin"] = "Save as Login",
        ["GeneratorLength"] = "Length",
        ["GeneratorLengthFormat"] = "Length: {0}",
        ["GeneratorMode"] = "Type",
        ["GeneratorTemplate"] = "Template",
        ["GeneratorWordCount"] = "Words",
        ["GeneratorWordCountFormat"] = "Words: {0}",
        ["GeneratorModeRandom"] = "Random password",
        ["GeneratorModePassphrase"] = "Passphrase",
        ["GeneratorModePin"] = "PIN",
        ["GeneratorModeUsername"] = "Username",
        ["GeneratorTemplateBalanced"] = "Balanced",
        ["GeneratorTemplateMaximum"] = "Maximum strength",
        ["GeneratorTemplateMemorable"] = "Memorable",
        ["GeneratorTemplatePin"] = "Short PIN",
        ["GeneratorTemplateUsername"] = "Username",
        ["GeneratorStrategyLengthFormat"] = "{0} · {1} chars",
        ["GeneratorStrategyPassphraseFormat"] = "{0} · {1} words",
        ["ExcludeSimilarCharacters"] = "Exclude similar characters",
        ["RecentGeneratedPasswords"] = "Recent generated",
        ["NoGeneratedPasswordHistory"] = "Generated passwords will appear here during this session.",
        ["UsePassword"] = "Use password",
        ["Reset"] = "Reset",
        ["ShowPassword"] = "Show password",
        ["HidePassword"] = "Hide password",
        ["AddPasswordRow"] = "Add another password",
        ["PasswordRowCountFormat"] = "{0} password row(s)",
        ["IncludeUppercase"] = "Include uppercase",
        ["IncludeLowercase"] = "Include lowercase",
        ["IncludeNumbers"] = "Include numbers",
        ["IncludeSymbols"] = "Include symbols",
        ["PasswordStrength"] = "Password strength",
        ["PasswordStrengthExcellent"] = "Excellent",
        ["PasswordStrengthStrong"] = "Strong",
        ["PasswordStrengthFair"] = "Fair",
        ["PasswordStrengthWeak"] = "Weak",
        ["PasswordStrengthVeryWeak"] = "Very weak",
        ["PasswordStrengthWarningShort"] = "Password is shorter than 12 characters.",
        ["PasswordStrengthWarningMixedCase"] = "Use both upper and lower case letters.",
        ["PasswordStrengthWarningNumbers"] = "Add numbers.",
        ["PasswordStrengthWarningSymbols"] = "Add symbols.",
        ["GeneratorNoPassword"] = "Generate a password to see its strength.",
        ["GeneratedPasswordStrengthFormat"] = "{0} ({1}/5). {2}",
        ["CopiedGeneratedPassword"] = "Copied generated password",
        ["GeneratedPasswordRestoredFromHistory"] = "Restored generated password from history",
        ["SecureNotesDescription"] = "Notes are stored as secure_items with NOTE item type and share the same encryption, folder, KeePass, Bitwarden and MDBX ownership model.",
        ["CreateSecureItem"] = "Create Secure Item",
        ["NewSecureNote"] = "New Note",
        ["NoteTitleWatermark"] = "Title",
        ["NoteTagsWatermark"] = "Tags, separated by commas",
        ["NoteContentWatermark"] = "Write a private note...",
        ["PlainText"] = "Plain text",
        ["Edit"] = "Edit",
        ["Preview"] = "Preview",
        ["SaveNote"] = "Save Note",
        ["SettingsSubtitle"] = "Configure Monica desktop behavior, security, appearance and integration options.",
        ["General"] = "General",
        ["GeneralSettingsDescription"] = "Language, visual theme, and the page shown after unlock.",
        ["Language"] = "Language",
        ["LanguageDescription"] = "Choose the display language used by Monica desktop.",
        ["Theme"] = "Theme",
        ["ThemeDescription"] = "Follow the system theme, force light or dark, or use a high contrast appearance.",
        ["StartupView"] = "Startup view",
        ["StartupViewDescription"] = "Choose the first page shown after the vault is unlocked.",
        ["Security"] = "Security",
        ["SecuritySettingsDescription"] = "Locking, clipboard, and export confirmation controls.",
        ["AutoLock"] = "Auto lock",
        ["AutoLockDescription"] = "Lock the vault after a period of desktop inactivity.",
        ["AutoLockAfter"] = "Auto-lock after",
        ["AutoLockAfterDescription"] = "Set how long Monica waits before locking an inactive vault.",
        ["ClearClipboard"] = "Clear clipboard",
        ["ClearClipboardDescription"] = "Remove copied passwords and TOTP codes after a timeout.",
        ["ClearClipboardAfter"] = "Clear after",
        ["ClearClipboardAfterDescription"] = "Set how long copied sensitive values remain on the clipboard.",
        ["RequirePasswordBeforeExport"] = "Require master password before export",
        ["RequirePasswordBeforeExportDescription"] = "Ask for the master password before preparing export data.",
        ["ChangeMasterPassword"] = "Change master password",
        ["ChangeMasterPasswordDescription"] = "Re-encrypt the local Avalonia vault with a new master password.",
        ["CurrentMasterPassword"] = "Current master password",
        ["NewMasterPassword"] = "New master password",
        ["ConfirmNewMasterPassword"] = "Confirm new master password",
        ["ChangeMasterPasswordAction"] = "Update master password",
        ["ResetMasterPassword"] = "Reset master password",
        ["ResetMasterPasswordDescription"] = "Use configured security questions to set a new master password while the vault is unlocked.",
        ["ResetMasterPasswordAction"] = "Verify and reset",
        ["EnterCurrentMasterPassword"] = "Enter the current master password.",
        ["EnterNewMasterPassword"] = "Enter the new master password.",
        ["ChangeMasterPasswordInProgress"] = "Updating master password and re-encrypting vault data...",
        ["MasterPasswordChangedFormat"] = "Master password updated. Re-encrypted {0} database secret(s).",
        ["ChangeMasterPasswordFailedFormat"] = "Master password update failed: {0}",
        ["SecurityQuestionAnswersRequired"] = "Enter both security-question answers.",
        ["SecurityQuestionAnswersIncorrect"] = "Security-question answers are incorrect.",
        ["ResetMasterPasswordInProgress"] = "Verifying recovery answers and re-encrypting vault data...",
        ["ResetMasterPasswordChangedFormat"] = "Master password reset. Re-encrypted {0} database secret(s).",
        ["ResetMasterPasswordFailedFormat"] = "Master password reset failed: {0}",
        ["SecurityRecovery"] = "Security questions",
        ["SecurityRecoveryDescription"] = "Configure two recovery questions that can later support master-password reset flows.",
        ["SecurityRecoveryEnabled"] = "Use security questions",
        ["SecurityQuestion1"] = "Security question 1",
        ["SecurityQuestion2"] = "Security question 2",
        ["SecurityQuestionAnswer"] = "Answer",
        ["CustomSecurityQuestion"] = "Custom question",
        ["SaveSecurityQuestions"] = "Save security questions",
        ["SecurityQuestionsConfigured"] = "Security questions are configured.",
        ["SecurityQuestionsNotConfigured"] = "Security questions are not configured.",
        ["SecurityQuestionsSaved"] = "Security questions saved.",
        ["SecurityQuestionsDisabled"] = "Security questions disabled.",
        ["SecurityQuestionsSaveFailedFormat"] = "Security questions could not be saved: {0}",
        ["Desktop"] = "Desktop",
        ["DesktopSettingsDescription"] = "Desktop-only controls for tray, search, browser bridge, and list density.",
        ["MinimizeToTray"] = "Minimize to tray",
        ["MinimizeToTrayDescription"] = "Keep Monica available from the system tray when the window is closed or minimized.",
        ["QuickSearch"] = "Quick search overlay",
        ["QuickSearchDescription"] = "Enable a desktop search entry point for credentials and secure notes.",
        ["QuickSearchHotkey"] = "Quick search hotkey",
        ["QuickSearchHotkeyDescription"] = "Keyboard shortcut reserved for opening quick search.",
        ["BrowserIntegration"] = "Browser extension bridge",
        ["BrowserIntegrationDescription"] = "Expose a local bridge endpoint for browser extension integration.",
        ["BrowserIntegrationPort"] = "Local bridge port",
        ["BrowserIntegrationPortDescription"] = "Local TCP port used by the desktop browser bridge.",
        ["CompactPasswordList"] = "Compact password list",
        ["CompactPasswordListDescription"] = "Use denser password rows for scanning large vaults.",
        ["PlatformIntegrations"] = "Platform integrations",
        ["PlatformIntegrationsDescriptionFormat"] = "{0}: {1}/{2} desktop integrations available or mapped.",
        ["Integration.browser-bridge.Title"] = "Browser bridge",
        ["Integration.browser-bridge.Description"] = "Local desktop bridge used by browser extensions and autofill equivalents.",
        ["Integration.external-links.Title"] = "External links",
        ["Integration.external-links.Description"] = "Open project, help and account links through the desktop shell.",
        ["Integration.file-picker.Title"] = "File picker",
        ["Integration.file-picker.Description"] = "Native or Avalonia storage picker for import, export and attachment workflows.",
        ["Integration.global-hotkey.Title"] = "Global hotkey",
        ["Integration.global-hotkey.Description"] = "System-wide shortcut registration for quick search and future autofill entry points.",
        ["Integration.native-notification.Title"] = "Native notifications",
        ["Integration.native-notification.Description"] = "Desktop notification surface for sync, backup and security events.",
        ["Integration.native-passkey.Title"] = "Native passkey",
        ["Integration.native-passkey.Description"] = "Platform WebAuthn or credential-provider integration boundary.",
        ["Integration.secret-protection.Title"] = "Secret protection",
        ["Integration.secret-protection.Description"] = "OS-backed protection for tokens, sync credentials and local secrets.",
        ["Integration.tray.Title"] = "System tray",
        ["Integration.tray.Description"] = "Desktop tray or menu-bar presence for lock, quick search and background sync.",
        ["Integration.window-security.Title"] = "Window security",
        ["Integration.window-security.Description"] = "Platform-specific window privacy, lock and screenshot-protection hooks.",
        ["SyncSubtitle"] = "Configure remote sync, backup targets and conflict behavior.",
        ["RemoteSync"] = "Remote sync",
        ["RemoteSyncDescription"] = "WebDAV connection details and automatic sync behavior.",
        ["WebDav"] = "WebDAV",
        ["EnableWebDav"] = "Enable WebDAV sync",
        ["EnableWebDavDescription"] = "Use a WebDAV endpoint as a remote Monica backup and sync target.",
        ["WebDavServerUrl"] = "Server URL",
        ["WebDavServerUrlDescription"] = "Base HTTPS URL of the WebDAV server.",
        ["WebDavUsername"] = "Username",
        ["WebDavUsernameDescription"] = "Account name Monica uses when connecting to the WebDAV endpoint.",
        ["WebDavPassword"] = "Password",
        ["WebDavPasswordDescription"] = "Password or app password used for WebDAV Basic authentication.",
        ["WebDavRemotePath"] = "Remote path",
        ["WebDavRemotePathDescription"] = "Folder path where Monica stores vault backup files.",
        ["WebDavBackupOptions"] = "Backup options",
        ["WebDavBackupOptionsDescription"] = "Choose which Monica data goes into manual WebDAV backups.",
        ["BackupNow"] = "Backup now",
        ["RestoreLatest"] = "Restore latest",
        ["IncludePasswords"] = "Passwords",
        ["IncludeTotp"] = "Authenticators",
        ["IncludeNotes"] = "Notes",
        ["IncludeCards"] = "Bank cards",
        ["IncludeDocuments"] = "Documents",
        ["IncludeImages"] = "Image references",
        ["IncludeCategories"] = "Folders",
        ["EncryptBackup"] = "Encrypt backup",
        ["EncryptBackupDescription"] = "Protect the WebDAV backup package with a separate backup password.",
        ["BackupEncryptionPassword"] = "Backup password",
        ["BackupEncryptionPasswordDescription"] = "Required for encrypted backup and restore.",
        ["SyncOnStartup"] = "Sync on startup",
        ["SyncOnStartupDescription"] = "Pull remote changes when the desktop vault is opened.",
        ["SyncAfterChanges"] = "Sync after local changes",
        ["SyncAfterChangesDescription"] = "Push vault changes automatically after local edits.",
        ["ConflictStrategy"] = "Conflict strategy",
        ["ConflictStrategyDescription"] = "Choose how Monica should resolve local and remote edits that overlap.",
        ["CloudAndLocalVaults"] = "Cloud and local vaults",
        ["CloudAndLocalVaultsDescription"] = "OneDrive boundary state and MDBX local vault metadata.",
        ["OneDrive"] = "OneDrive",
        ["EnableOneDrive"] = "Enable OneDrive boundary",
        ["EnableOneDriveDescription"] = "Reserve OneDrive integration state for Microsoft Graph based sync.",
        ["MdbxLocalCache"] = "Keep MDBX local cache",
        ["MdbxLocalCacheDescription"] = "Retain a local MDBX working file for desktop vault operations.",
        ["CreateMdbxMetadataDescription"] = "Create local metadata for the desktop MDBX vault file.",
        ["ImportData"] = "Import data",
        ["ImportDataDescription"] = "Bring Monica JSON packages or password CSV records into this vault.",
        ["ExportData"] = "Export data",
        ["ExportDataDescription"] = "Prepare readable Monica JSON and password CSV previews before saving elsewhere.",
        ["BackupHistory"] = "Backup history",
        ["NoBackupsFound"] = "No backup files found.",
        ["FeatureParityMapDescription"] = "Desktop availability for Android-originated Monica features.",
        ["Available"] = "Available",
        ["Enabled"] = "Enabled",
        ["Disabled"] = "Disabled",
        ["NeedsAttention"] = "Needs attention",
        ["DesktopEquivalent"] = "Desktop equivalent",
        ["PlatformLimited"] = "Platform limited",
        ["Unsupported"] = "Unsupported",
        ["Planned"] = "Planned",
        ["FeatureEnabled"] = "Enabled",
        ["FeatureDisabled"] = "Disabled",
        ["Capability.passwords.Title"] = "Passwords",
        ["Capability.passwords.Description"] = "Login credentials with websites, app bindings, folders, favorites, archive, recycle bin and history.",
        ["Capability.notes.Title"] = "Secure Notes",
        ["Capability.notes.Description"] = "Encrypted notes and note binding for password entries.",
        ["Capability.totp.Title"] = "TOTP",
        ["Capability.totp.Description"] = "TOTP/HOTP/Steam-compatible authenticator records with QR import and copy actions.",
        ["Capability.cards.Title"] = "Wallet",
        ["Capability.cards.Description"] = "Bank cards, identity documents and images stored as secure items.",
        ["Capability.passkeys.Title"] = "Passkeys",
        ["Capability.passkeys.Description"] = "WebAuthn/FIDO2 metadata with Bitwarden and KeePass-compatible modes.",
        ["Capability.wifi.Title"] = "Wi-Fi",
        ["Capability.wifi.Description"] = "Wi-Fi secrets stored as typed credential entries.",
        ["Capability.ssh.Title"] = "SSH Keys",
        ["Capability.ssh.Description"] = "Structured SSH key records stored alongside password entries.",
        ["Capability.security-analysis.Title"] = "Security Analysis",
        ["Capability.security-analysis.Description"] = "Weak, duplicate and stale password checks.",
        ["Capability.generator.Title"] = "Generator",
        ["Capability.generator.Description"] = "Password and passphrase generation.",
        ["Capability.import-export.Title"] = "Import / Export",
        ["Capability.import-export.Description"] = "Monica JSON, CSV, Bitwarden JSON, KeePass KDBX and Aegis-oriented pipelines.",
        ["Capability.trash.Title"] = "Recycle Bin",
        ["Capability.trash.Description"] = "Soft-delete and restore flows.",
        ["Capability.timeline.Title"] = "Timeline",
        ["Capability.timeline.Description"] = "Operation log and rollback metadata.",
        ["Capability.categories.Title"] = "Folders",
        ["Capability.categories.Description"] = "Local categories plus KeePass, Bitwarden and MDBX ownership metadata.",
        ["Capability.customization.Title"] = "Personalization",
        ["Capability.customization.Description"] = "Page, card, icon and list customization entry points.",
        ["Capability.plus.Title"] = "Monica Plus",
        ["Capability.plus.Description"] = "Subscription/status page shell for parity with mobile.",
        ["Capability.bitwarden.Title"] = "Bitwarden",
        ["Capability.bitwarden.Description"] = "Vault mapping and sync service boundary.",
        ["Capability.keepass.Title"] = "KeePass",
        ["Capability.keepass.Description"] = "KDBX metadata and library-backed open/read boundary.",
        ["Capability.mdbx.Title"] = "MDBX",
        ["Capability.mdbx.Description"] = "Vault create/open/sync metadata and local file-stream management.",
        ["Capability.webdav.Title"] = "WebDAV",
        ["Capability.webdav.Description"] = "Remote backup and sync path handling.",
        ["Capability.onedrive.Title"] = "OneDrive",
        ["Capability.onedrive.Description"] = "Microsoft Graph/MSAL service boundary.",
        ["Capability.autofill.Title"] = "Desktop Autofill",
        ["Capability.autofill.Description"] = "Android Autofill/IME/Accessibility becomes quick search, clipboard, tray and browser-extension bridge.",
        ["Capability.credential-provider.Title"] = "Credential Provider",
        ["Capability.credential-provider.Description"] = "Android Credential Provider equivalent is platform-specific and exposed as limited status.",
        ["SystemDefault"] = "System default",
        ["English"] = "English",
        ["SimplifiedChinese"] = "Simplified Chinese",
        ["Light"] = "Light",
        ["Dark"] = "Dark",
        ["HighContrast"] = "High contrast",
        ["AskEveryTime"] = "Ask every time",
        ["LocalWins"] = "Local wins",
        ["RemoteWins"] = "Remote wins",
        ["MinuteFormat"] = "{0} min",
        ["SecondFormat"] = "{0} sec",
        ["PasswordCountFormat"] = "{0} items",
        ["DatabaseSummaryFormat"] = "{0} passwords, {1} notes, {2} authenticators, {3} wallet items",
        ["MdbxDatabaseCountFormat"] = "{0} MDBX metadata record(s)",
        ["MdbxSourceCountFormat"] = "{0} vault(s)",
        ["MdbxWorkingCopyCountFormat"] = "{0} ready",
        ["MdbxRemoteSourceCountFormat"] = "{0} remote",
        ["MdbxDefaultVault"] = "Default vault",
        ["MdbxWorkingCopies"] = "Working copies",
        ["MdbxRemoteSources"] = "Remote sources",
        ["MdbxDefaultVaultFormat"] = "Default: {0}",
        ["MdbxDefaultVaultMissing"] = "No default vault",
        ["MdbxNoWorkingCopies"] = "No MDBX working copy is ready yet.",
        ["MdbxWorkingCopySummaryFormat"] = "{0}/{1} working copy file(s) are ready; {2} can be used offline.",
        ["MdbxRemoteSourceEmpty"] = "No remote MDBX source has been registered.",
        ["MdbxRemoteSummaryFormat"] = "{0} remote source(s), {1} pending sync item(s).",
        ["MdbxSyncErrorsFormat"] = "{0} MDBX sync issue(s) need attention.",
        ["MdbxPendingSyncFormat"] = "{0} MDBX source(s) are waiting for sync.",
        ["MdbxNoSyncErrors"] = "No MDBX sync errors recorded.",
        ["MdbxCacheEnabled"] = "Local MDBX cache is kept for desktop operations.",
        ["MdbxCacheDisabled"] = "Local MDBX cache is disabled; remote sources may need to rebuild working copies.",
        ["MdbxWorkingCopyReady"] = "Working copy ready",
        ["MdbxWorkingCopyMissing"] = "Working copy missing",
        ["MdbxRemoteStatusFormat"] = "{0}: {1}",
        ["MdbxLocalSourceReadyFormat"] = "{0} local MDBX vault(s) are ready.",
        ["MdbxLocalSourceEmpty"] = "Create a local MDBX working copy for desktop vault operations.",
        ["MdbxWebDavSourceReadyFormat"] = "{0} WebDAV MDBX source(s) are registered.",
        ["MdbxWebDavSourceEmpty"] = "WebDAV is enabled; register an MDBX source record for this remote path.",
        ["MdbxOneDriveSourceReadyFormat"] = "{0} OneDrive MDBX source(s) are registered.",
        ["MdbxOneDriveSourceEmpty"] = "OneDrive is enabled; register an MDBX source record for Microsoft Graph sync.",
        ["MdbxRuntimeSummary"] = "Local and remote-source MDBX working copies are active for business storage. Remote upload, commit history and conflict handling still require the full MDBX sync engine port.",
        ["MdbxSecuritySummary"] = "MDBX secrets are stored through the vault encryption layer; file contents still depend on the MDBX engine implementation.",
        ["MdbxSourceLocal"] = "Local",
        ["MdbxSourceExternal"] = "External",
        ["MdbxNoDescription"] = "No description",
        ["Never"] = "Never",
        ["MdbxLocalVaultName"] = "Local Monica Vault",
        ["MdbxWebDavVaultName"] = "WebDAV Monica Vault",
        ["MdbxOneDriveVaultName"] = "OneDrive Monica Vault",
        ["MdbxLocalMetadataDescription"] = "Local desktop MDBX metadata and working copy.",
        ["MdbxWebDavMetadataDescription"] = "WebDAV MDBX metadata record for the upcoming remote sync engine.",
        ["MdbxOneDriveMetadataDescription"] = "OneDrive MDBX metadata record for the upcoming Microsoft Graph sync engine.",
        ["MdbxMetadataAlreadyRegisteredFormat"] = "{0} is already registered.",
        ["CreatedMdbxWebDavMetadata"] = "Created WebDAV MDBX working copy and registered remote source metadata.",
        ["CreatedMdbxOneDriveMetadata"] = "Created OneDrive MDBX working copy and registered remote source metadata.",
        ["EnableOneDriveFirst"] = "Enable OneDrive first.",
        ["MdbxVaultsRefreshed"] = "MDBX vault metadata refreshed.",
        ["MdbxRemoteOpenPending"] = "This MDBX source does not have a local working copy yet.",
        ["OpenedMdbxDatabaseFormat"] = "Opened {0}; local file size is {1} byte(s).",
        ["SelectedMdbxDefaultFormat"] = "{0} is now the default MDBX vault.",
        ["ConfigureMdbxRemoteSourcesHint"] = "Configure WebDAV or OneDrive before registering remote MDBX sources.",
        ["VaultSourceCountFormat"] = "{0} registered source(s)",
        ["WebDavConfiguredFormat"] = "Configured for {0}",
        ["WebDavDisabled"] = "WebDAV is disabled. Local vault operations remain available.",
        ["SyncStatusSummaryFormat"] = "{0}; {1} backup file(s) loaded in history.",
        ["SyncStatusLocalOnly"] = "Remote sync is disabled. Local vault operations remain available.",
        ["SyncConfigurationDisabled"] = "Enable WebDAV before configuring remote sync.",
        ["SyncConfigurationReadyFormat"] = "WebDAV is configured for remote path {0}.",
        ["SyncConfigurationIncomplete"] = "WebDAV is enabled but the server URL is incomplete.",
        ["SyncRecoveryLocalOnly"] = "No remote recovery source is active.",
        ["SyncRecoveryNoBackupsLoaded"] = "Load backup history or create a backup to establish a recovery point.",
        ["SyncRecoveryBackupReadyFormat"] = "{0} backup file(s) can be used for recovery.",
        ["OneDriveBoundaryEnabled"] = "OneDrive boundary enabled",
        ["OneDriveBoundaryDescription"] = "OneDrive is reserved for Microsoft Graph based MDBX sync metadata.",
        ["WebDavConnectionTestSucceededFormat"] = "WebDAV connection test succeeded; {0} remote item(s) are visible.",
        ["WebDavConnectionTestFailedFormat"] = "WebDAV connection test failed: {0}",
        ["EnableWebDavFirst"] = "Enable WebDAV and configure the server before loading backups.",
        ["WebDavServerUrlRequired"] = "Enter a valid WebDAV server URL.",
        ["WebDavBackupHistoryCountFormat"] = "{0} backup file(s)",
        ["LoadedWebDavBackupsFormat"] = "Loaded {0} WebDAV backup file(s).",
        ["WebDavBackupHistoryFailedFormat"] = "WebDAV backup history failed: {0}",
        ["WebDavBackupOptionsSummaryFormat"] = "{0} data group(s), {1}",
        ["Encrypted"] = "encrypted",
        ["PlainJson"] = "plain Monica JSON",
        ["SelectWebDavBackupContent"] = "Select at least one WebDAV backup data group.",
        ["WebDavEncryptionPasswordRequired"] = "Enter the backup encryption password.",
        ["CreatedWebDavBackupFormat"] = "Created WebDAV backup {0}.",
        ["CreateWebDavBackupFailedFormat"] = "Create WebDAV backup failed: {0}",
        ["RestoredWebDavBackupFormat"] = "Restored WebDAV backup {0}: {1} passwords, {2} secure items and {3} folders imported.",
        ["RestoreWebDavBackupFailedFormat"] = "Restore WebDAV backup failed: {0}",
        ["DeletedWebDavBackupFormat"] = "Deleted WebDAV backup {0}.",
        ["DeleteWebDavBackupFailedFormat"] = "Delete WebDAV backup failed: {0}",
        ["UnknownDate"] = "Unknown date",
        ["UnknownSize"] = "Unknown size",
        ["CanonicalVault"] = "Monica v69 SQLite canonical vault",
        ["LocalOnly"] = "Local only",
        ["NotConfigured"] = "Not configured",
        ["KeePassSourceNameFormat"] = "KeePass source #{0}",
        ["BitwardenSourceNameFormat"] = "Bitwarden source #{0}",
        ["EntryCountFormat"] = "{0} entry record(s)",
        ["PendingSyncCountFormat"] = "{0} pending local change(s)",
        ["NoPendingChanges"] = "No pending changes",
        ["AutomaticSync"] = "Automatic sync",
        ["StartupSync"] = "Sync on startup",
        ["ChangeSync"] = "Sync after changes",
        ["ManualSync"] = "Manual sync",
        ["Synced"] = "Synced",
        ["Syncing"] = "Syncing",
        ["Pending"] = "Pending",
        ["PendingUpload"] = "Pending upload",
        ["RemoteChanged"] = "Remote changed",
        ["Conflict"] = "Conflict",
        ["Failed"] = "Failed",
        ["None"] = "None",
        ["ArchivedPasswordCountFormat"] = "{0} archived passwords",
        ["SelectedPasswordCountFormat"] = "{0} selected",
        ["SelectedTotpCountFormat"] = "{0} selected",
        ["SelectedWalletCountFormat"] = "{0} selected",
        ["DeletedPasswordCountFormat"] = "{0} deleted passwords",
        ["NoteCountFormat"] = "{0} notes",
        ["TotpCountFormat"] = "{0} authenticators",
        ["WalletCountFormat"] = "{0} cards and documents",
        ["TimelineCountFormat"] = "{0} events",
        ["Locked"] = "Locked",
        ["LockVault"] = "Lock vault",
        ["VaultLocked"] = "Vault locked",
        ["WebDavEncryptionRequired"] = "WebDAV backups containing vault data must be encrypted.",
        ["WebDavHttpsRequired"] = "WebDAV requires an HTTPS endpoint without credentials in the URL.",
        ["AuthorizeExportTitle"] = "Authorize sensitive export",
        ["AuthorizeExportDescription"] = "Enter the master password before exporting decrypted vault data.",
        ["AuthorizeExportAction"] = "Authorize export",
        ["ExportAuthorizationFailed"] = "Export authorization was cancelled or the master password was incorrect.",
        ["FirstRunCreateMasterPassword"] = "First run: create a master password.",
        ["LegacyVaultImportRequired"] = "A Monica for Windows vault was detected. Import is required before Avalonia can use this path.",
        ["LegacyVaultImportPromptFormat"] = "Found legacy data at {0}. Monica by Avalonia will not modify this PascalCase database automatically. A one-time import flow must migrate it into the v69 snake_case schema first.",
        ["VaultMetadataLoadFailedFormat"] = "Vault metadata could not be loaded: {0}",
        ["SettingsLoaded"] = "Settings loaded",
        ["SettingsSaved"] = "Settings saved",
        ["SettingsSaveFailedFormat"] = "Settings could not be saved: {0}",
        ["EnterMasterPassword"] = "Enter a master password.",
        ["MasterPasswordMinLength"] = "Use at least 8 characters for the master password.",
        ["ConfirmationMismatch"] = "The confirmation password does not match.",
        ["WrongMasterPassword"] = "Wrong master password.",
        ["VaultUnlocked"] = "Vault unlocked",
        ["VaultUnlockedLegacyBusinessDataPending"] = "MDBX is active. Legacy desktop SQLite data was detected and left untouched; keep the old data until the migration tool is available.",
        ["UnlockFailedFormat"] = "Unlock failed: {0}",
        ["VaultLoadFailedFormat"] = "Vault load failed: {0}",
        ["CreatedPasswordFormat"] = "Created {0}",
        ["UpdatedPasswordFormat"] = "Updated {0}",
        ["SavedTotpFormat"] = "Saved authenticator {0}",
        ["SavedWalletItemFormat"] = "Saved wallet item {0}",
        ["ArchivedPasswordFormat"] = "Archived {0}",
        ["UnarchivedPasswordFormat"] = "Unarchived {0}",
        ["RestoredPasswordFormat"] = "Restored {0}",
        ["EditingNewSecureNote"] = "Editing a new secure note",
        ["EditingNoteFormat"] = "Editing {0}",
        ["NoteRequiresContent"] = "Enter a title or note content.",
        ["SavedNoteFormat"] = "Saved note {0}",
        ["CopiedPasswordFormat"] = "Copied password for {0}",
        ["CopiedUsernameFormat"] = "Copied username for {0}",
        ["CopiedWebsiteFormat"] = "Copied website for {0}",
        ["CopiedTotpFormat"] = "Copied TOTP for {0}",
        ["CopiedFieldFormat"] = "Copied {0}",
        ["CopiedWalletFieldFormat"] = "Copied {0}",
        ["CopiedPasswordHistory"] = "Copied historical password",
        ["DeletedPasswordHistoryEntry"] = "Deleted password history entry",
        ["ClearedPasswordHistory"] = "Cleared password history",
        ["PasswordHistoryUnavailable"] = "Password history is unavailable.",
        ["PasswordHistoryLastUsedFormat"] = "Last used: {0}",
        ["FavoritedPasswordCountFormat"] = "Favorited {0} passwords",
        ["FavoritedTotpFormat"] = "Favorited authenticator {0}",
        ["UnfavoritedTotpFormat"] = "Removed favorite from authenticator {0}",
        ["FavoritedTotpCountFormat"] = "Favorited {0} authenticators",
        ["ArchivedSelectedPasswordsFormat"] = "Archived {0} selected passwords",
        ["StackedPasswordCountFormat"] = "Stacked {0} passwords",
        ["MovedToRecycleBinFormat"] = "Moved {0} to recycle bin",
        ["MovedSelectedPasswordsToRecycleBinFormat"] = "Moved {0} selected passwords to recycle bin",
        ["MovedSelectedTotpToRecycleBinFormat"] = "Moved {0} selected authenticators to recycle bin",
        ["MovedSelectedWalletItemsToRecycleBinFormat"] = "Moved {0} selected wallet items to recycle bin",
        ["MovedSelectedPasswordsToFolderFormat"] = "Moved {0} selected passwords to {1}",
        ["AuthenticatorTitleRequired"] = "Enter an authenticator title.",
        ["TotpSecretRequired"] = "Enter a TOTP secret.",
        ["DocumentNumberRequired"] = "Enter a document number.",
        ["CardNumberRequired"] = "Enter a card number.",
        ["BoundPasswordMissing"] = "The password bound to this authenticator could not be found.",
        ["FolderNameRequired"] = "Enter a folder name.",
        ["CreatedFolderFormat"] = "Created folder {0}",
        ["SelectedFolderFormat"] = "Selected folder {0}",
        ["SelectFolderToManage"] = "Select a folder first.",
        ["FolderAlreadyExistsFormat"] = "Folder {0} already exists.",
        ["RenamedFolderFormat"] = "Renamed folder {0} to {1}",
        ["DeletedFolderFormat"] = "Deleted folder {0}; moved {1} passwords to No folder",
        ["DeletedPasswordPermanentlyFormat"] = "Permanently deleted {0}",
        ["EmptiedRecycleBinFormat"] = "Permanently deleted {0} recycle bin item(s)",
        ["AddedAttachmentFormat"] = "Added {0} to {1}",
        ["DeletedAttachmentFormat"] = "Deleted attachment {0}",
        ["AttachmentAddedRefreshDetails"] = "Attachment added. Reopen details to see the encrypted file metadata.",
        ["TimelineEntryDescriptionFormat"] = "{0} {1} on {2}",
        ["OperationCreate"] = "Created",
        ["OperationUpdate"] = "Updated",
        ["OperationDelete"] = "Deleted",
        ["OperationRestore"] = "Restored",
        ["OperationPurge"] = "Permanently deleted",
        ["OperationFavorite"] = "Favorited",
        ["OperationMoveCategory"] = "Moved",
        ["OperationStack"] = "Stacked",
        ["OperationAttachment"] = "Attached file",
        ["OperationArchive"] = "Archived",
        ["OperationUnarchive"] = "Unarchived",
        ["OperationImport"] = "Imported",
        ["GeneratedPassword"] = "Generated password",
        ["ExportPrepared"] = "Prepared Monica JSON export preview",
        ["ImportJsonRequired"] = "Paste Monica JSON before importing.",
        ["ImportedMonicaJsonFormat"] = "Imported {0} passwords and {1} secure items.",
        ["ImportedMonicaJsonWithCategoriesFormat"] = "Imported {0} passwords, {1} secure items and {2} folders.",
        ["ImportAegisJsonRequired"] = "Paste Aegis JSON before importing.",
        ["ImportedAegisJsonFormat"] = "Imported {0} authenticators from Aegis JSON. Skipped {1} duplicates.",
        ["ImportTotpCsvRequired"] = "Paste TOTP CSV before importing.",
        ["ImportedTotpCsvFormat"] = "Imported {0} authenticators from TOTP CSV. Skipped {1} duplicates.",
        ["ImportNoteCsvRequired"] = "Paste Notes CSV before importing.",
        ["ImportedNoteCsvFormat"] = "Imported {0} notes from Notes CSV. Skipped {1} duplicates.",
        ["ImportCsvRequired"] = "Paste password CSV before importing.",
        ["ImportedPasswordCsvFormat"] = "Imported {0} passwords from CSV.",
        ["ExportedPasswordCsv"] = "Prepared password CSV export preview",
        ["ExportedTotpCsv"] = "Prepared TOTP CSV export preview",
        ["ExportedNoteCsv"] = "Prepared notes CSV export preview",
        ["ExportedTimelineFormat"] = "Exported {0} timeline entries",
        ["TimelineExportEmpty"] = "No timeline entries to export",
        ["ExportedAegisJson"] = "Prepared Aegis JSON export preview",
        ["SavedExportFileFormat"] = "Saved export to {0}.",
        ["SaveExportFileFailedFormat"] = "Save export failed: {0}",
        ["ImportFailedFormat"] = "Import failed: {0}",
        ["MonicaJson"] = "Monica JSON",
        ["AegisJson"] = "Aegis JSON",
        ["TotpCsv"] = "TOTP CSV",
        ["NoteCsv"] = "Notes CSV",
        ["PasswordCsv"] = "Password CSV",
        ["CreatedMdbxMetadata"] = "Created a real MDBX-1 vault and registered its local metadata"
    };

    private static readonly Dictionary<string, string> Chinese = new()
    {
        ["Passwords"] = "密码",
        ["SecureNotes"] = "安全笔记",
        ["Totp"] = "动态口令",
        ["Cards"] = "卡包",
        ["Generator"] = "生成器",
        ["Archive"] = "归档",
        ["RecycleBin"] = "回收站",
        ["ArchiveEmptyHint"] = "归档的密码会显示在这里。需要先回到密码库选择条目并归档。",
        ["RecycleBinEmptyHint"] = "删除的密码会先显示在这里，确认后才会永久移除。需要管理当前条目时请回到密码库。",
        ["Timeline"] = "时间线",
        ["TimelineEmptyHint"] = "创建、恢复、导入、同步或永久删除等保险库操作发生后，活动记录会显示在这里。",
        ["TimelineEmptySelectionHint"] = "选择一条事件后，可以查看时间、项目类型和操作详情。",
        ["SecurityAnalysis"] = "安全分析",
        ["SecurityAnalysisSubtitle"] = "本地检查弱密码、复用密码、重复网站、过期密码、未受保护记录和已泄露密码。",
        ["SecurityIssueCountFormat"] = "{0} 个问题",
        ["SecurityScore"] = "安全评分",
        ["SecurityScoreFormat"] = "{0}/100",
        ["SecurityAnalyzedPasswordCountFormat"] = "已分析 {0} 个可用密码",
        ["WeakPasswords"] = "弱密码",
        ["WeakPasswordsSummary"] = "评分为弱或非常弱的密码。",
        ["DuplicatePasswords"] = "重复密码",
        ["DuplicatePasswordsSummary"] = "同一个密码被多个登录项复用。",
        ["DuplicateWebsites"] = "重复网站",
        ["MissingTwoFactor"] = "缺少两步验证",
        ["MissingTwoFactorSummary"] = "支持两步验证的网站尚未保存验证器或 Passkey 绑定。",
        ["StalePasswords"] = "长期未更新",
        ["CompromisedPasswords"] = "已泄露密码",
        ["CompromisedPasswordsSummary"] = "在已知泄露语料中发现的密码。",
        ["CheckCompromisedPasswords"] = "检查泄露密码",
        ["CompromisedPasswordNotChecked"] = "本次会话尚未检查已泄露密码。",
        ["CompromisedPasswordCheckingFormat"] = "正在使用 k-anonymity 范围查询检查 {0} 个密码...",
        ["CompromisedPasswordCheckCompleteFormat"] = "已检查 {0} 个可用密码；发现 {1} 个已泄露密码。",
        ["CompromisedPasswordCheckUnavailableFormat"] = "检查泄露密码失败：{0}",
        ["CompromisedPasswordIssueFormat"] = "在泄露数据中出现 {0} 次。请立即更换。",
        ["WeakPasswordIssueFormat"] = "密码强度为 {0}。建议替换为生成器创建的密码。",
        ["DuplicatePasswordIssueFormat"] = "此密码被 {0} 个条目复用，包括 {1}。",
        ["DuplicateWebsiteIssueFormat"] = "{0} 出现在 {1} 个密码条目中。",
        ["MissingTwoFactorIssueFormat"] = "{0} 通常支持两步验证。",
        ["StalePasswordIssueFormat"] = "上次更新于 {0}。建议定期轮换。",
        ["HighSeverity"] = "高",
        ["MediumSeverity"] = "中",
        ["LowSeverity"] = "低",
        ["SyncAndBackup"] = "同步与备份",
        ["DatabaseManagement"] = "数据库管理",
        ["DataManagement"] = "数据管理",
        ["DataManagementDescription"] = "与 Monica for Windows 设置页对齐的文件导入与导出操作。",
        ["Settings"] = "设置",
        ["Folders"] = "文件夹",
        ["FolderScopes"] = "范围",
        ["Personal"] = "个人",
        ["Refresh"] = "刷新",
        ["Export"] = "导出",
        ["UnlockMonica"] = "解锁 Monica",
        ["CreateMonicaVault"] = "创建 Monica 保险库",
        ["LegacyVaultDetected"] = "检测到 Monica for Windows 保险库",
        ["UnlockDescription"] = "使用主密码打开 Avalonia 桌面保险库。",
        ["CreateVaultDescription"] = "设置一个主密码。之后每次打开桌面保险库都需要它。",
        ["MasterPasswordWatermark"] = "主密码",
        ["ConfirmMasterPasswordWatermark"] = "确认主密码",
        ["Unlock"] = "解锁",
        ["CreateVault"] = "创建保险库",
        ["UnlockingVault"] = "正在解锁...",
        ["CreatingVault"] = "正在创建保险库...",
        ["MasterPasswordPrivacyNotice"] = "主密码仅用于解锁此保险库，不会以明文形式存储。",
        ["UnsupportedMasterPasswordCharactersRemoved"] = "已移除主密码中不支持的控制字符。",
        ["VaultUnlockedLoadingFormat"] = "{0}，正在加载保险库数据...",
        ["PasswordManager"] = "密码管理",
        ["PasswordEmptyHint"] = "还没有密码。创建第一个密码即可开始使用此保险库。",
        ["PasswordNoFilteredResults"] = "当前搜索或筛选条件下没有匹配的密码。",
        ["SelectPasswordItems"] = "选择项目",
        ["SelectAllVisiblePasswords"] = "选择当前可见的全部密码",
        ["Search"] = "搜索...",
        ["AddPassword"] = "添加密码",
        ["EditPassword"] = "编辑密码",
        ["PasswordDetails"] = "密码详情",
        ["LoadingPasswordDetails"] = "正在加载密码详情...",
        ["BackToPasswordList"] = "返回密码列表",
        ["RetryPasswordDetails"] = "重新加载详情",
        ["Details"] = "详情",
        ["PasswordDetailsLoadFailedFormat"] = "加载密码详情失败：{0}",
        ["PasswordHistory"] = "密码历史",
        ["PasswordHistoryDescription"] = "保存在此保险库本地。Monica 会保留此条目的最近 10 个旧密码。",
        ["PasswordHistoryLatest"] = "最新",
        ["ClearPasswordHistory"] = "清空密码历史",
        ["Favorite"] = "收藏",
        ["Copy"] = "复制",
        ["CopyPassword"] = "复制密码",
        ["CopyUsername"] = "复制用户名",
        ["CopyWebsite"] = "复制网站",
        ["SortPasswords"] = "排序密码",
        ["SortUpdated"] = "最近更新",
        ["SortTitle"] = "标题",
        ["SortWebsite"] = "网站",
        ["SortUsername"] = "用户名",
        ["SortCreated"] = "最近创建",
        ["SortFavorites"] = "收藏优先",
        ["QuickFilterFavorite"] = "收藏",
        ["QuickFilter2Fa"] = "两步验证",
        ["QuickFilterNotes"] = "笔记",
        ["QuickFilterPasskey"] = "通行密钥",
        ["QuickFilterBoundNote"] = "已绑定笔记",
        ["QuickFilterUncategorized"] = "未分类",
        ["QuickFilterLocalOnly"] = "仅本地",
        ["QuickFilterAttachments"] = "附件",
        ["MoreOptions"] = "更多操作",
        ["MoveToRecycleBin"] = "移到回收站",
        ["BatchFavorite"] = "收藏所选",
        ["BatchArchive"] = "归档所选",
        ["BatchDelete"] = "删除所选",
        ["MoveToFolder"] = "移动到文件夹",
        ["Move"] = "移动",
        ["ArchivePassword"] = "归档密码",
        ["UnarchivePassword"] = "取消归档",
        ["RestorePassword"] = "恢复密码",
        ["DeletePermanently"] = "永久删除",
        ["Delete"] = "删除",
        ["Select"] = "选择",
        ["Edit"] = "编辑",
        ["DeletePasswordConfirmationTitle"] = "移到回收站？",
        ["DeletePasswordConfirmationMessageFormat"] = "要将“{0}”移到回收站吗？之后可以从回收站恢复。",
        ["DeleteSelectedPasswordsConfirmationTitle"] = "移动选中的密码？",
        ["DeleteSelectedPasswordsConfirmationMessageFormat"] = "要将 {0} 个选中的密码移到回收站吗？之后可以从回收站恢复。",
        ["DeleteItemConfirmationTitle"] = "移到回收站？",
        ["DeleteItemConfirmationMessageFormat"] = "要将“{0}”移到回收站吗？之后可以从回收站恢复。",
        ["DeleteSelectedItemsConfirmationTitle"] = "移动选中的项目？",
        ["DeleteSelectedItemsConfirmationMessageFormat"] = "要将 {0} 个选中的项目移到回收站吗？之后可以从回收站恢复。",
        ["DeletePermanentlyConfirmationTitle"] = "永久删除？",
        ["DeletePermanentlyConfirmationMessageFormat"] = "要永久删除“{0}”吗？此操作无法撤销。",
        ["PermanentDeleteConfirmationPhrase"] = "永久删除",
        ["PermanentDeleteConfirmationInstructionFormat"] = "请输入“{0}”以启用永久删除。",
        ["EmptyRecycleBin"] = "清空回收站",
        ["EmptyRecycleBinConfirmationTitle"] = "清空回收站？",
        ["EmptyRecycleBinConfirmationMessageFormat"] = "要永久删除回收站中的全部 {0} 个项目吗？此操作无法撤销。",
        ["EmptyRecycleBinConfirmationPhrase"] = "清空回收站",
        ["EmptyRecycleBinConfirmationInstructionFormat"] = "请输入“{0}”以永久删除回收站中的全部项目。",
        ["DeleteWebDavBackupConfirmationTitle"] = "删除 WebDAV 备份？",
        ["DeleteWebDavBackupConfirmationMessageFormat"] = "要删除远端备份“{0}”吗？此操作无法撤销。",
        ["DeleteWebDavBackupConfirmationPhrase"] = "删除远端备份",
        ["DeleteWebDavBackupConfirmationInstructionFormat"] = "请输入“{0}”以删除此远端备份。",
        ["DeleteFolderConfirmationTitle"] = "删除文件夹？",
        ["DeleteFolderConfirmationMessageFormat"] = "要删除文件夹“{0}”吗？{1} 个密码会移动到无文件夹。",
        ["DeleteAttachmentConfirmationTitle"] = "删除附件？",
        ["DeleteAttachmentConfirmationMessageFormat"] = "要删除附件“{0}”吗？这会从保险库移除已存储的文件。",
        ["DeletePasswordHistoryConfirmationTitle"] = "删除密码历史？",
        ["DeletePasswordHistoryConfirmationMessage"] = "要删除这条密码历史吗？此操作无法撤销。",
        ["ClearPasswordHistoryConfirmationTitle"] = "清空密码历史？",
        ["ClearPasswordHistoryConfirmationMessage"] = "要清空此登录项的全部密码历史吗？此操作无法撤销。",
        ["Save"] = "保存",
        ["Cancel"] = "取消",
        ["Discard"] = "放弃",
        ["NoteVault"] = "保险库",
        ["NewSecureNote"] = "新建笔记",
        ["NoteTitleWatermark"] = "标题",
        ["NoteTagsWatermark"] = "标签，用逗号分隔",
        ["NoteContentWatermark"] = "编写私人笔记…",
        ["PlainText"] = "纯文本",
        ["Preview"] = "预览",
        ["SaveNote"] = "保存笔记",
        ["NoteCountFormat"] = "{0} 个笔记",
        ["EditingNewSecureNote"] = "正在编辑新的安全笔记",
        ["EditingNoteFormat"] = "正在编辑 {0}",
        ["NoteRequiresContent"] = "请输入标题或笔记内容。",
        ["SavedNoteFormat"] = "已保存笔记 {0}",
        ["NoteSearchWatermark"] = "搜索笔记、标签和正文",
        ["ClearNoteSearch"] = "清除笔记搜索",
        ["NoteHome"] = "主页",
        ["NoteTagTree"] = "标签文件树",
        ["NoteNoMatchingItems"] = "没有符合当前搜索条件的笔记。",
        ["NoteEmptyTitle"] = "尚未打开笔记",
        ["NoteEmptyDescription"] = "新建安全笔记，或从列表中选择一项。",
        ["BackToNoteList"] = "返回笔记列表",
        ["PreviousNoteTabs"] = "向左滚动标签",
        ["NextNoteTabs"] = "向右滚动标签",
        ["CloseNoteTab"] = "关闭笔记标签",
        ["SaveAllNotes"] = "保存全部笔记",
        ["ImportMarkdown"] = "导入 Markdown",
        ["ExportMarkdown"] = "导出 Markdown",
        ["InsertImage"] = "插入图片",
        ["NoteEditMode"] = "编辑模式",
        ["NotePreviewMode"] = "预览模式",
        ["NoteSplitMode"] = "分屏模式",
        ["NoteFind"] = "查找",
        ["NoteReplaceWith"] = "替换为",
        ["NotePreviousMatch"] = "上一个匹配项",
        ["NoteNextMatch"] = "下一个匹配项",
        ["NoteReplaceCurrentMatch"] = "替换当前匹配项",
        ["NoteReplaceAllMatches"] = "替换全部匹配项",
        ["NoteReplace"] = "替换",
        ["NoteReplaceAll"] = "全部替换",
        ["NoteMatchCase"] = "区分大小写",
        ["NoteCloseFind"] = "关闭查找",
        ["NoteNoMatches"] = "无匹配",
        ["NoteReplacedMatchesFormat"] = "替换了 {0} 处",
        ["NoteView"] = "视图",
        ["NoteFormat"] = "格式",
        ["NoteMode"] = "模式",
        ["NoteLayout"] = "布局",
        ["NoteSinglePane"] = "单栏",
        ["NoteSplitPane"] = "分屏",
        ["NoteInformation"] = "信息",
        ["NoteLineCountLabel"] = "行数",
        ["NoteWordCountLabel"] = "词数",
        ["NoteCharacterCountLabel"] = "字符",
        ["NoteProperties"] = "属性",
        ["NoteTagsLabel"] = "标签",
        ["NoteOutline"] = "大纲",
        ["NoteNoOutline"] = "没有 Markdown 标题",
        ["NoteReferences"] = "引用",
        ["NoteNoReferences"] = "没有链接或图片引用",
        ["NoteToolbarUndo"] = "撤销",
        ["NoteToolbarRedo"] = "重做",
        ["NoteToolbarFindReplace"] = "查找和替换",
        ["NoteToolbarHeading1"] = "一级标题",
        ["NoteToolbarHeading2"] = "二级标题",
        ["NoteToolbarHeading3"] = "三级标题",
        ["NoteToolbarBold"] = "粗体",
        ["NoteToolbarItalic"] = "斜体",
        ["NoteToolbarStrikethrough"] = "删除线",
        ["NoteToolbarInlineCode"] = "行内代码",
        ["NoteToolbarQuote"] = "引用",
        ["NoteToolbarCodeBlock"] = "代码块",
        ["NoteToolbarUnorderedList"] = "无序列表",
        ["NoteToolbarOrderedList"] = "有序列表",
        ["NoteToolbarTaskList"] = "任务列表",
        ["NoteToolbarTable"] = "表格",
        ["NoteToolbarLink"] = "链接",
        ["NoteToolbarHorizontalRule"] = "分割线",
        ["NoteUntagged"] = "未分类",
        ["InsertedNoteImageFormat"] = "已插入图片 {0}",
        ["InsertNoteImageFailedFormat"] = "插入图片失败：{0}",
        ["NoNotesToSave"] = "没有需要保存的笔记更改。",
        ["SavedNotesFormat"] = "已保存 {0} 个笔记",
        ["SavedNotesWithSkippedFormat"] = "已保存 {0} 个笔记，跳过 {1} 个空笔记",
        ["ImportedMarkdownDraftFormat"] = "已导入 Markdown 草稿 {0}",
        ["ImportMarkdownFailedFormat"] = "导入 Markdown 失败：{0}",
        ["NoteEditorStatusFormat"] = "行 {0}，列 {1} · {2} 行 · {3} 词 · {4} 字符",
        ["NoteEditorSelectionStatusFormat"] = "行 {0}，列 {1} · 已选 {2} · {3} 行 · {4} 词 · {5} 字符",
        ["ReferenceCannotOpen"] = "无法打开此引用。",
        ["OpenedReferenceFormat"] = "已打开 {0}",
        ["OpenReferenceFailedFormat"] = "打开引用失败：{0}",
        ["CopiedReference"] = "已复制引用",
        ["SaveNoteChangesTitle"] = "保存对此笔记的更改？",
        ["UnsavedNoteMessageFormat"] = "“{0}”有未保存的更改。关闭前要保存吗？",
        ["SaveUnsavedNotesTitle"] = "保存未保存的笔记？",
        ["UnsavedNotesMessageFormat"] = "还有 {0} 个笔记标签包含未保存的更改。关闭 Monica 前要保存全部吗？",
        ["NoteImageAttachment"] = "图片附件",
        ["NoteImageAttachmentFormat"] = "图片附件：{0}",
        ["NoteImage"] = "图片",
        ["NoteImageNumberFormat"] = "图片 {0}",
        ["ClearPasswordFilters"] = "清除筛选",
        ["ClearedPasswordFilters"] = "已清除密码筛选",
        ["NoFolder"] = "无文件夹",
        ["NewPassword"] = "新建密码",
        ["PasswordTitleRequired"] = "请输入密码标题。",
        ["PasswordValueRequired"] = "请输入密码内容。",
        ["PasswordTitle"] = "标题",
        ["Website"] = "网站",
        ["Username"] = "用户名",
        ["Password"] = "密码",
        ["Category"] = "分类",
        ["BoundNote"] = "绑定笔记",
        ["SecurityVerification"] = "安全验证",
        ["AuthenticatorKey"] = "验证器密钥",
        ["AuthenticatorKeyHint"] = "可选的 TOTP 密钥，对应 Android 端的验证器字段。二维码导入和多密码存储会继续复用这个模型扩展。",
        ["AppBinding"] = "应用绑定",
        ["AppName"] = "应用名称",
        ["AppPackageName"] = "应用包名或 Bundle ID",
        ["NoBoundNote"] = "不绑定笔记",
        ["Untitled"] = "未命名",
        ["PersonalInfo"] = "个人信息",
        ["Email"] = "邮箱",
        ["Phone"] = "电话",
        ["AddressLine"] = "地址",
        ["City"] = "城市",
        ["State"] = "省/州",
        ["ZipCode"] = "邮编",
        ["Country"] = "国家/地区",
        ["CardInfo"] = "卡片信息",
        ["CreditCardNumber"] = "卡号",
        ["CreditCardHolder"] = "持卡人",
        ["CreditCardExpiry"] = "有效期",
        ["CreditCardCvv"] = "CVV",
        ["AdvancedLogin"] = "高级登录",
        ["LoginTypePassword"] = "密码",
        ["LoginTypeSso"] = "SSO",
        ["LoginTypeWifi"] = "Wi-Fi",
        ["LoginTypeSshKey"] = "SSH 密钥",
        ["SsoProvider"] = "SSO 提供商",
        ["PasskeyBindings"] = "Passkey 绑定",
        ["WifiMetadata"] = "Wi-Fi 元数据",
        ["SshKeyData"] = "SSH 密钥数据",
        ["CustomIcon"] = "自定义图标",
        ["CustomIconType"] = "图标类型",
        ["CustomIconValue"] = "图标值",
        ["CustomIconDescription"] = "对齐 Android 的自定义图标元数据：简单图标 slug 或上传图标文件/路径。",
        ["CustomIconUseDefault"] = "使用网站/默认图标",
        ["CustomIconSimple"] = "简单图标 slug",
        ["CustomIconUploaded"] = "上传图标文件",
        ["CustomIconSimpleHint"] = "github, microsoft, bank, mail...",
        ["CustomIconUploadedHint"] = "本地图标文件名或路径",
        ["CustomFields"] = "自定义字段",
        ["CustomFieldsHint"] = "每行一个字段，格式为 标题=值；标题前加 ! 表示受保护字段。",
        ["Attachments"] = "附件",
        ["Attachment"] = "附件",
        ["AddAttachment"] = "添加附件",
        ["NoAttachments"] = "没有附件",
        ["SelectAttachment"] = "选择附件",
        ["Notes"] = "备注",
        ["SourceMetadata"] = "来源元数据",
        ["BitwardenVault"] = "Bitwarden 保险库",
        ["BitwardenCipher"] = "Bitwarden 密文",
        ["KeePassDatabase"] = "KeePass 数据库",
        ["KeePassGroup"] = "KeePass 分组",
        ["MdbxDatabase"] = "MDBX 数据库",
        ["MdbxFolder"] = "MDBX 文件夹",
        ["CreatedAt"] = "创建时间",
        ["UpdatedAt"] = "更新时间",
        ["Close"] = "关闭",
        ["TwoStepVerification"] = "两步验证",
        ["AddAuthenticator"] = "添加验证器",
        ["EditAuthenticator"] = "编辑验证器",
        ["TotpPageDescription"] = "管理动态口令：复制、编辑、收藏、删除和批量操作都在此完成。",
        ["TotpEmptyHint"] = "可以扫描二维码或手动输入 Base32 密钥来添加动态口令。",
        ["TotpConsoleStatusFormat"] = "{0} 个验证器 · {1} 个即将过期",
        ["TotpFilteredStatusFormat"] = "显示 {0} 个 · 共 {1} 个",
        ["TotpScanQr"] = "扫描二维码",
        ["TotpManualAdd"] = "手动添加",
        ["TotpScanQrFallback"] = "当前桌面版会打开验证器录入对话框，后续可接入真实二维码扫描。",
        ["TotpFilterTitle"] = "分组",
        ["TotpIssuerGroups"] = "发行方",
        ["TotpFilterAll"] = "全部",
        ["TotpFilterExpiringSoon"] = "即将过期",
        ["TotpFilterUnbound"] = "未绑定密码",
        ["TotpNoFilteredResults"] = "当前搜索或分组下没有匹配的验证器。",
        ["ClearTotpFilters"] = "清除筛选",
        ["ClearedTotpFilters"] = "已清除验证器筛选",
        ["MoreActions"] = "更多操作",
        ["ShowHidden"] = "显示隐藏项",
        ["Help"] = "帮助",
        ["AdvancedTotpOptions"] = "高级选项",
        ["TotpSecretHint"] = "粘贴 Base32 密钥或 otpauth URI。Monica 会把规范化后的动态口令元数据保存在本地保险库。",
        ["TotpCode"] = "动态口令",
        ["RemainingTime"] = "剩余时间",
        ["Issuer"] = "发行方",
        ["Account"] = "账号",
        ["TotpSecret"] = "动态口令密钥",
        ["CopyCode"] = "复制验证码",
        ["Wallet"] = "卡包",
        ["AddItem"] = "添加项目",
        ["AddWalletItem"] = "添加卡包项目",
        ["EditWalletItem"] = "编辑卡包项目",
        ["WalletPageDescription"] = "管理银行卡和证件，支持详情、编辑、图片路径和批量删除。",
        ["Document"] = "证件",
        ["BankCard"] = "银行卡",
        ["DocumentNumber"] = "证件号码",
        ["FullName"] = "姓名",
        ["IssuedDate"] = "签发日期",
        ["ExpiryDate"] = "到期日期",
        ["IssuedBy"] = "签发机构",
        ["Nationality"] = "国籍",
        ["AdditionalInfo"] = "附加信息",
        ["CardNumber"] = "卡号",
        ["CardholderName"] = "持卡人",
        ["Expiry"] = "有效期",
        ["ExpiryMonth"] = "有效月份",
        ["ExpiryYear"] = "有效年份",
        ["BankName"] = "银行名称",
        ["BillingAddress"] = "账单地址",
        ["CardBrand"] = "卡组织",
        ["DocumentPhotos"] = "证件照片",
        ["NoDocumentPhotos"] = "没有证件照片",
        ["ImagePathsWatermark"] = "每行一个本地图片路径",
        ["ImagePathsDescription"] = "图片仍以路径引用；后续可迁移到加密附件。",
        ["DesktopEquivalents"] = "桌面等价能力",
        ["DesktopEquivalentsMessage"] = "Android 的自动填充、输入法、无障碍和凭据提供程序能力，在桌面端通过快速搜索、剪贴板、托盘/浏览器扩展接口或平台受限状态呈现。",
        ["CreateMdbxMetadata"] = "创建 MDBX 元数据",
        ["MdbxVaults"] = "MDBX 保险库",
        ["MdbxVaultsDescription"] = "在一个页面管理本地、WebDAV 与 OneDrive 的 MDBX 保险库元数据。本地 MDBX 现在可在桌面端打开；远程 MDBX 会先登记为后续同步引擎使用的来源。",
        ["MdbxLocalSource"] = "本地 MDBX",
        ["MdbxWebDavSource"] = "WebDAV MDBX",
        ["MdbxOneDriveSource"] = "OneDrive MDBX",
        ["CreateLocalMdbxVault"] = "创建本地 MDBX",
        ["RegisterMdbxSource"] = "登记来源",
        ["Configure"] = "配置",
        ["MdbxSourcesSection"] = "来源",
        ["MdbxWorkingCopiesSection"] = "工作副本",
        ["MdbxHealthSection"] = "健康与诊断",
        ["MdbxDiagnostics"] = "诊断",
        ["MdbxRemotePath"] = "远程路径",
        ["MdbxLastSynced"] = "上次同步",
        ["RegisteredMdbxVaults"] = "已登记 MDBX 保险库",
        ["NoMdbxVaults"] = "还没有登记 MDBX 保险库元数据。",
        ["MdbxEmptyHint"] = "创建本地 MDBX 工作副本，或配置远程 MDBX 来源，以开始使用加密业务存储。",
        ["Default"] = "默认",
        ["LocalPath"] = "本地路径",
        ["SetDefault"] = "设为默认",
        ["Open"] = "打开",
        ["MdbxRuntime"] = "运行状态",
        ["MdbxSecurity"] = "安全",
        ["MdbxAndroidParity"] = "Android 对齐",
        ["MdbxAndroidParityDescription"] = "桌面端 MDBX 现在拥有独立管理页，形态对齐 Android 的来源管理中心。",
        ["MdbxAndroidParityLocal"] = "桌面端已支持本地 MDBX 元数据和工作副本打开。",
        ["MdbxAndroidParityRemote"] = "WebDAV 与 OneDrive MDBX 来源现在会创建本地 MDBX 工作副本承载业务数据；完整远程同步、提交历史和冲突处理仍需要移植 Android MDBX 引擎。",
        ["LocalDatabase"] = "本地数据库",
        ["LocalDatabaseDescription"] = "Avalonia 保留 SQLite 用于应用设置、本地缓存和迁移索引，敏感业务数据由 MDBX 工作副本承载。",
        ["ExternalDatabases"] = "外部数据库",
        ["ExternalDatabasesDescription"] = "KeePass KDBX、MDBX、Bitwarden 与 WebDAV 来源通过平台无关服务接入。",
        ["MdbxDatabaseCount"] = "MDBX 保险库元数据",
        ["RegisteredDatabases"] = "已登记数据库",
        ["DatabaseSourcesEmptyHint"] = "登记本地 MDBX、WebDAV、OneDrive 或迁移元数据后，数据库来源会显示在这里。",
        ["WebDavConnection"] = "WebDAV 连接",
        ["SyncOverview"] = "同步概览",
        ["SyncConfiguration"] = "同步配置",
        ["TestConnection"] = "测试连接",
        ["FeatureParityMap"] = "功能对齐表",
        ["DangerZone"] = "危险区",
        ["About"] = "关于",
        ["AboutDescription"] = "Monica 桌面设置中的版本信息与项目链接。",
        ["AppVersion"] = "应用版本",
        ["GitHubRepository"] = "GitHub 仓库",
        ["OpenRepository"] = "打开仓库",
        ["GitHubRepositoryOpened"] = "已打开 GitHub 仓库。",
        ["GitHubRepositoryOpenFailedFormat"] = "无法打开 GitHub 仓库：{0}",
        ["DangerZoneDescription"] = "对齐 WinUI 桌面设置中的破坏性保险库维护操作。",
        ["ClearVaultData"] = "清空保险库数据",
        ["ClearVaultDataDescription"] = "删除密码、安全项目或完整的本地 Avalonia v69 保险库数据；主密码记录会保留。",
        ["ClearPasswordsOnly"] = "清空密码",
        ["ClearSecureItemsOnly"] = "清空安全项目",
        ["ClearAllVaultData"] = "清空全部数据",
        ["ClearVaultConfirmationPhrase"] = "清空 Monica 数据",
        ["ClearVaultConfirmationInstructionFormat"] = "执行破坏性操作前请输入“{0}”。",
        ["ClearVaultConfirmationFailedFormat"] = "请输入“{0}”以确认清空保险库数据。",
        ["ClearedVaultDataFormat"] = "已清空：{0}。",
        ["ExportPreview"] = "导出预览",
        ["ImportMonicaJson"] = "导入 Monica JSON",
        ["ImportMonicaJsonDescription"] = "选择或粘贴 Monica JSON 导出包，将密码、笔记、卡包和验证器导入此保险库。",
        ["ImportJsonWatermark"] = "在此粘贴 Monica JSON 导出内容",
        ["ImportAegisJson"] = "导入 Aegis JSON",
        ["ImportAegisJsonDescription"] = "将未加密的 Aegis JSON 验证器导出导入到 TOTP 保险库。加密 Aegis 文件目前会提示暂不支持。",
        ["ImportAegisJsonWatermark"] = "在此粘贴未加密的 Aegis JSON",
        ["ImportTotpCsv"] = "导入 TOTP CSV",
        ["ImportTotpCsvDescription"] = "导入 Monica for Windows 兼容的 TOTP 安全项目 CSV 行。",
        ["ImportTotpCsvWatermark"] = "在此粘贴 TOTP CSV",
        ["ImportNoteCsv"] = "导入笔记 CSV",
        ["ImportNoteCsvDescription"] = "导入 Monica for Windows 兼容的 NOTE 安全项目 CSV 行。",
        ["ImportNoteCsvWatermark"] = "在此粘贴笔记 CSV",
        ["ImportPasswordCsv"] = "导入密码 CSV",
        ["ImportPasswordCsvDescription"] = "选择或粘贴 Monica、Bitwarden 风格或其他密码管理器的 CSV。保存前会加密密码。",
        ["ImportCsvWatermark"] = "在此粘贴密码 CSV",
        ["ExportPasswordCsv"] = "导出密码 CSV",
        ["ExportCsvPreview"] = "密码 CSV 预览",
        ["ExportTotpCsv"] = "导出 TOTP CSV",
        ["ExportTotpCsvDescription"] = "将验证器导出为 Monica for Windows 兼容的安全项目 CSV 行。",
        ["ExportTotpCsvPreview"] = "TOTP CSV 预览",
        ["ExportNoteCsv"] = "导出笔记 CSV",
        ["ExportNoteCsvDescription"] = "将安全笔记导出为 Monica for Windows 兼容的 NOTE 安全项目 CSV 行。",
        ["ExportNoteCsvPreview"] = "笔记 CSV 预览",
        ["ExportAegisJson"] = "导出 Aegis JSON",
        ["ExportAegisJsonDescription"] = "将验证器导出为未加密的 Aegis JSON。文件会包含明文 TOTP 密钥。",
        ["ExportAegisPreview"] = "Aegis JSON 预览",
        ["Import"] = "导入",
        ["ImportFromFile"] = "从文件导入",
        ["SaveJsonExport"] = "保存 JSON 导出",
        ["SaveCsvExport"] = "保存 CSV 导出",
        ["SaveTotpCsvExport"] = "保存 TOTP CSV",
        ["SaveNoteCsvExport"] = "保存笔记 CSV",
        ["SaveAegisExport"] = "保存 Aegis JSON",
        ["PasswordGenerator"] = "密码生成器",
        ["GeneratedPasswordLabel"] = "生成结果",
        ["GeneratedPasswordPlaceholder"] = "点击生成，或输入密码。",
        ["Generate"] = "生成",
        ["SaveAsLogin"] = "保存为登录项",
        ["GeneratorLength"] = "长度",
        ["GeneratorLengthFormat"] = "长度：{0}",
        ["GeneratorMode"] = "类型",
        ["GeneratorTemplate"] = "模板",
        ["GeneratorWordCount"] = "词数",
        ["GeneratorWordCountFormat"] = "词数：{0}",
        ["GeneratorModeRandom"] = "随机密码",
        ["GeneratorModePassphrase"] = "口令短语",
        ["GeneratorModePin"] = "PIN",
        ["GeneratorModeUsername"] = "用户名",
        ["GeneratorTemplateBalanced"] = "均衡",
        ["GeneratorTemplateMaximum"] = "最高强度",
        ["GeneratorTemplateMemorable"] = "易记",
        ["GeneratorTemplatePin"] = "短 PIN",
        ["GeneratorTemplateUsername"] = "用户名",
        ["GeneratorStrategyLengthFormat"] = "{0} · {1} 个字符",
        ["GeneratorStrategyPassphraseFormat"] = "{0} · {1} 个词",
        ["ExcludeSimilarCharacters"] = "排除相似字符",
        ["RecentGeneratedPasswords"] = "最近生成",
        ["NoGeneratedPasswordHistory"] = "本次会话生成的密码会显示在这里。",
        ["UsePassword"] = "使用密码",
        ["Reset"] = "重置",
        ["ShowPassword"] = "显示密码",
        ["HidePassword"] = "隐藏密码",
        ["AddPasswordRow"] = "添加另一个密码",
        ["PasswordRowCountFormat"] = "{0} 行密码",
        ["IncludeUppercase"] = "包含大写字母",
        ["IncludeLowercase"] = "包含小写字母",
        ["IncludeNumbers"] = "包含数字",
        ["IncludeSymbols"] = "包含符号",
        ["PasswordStrength"] = "密码强度",
        ["PasswordStrengthExcellent"] = "极佳",
        ["PasswordStrengthStrong"] = "强",
        ["PasswordStrengthFair"] = "一般",
        ["PasswordStrengthWeak"] = "弱",
        ["PasswordStrengthVeryWeak"] = "非常弱",
        ["PasswordStrengthWarningShort"] = "密码少于 12 个字符。",
        ["PasswordStrengthWarningMixedCase"] = "同时使用大小写字母。",
        ["PasswordStrengthWarningNumbers"] = "添加数字。",
        ["PasswordStrengthWarningSymbols"] = "添加符号。",
        ["GeneratorNoPassword"] = "生成或输入密码后查看强度。",
        ["GeneratedPasswordStrengthFormat"] = "{0}（{1}/5）。{2}",
        ["GeneratedPasswordRestoredFromHistory"] = "已从历史恢复生成结果",
        ["SecureNotesDescription"] = "笔记以 NOTE 类型存储在 secure_items 中，并共享同一套加密、文件夹、KeePass、Bitwarden 和 MDBX 归属模型。",
        ["CreateSecureItem"] = "创建安全项目",
        ["SettingsSubtitle"] = "配置 Monica 桌面端的行为、安全、外观和集成选项。",
        ["General"] = "通用",
        ["GeneralSettingsDescription"] = "语言、主题和解锁后默认打开的页面。",
        ["Language"] = "语言",
        ["LanguageDescription"] = "选择 Monica 桌面端的显示语言。",
        ["Theme"] = "主题",
        ["ThemeDescription"] = "跟随系统主题，或固定使用浅色、深色、高对比度外观。",
        ["StartupView"] = "启动页",
        ["StartupViewDescription"] = "选择保险库解锁后首先显示的页面。",
        ["Security"] = "安全",
        ["SecuritySettingsDescription"] = "锁定、剪贴板和导出确认相关控制。",
        ["AutoLock"] = "自动锁定",
        ["AutoLockDescription"] = "桌面端空闲一段时间后锁定保险库。",
        ["AutoLockAfter"] = "自动锁定时间",
        ["AutoLockAfterDescription"] = "设置 Monica 在空闲多久后自动锁定保险库。",
        ["ClearClipboard"] = "清空剪贴板",
        ["ClearClipboardDescription"] = "复制密码或动态口令后，按超时时间清空剪贴板。",
        ["ClearClipboardAfter"] = "清空时间",
        ["ClearClipboardAfterDescription"] = "设置敏感内容在剪贴板中保留多久。",
        ["RequirePasswordBeforeExport"] = "导出前要求主密码",
        ["RequirePasswordBeforeExportDescription"] = "准备导出数据前再次验证主密码。",
        ["ChangeMasterPassword"] = "修改主密码",
        ["ChangeMasterPasswordDescription"] = "使用新主密码重新加密本地 Avalonia 保险库。",
        ["CurrentMasterPassword"] = "当前主密码",
        ["NewMasterPassword"] = "新主密码",
        ["ConfirmNewMasterPassword"] = "确认新主密码",
        ["ChangeMasterPasswordAction"] = "更新主密码",
        ["ResetMasterPassword"] = "重设主密码",
        ["ResetMasterPasswordDescription"] = "在保险库已解锁时，通过已配置的密保问题设置新主密码。",
        ["ResetMasterPasswordAction"] = "验证并重设",
        ["EnterCurrentMasterPassword"] = "请输入当前主密码。",
        ["EnterNewMasterPassword"] = "请输入新主密码。",
        ["ChangeMasterPasswordInProgress"] = "正在更新主密码并重新加密保险库数据...",
        ["MasterPasswordChangedFormat"] = "主密码已更新，已重新加密 {0} 个数据库密文。",
        ["ChangeMasterPasswordFailedFormat"] = "主密码更新失败：{0}",
        ["SecurityQuestionAnswersRequired"] = "请输入两个密保答案。",
        ["SecurityQuestionAnswersIncorrect"] = "密保答案不正确。",
        ["ResetMasterPasswordInProgress"] = "正在验证密保答案并重新加密保险库数据...",
        ["ResetMasterPasswordChangedFormat"] = "主密码已重设，已重新加密 {0} 个数据库密文。",
        ["ResetMasterPasswordFailedFormat"] = "主密码重设失败：{0}",
        ["SecurityRecovery"] = "密保问题",
        ["SecurityRecoveryDescription"] = "配置两个找回问题，后续用于支持主密码重置流程。",
        ["SecurityRecoveryEnabled"] = "启用密保问题",
        ["SecurityQuestion1"] = "密保问题 1",
        ["SecurityQuestion2"] = "密保问题 2",
        ["SecurityQuestionAnswer"] = "答案",
        ["CustomSecurityQuestion"] = "自定义问题",
        ["SaveSecurityQuestions"] = "保存密保问题",
        ["SecurityQuestionsConfigured"] = "密保问题已配置。",
        ["SecurityQuestionsNotConfigured"] = "密保问题尚未配置。",
        ["SecurityQuestionsSaved"] = "密保问题已保存。",
        ["SecurityQuestionsDisabled"] = "密保问题已关闭。",
        ["SecurityQuestionsSaveFailedFormat"] = "无法保存密保问题：{0}",
        ["Desktop"] = "桌面",
        ["DesktopSettingsDescription"] = "托盘、快速搜索、浏览器桥接和列表密度设置。",
        ["MinimizeToTray"] = "最小化到托盘",
        ["MinimizeToTrayDescription"] = "关闭窗口时保留后台托盘入口。",
        ["QuickSearch"] = "快速搜索浮层",
        ["QuickSearchDescription"] = "通过桌面浮层快速查找和复制保险库项目。",
        ["QuickSearchHotkey"] = "快速搜索快捷键",
        ["QuickSearchHotkeyDescription"] = "设置唤起快速搜索的全局快捷键。",
        ["BrowserIntegration"] = "浏览器扩展桥接",
        ["BrowserIntegrationDescription"] = "启用桌面端给浏览器扩展使用的本地桥接服务。",
        ["BrowserIntegrationPort"] = "本地桥接端口",
        ["BrowserIntegrationPortDescription"] = "浏览器桥接服务监听的本地端口。",
        ["CompactPasswordList"] = "紧凑密码列表",
        ["CompactPasswordListDescription"] = "让密码列表显示得更紧凑，适合小窗口和高密度浏览。",
        ["PlatformIntegrations"] = "平台集成",
        ["PlatformIntegrationsDescriptionFormat"] = "{0}：{1}/{2} 个桌面集成可用或已有等价能力。",
        ["Integration.browser-bridge.Title"] = "浏览器桥接",
        ["Integration.browser-bridge.Description"] = "用于浏览器扩展和自动填充等价能力的本地桌面桥接。",
        ["Integration.external-links.Title"] = "外部链接",
        ["Integration.external-links.Description"] = "通过桌面 shell 打开项目、帮助和账户链接。",
        ["Integration.file-picker.Title"] = "文件选择器",
        ["Integration.file-picker.Description"] = "用于导入、导出和附件流程的原生或 Avalonia 存储选择器。",
        ["Integration.global-hotkey.Title"] = "全局快捷键",
        ["Integration.global-hotkey.Description"] = "用于快速搜索和后续自动填充入口的系统级快捷键注册。",
        ["Integration.native-notification.Title"] = "原生通知",
        ["Integration.native-notification.Description"] = "用于同步、备份和安全事件的桌面通知。",
        ["Integration.native-passkey.Title"] = "原生 Passkey",
        ["Integration.native-passkey.Description"] = "平台 WebAuthn 或凭据提供程序集成边界。",
        ["Integration.secret-protection.Title"] = "密钥保护",
        ["Integration.secret-protection.Description"] = "用于令牌、同步凭据和本地秘密的系统级保护。",
        ["Integration.tray.Title"] = "系统托盘",
        ["Integration.tray.Description"] = "用于锁定、快速搜索和后台同步的桌面托盘或菜单栏入口。",
        ["Integration.window-security.Title"] = "窗口安全",
        ["Integration.window-security.Description"] = "平台相关的窗口隐私、锁定和截图保护挂钩。",
        ["SyncSubtitle"] = "配置远程同步、备份目标和冲突处理方式。",
        ["WebDav"] = "WebDAV",
        ["EnableWebDav"] = "启用 WebDAV 同步",
        ["EnableWebDavDescription"] = "使用 WebDAV 端点作为 Monica 远程备份和同步目标。",
        ["WebDavServerUrl"] = "服务器地址",
        ["WebDavServerUrlDescription"] = "WebDAV 服务器的 HTTPS 基础地址。",
        ["WebDavUsername"] = "用户名",
        ["WebDavUsernameDescription"] = "Monica 连接 WebDAV 端点时使用的账号名。",
        ["WebDavPassword"] = "密码",
        ["WebDavPasswordDescription"] = "用于 WebDAV Basic 认证的密码或应用密码。",
        ["WebDavRemotePath"] = "远程路径",
        ["WebDavRemotePathDescription"] = "Monica 保存保险库备份文件的远程文件夹路径。",
        ["RemoteSync"] = "远程同步",
        ["RemoteSyncDescription"] = "WebDAV 连接信息和自动同步行为。",
        ["WebDavBackupOptions"] = "备份选项",
        ["WebDavBackupOptionsDescription"] = "选择手动 WebDAV 备份包含哪些 Monica 数据。",
        ["SyncOnStartup"] = "启动时同步",
        ["SyncOnStartupDescription"] = "桌面保险库打开时拉取远程变更。",
        ["SyncAfterChanges"] = "本地变更后同步",
        ["SyncAfterChangesDescription"] = "本地编辑后自动推送保险库变更。",
        ["ConflictStrategy"] = "冲突处理",
        ["ConflictStrategyDescription"] = "远程和本地同时变更时使用的处理策略。",
        ["OneDrive"] = "OneDrive",
        ["EnableOneDrive"] = "启用 OneDrive 接口",
        ["EnableOneDriveDescription"] = "预留基于 Microsoft Graph 的 OneDrive 同步状态。",
        ["MdbxLocalCache"] = "保留 MDBX 本地缓存",
        ["MdbxLocalCacheDescription"] = "为桌面端保险库操作保留本地 MDBX 工作文件。",
        ["CreateMdbxMetadataDescription"] = "为桌面端 MDBX 保险库文件创建本地元数据。",
        ["CloudAndLocalVaults"] = "云端与本地保险库",
        ["CloudAndLocalVaultsDescription"] = "OneDrive 边界状态和 MDBX 本地保险库元数据。",
        ["BackupHistory"] = "备份历史",
        ["BackupNow"] = "立即备份",
        ["RestoreLatest"] = "恢复最新备份",
        ["NoBackupsFound"] = "未找到备份文件。",
        ["Available"] = "可用",
        ["Enabled"] = "已启用",
        ["Disabled"] = "已禁用",
        ["NeedsAttention"] = "需要处理",
        ["DesktopEquivalent"] = "桌面等价",
        ["FeatureParityMapDescription"] = "显示移动端能力在桌面端的当前等价状态。",
        ["PlatformLimited"] = "平台受限",
        ["Unsupported"] = "不支持",
        ["Planned"] = "计划中",
        ["FeatureEnabled"] = "已开启",
        ["FeatureDisabled"] = "已关闭",
        ["Capability.passwords.Title"] = "密码",
        ["Capability.passwords.Description"] = "登录凭据，支持网站、应用绑定、文件夹、收藏、归档、回收站和历史记录。",
        ["Capability.notes.Title"] = "安全笔记",
        ["Capability.notes.Description"] = "加密笔记，以及密码条目的笔记绑定。",
        ["Capability.totp.Title"] = "动态口令",
        ["Capability.totp.Description"] = "支持 TOTP、HOTP 和 Steam 兼容验证器记录，包含二维码导入和复制操作。",
        ["Capability.cards.Title"] = "卡包",
        ["Capability.cards.Description"] = "银行卡、身份证件和图片以安全项目形式保存。",
        ["Capability.passkeys.Title"] = "Passkey",
        ["Capability.passkeys.Description"] = "WebAuthn/FIDO2 元数据，兼容 Bitwarden 和 KeePass 模式。",
        ["Capability.wifi.Title"] = "Wi-Fi",
        ["Capability.wifi.Description"] = "Wi-Fi 密钥以类型化凭据条目保存。",
        ["Capability.ssh.Title"] = "SSH 密钥",
        ["Capability.ssh.Description"] = "结构化 SSH 密钥记录与密码条目一同保存。",
        ["Capability.security-analysis.Title"] = "安全分析",
        ["Capability.security-analysis.Description"] = "弱密码、重复密码和过期密码检查。",
        ["Capability.generator.Title"] = "生成器",
        ["Capability.generator.Description"] = "密码和密码短语生成。",
        ["Capability.import-export.Title"] = "导入 / 导出",
        ["Capability.import-export.Description"] = "Monica JSON、CSV、Bitwarden JSON、KeePass KDBX 和 Aegis 导入导出管线。",
        ["Capability.trash.Title"] = "回收站",
        ["Capability.trash.Description"] = "软删除和恢复流程。",
        ["Capability.timeline.Title"] = "时间线",
        ["Capability.timeline.Description"] = "操作日志和回滚元数据。",
        ["Capability.categories.Title"] = "文件夹",
        ["Capability.categories.Description"] = "本地分类，以及 KeePass、Bitwarden 和 MDBX 归属元数据。",
        ["Capability.customization.Title"] = "个性化",
        ["Capability.customization.Description"] = "页面、卡片、图标和列表自定义入口。",
        ["Capability.plus.Title"] = "Monica Plus",
        ["Capability.plus.Description"] = "与移动端对齐的订阅/状态页面框架。",
        ["Capability.bitwarden.Title"] = "Bitwarden",
        ["Capability.bitwarden.Description"] = "保险库映射和同步服务边界。",
        ["Capability.keepass.Title"] = "KeePass",
        ["Capability.keepass.Description"] = "KDBX 元数据，以及基于库的打开/读取边界。",
        ["Capability.mdbx.Title"] = "MDBX",
        ["Capability.mdbx.Description"] = "保险库创建、打开、同步元数据和本地文件流管理。",
        ["Capability.webdav.Title"] = "WebDAV",
        ["Capability.webdav.Description"] = "远程备份和同步路径处理。",
        ["Capability.onedrive.Title"] = "OneDrive",
        ["Capability.onedrive.Description"] = "Microsoft Graph/MSAL 服务边界。",
        ["Capability.autofill.Title"] = "桌面自动填充",
        ["Capability.autofill.Description"] = "Android 自动填充、输入法和无障碍能力在桌面端转换为快速搜索、剪贴板、托盘和浏览器扩展桥接。",
        ["Capability.credential-provider.Title"] = "凭据提供程序",
        ["Capability.credential-provider.Description"] = "Android 凭据提供程序的桌面等价能力依赖具体平台，因此显示为受限状态。",
        ["SystemDefault"] = "跟随系统",
        ["English"] = "英语",
        ["SimplifiedChinese"] = "简体中文",
        ["Light"] = "浅色",
        ["Dark"] = "深色",
        ["HighContrast"] = "高对比度",
        ["AskEveryTime"] = "每次询问",
        ["LocalWins"] = "本地优先",
        ["RemoteWins"] = "远端优先",
        ["MinuteFormat"] = "{0} 分钟",
        ["SecondFormat"] = "{0} 秒",
        ["PasswordCountFormat"] = "{0} 项",
        ["ArchivedPasswordCountFormat"] = "{0} 个已归档密码",
        ["DeletedPasswordCountFormat"] = "{0} 个已删除密码",
        ["TimelineCountFormat"] = "{0} 条事件",
        ["DatabaseSummaryFormat"] = "{0} 个密码、{1} 条笔记、{2} 个验证器、{3} 个卡包项目",
        ["MdbxDatabaseCountFormat"] = "{0} 条 MDBX 元数据",
        ["MdbxSourceCountFormat"] = "{0} 个保险库",
        ["MdbxWorkingCopyCountFormat"] = "{0} 个就绪",
        ["MdbxRemoteSourceCountFormat"] = "{0} 个远程",
        ["MdbxDefaultVault"] = "默认保险库",
        ["MdbxWorkingCopies"] = "工作副本",
        ["MdbxRemoteSources"] = "远程来源",
        ["MdbxDefaultVaultFormat"] = "默认：{0}",
        ["MdbxDefaultVaultMissing"] = "尚未设置默认保险库",
        ["MdbxNoWorkingCopies"] = "还没有可用的 MDBX 工作副本。",
        ["MdbxWorkingCopySummaryFormat"] = "{0}/{1} 个工作副本文件已就绪；{2} 个可离线使用。",
        ["MdbxRemoteSourceEmpty"] = "还没有登记远程 MDBX 来源。",
        ["MdbxRemoteSummaryFormat"] = "{0} 个远程来源，{1} 个等待同步。",
        ["MdbxSyncErrorsFormat"] = "{0} 个 MDBX 同步问题需要处理。",
        ["MdbxPendingSyncFormat"] = "{0} 个 MDBX 来源正在等待同步。",
        ["MdbxNoSyncErrors"] = "没有记录到 MDBX 同步错误。",
        ["MdbxCacheEnabled"] = "已保留本地 MDBX 缓存，用于桌面端操作。",
        ["MdbxCacheDisabled"] = "本地 MDBX 缓存已关闭；远程来源可能需要重建工作副本。",
        ["MdbxWorkingCopyReady"] = "工作副本就绪",
        ["MdbxWorkingCopyMissing"] = "缺少工作副本",
        ["MdbxRemoteStatusFormat"] = "{0}：{1}",
        ["MdbxLocalSourceReadyFormat"] = "{0} 个本地 MDBX 保险库已就绪。",
        ["MdbxLocalSourceEmpty"] = "创建本地 MDBX 工作副本，用于桌面保险库操作。",
        ["MdbxWebDavSourceReadyFormat"] = "已登记 {0} 个 WebDAV MDBX 来源。",
        ["MdbxWebDavSourceEmpty"] = "WebDAV 已启用；可以为该远程路径登记 MDBX 来源。",
        ["MdbxOneDriveSourceReadyFormat"] = "已登记 {0} 个 OneDrive MDBX 来源。",
        ["MdbxOneDriveSourceEmpty"] = "OneDrive 已启用；可以登记用于 Microsoft Graph 同步的 MDBX 来源。",
        ["MdbxRuntimeSummary"] = "本地与远程来源的 MDBX 工作副本已可承载业务数据；远程上传、提交历史和冲突处理仍需要完整 MDBX 同步引擎移植。",
        ["MdbxSecuritySummary"] = "MDBX 密钥类字段通过统一保险库加密层保存；文件内容加密仍取决于 MDBX 引擎实现。",
        ["MdbxSourceLocal"] = "本地",
        ["MdbxSourceExternal"] = "外部",
        ["MdbxNoDescription"] = "无描述",
        ["Never"] = "从未",
        ["MdbxLocalVaultName"] = "本地 Monica 保险库",
        ["MdbxWebDavVaultName"] = "WebDAV Monica 保险库",
        ["MdbxOneDriveVaultName"] = "OneDrive Monica 保险库",
        ["MdbxLocalMetadataDescription"] = "本地桌面 MDBX 元数据和工作副本。",
        ["MdbxWebDavMetadataDescription"] = "WebDAV 远程来源的本地 MDBX 工作副本，等待后续远程同步引擎上传。",
        ["MdbxOneDriveMetadataDescription"] = "OneDrive 远程来源的本地 MDBX 工作副本，等待后续 Microsoft Graph 同步引擎上传。",
        ["MdbxMetadataAlreadyRegisteredFormat"] = "{0} 已经登记。",
        ["CreatedMdbxWebDavMetadata"] = "已创建 WebDAV MDBX 工作副本并登记远程来源元数据。",
        ["CreatedMdbxOneDriveMetadata"] = "已创建 OneDrive MDBX 工作副本并登记远程来源元数据。",
        ["EnableOneDriveFirst"] = "请先启用 OneDrive。",
        ["MdbxVaultsRefreshed"] = "MDBX 保险库元数据已刷新。",
        ["MdbxRemoteOpenPending"] = "该 MDBX 来源还没有本地工作副本。",
        ["OpenedMdbxDatabaseFormat"] = "已打开 {0}；本地文件大小为 {1} 字节。",
        ["SelectedMdbxDefaultFormat"] = "{0} 已设为默认 MDBX 保险库。",
        ["ConfigureMdbxRemoteSourcesHint"] = "登记远程 MDBX 来源前，请先配置 WebDAV 或 OneDrive。",
        ["VaultSourceCountFormat"] = "{0} 个已登记来源",
        ["WebDavConfiguredFormat"] = "已配置到 {0}",
        ["WebDavDisabled"] = "WebDAV 已禁用。本地保险库操作仍可使用。",
        ["SyncStatusSummaryFormat"] = "{0}；历史中已加载 {1} 个备份文件。",
        ["SyncStatusLocalOnly"] = "远程同步已关闭。本地保险库操作仍可使用。",
        ["SyncConfigurationDisabled"] = "启用 WebDAV 后才能配置远程同步。",
        ["SyncConfigurationReadyFormat"] = "WebDAV 已配置到远程路径 {0}。",
        ["SyncConfigurationIncomplete"] = "WebDAV 已启用，但服务器地址还不完整。",
        ["SyncRecoveryLocalOnly"] = "当前没有启用远程恢复来源。",
        ["SyncRecoveryNoBackupsLoaded"] = "加载备份历史或创建备份后，才能形成恢复点。",
        ["SyncRecoveryBackupReadyFormat"] = "已有 {0} 个备份文件可用于恢复。",
        ["OneDriveBoundaryEnabled"] = "OneDrive 接口已启用",
        ["OneDriveBoundaryDescription"] = "OneDrive 当前用于 Microsoft Graph MDBX 同步元数据边界。",
        ["WebDavConnectionTestSucceededFormat"] = "WebDAV 连接测试成功；可见 {0} 个远程项目。",
        ["WebDavConnectionTestFailedFormat"] = "WebDAV 连接测试失败：{0}",
        ["EnableWebDavFirst"] = "请先启用 WebDAV 并配置服务器，再加载备份。",
        ["WebDavServerUrlRequired"] = "请输入有效的 WebDAV 服务器地址。",
        ["WebDavBackupHistoryCountFormat"] = "{0} 个备份文件",
        ["LoadedWebDavBackupsFormat"] = "已加载 {0} 个 WebDAV 备份文件。",
        ["WebDavBackupHistoryFailedFormat"] = "WebDAV 备份历史加载失败：{0}",
        ["DeletedWebDavBackupFormat"] = "已删除 WebDAV 备份 {0}。",
        ["DeleteWebDavBackupFailedFormat"] = "删除 WebDAV 备份失败：{0}",
        ["UnknownDate"] = "未知日期",
        ["UnknownSize"] = "未知大小",
        ["CanonicalVault"] = "Monica v69 SQLite 主保险库",
        ["LocalOnly"] = "仅本地",
        ["NotConfigured"] = "未配置",
        ["KeePassSourceNameFormat"] = "KeePass 来源 #{0}",
        ["BitwardenSourceNameFormat"] = "Bitwarden 来源 #{0}",
        ["EntryCountFormat"] = "{0} 条项目记录",
        ["PendingSyncCountFormat"] = "{0} 条本地待同步变更",
        ["NoPendingChanges"] = "没有待同步变更",
        ["AutomaticSync"] = "自动同步",
        ["StartupSync"] = "启动时同步",
        ["ChangeSync"] = "变更后同步",
        ["ManualSync"] = "手动同步",
        ["Synced"] = "已同步",
        ["Syncing"] = "同步中",
        ["Pending"] = "待处理",
        ["PendingUpload"] = "待上传",
        ["RemoteChanged"] = "远端已变更",
        ["Conflict"] = "冲突",
        ["Failed"] = "失败",
        ["None"] = "无",
        ["TotpCountFormat"] = "{0} 个验证器",
        ["WalletCountFormat"] = "{0} 张卡片与证件",
        ["Locked"] = "已锁定",
        ["LockVault"] = "锁定保险库",
        ["VaultLocked"] = "保险库已锁定",
        ["WebDavEncryptionRequired"] = "包含保险库数据的 WebDAV 备份必须加密。",
        ["WebDavHttpsRequired"] = "WebDAV 必须使用 HTTPS 地址，且地址中不能包含凭据。",
        ["AuthorizeExportTitle"] = "授权敏感数据导出",
        ["AuthorizeExportDescription"] = "导出已解密的保险库数据前，请输入主密码。",
        ["AuthorizeExportAction"] = "授权导出",
        ["ExportAuthorizationFailed"] = "导出授权已取消或主密码错误。",
        ["FirstRunCreateMasterPassword"] = "首次运行：请创建主密码。",
        ["LegacyVaultImportRequired"] = "检测到 Monica for Windows 旧保险库。Avalonia 使用此路径前需要先执行导入。",
        ["LegacyVaultImportPromptFormat"] = "在 {0} 发现旧版数据。Monica by Avalonia 不会自动修改这个 PascalCase 数据库；需要先通过一次性导入流程迁移到 v69 snake_case 架构。",
        ["VaultMetadataLoadFailedFormat"] = "无法加载保险库元数据：{0}",
        ["SettingsLoaded"] = "设置已加载",
        ["SettingsSaved"] = "设置已保存",
        ["SettingsSaveFailedFormat"] = "无法保存设置：{0}",
        ["EnterMasterPassword"] = "请输入主密码。",
        ["MasterPasswordMinLength"] = "主密码至少需要 8 个字符。",
        ["ConfirmationMismatch"] = "两次输入的主密码不一致。",
        ["WrongMasterPassword"] = "主密码错误。",
        ["VaultUnlocked"] = "保险库已解锁",
        ["VaultUnlockedLegacyBusinessDataPending"] = "MDBX 已启用。检测到旧版桌面 SQLite 数据，现已保持原样且未自动导入；迁移工具可用前请保留旧数据。",
        ["UnlockFailedFormat"] = "解锁失败：{0}",
        ["VaultLoadFailedFormat"] = "保险库加载失败：{0}",
        ["CreatedPasswordFormat"] = "已创建 {0}",
        ["UpdatedPasswordFormat"] = "已更新 {0}",
        ["CopiedPasswordFormat"] = "已复制 {0} 的密码",
        ["CopiedTotpFormat"] = "已复制 {0} 的动态口令",
        ["CopiedWalletFieldFormat"] = "已复制{0}",
        ["MovedToRecycleBinFormat"] = "已将 {0} 移到回收站",
        ["EmptiedRecycleBinFormat"] = "已永久删除回收站中的 {0} 个项目",
        ["GeneratedPassword"] = "已生成密码",
        ["ExportPrepared"] = "已准备 Monica JSON 导出预览",
        ["ImportJsonRequired"] = "请先粘贴 Monica JSON 再导入。",
        ["ImportedMonicaJsonFormat"] = "已导入 {0} 个密码和 {1} 个安全项目。",
        ["ImportedMonicaJsonWithCategoriesFormat"] = "已导入 {0} 个密码、{1} 个安全项目和 {2} 个文件夹。",
        ["ImportAegisJsonRequired"] = "请先粘贴 Aegis JSON 再导入。",
        ["ImportedAegisJsonFormat"] = "已从 Aegis JSON 导入 {0} 个验证器，跳过 {1} 个重复项。",
        ["ImportTotpCsvRequired"] = "请先粘贴 TOTP CSV 再导入。",
        ["ImportedTotpCsvFormat"] = "已从 TOTP CSV 导入 {0} 个验证器，跳过 {1} 个重复项。",
        ["ImportNoteCsvRequired"] = "请先粘贴笔记 CSV 再导入。",
        ["ImportedNoteCsvFormat"] = "已从笔记 CSV 导入 {0} 个笔记，跳过 {1} 个重复项。",
        ["ImportCsvRequired"] = "请先粘贴密码 CSV 再导入。",
        ["ImportedPasswordCsvFormat"] = "已从 CSV 导入 {0} 个密码。",
        ["ExportedPasswordCsv"] = "已准备密码 CSV 导出预览",
        ["ExportedTotpCsv"] = "已准备 TOTP CSV 导出预览",
        ["ExportedNoteCsv"] = "已准备笔记 CSV 导出预览",
        ["ExportedTimelineFormat"] = "已导出 {0} 条时间线记录",
        ["TimelineExportEmpty"] = "没有可导出的时间线记录",
        ["ExportedAegisJson"] = "已准备 Aegis JSON 导出预览",
        ["SavedExportFileFormat"] = "已保存导出到 {0}。",
        ["SaveExportFileFailedFormat"] = "保存导出失败：{0}",
        ["ImportFailedFormat"] = "导入失败：{0}",
        ["MonicaJson"] = "Monica JSON",
        ["AegisJson"] = "Aegis JSON",
        ["TotpCsv"] = "TOTP CSV",
        ["NoteCsv"] = "笔记 CSV",
        ["PasswordCsv"] = "密码 CSV",
        ["CreatedMdbxMetadata"] = "已创建真实 MDBX-1 保险库并登记本地元数据"
    };
}
