using Avalonia.Threading;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class MdbxUiResponsivenessTests
{
    public MdbxUiResponsivenessTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public async Task Native_vault_creation_keeps_the_ui_dispatcher_responsive()
    {
        var bridge = new MdbxUniffiNativeBridge();
        Assert.True(bridge.IsAvailable);
        var root = Directory.CreateTempSubdirectory("monica-ui-mdbx-");
        var path = Path.Combine(root.FullName, "responsive.mdbx");
        var dispatcherTicks = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => dispatcherTicks++;

        try
        {
            timer.Start();
            var service = new MdbxVaultService(new ThrowingMdbxVaultEngine(), bridge);

            var metadata = await service.CreateLocalMetadataAsync(
                "Responsive native vault",
                path,
                MdbxTigaMode.Multi,
                TestContext.Current.CancellationToken);

            timer.Stop();
            Assert.True(dispatcherTicks > 0, "Native MDBX creation blocked the Avalonia UI dispatcher.");
            Assert.True(File.Exists(metadata.WorkingCopyPath));
        }
        finally
        {
            timer.Stop();
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Native_vault_open_keeps_the_ui_dispatcher_responsive()
    {
        var bridge = new MdbxUniffiNativeBridge();
        Assert.True(bridge.IsAvailable);
        var root = Directory.CreateTempSubdirectory("monica-ui-mdbx-open-");
        var path = Path.Combine(root.FullName, "responsive.mdbx");
        var dispatcherTicks = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => dispatcherTicks++;

        try
        {
            var service = new MdbxVaultService(new ThrowingMdbxVaultEngine(), bridge);
            var metadata = await service.CreateLocalMetadataAsync(
                "Responsive native vault",
                path,
                MdbxTigaMode.Multi,
                TestContext.Current.CancellationToken);

            timer.Start();
            await using var stream = await service.OpenLocalStreamAsync(
                metadata,
                TestContext.Current.CancellationToken);

            timer.Stop();
            Assert.True(dispatcherTicks > 0, "Native MDBX open blocked the Avalonia UI dispatcher.");
        }
        finally
        {
            timer.Stop();
            root.Delete(recursive: true);
        }
    }

    private sealed class ThrowingMdbxVaultEngine : IMdbxVaultEngine
    {
        public Task CreateVaultAsync(
            string path,
            string password,
            MdbxTigaMode mode,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("CLI fallback must not run when native MDBX is available.");

        public Task OpenVaultAsync(
            string path,
            string password,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("CLI fallback must not run when native MDBX is available.");

        public Task<MdbxVaultInspection> InspectAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("CLI fallback must not run when native MDBX is available.");
    }
}
