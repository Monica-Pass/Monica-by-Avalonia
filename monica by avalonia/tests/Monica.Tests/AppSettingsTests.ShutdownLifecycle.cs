using Microsoft.Extensions.DependencyInjection;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task ViewModel_shutdown_is_idempotent_and_clears_sensitive_boundaries_once()
    {
        var settings = new RecordingSettingsService();
        var clipboard = new ShutdownClipboardService();
        var viewModel = CreateViewModel(
            GetTempPath(),
            settingsService: settings,
            clipboardService: clipboard);
        viewModel.IsUnlocked = true;
        viewModel.NoteContent = "shutdown secret";

        await Task.WhenAll(
            viewModel.PrepareForShutdownAsync(),
            viewModel.PrepareForShutdownAsync());

        Assert.False(viewModel.IsUnlocked);
        Assert.Empty(viewModel.NoteContent);
        Assert.Equal(1, settings.Calls.Count(call => call == "clear"));
        Assert.Equal(1, clipboard.ClearCallCount);
    }

    [Fact]
    public async Task Application_shutdown_disposes_the_service_provider_after_sensitive_cleanup()
    {
        var settings = new RecordingSettingsService();
        var clipboard = new ShutdownClipboardService();
        var probe = new DisposableProbe();
        var services = new ServiceCollection()
            .AddSingleton(_ => probe)
            .BuildServiceProvider();
        services.GetRequiredService<DisposableProbe>();
        var viewModel = CreateViewModel(
            GetTempPath(),
            settingsService: settings,
            clipboardService: clipboard);
        viewModel.IsUnlocked = true;

        await Monica.App.App.ShutdownServicesAsync(viewModel, services);

        Assert.True(probe.IsDisposed);
        Assert.Equal(1, settings.Calls.Count(call => call == "clear"));
        Assert.Equal(1, clipboard.ClearCallCount);
    }

    private sealed class ShutdownClipboardService : IClipboardService
    {
        public int ClearCallCount { get; private set; }

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ClearOwnedContentAsync(CancellationToken cancellationToken = default)
        {
            ClearCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class DisposableProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
