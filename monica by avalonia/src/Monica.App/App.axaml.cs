using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Mdbx;
using Monica.Data.Repositories;
using Monica.Data.Services;
using Monica.Platform.Services;

namespace Monica.App;

public partial class App : Application
{
    private readonly object _shutdownSync = new();
    private ServiceProvider? _services;
    private MainWindow? _mainWindow;
    private Task? _shutdownTask;

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
            var viewModel = _services.GetRequiredService<MainWindowViewModel>();
            _mainWindow.DataContext = viewModel;
            _services.GetRequiredService<DesktopIntegrationCoordinator>().Initialize(viewModel);
            _mainWindow.ShutdownRequestedAsync = () => EnsureShutdownAsync(viewModel);
            desktop.MainWindow = _mainWindow;
            desktop.Exit += OnDesktopExit;

            var smokePassword = GetSmokeUiUnlockPassword(desktop.Args);
            var smokeSection = GetSmokeUiSection(desktop.Args);
            var smokePasswordSelectionCount = GetSmokeUiSelectPasswordCount(desktop.Args);
            var smokeOpenNoteCount = GetSmokeUiOpenNoteCount(desktop.Args);
            var smokeNoteMode = GetSmokeUiNoteMode(desktop.Args);
            var smokeNoteLongLineCount = GetSmokeUiNoteLongLineCount(desktop.Args);
            var smokeTheme = GetSmokeUiTheme(desktop.Args);
            var smokeViewportSize = GetSmokeUiViewportSize(desktop.Args);
            var smokeScreenshotDirectory = GetSmokeUiArgument(desktop.Args, "--smoke-ui-screenshot-dir");
            var smokeVaultLoadDelayMilliseconds = GetSmokeUiCount(desktop.Args, "--smoke-ui-load-delay-ms");
            var smokeMaxVaultLoadMilliseconds = GetSmokeUiCount(desktop.Args, "--smoke-ui-max-vault-load-ms");
            var smokeH04ListInteractions = HasSmokeUiFlag(desktop.Args, "--smoke-ui-h04-lists");
            var smokeNoteEditorChecks = HasSmokeUiFlag(desktop.Args, "--smoke-ui-note-editor-checks");
            var smokeOtherPagesChecks = HasSmokeUiFlag(desktop.Args, "--smoke-ui-other-pages-checks");
            var smokeKeyboardChecks = HasSmokeUiFlag(desktop.Args, "--smoke-ui-keyboard-checks");
            var smokeExitAfterChecks = HasSmokeUiFlag(desktop.Args, "--smoke-ui-exit-after-checks");
            ApplySmokeUiViewportSize(_mainWindow, smokeViewportSize);
            if (!string.IsNullOrWhiteSpace(smokePassword))
            {
                QueueSmokeUiUnlock(
                    desktop,
                    _mainWindow,
                    viewModel,
                    smokePassword,
                    smokeSection,
                    smokePasswordSelectionCount,
                    smokeOpenNoteCount,
                    smokeNoteMode,
                    smokeNoteLongLineCount,
                    smokeTheme,
                    smokeScreenshotDirectory,
                    smokeVaultLoadDelayMilliseconds,
                    smokeMaxVaultLoadMilliseconds,
                    smokeH04ListInteractions,
                    smokeNoteEditorChecks,
                    smokeOtherPagesChecks,
                    smokeKeyboardChecks,
                    smokeExitAfterChecks);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private Task EnsureShutdownAsync(MainWindowViewModel viewModel)
    {
        lock (_shutdownSync)
        {
            return _shutdownTask ??= ShutdownCoreAsync(viewModel);
        }
    }

    private async Task ShutdownCoreAsync(MainWindowViewModel viewModel)
    {
        var services = _services;
        if (services is null)
        {
            return;
        }

        try
        {
            await ShutdownServicesAsync(viewModel, services);
        }
        finally
        {
            _services = null;
        }
    }

    internal static async Task ShutdownServicesAsync(
        MainWindowViewModel viewModel,
        ServiceProvider services)
    {
        await viewModel.PrepareForShutdownAsync();
        await services.DisposeAsync();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        ServiceProvider? services = null;
        lock (_shutdownSync)
        {
            if (_shutdownTask is null)
            {
                services = _services;
                _services = null;
            }
        }

        services?.Dispose();
    }


    internal static ServiceProvider ConfigureServices(
        MainWindow mainWindow,
        Action<IServiceCollection>? configureOverrides = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<ILegacyVaultDetector, LegacyVaultDetector>();
        services.AddSingleton<IDatabaseMigrator, DatabaseMigrator>();
        services.AddSingleton<ILegacyBusinessDataInspector, LegacyBusinessDataInspector>();
        services.AddSingleton<IVaultCredentialStore, VaultCredentialStore>();
        services.AddSingleton<MonicaRepository>(provider => new MonicaRepository(
            provider.GetRequiredService<ISqliteConnectionFactory>(),
            provider.GetRequiredService<IDatabaseMigrator>(),
            provider.GetService<IVaultDataProtector>(),
            provider.GetRequiredService<IAttachmentContentStore>()));
        services.AddSingleton<IMdbxNativeBridge, MdbxUniffiNativeBridge>();
        services.AddSingleton<IMdbxVaultStore, MdbxVaultStore>();
        services.AddSingleton<IMonicaRepository>(provider => new MdbxBackedMonicaRepository(
            provider.GetRequiredService<MonicaRepository>(),
            provider.GetRequiredService<IMdbxVaultStore>(),
            provider.GetRequiredService<IAttachmentContentStore>()));
        services.AddSingleton<IMasterPasswordMaintenanceService, MasterPasswordMaintenanceService>();
        services.AddSingleton<ICryptoService, CryptoService>();
        services.AddSingleton<IVaultSessionService, VaultSessionService>();
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
        services.AddSingleton<IBrowserBridgeService>(provider => OperatingSystem.IsWindows()
            ? new WindowsBrowserBridgeService(provider.GetRequiredService<IPlatformIntegrationService>())
            : new CapabilityOnlyBrowserBridgeService(provider.GetRequiredService<IPlatformIntegrationService>()));
        services.AddSingleton<INativePasskeyService, CapabilityOnlyNativePasskeyService>();
        services.AddSingleton<ITrayService>(provider => OperatingSystem.IsWindows()
            ? new AvaloniaTrayService(
                provider.GetRequiredService<IPlatformIntegrationService>(),
                provider.GetRequiredService<ILocalizationService>())
            : new CapabilityOnlyTrayService(provider.GetRequiredService<IPlatformIntegrationService>()));
        services.AddSingleton<IGlobalHotkeyService>(provider => OperatingSystem.IsWindows()
            ? new WindowsGlobalHotkeyService(provider.GetRequiredService<IPlatformIntegrationService>())
            : new CapabilityOnlyGlobalHotkeyService(provider.GetRequiredService<IPlatformIntegrationService>()));
        services.AddSingleton<IExternalLinkService, SystemExternalLinkService>();
        services.AddSingleton<IWebDavBackupService, WebDavBackupService>();
        services.AddSingleton<IWebDavBackupCryptoService, WebDavBackupCryptoService>();
        services.AddSingleton<IOneDriveAccessTokenProvider, MsalOneDriveAccessTokenProvider>();
        services.AddSingleton<IOneDriveBackupService>(provider => new OneDriveBackupService(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OneDriveBackupService)),
            provider.GetRequiredService<IOneDriveAccessTokenProvider>()));
        services.AddSingleton<IKeePassVaultService, KeePassVaultService>();
        services.AddSingleton<IMdbxVaultService>(provider => new MdbxVaultService(
            nativeBridge: provider.GetRequiredService<IMdbxNativeBridge>()));
        services.AddSingleton<ICanonicalVaultPathProvider, CanonicalVaultPathProvider>();
        services.AddSingleton<ICanonicalVaultBootstrapService, CanonicalVaultBootstrapService>();
        services.AddSingleton<IClipboardAdapter>(_ => new AvaloniaClipboardAdapter(() => mainWindow));
        services.AddSingleton<IClipboardService, SecureClipboardService>();
        services.AddSingleton<IWindowPrivacyService>(_ => new WindowPrivacyService(() => mainWindow));
        services.AddSingleton<PasswordAttachmentFileService>(_ => new PasswordAttachmentFileService(
            () => mainWindow,
            _.GetRequiredService<ILocalizationService>(),
            _.GetRequiredService<ICryptoService>()));
        services.AddSingleton<IPasswordAttachmentFileService>(provider => provider.GetRequiredService<PasswordAttachmentFileService>());
        services.AddSingleton<IAttachmentContentStore>(provider => provider.GetRequiredService<PasswordAttachmentFileService>());
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
        services.AddSingleton<IConfirmationDialogService>(_ => new ConfirmationDialogService(
            () => mainWindow,
            _.GetRequiredService<ILocalizationService>()));
        services.AddSingleton<IExportAuthorizationService>(_ => new MasterPasswordExportAuthorizationService(
            () => mainWindow,
            _.GetRequiredService<IVaultCredentialStore>(),
            _.GetRequiredService<ICryptoService>(),
            _.GetRequiredService<ILocalizationService>()));
        services.AddSingleton<ITotpEditorDialogService>(_ => new TotpEditorDialogService(
            () => mainWindow,
            _.GetRequiredService<ILocalizationService>(),
            _.GetRequiredService<IMonicaRepository>()));
        services.AddSingleton<IWalletItemEditorDialogService>(_ => new WalletItemEditorDialogService(
            () => mainWindow,
            _.GetRequiredService<ILocalizationService>(),
            _.GetRequiredService<IMonicaRepository>()));
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IVaultUnlockCoordinator, VaultUnlockCoordinator>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton(provider => new DesktopIntegrationCoordinator(
            mainWindow,
            provider.GetRequiredService<ITrayService>(),
            provider.GetRequiredService<IGlobalHotkeyService>(),
            provider.GetRequiredService<IBrowserBridgeService>()));
        configureOverrides?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
