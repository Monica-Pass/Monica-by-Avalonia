namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task App_settings_default_window_capture_protection_off_and_roundtrip_on()
    {
        var path = GetTempPath();
        await File.WriteAllTextAsync(path, "{}");
        var first = new Monica.App.Services.AppSettingsService(path);

        await first.LoadAsync();

        Assert.False(first.Current.WindowCaptureProtectionEnabled);

        first.Current.WindowCaptureProtectionEnabled = true;
        await first.SaveAsync();

        var second = new Monica.App.Services.AppSettingsService(path);
        await second.LoadAsync();

        Assert.True(second.Current.WindowCaptureProtectionEnabled);
    }

    [Fact]
    public async Task Legacy_default_window_capture_setting_migrates_to_off()
    {
        var path = GetTempPath();
        await File.WriteAllTextAsync(path, "{\"WindowCaptureProtectionEnabled\":true}");
        var settings = new Monica.App.Services.AppSettingsService(path);

        await settings.LoadAsync();

        Assert.False(settings.Current.WindowCaptureProtectionEnabled);
    }

    [Fact]
    public void Window_capture_stays_off_before_settings_finish_loading()
    {
        var privacy = new CapturingWindowPrivacyService();
        var viewModel = CreateViewModel(GetTempPath(), windowPrivacyService: privacy);

        viewModel.ApplyWindowCapturePolicy();

        Assert.False(viewModel.WindowCaptureProtectionEnabled);
        Assert.False(privacy.LastEnabled);
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

        viewModel.ApplyWindowCapturePolicy();
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

    [Fact]
    public async Task Explicit_window_capture_setting_enables_after_settings_load()
    {
        var path = GetTempPath();
        var settings = new Monica.App.Services.AppSettingsService(path);
        await settings.LoadAsync();
        settings.Current.WindowCaptureProtectionEnabled = true;
        await settings.SaveAsync();
        var privacy = new CapturingWindowPrivacyService();
        var viewModel = CreateViewModel(path, windowPrivacyService: privacy);

        viewModel.ApplyWindowCapturePolicy();
        Assert.False(privacy.LastEnabled);

        await viewModel.InitializeCommand.ExecuteAsync(null);

        Assert.True(viewModel.WindowCaptureProtectionEnabled);
        Assert.True(privacy.LastEnabled);
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
