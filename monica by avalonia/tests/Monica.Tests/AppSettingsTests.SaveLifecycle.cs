using Monica.App.Services;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task ViewModel_lock_cancels_pending_settings_save_before_sensitive_cache_clear()
    {
        var settings = new RecordingSettingsService();
        var viewModel = CreateViewModel(GetTempPath(), settingsService: settings);
        viewModel.IsUnlocked = true;
        viewModel.WebDavEnabled = true;

        await viewModel.LockCommand.ExecuteAsync(null);
        await Task.Delay(TimeSpan.FromMilliseconds(350));

        Assert.Equal(["clear"], settings.Calls);
    }

    [Fact]
    public async Task ViewModel_lock_waits_for_active_settings_save_to_finish_before_cache_clear()
    {
        var settings = new BlockingSettingsService();
        var viewModel = CreateViewModel(GetTempPath(), settingsService: settings);
        viewModel.IsUnlocked = true;
        viewModel.WebDavEnabled = true;
        await settings.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var lockOperation = viewModel.LockCommand.ExecuteAsync(null);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        var lockWaitedForSave = !lockOperation.IsCompleted;
        var clearRanBeforeSaveFinished = settings.ClearCallCount != 0;
        var clearObservedActiveSave = settings.ClearObservedActiveSave;
        var saveWasCanceled = settings.SaveWasCanceled;
        settings.ReleaseSave();
        await lockOperation;
        await settings.SaveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(lockWaitedForSave);
        Assert.False(clearRanBeforeSaveFinished);
        Assert.False(clearObservedActiveSave);
        Assert.False(saveWasCanceled);
    }

    [Fact]
    public async Task ViewModel_lock_persists_latest_settings_while_clearing_sensitive_cache()
    {
        var settingsPath = GetTempPath();
        var viewModel = CreateViewModel(settingsPath);
        viewModel.IsUnlocked = true;
        viewModel.WebDavEnabled = true;
        viewModel.WebDavServerUrl = "https://dav.example.com";
        viewModel.StartupSection = "Notes";

        await viewModel.LockCommand.ExecuteAsync(null);

        var reloaded = new AppSettingsService(settingsPath);
        await reloaded.LoadAsync();
        Assert.True(reloaded.Current.WebDavEnabled);
        Assert.Equal("https://dav.example.com", reloaded.Current.WebDavServerUrl);
        Assert.Equal("Notes", reloaded.Current.StartupSection);
    }

    [Fact]
    public async Task ViewModel_setting_change_during_active_save_schedules_followup_save()
    {
        var settings = new TwoPassSettingsService();
        var viewModel = CreateViewModel(GetTempPath(), settingsService: settings);
        viewModel.WebDavEnabled = true;
        await settings.FirstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.StartupSection = "Notes";
        settings.ReleaseFirstSave();

        await settings.SecondSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, settings.SaveCallCount);
    }

    [Fact]
    public async Task ViewModel_repeated_lock_clears_sensitive_settings_once()
    {
        var settings = new RecordingSettingsService();
        var viewModel = CreateViewModel(GetTempPath(), settingsService: settings);
        viewModel.IsUnlocked = true;

        await viewModel.LockCommand.ExecuteAsync(null);
        await viewModel.LockCommand.ExecuteAsync(null);

        Assert.Equal(1, settings.Calls.Count(call => call == "clear"));
    }

    private sealed class RecordingSettingsService : IAppSettingsService
    {
        public DesktopAppSettings Current { get; } = new();
        public List<string> Calls { get; } = [];

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("save");
            return Task.CompletedTask;
        }

        public Task ClearSensitiveCacheAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("clear");
            return Task.CompletedTask;
        }

        public IReadOnlyDictionary<string, bool> GetFeatureToggles() => Current.FeatureToggles;

        public bool IsFeatureEnabled(string featureKey) =>
            Current.FeatureToggles.GetValueOrDefault(featureKey);

        public void SetFeatureEnabled(string featureKey, bool isEnabled) =>
            Current.FeatureToggles[featureKey] = isEnabled;
    }

    private sealed class BlockingSettingsService : IAppSettingsService
    {
        private readonly TaskCompletionSource _releaseSave =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _saveActive;

        public DesktopAppSettings Current { get; } = new();
        public TaskCompletionSource SaveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SaveCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ClearObservedActiveSave { get; private set; }
        public bool SaveWasCanceled { get; private set; }
        public int ClearCallCount { get; private set; }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task SaveAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Exchange(ref _saveActive, 1);
            SaveStarted.TrySetResult();
            try
            {
                await _releaseSave.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SaveWasCanceled = true;
                throw;
            }
            finally
            {
                Interlocked.Exchange(ref _saveActive, 0);
                SaveCompleted.TrySetResult();
            }
        }

        public Task ClearSensitiveCacheAsync(CancellationToken cancellationToken = default)
        {
            ClearCallCount++;
            ClearObservedActiveSave = Volatile.Read(ref _saveActive) != 0;
            return Task.CompletedTask;
        }

        public IReadOnlyDictionary<string, bool> GetFeatureToggles() => Current.FeatureToggles;

        public bool IsFeatureEnabled(string featureKey) =>
            Current.FeatureToggles.GetValueOrDefault(featureKey);

        public void SetFeatureEnabled(string featureKey, bool isEnabled) =>
            Current.FeatureToggles[featureKey] = isEnabled;

        public void ReleaseSave() => _releaseSave.TrySetResult();
    }

    private sealed class TwoPassSettingsService : IAppSettingsService
    {
        private readonly TaskCompletionSource _releaseFirstSave =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DesktopAppSettings Current { get; } = new();
        public TaskCompletionSource FirstSaveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondSaveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SaveCallCount { get; private set; }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task SaveAsync(CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            if (SaveCallCount == 1)
            {
                FirstSaveStarted.TrySetResult();
                await _releaseFirstSave.Task.WaitAsync(cancellationToken);
                return;
            }

            SecondSaveStarted.TrySetResult();
        }

        public Task ClearSensitiveCacheAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public IReadOnlyDictionary<string, bool> GetFeatureToggles() => Current.FeatureToggles;

        public bool IsFeatureEnabled(string featureKey) =>
            Current.FeatureToggles.GetValueOrDefault(featureKey);

        public void SetFeatureEnabled(string featureKey, bool isEnabled) =>
            Current.FeatureToggles[featureKey] = isEnabled;

        public void ReleaseFirstSave() => _releaseFirstSave.TrySetResult();
    }
}
