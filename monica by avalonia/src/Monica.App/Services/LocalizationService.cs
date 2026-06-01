using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Monica.App.Services;

public interface ILocalizationService : INotifyPropertyChanged
{
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
    string Timeline { get; }
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
    string Settings { get; }
    string Folders { get; }
    string Personal { get; }
    string AllFolders { get; }
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
    string RestorePassword { get; }
    string DeletePermanently { get; }
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
    string LocalDatabase { get; }
    string LocalDatabaseDescription { get; }
    string ExternalDatabases { get; }
    string ExternalDatabasesDescription { get; }
    string MdbxDatabaseCount { get; }
    string RegisteredDatabases { get; }
    string WebDavConnection { get; }
    string FeatureParityMap { get; }
    string FeatureParityMapDescription { get; }
    string ExportPreview { get; }
    string ImportMonicaJson { get; }
    string ImportMonicaJsonDescription { get; }
    string ImportJsonWatermark { get; }
    string ImportPasswordCsv { get; }
    string ImportPasswordCsvDescription { get; }
    string ImportCsvWatermark { get; }
    string ExportPasswordCsv { get; }
    string ExportCsvPreview { get; }
    string Import { get; }
    string ImportFromFile { get; }
    string SaveJsonExport { get; }
    string SaveCsvExport { get; }
    string PasswordGenerator { get; }
    string Generate { get; }
    string SaveAsLogin { get; }
    string GeneratorLength { get; }
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
    public string Timeline => Text();
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
    public string Settings => Text();
    public string Folders => Text();
    public string Personal => Text();
    public string AllFolders => Text();
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
    public string RestorePassword => Text();
    public string DeletePermanently => Text();
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
    public string LocalDatabase => Text();
    public string LocalDatabaseDescription => Text();
    public string ExternalDatabases => Text();
    public string ExternalDatabasesDescription => Text();
    public string MdbxDatabaseCount => Text();
    public string RegisteredDatabases => Text();
    public string WebDavConnection => Text();
    public string FeatureParityMap => Text();
    public string FeatureParityMapDescription => Text();
    public string ExportPreview => Text();
    public string ImportMonicaJson => Text();
    public string ImportMonicaJsonDescription => Text();
    public string ImportJsonWatermark => Text();
    public string ImportPasswordCsv => Text();
    public string ImportPasswordCsvDescription => Text();
    public string ImportCsvWatermark => Text();
    public string ExportPasswordCsv => Text();
    public string ExportCsvPreview => Text();
    public string Import => Text();
    public string ImportFromFile => Text();
    public string SaveJsonExport => Text();
    public string SaveCsvExport => Text();
    public string PasswordGenerator => Text();
    public string Generate => Text();
    public string SaveAsLogin => Text();
    public string GeneratorLength => Text();
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
        ["Timeline"] = "Timeline",
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
        ["Settings"] = "Settings",
        ["Folders"] = "Folders",
        ["Personal"] = "Personal",
        ["AllFolders"] = "All folders",
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
        ["PasswordManager"] = "Password Manager",
        ["DeletedPasswords"] = "Deleted Passwords",
        ["Search"] = "Search...",
        ["AddPassword"] = "Add Password",
        ["EditPassword"] = "Edit Password",
        ["PasswordDetails"] = "Password Details",
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
        ["QuickFilterFavorite"] = "Favorites",
        ["QuickFilter2Fa"] = "2FA",
        ["QuickFilterNotes"] = "Notes",
        ["QuickFilterPasskey"] = "Passkeys",
        ["QuickFilterBoundNote"] = "Bound note",
        ["QuickFilterUncategorized"] = "Uncategorized",
        ["QuickFilterLocalOnly"] = "Local only",
        ["QuickFilterAttachments"] = "Attachments",
        ["QuickAccessRecent"] = "Recently opened",
        ["QuickAccessFrequent"] = "Frequently opened",
        ["SortPasswords"] = "Sort passwords",
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
        ["CreatedAt"] = "Created",
        ["UpdatedAt"] = "Updated",
        ["Close"] = "Close",
        ["TwoStepVerification"] = "Two-Step Verification",
        ["AddAuthenticator"] = "Add Authenticator",
        ["EditAuthenticator"] = "Edit Authenticator",
        ["TotpPageDescription"] = "TOTP authenticators with copy, edit, favorite, delete, context menu, and batch actions.",
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
        ["LocalDatabase"] = "Local database",
        ["LocalDatabaseDescription"] = "Avalonia uses the Monica v68 SQLite schema as the canonical desktop vault.",
        ["ExternalDatabases"] = "External databases",
        ["ExternalDatabasesDescription"] = "KeePass KDBX, MDBX, Bitwarden and WebDAV sources are exposed through platform-neutral services.",
        ["MdbxDatabaseCount"] = "MDBX vault metadata",
        ["RegisteredDatabases"] = "Registered databases",
        ["WebDavConnection"] = "WebDAV connection",
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
        ["ClearVaultDataDescription"] = "Delete passwords, secure items, or the full local Avalonia v68 vault data set. The master password record is kept.",
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
        ["ImportPasswordCsv"] = "Import Password CSV",
        ["ImportPasswordCsvDescription"] = "Paste a password CSV from Monica, Bitwarden-style exports or another manager. Passwords are encrypted before they are saved.",
        ["ImportCsvWatermark"] = "Paste password CSV here",
        ["ExportPasswordCsv"] = "Export Password CSV",
        ["ExportCsvPreview"] = "Password CSV Preview",
        ["Import"] = "Import",
        ["ImportFromFile"] = "Import from file",
        ["SaveJsonExport"] = "Save JSON export",
        ["SaveCsvExport"] = "Save CSV export",
        ["PasswordGenerator"] = "Password Generator",
        ["Generate"] = "Generate",
        ["SaveAsLogin"] = "Save as Login",
        ["GeneratorLength"] = "Length",
        ["GeneratorLengthFormat"] = "Length: {0}",
        ["ShowPassword"] = "Show password",
        ["HidePassword"] = "Hide password",
        ["AddPasswordRow"] = "Add another password",
        ["PasswordRowCountFormat"] = "{0} password row(s)",
        ["IncludeUppercase"] = "Include uppercase",
        ["IncludeLowercase"] = "Include lowercase",
        ["IncludeNumbers"] = "Include numbers",
        ["IncludeSymbols"] = "Include symbols",
        ["PasswordStrength"] = "Password strength",
        ["GeneratorNoPassword"] = "Generate a password to see its strength.",
        ["GeneratedPasswordStrengthFormat"] = "{0} ({1}/5). {2}",
        ["CopiedGeneratedPassword"] = "Copied generated password",
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
        ["ThemeDescription"] = "Follow the system theme or force a light or dark appearance.",
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
        ["AskEveryTime"] = "Ask every time",
        ["LocalWins"] = "Local wins",
        ["RemoteWins"] = "Remote wins",
        ["MinuteFormat"] = "{0} min",
        ["SecondFormat"] = "{0} sec",
        ["PasswordCountFormat"] = "{0} items",
        ["DatabaseSummaryFormat"] = "{0} passwords, {1} notes, {2} authenticators, {3} wallet items",
        ["MdbxDatabaseCountFormat"] = "{0} MDBX metadata record(s)",
        ["VaultSourceCountFormat"] = "{0} registered source(s)",
        ["WebDavConfiguredFormat"] = "Configured for {0}",
        ["WebDavDisabled"] = "WebDAV is disabled. Local vault operations remain available.",
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
        ["CanonicalVault"] = "Monica v68 SQLite canonical vault",
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
        ["VaultLocked"] = "Vault locked",
        ["FirstRunCreateMasterPassword"] = "First run: create a master password.",
        ["LegacyVaultImportRequired"] = "A Monica for Windows vault was detected. Import is required before Avalonia can use this path.",
        ["LegacyVaultImportPromptFormat"] = "Found legacy data at {0}. Monica by Avalonia will not modify this PascalCase database automatically. A one-time import flow must migrate it into the v68 snake_case schema first.",
        ["VaultMetadataLoadFailedFormat"] = "Vault metadata could not be loaded: {0}",
        ["SettingsLoaded"] = "Settings loaded",
        ["SettingsSaved"] = "Settings saved",
        ["EnterMasterPassword"] = "Enter a master password.",
        ["MasterPasswordMinLength"] = "Use at least 8 characters for the master password.",
        ["ConfirmationMismatch"] = "The confirmation password does not match.",
        ["WrongMasterPassword"] = "Wrong master password.",
        ["VaultUnlocked"] = "Vault unlocked",
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
        ["ImportCsvRequired"] = "Paste password CSV before importing.",
        ["ImportedPasswordCsvFormat"] = "Imported {0} passwords from CSV.",
        ["ExportedPasswordCsv"] = "Prepared password CSV export preview",
        ["SavedExportFileFormat"] = "Saved export to {0}.",
        ["SaveExportFileFailedFormat"] = "Save export failed: {0}",
        ["ImportFailedFormat"] = "Import failed: {0}",
        ["MonicaJson"] = "Monica JSON",
        ["PasswordCsv"] = "Password CSV",
        ["CreatedMdbxMetadata"] = "Created MDBX metadata and local working file path"
    };

    private static readonly Dictionary<string, string> Chinese = new()
    {
        ["Passwords"] = "密码",
        ["SecureNotes"] = "安全笔记",
        ["Totp"] = "动态口令",
        ["Cards"] = "卡包",
        ["Generator"] = "生成器",
        ["SyncAndBackup"] = "同步与备份",
        ["DatabaseManagement"] = "数据库管理",
        ["Settings"] = "设置",
        ["Folders"] = "文件夹",
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
        ["PasswordManager"] = "密码管理",
        ["Search"] = "搜索...",
        ["AddPassword"] = "添加密码",
        ["EditPassword"] = "编辑密码",
        ["Favorite"] = "收藏",
        ["CopyPassword"] = "复制密码",
        ["MoveToRecycleBin"] = "移到回收站",
        ["Save"] = "保存",
        ["Cancel"] = "取消",
        ["NoFolder"] = "无文件夹",
        ["NewPassword"] = "新建密码",
        ["PasswordTitleRequired"] = "请输入密码标题。",
        ["PasswordValueRequired"] = "请输入密码内容。",
        ["PasswordTitle"] = "标题",
        ["Website"] = "网站",
        ["Username"] = "用户名",
        ["Password"] = "密码",
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
        ["Notes"] = "备注",
        ["TwoStepVerification"] = "两步验证",
        ["AddAuthenticator"] = "添加验证器",
        ["CopyCode"] = "复制验证码",
        ["Wallet"] = "卡包",
        ["AddItem"] = "添加项目",
        ["DesktopEquivalents"] = "桌面等价能力",
        ["DesktopEquivalentsMessage"] = "Android 的自动填充、输入法、无障碍和凭据提供程序能力，在桌面端通过快速搜索、剪贴板、托盘/浏览器扩展接口或平台受限状态呈现。",
        ["CreateMdbxMetadata"] = "创建 MDBX 元数据",
        ["LocalDatabase"] = "本地数据库",
        ["LocalDatabaseDescription"] = "Avalonia 使用 Monica v68 SQLite 架构作为桌面端主保险库。",
        ["ExternalDatabases"] = "外部数据库",
        ["ExternalDatabasesDescription"] = "KeePass KDBX、MDBX、Bitwarden 与 WebDAV 来源通过平台无关服务接入。",
        ["MdbxDatabaseCount"] = "MDBX 保险库元数据",
        ["RegisteredDatabases"] = "已登记数据库",
        ["WebDavConnection"] = "WebDAV 连接",
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
        ["ClearVaultDataDescription"] = "删除密码、安全项目或完整的本地 Avalonia v68 保险库数据；主密码记录会保留。",
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
        ["ImportPasswordCsv"] = "导入密码 CSV",
        ["ImportPasswordCsvDescription"] = "选择或粘贴 Monica、Bitwarden 风格或其他密码管理器的 CSV。保存前会加密密码。",
        ["ImportCsvWatermark"] = "在此粘贴密码 CSV",
        ["ExportPasswordCsv"] = "导出密码 CSV",
        ["ExportCsvPreview"] = "密码 CSV 预览",
        ["Import"] = "导入",
        ["ImportFromFile"] = "从文件导入",
        ["SaveJsonExport"] = "保存 JSON 导出",
        ["SaveCsvExport"] = "保存 CSV 导出",
        ["PasswordGenerator"] = "密码生成器",
        ["Generate"] = "生成",
        ["SaveAsLogin"] = "保存为登录项",
        ["GeneratorLength"] = "长度",
        ["GeneratorLengthFormat"] = "长度：{0}",
        ["ShowPassword"] = "显示密码",
        ["HidePassword"] = "隐藏密码",
        ["AddPasswordRow"] = "添加另一个密码",
        ["PasswordRowCountFormat"] = "{0} 行密码",
        ["IncludeUppercase"] = "包含大写字母",
        ["IncludeLowercase"] = "包含小写字母",
        ["IncludeNumbers"] = "包含数字",
        ["IncludeSymbols"] = "包含符号",
        ["PasswordStrength"] = "密码强度",
        ["GeneratorNoPassword"] = "生成或输入密码后查看强度。",
        ["GeneratedPasswordStrengthFormat"] = "{0}（{1}/5）。{2}",
        ["SecureNotesDescription"] = "笔记以 NOTE 类型存储在 secure_items 中，并共享同一套加密、文件夹、KeePass、Bitwarden 和 MDBX 归属模型。",
        ["CreateSecureItem"] = "创建安全项目",
        ["SettingsSubtitle"] = "配置 Monica 桌面端的行为、安全、外观和集成选项。",
        ["General"] = "通用",
        ["Language"] = "语言",
        ["Theme"] = "主题",
        ["StartupView"] = "启动页",
        ["Security"] = "安全",
        ["AutoLock"] = "自动锁定",
        ["AutoLockDescription"] = "桌面端空闲一段时间后锁定保险库。",
        ["AutoLockAfter"] = "自动锁定时间",
        ["ClearClipboard"] = "清空剪贴板",
        ["ClearClipboardDescription"] = "复制密码或动态口令后，按超时时间清空剪贴板。",
        ["ClearClipboardAfter"] = "清空时间",
        ["RequirePasswordBeforeExport"] = "导出前要求主密码",
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
        ["MinimizeToTray"] = "最小化到托盘",
        ["QuickSearch"] = "快速搜索浮层",
        ["QuickSearchHotkey"] = "快速搜索快捷键",
        ["BrowserIntegration"] = "浏览器扩展桥接",
        ["BrowserIntegrationPort"] = "本地桥接端口",
        ["CompactPasswordList"] = "紧凑密码列表",
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
        ["WebDavServerUrl"] = "服务器地址",
        ["WebDavUsername"] = "用户名",
        ["WebDavPassword"] = "密码",
        ["WebDavPasswordDescription"] = "用于 WebDAV Basic 认证的密码或应用密码。",
        ["WebDavRemotePath"] = "远程路径",
        ["SyncOnStartup"] = "启动时同步",
        ["SyncAfterChanges"] = "本地变更后同步",
        ["ConflictStrategy"] = "冲突处理",
        ["OneDrive"] = "OneDrive",
        ["EnableOneDrive"] = "启用 OneDrive 接口",
        ["MdbxLocalCache"] = "保留 MDBX 本地缓存",
        ["BackupHistory"] = "备份历史",
        ["NoBackupsFound"] = "未找到备份文件。",
        ["Available"] = "可用",
        ["DesktopEquivalent"] = "桌面等价",
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
        ["AskEveryTime"] = "每次询问",
        ["LocalWins"] = "本地优先",
        ["RemoteWins"] = "远端优先",
        ["MinuteFormat"] = "{0} 分钟",
        ["SecondFormat"] = "{0} 秒",
        ["PasswordCountFormat"] = "{0} 项",
        ["DatabaseSummaryFormat"] = "{0} 个密码、{1} 条笔记、{2} 个验证器、{3} 个卡包项目",
        ["MdbxDatabaseCountFormat"] = "{0} 条 MDBX 元数据",
        ["VaultSourceCountFormat"] = "{0} 个已登记来源",
        ["WebDavConfiguredFormat"] = "已配置到 {0}",
        ["WebDavDisabled"] = "WebDAV 已禁用。本地保险库操作仍可使用。",
        ["EnableWebDavFirst"] = "请先启用 WebDAV 并配置服务器，再加载备份。",
        ["WebDavServerUrlRequired"] = "请输入有效的 WebDAV 服务器地址。",
        ["WebDavBackupHistoryCountFormat"] = "{0} 个备份文件",
        ["LoadedWebDavBackupsFormat"] = "已加载 {0} 个 WebDAV 备份文件。",
        ["WebDavBackupHistoryFailedFormat"] = "WebDAV 备份历史加载失败：{0}",
        ["DeletedWebDavBackupFormat"] = "已删除 WebDAV 备份 {0}。",
        ["DeleteWebDavBackupFailedFormat"] = "删除 WebDAV 备份失败：{0}",
        ["UnknownDate"] = "未知日期",
        ["UnknownSize"] = "未知大小",
        ["CanonicalVault"] = "Monica v68 SQLite 主保险库",
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
        ["VaultLocked"] = "保险库已锁定",
        ["FirstRunCreateMasterPassword"] = "首次运行：请创建主密码。",
        ["LegacyVaultImportRequired"] = "检测到 Monica for Windows 旧保险库。Avalonia 使用此路径前需要先执行导入。",
        ["LegacyVaultImportPromptFormat"] = "在 {0} 发现旧版数据。Monica by Avalonia 不会自动修改这个 PascalCase 数据库；需要先通过一次性导入流程迁移到 v68 snake_case 架构。",
        ["VaultMetadataLoadFailedFormat"] = "无法加载保险库元数据：{0}",
        ["SettingsLoaded"] = "设置已加载",
        ["SettingsSaved"] = "设置已保存",
        ["EnterMasterPassword"] = "请输入主密码。",
        ["MasterPasswordMinLength"] = "主密码至少需要 8 个字符。",
        ["ConfirmationMismatch"] = "两次输入的主密码不一致。",
        ["WrongMasterPassword"] = "主密码错误。",
        ["VaultUnlocked"] = "保险库已解锁",
        ["UnlockFailedFormat"] = "解锁失败：{0}",
        ["VaultLoadFailedFormat"] = "保险库加载失败：{0}",
        ["CreatedPasswordFormat"] = "已创建 {0}",
        ["UpdatedPasswordFormat"] = "已更新 {0}",
        ["CopiedPasswordFormat"] = "已复制 {0} 的密码",
        ["CopiedTotpFormat"] = "已复制 {0} 的动态口令",
        ["MovedToRecycleBinFormat"] = "已将 {0} 移到回收站",
        ["GeneratedPassword"] = "已生成密码",
        ["ExportPrepared"] = "已准备 Monica JSON 导出预览",
        ["ImportJsonRequired"] = "请先粘贴 Monica JSON 再导入。",
        ["ImportedMonicaJsonFormat"] = "已导入 {0} 个密码和 {1} 个安全项目。",
        ["ImportedMonicaJsonWithCategoriesFormat"] = "已导入 {0} 个密码、{1} 个安全项目和 {2} 个文件夹。",
        ["ImportCsvRequired"] = "请先粘贴密码 CSV 再导入。",
        ["ImportedPasswordCsvFormat"] = "已从 CSV 导入 {0} 个密码。",
        ["ExportedPasswordCsv"] = "已准备密码 CSV 导出预览",
        ["SavedExportFileFormat"] = "已保存导出到 {0}。",
        ["SaveExportFileFailedFormat"] = "保存导出失败：{0}",
        ["ImportFailedFormat"] = "导入失败：{0}",
        ["MonicaJson"] = "Monica JSON",
        ["PasswordCsv"] = "密码 CSV",
        ["CreatedMdbxMetadata"] = "已创建 MDBX 元数据和本地工作文件路径"
    };
}
