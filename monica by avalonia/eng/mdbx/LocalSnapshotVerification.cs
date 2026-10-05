using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App;
using Monica.App.Features.Mdbx;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Mdbx;
using Monica.Data.Repositories;
using Monica.Mdbx.Ffi;
using Monica.Platform.Services;

// This verifier runs in memory against product assemblies and the third-party headless backend.
public static class LocalSnapshotVerification
{
    public static string CurrentCheck { get; private set; } = "initialize";
    private static int _checks;

    public static void ConfigureDependencies(string productDirectory) =>
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(productDirectory, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };

    private static void Check(bool condition, string name)
    {
        CurrentCheck = name;
        if (!condition) throw new InvalidOperationException("Local snapshot check failed.");
        _checks++;
        Console.WriteLine("PASS " + name);
    }

    public static void Run(string directory, string imageDirectory)
    {
        CurrentCheck = "headless-bootstrap";
        var icons = typeof(App).Assembly.GetType("Monica.App.Services.WebsiteIconCache");
        icons?.GetMethod("SetAutomatedRunNetworkAllowed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, new object[] { false });
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions
        {
            UseHeadlessDrawing = false
        }).SetupWithoutStarting();
        var operation = RunOnUiAsync(directory, imageDirectory);
        while (!operation.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }
        operation.GetAwaiter().GetResult();
        Console.WriteLine("Local snapshot verification passed: " + _checks + " checks.");
    }

    private static byte[] Hash(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return SHA256.HashData(input);
    }

    private static async Task RunOnUiAsync(string directory, string imageDirectory)
    {
        CurrentCheck = "native-fixture";
        var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var source = Path.Combine(directory, "source.mdbx");
        var backup = Path.Combine(directory, "saved.mdbx");
        string projectId, objectId;
        using (var native = MdbxFfi.CreateVaultWithTigaMode(source, credential,
            "local-snapshot-fixture", Monica.Mdbx.Ffi.MdbxTigaMode.Multi))
        {
            projectId = native.CreateProject("Fixture folder").ProjectId;
            objectId = native.CreateObject(projectId, "login", "Frozen title",
                "{\"kind\":\"password\",\"room_id\":101,\"username\":\"fixture\",\"password_plain\":\"fixture\"}", 1).ObjectId;
        }
        var factory = new SqliteConnectionFactory(Path.Combine(directory, "metadata.db"));
        var migrator = new DatabaseMigrator(factory);
        var inner = new MonicaRepository(factory, migrator, null, null);
        var bridge = new MdbxUniffiNativeBridge();
        using var session = new VaultSessionService();
        using var crypto = new CryptoLease(credential);
        using var store = new MdbxVaultStore(bridge, null, session);
        var repository = new MdbxBackedMonicaRepository(inner, store, null);
        var proxy = DispatchProxy.Create<IMonicaRepository, ReadFailureProxy>();
        var failures = (ReadFailureProxy)(object)proxy;
        failures.Inner = repository;
        var snapshotService = new MdbxVaultService(null, bridge, store);
        var database = new LocalMdbxDatabase
        {
            Name = "Fixture vault", FilePath = source, WorkingCopyPath = source,
            EncryptedPassword = credential, StorageLocation = MdbxStorageLocation.Internal,
            IsOfflineAvailable = true, LastSyncStatus = SyncStatus.LocalOnly
        };
        await repository.SaveMdbxDatabaseAsync(database);
        var picker = new PathPicker { SaveTarget = new PickedSaveTarget("saved.mdbx", backup) };
        var confirm = new Confirmation();
        Action<IServiceCollection> overrides = services =>
        {
            services.AddSingleton<ISqliteConnectionFactory>(factory);
            services.AddSingleton<IDatabaseMigrator>(migrator);
            services.AddSingleton<IMonicaRepository>(repository);
            services.AddSingleton<IMdbxUnknownEntryDiagnostics>(repository);
            services.AddSingleton<IVaultSessionService>(session);
            services.AddSingleton<ICryptoService>(crypto.Service);
            services.AddSingleton<IMdbxVaultService>(snapshotService);
            services.AddSingleton<IFileSystemPickerService>(picker);
            services.AddSingleton<IConfirmationDialogService>(confirm);
            services.AddSingleton<IExportAuthorizationService>(new Authorization());
            services.AddSingleton<IClipboardService>(new Clipboard());
        };
        var configure = typeof(App).GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);
        CurrentCheck = "product-container";
        using var services = (ServiceProvider)configure.Invoke(null, new object[] { null, overrides });
        var vm = services.GetRequiredService<MainWindowViewModel>();
        vm.IsUnlocked = true;
        vm.SelectedSection = "Mdbx";
        await vm.RefreshMdbxVaultsCommand.ExecuteAsync(null);
        var window = new Window { Width = 1200, Height = 800,
            Content = new MdbxWorkspaceView { DataContext = vm } };
        window.Show();
        CurrentCheck = "compiled-view";
        try
        {
            Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<MdbxLocalSnapshotView>().Single();
            var export = view.FindControl<Button>("ExportMdbxSnapshotButton");
            var restore = view.FindControl<Button>("RestoreMdbxSnapshotButton");
            Check(vm.CanUseMdbxSnapshotActions && export.IsEnabled && restore.IsEnabled, "compiled-buttons-enabled-for-unlocked-source");
            Check(ReferenceEquals(export.Command, vm.ExportMdbxSnapshotCommand) &&
                ReferenceEquals(restore.Command, vm.RestoreMdbxSnapshotCommand), "compiled-command-bindings");
            Check(ReferenceEquals(export.CommandParameter, vm.SelectedMdbxDatabaseItem) &&
                ReferenceEquals(restore.CommandParameter, vm.SelectedMdbxDatabaseItem), "compiled-source-parameter-bindings");
            vm.L.SetLanguage("en-US");
            Dispatcher.UIThread.RunJobs();
            Check(AutomationProperties.GetName(export) == vm.L.Get("MdbxExportSnapshot"), "english-accessible-button-name");
            vm.L.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            Check(AutomationProperties.GetName(restore) == vm.L.Get("MdbxRestoreSnapshot"), "chinese-accessible-button-name");

            await vm.ExportMdbxSnapshotCommand.ExecuteAsync(vm.SelectedMdbxDatabaseItem);
            Check(File.Exists(backup) && !vm.IsStatusMessageFailure && picker.EagerReads == 0, "real-command-exports-without-buffering-vault");
            var frozen = Hash(backup);
            await vm.ExportMdbxSnapshotCommand.ExecuteAsync(vm.SelectedMdbxDatabaseItem);
            Check(vm.IsStatusMessageFailure && frozen.SequenceEqual(Hash(backup)), "real-command-refuses-backup-overwrite");
            using (var native = MdbxFfi.OpenVault(source, credential, "local-snapshot-fixture"))
                native.UpdateObject(projectId, objectId, "login", "New local title",
                    "{\"kind\":\"password\",\"room_id\":101,\"username\":\"changed\",\"password_plain\":\"fixture\"}", 1);
            var localBefore = Hash(source);
            picker.OpenTarget = null;
            await vm.RestoreMdbxSnapshotCommand.ExecuteAsync(vm.SelectedMdbxDatabaseItem);
            Check(localBefore.SequenceEqual(Hash(source)) && !vm.IsStatusMessageFailure, "cancel-picker-preserves-source");
            picker.OpenTarget = new PickedOpenTarget("saved.mdbx", backup);
            confirm.Approve = false;
            await vm.RestoreMdbxSnapshotCommand.ExecuteAsync(vm.SelectedMdbxDatabaseItem);
            Check(localBefore.SequenceEqual(Hash(source)), "decline-confirmation-preserves-source");
            confirm.Approve = true;

            foreach (var location in new[] { MdbxStorageLocation.RemoteWebDav, MdbxStorageLocation.RemoteOneDrive,
                MdbxStorageLocation.External, MdbxStorageLocation.Internal })
            {
                database = (await repository.GetMdbxDatabasesAsync()).Single();
                database.StorageLocation = location;
                database.RemoteETag = "fixture-validator";
                database.LastSyncedAt = DateTimeOffset.Parse("2026-01-02T03:04:05Z");
                database.RemoteLastModifiedAt = database.LastSyncedAt;
                await repository.SaveMdbxDatabaseAsync(database);
                await vm.RefreshMdbxVaultsCommand.ExecuteAsync(null);
                await vm.RestoreMdbxSnapshotCommand.ExecuteAsync(vm.SelectedMdbxDatabaseItem);
                var restored = (await repository.GetMdbxDatabasesAsync()).Single();
                var expected = location is MdbxStorageLocation.Internal or MdbxStorageLocation.External
                    ? SyncStatus.LocalOnly : SyncStatus.PendingUpload;
                Check(!vm.IsStatusMessageFailure && restored.LastSyncStatus == expected, "restore-status-" + location);
                Check(restored.RemoteETag == "fixture-validator" && restored.LastSyncedAt == database.LastSyncedAt &&
                    restored.RemoteLastModifiedAt == database.RemoteLastModifiedAt, "restore-preserves-validator-" + location);
                Check(frozen.SequenceEqual(Hash(backup)), "restore-never-modifies-user-backup-" + location);
                if (location == MdbxStorageLocation.RemoteWebDav)
                {
                    Check(vm.HasMdbxSnapshotRecovery && File.Exists(vm.MdbxSnapshotRecoveryPath), "recovery-receipt-displayed");
                    using var recovered = MdbxFfi.OpenVault(vm.MdbxSnapshotRecoveryPath, credential, "read-recovery");
                    Check(recovered.GetObject(projectId, objectId).Title == "New local title", "recovery-has-pre-restore-data");
                }
            }

            using (await store.AcquireFileReplacementAsync(source)) { }
            using (var native = MdbxFfi.OpenVault(source, credential, "local-snapshot-fixture"))
                native.UpdateObject(projectId, objectId, "login", "Before failed refresh",
                    "{\"kind\":\"password\",\"room_id\":101,\"username\":\"fixture\",\"password_plain\":\"fixture\"}", 1);
            failures.ArmAfterSave = true;
            // Restrict fault injection to this non-default restore. The production repository's
            // optional bulk-read/cache interfaces must remain in place for normal workspace loads.
            var repositoryField = typeof(MainWindowViewModel).GetField("_repository", BindingFlags.Instance | BindingFlags.NonPublic);
            repositoryField.SetValue(vm, proxy);
            try { await vm.RestoreMdbxSnapshotCommand.ExecuteAsync(vm.SelectedMdbxDatabaseItem); }
            finally { repositoryField.SetValue(vm, repository); }
            Check(vm.IsStatusMessageFailure && vm.StatusMessage == vm.L.Get("MdbxSnapshotRefreshAfterCommitFailed"),
                "committed-restore-reports-refresh-failure-separately");
            using (var native = MdbxFfi.OpenVault(source, credential, "local-snapshot-fixture"))
                Check(native.GetObject(projectId, objectId).Title == "Frozen title" && vm.HasMdbxSnapshotRecovery,
                    "refresh-failure-keeps-restored-data-and-recovery-receipt");

            database = (await repository.GetMdbxDatabasesAsync()).Single();
            database.IsDefault = true;
            await repository.SaveMdbxDatabaseAsync(database);
            await vm.RefreshMdbxVaultsCommand.ExecuteAsync(null);
            var draft = new NoteEditorTab(-1, null, "Unsaved fixture") { IsDirty = true,
                DraftInitialized = true, DraftContent = "Uncommitted fixture text" };
            vm.OpenNoteTabs.Add(draft);
            var noteProtected = Hash(source);
            await vm.RestoreMdbxSnapshotCommand.ExecuteAsync(vm.SelectedMdbxDatabaseItem);
            Check(noteProtected.SequenceEqual(Hash(source)) && vm.OpenNoteTabs.Contains(draft) && draft.IsDirty,
                "default-restore-preserves-unsaved-note");
            vm.OpenNoteTabs.Clear();
            await vm.LoadAsync();
            Check(vm.Passwords.Single().Title == "Frozen title", "default-workspace-loaded");
            using (var native = MdbxFfi.OpenVault(source, credential, "local-snapshot-fixture"))
                native.UpdateObject(projectId, objectId, "login", "Before second restore",
                    "{\"kind\":\"password\",\"room_id\":101,\"username\":\"fixture\",\"password_plain\":\"fixture\"}", 1);
            await vm.LoadAsync();
            Check(vm.Passwords.Single().Title == "Before second restore", "workspace-holds-pre-restore-objects");
            await vm.RestoreMdbxSnapshotCommand.ExecuteAsync(vm.SelectedMdbxDatabaseItem);
            Check(vm.IsUnlocked && vm.Passwords.Single().Title == "Frozen title" && vm.HasMdbxSnapshotRecovery,
                "default-restore-reloads-business-workspace-and-recovery");

            Directory.CreateDirectory(imageDirectory);
            vm.L.SetLanguage("en-US");
            Dispatcher.UIThread.RunJobs();
            using (var bitmap = new RenderTargetBitmap(new PixelSize(1200, 800), new Vector(96, 96)))
            {
                bitmap.Render(window);
                bitmap.Save(Path.Combine(imageDirectory, "local-snapshots-en.png"), new PngBitmapEncoderOptions());
            }
            Check(export.Focus(), "export-button-receives-keyboard-focus");
            vm.IsUnlocked = false;
            Dispatcher.UIThread.RunJobs();
            Check(!vm.CanUseMdbxSnapshotActions && !export.IsEnabled && !restore.IsEnabled && !vm.HasMdbxSnapshotRecovery,
                "lock-disables-actions-and-clears-recovery-path");
        }
        finally
        {
            window.Close();
            if (vm.IsUnlocked) vm.IsUnlocked = false;
        }
    }

    private sealed class CryptoLease : IDisposable
    {
        public CryptoService Service { get; } = new();
        public CryptoLease(string credential) => Service.InitializeSession(credential, RandomNumberGenerator.GetBytes(16));
        public void Dispose() => Service.Lock();
    }

    public class ReadFailureProxy : DispatchProxy
    {
        public IMonicaRepository Inner;
        public bool ArmAfterSave;
        private bool _failNextRead;
        protected override object Invoke(MethodInfo method, object[] arguments)
        {
            if (_failNextRead && method.Name == "GetMdbxDatabasesAsync")
            {
                _failNextRead = false;
                throw new InvalidOperationException("Injected fixture read failure.");
            }
            var result = method.Invoke(Inner, arguments);
            if (ArmAfterSave && method.Name == "SaveMdbxDatabaseAsync")
            {
                ArmAfterSave = false;
                _failNextRead = true;
            }
            return result;
        }
    }

    private sealed class PathPicker : IFileSystemPickerService
    {
        public PickedSaveTarget SaveTarget;
        public PickedOpenTarget OpenTarget;
        public int EagerReads;
        public PlatformIntegrationCapability Capability => new(PlatformFeatureKeys.FilePicker,
            PlatformFeatureStatus.Available, "Fixture picker");
        public Task<PickedSaveTarget> PickSaveFileTargetAsync(string title, string name,
            IReadOnlyList<PlatformFilePickerFileType> types, CancellationToken cancellationToken = default) => Task.FromResult(SaveTarget);
        public Task<PickedOpenTarget> PickOpenFileTargetAsync(string title, IReadOnlyList<PlatformFilePickerFileType> types,
            CancellationToken cancellationToken = default) => Task.FromResult(OpenTarget);
        public Task<PickedTextFile> OpenTextFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> types,
            CancellationToken cancellationToken = default) { EagerReads++; throw new InvalidOperationException("Unexpected eager read."); }
        public Task<PickedBinaryFile> OpenBinaryFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> types,
            CancellationToken cancellationToken = default) { EagerReads++; throw new InvalidOperationException("Unexpected eager read."); }
        public Task<string> SaveTextFileAsync(string title, string name, string content, IReadOnlyList<PlatformFilePickerFileType> types,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected write.");
        public Task<string> SaveBinaryFileAsync(string title, string name, ReadOnlyMemory<byte> content,
            IReadOnlyList<PlatformFilePickerFileType> types, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected buffered write.");
    }

    private sealed class Confirmation : IConfirmationDialogService
    {
        public bool Approve = true;
        public Task<bool> ConfirmAsync(string title, string message, string action, string close = null,
            CancellationToken cancellationToken = default) => Task.FromResult(Approve);
        public Task<bool> ConfirmTypedAsync(string title, string message, string phrase, string instruction,
            string action, string close = null, CancellationToken cancellationToken = default) => Task.FromResult(Approve);
    }
    private sealed class Authorization : IExportAuthorizationService
    {
        public Task<bool> AuthorizeAsync(bool requireMasterPassword, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
    private sealed class Clipboard : IClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
