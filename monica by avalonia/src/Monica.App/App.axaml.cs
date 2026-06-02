using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.ImportExport;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Mdbx;
using Monica.Data.Repositories;
using Monica.Data.Services;
using Monica.Platform.Services;

namespace Monica.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = new MainWindow();
            _services = ConfigureServices(_mainWindow);
            _mainWindow.DataContext = _services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = _mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices(MainWindow mainWindow)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<ILegacyVaultDetector, LegacyVaultDetector>();
        services.AddSingleton<IDatabaseMigrator, DatabaseMigrator>();
        services.AddSingleton<IVaultCredentialStore, VaultCredentialStore>();
        services.AddSingleton<MonicaRepository>();
        services.AddSingleton<IMdbxNativeBridge, MdbxUniffiNativeBridge>();
        services.AddSingleton<IMdbxVaultStore, MdbxVaultStore>();
        services.AddSingleton<IMonicaRepository>(provider => new MdbxBackedMonicaRepository(
            provider.GetRequiredService<MonicaRepository>(),
            provider.GetRequiredService<IMdbxVaultStore>()));
        services.AddSingleton<IMasterPasswordMaintenanceService, MasterPasswordMaintenanceService>();
        services.AddSingleton<ICryptoService, CryptoService>();
        services.AddSingleton<IVaultDataProtector, VaultDataProtector>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<IPasswordGeneratorService, PasswordGeneratorService>();
        services.AddSingleton<IPwnedPasswordService, PwnedPasswordService>();
        services.AddSingleton<IImportExportService, ImportExportService>();
        services.AddSingleton<IPlatformIntegrationService, PlatformIntegrationService>();
        services.AddSingleton<IPlatformCapabilityService, PlatformCapabilityService>();
        services.AddSingleton<ISecretProtector>(provider =>
            SecretProtectorFactory.Create(provider.GetRequiredService<IPlatformIntegrationService>()));
        services.AddSingleton<IFileSystemPickerService>(_ => new AvaloniaFileSystemPickerService(
            () => mainWindow,
            _.GetRequiredService<IPlatformIntegrationService>()));
        services.AddSingleton<IBrowserBridgeService, CapabilityOnlyBrowserBridgeService>();
        services.AddSingleton<INativePasskeyService, CapabilityOnlyNativePasskeyService>();
        services.AddSingleton<ITrayService, CapabilityOnlyTrayService>();
        services.AddSingleton<IGlobalHotkeyService, CapabilityOnlyGlobalHotkeyService>();
        services.AddSingleton<IExternalLinkService, SystemExternalLinkService>();
        services.AddSingleton<IWebDavBackupService, WebDavBackupService>();
        services.AddSingleton<IOneDriveBackupService, OneDriveBackupService>();
        services.AddSingleton<IKeePassVaultService, KeePassVaultService>();
        services.AddSingleton<IMdbxVaultService>(provider => new MdbxVaultService(
            nativeBridge: provider.GetRequiredService<IMdbxNativeBridge>()));
        services.AddSingleton<IClipboardService>(_ => new AvaloniaClipboardService(() => mainWindow));
        services.AddSingleton<IPasswordAttachmentFileService>(_ => new PasswordAttachmentFileService(
            () => mainWindow,
            _.GetRequiredService<ILocalizationService>(),
            _.GetRequiredService<ICryptoService>()));
        services.AddSingleton<IPasswordEditorDialogService>(_ => new PasswordEditorDialogService(
            () => mainWindow,
            _.GetRequiredService<ILocalizationService>(),
            _.GetRequiredService<IPasswordGeneratorService>()));
        services.AddSingleton<IPasswordDetailDialogService>(_ => new PasswordDetailDialogService(
            () => mainWindow,
            _.GetRequiredService<ILocalizationService>(),
            _.GetRequiredService<IClipboardService>(),
            _.GetRequiredService<ICryptoService>(),
            _.GetRequiredService<ITotpService>()));
        services.AddSingleton<ICategoryPickerDialogService>(_ => new CategoryPickerDialogService(
            () => mainWindow,
            _.GetRequiredService<ILocalizationService>()));
        services.AddSingleton<ITotpEditorDialogService>(_ => new TotpEditorDialogService(
            () => mainWindow,
            _.GetRequiredService<ILocalizationService>()));
        services.AddSingleton<IWalletItemEditorDialogService>(_ => new WalletItemEditorDialogService(
            () => mainWindow,
            _.GetRequiredService<ILocalizationService>()));
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IVaultUnlockCoordinator, VaultUnlockCoordinator>();
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider();
    }
}
