namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task App_settings_default_window_capture_protection_on_and_roundtrip_off()
    {
        var path = GetTempPath();
        await File.WriteAllTextAsync(path, "{}");
        var first = new Monica.App.Services.AppSettingsService(path);

        await first.LoadAsync();

        Assert.True(first.Current.WindowCaptureProtectionEnabled);

        first.Current.WindowCaptureProtectionEnabled = false;
        await first.SaveAsync();

        var second = new Monica.App.Services.AppSettingsService(path);
        await second.LoadAsync();

        Assert.False(second.Current.WindowCaptureProtectionEnabled);
    }

    [Fact]
    public async Task Window_capture_setting_applies_loaded_policy_and_changes_immediately()
    {
        var path = GetTempPath();
        var settings = new Monica.App.Services.AppSettingsService(path);
        await settings.LoadAsync();
        settings.Current.WindowCaptureProtectionEnabled = false;
        await settings.SaveAsync();
        var privacy = new CapturingWindowPrivacyService();
        var viewModel = CreateViewModel(path, windowPrivacyService: privacy);

        await viewModel.InitializeCommand.ExecuteAsync(null);

        Assert.False(viewModel.WindowCaptureProtectionEnabled);
        Assert.False(privacy.LastEnabled);

        viewModel.WindowCaptureProtectionEnabled = true;

        Assert.True(privacy.LastEnabled);

        viewModel.WindowCaptureProtectionEnabled = false;

        Assert.False(privacy.LastEnabled);
        var reloaded = await LoadSettingsUntilAsync(
            path,
            current => !current.WindowCaptureProtectionEnabled);
        Assert.False(reloaded.Current.WindowCaptureProtectionEnabled);
    }

    private sealed class CapturingWindowPrivacyService : Monica.App.Services.IWindowPrivacyService
    {
        public bool LastEnabled { get; private set; } = true;

        public bool SetCaptureProtection(bool enabled)
        {
            LastEnabled = enabled;
            return true;
        }
    }
}
