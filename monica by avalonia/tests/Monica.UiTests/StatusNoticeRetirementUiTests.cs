using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.ViewModels;

namespace Monica.UiTests;

/// <summary>
/// An acknowledgement in the status bar is only true for a moment. These cover the whole contract:
/// it leaves on its own, a sentence the user still has to act on does not, a later notice is not
/// cut short by the earlier one's deadline, and leaving the page retires it immediately.
/// </summary>
[Collection(AvaloniaUiTestCollection.Name)]
public sealed class StatusNoticeRetirementUiTests
{
    public StatusNoticeRetirementUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Acknowledgement_retires_itself_once_its_dwell_has_elapsed()
    {
        var (window, viewModel) = Boot(out var clock);
        try
        {
            viewModel.ClearTotpFiltersCommand.Execute(null);
            Assert.Equal(viewModel.L.Get("ClearedTotpFilters"), viewModel.StatusMessage);
            Assert.True(window.IsStatusNoticeRetirementScheduled);
            Assert.Equal(TimeSpan.FromSeconds(8), window.StatusNoticeRetirementInterval);

            clock.Advance(TimeSpan.FromSeconds(9));
            window.RunScheduledStatusNoticeRetirement();

            Assert.Equal(string.Empty, viewModel.StatusMessage);
            Assert.False(window.IsStatusNoticeRetirementScheduled);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Sentence_the_user_still_has_to_act_on_outlives_the_notice_dwell()
    {
        var (window, viewModel) = Boot(out var clock);
        try
        {
            // A standing line never arms the clock in the first place, so the only thing that
            // could clear it is a mechanism that should not have touched it.
            viewModel.ConfigureMdbxRemoteSourcesCommand.Execute(null);
            Assert.Equal(viewModel.L.Get("ConfigureMdbxRemoteSourcesHint"), viewModel.StatusMessage);
            Assert.False(window.IsStatusNoticeRetirementScheduled);

            clock.Advance(TimeSpan.FromMinutes(5));
            window.RunScheduledStatusNoticeRetirement();

            Assert.Equal(viewModel.L.Get("ConfigureMdbxRemoteSourcesHint"), viewModel.StatusMessage);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Later_notice_is_not_cut_short_by_the_deadline_of_the_one_it_replaced()
    {
        var (window, viewModel) = Boot(out var clock);
        try
        {
            viewModel.ClearTotpFiltersCommand.Execute(null);
            clock.Advance(TimeSpan.FromSeconds(5));
            window.RunScheduledStatusNoticeRetirement();
            Assert.NotEqual(string.Empty, viewModel.StatusMessage);

            // Five seconds of the second notice's own dwell are on the clock; the five the first
            // one already spent must not count towards it.
            viewModel.ClearTotpFiltersCommand.Execute(null);
            clock.Advance(TimeSpan.FromSeconds(5));
            window.RunScheduledStatusNoticeRetirement();
            Assert.Equal(viewModel.L.Get("ClearedTotpFilters"), viewModel.StatusMessage);
            Assert.True(window.IsStatusNoticeRetirementScheduled);

            clock.Advance(TimeSpan.FromSeconds(4));
            window.RunScheduledStatusNoticeRetirement();
            Assert.Equal(string.Empty, viewModel.StatusMessage);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Notice_leaves_the_moment_its_page_is_left()
    {
        var (window, viewModel) = Boot(out _);
        try
        {
            viewModel.SelectSectionCommand.Execute("Totp");
            Drain();
            viewModel.ClearTotpFiltersCommand.Execute(null);
            Assert.Equal(viewModel.L.Get("ClearedTotpFilters"), viewModel.StatusMessage);

            viewModel.SelectSectionCommand.Execute("Vault");

            Assert.Equal(string.Empty, viewModel.StatusMessage);
            Assert.False(window.IsStatusNoticeRetirementScheduled);
        }
        finally
        {
            window.Close();
        }
    }

    private static (Monica.App.MainWindow Window, MainWindowViewModel ViewModel) Boot(
        out ManualTimeProvider clock)
    {
        clock = new ManualTimeProvider();
        var window = new Monica.App.MainWindow { Width = 1280, Height = 800 };
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        window.DataContext = viewModel;
        viewModel.IsUnlocked = true;
        viewModel.L.SetLanguage("zh-CN");
        viewModel.StatusTimeProvider = clock;
        Drain();
        return (window, viewModel);
    }

    private static void Drain()
    {
        for (var i = 0; i < 60; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
