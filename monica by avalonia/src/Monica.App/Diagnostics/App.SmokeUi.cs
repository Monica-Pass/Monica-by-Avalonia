using System.Runtime;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Data;
using Monica.Platform.Services;

namespace Monica.App;

public partial class App
{
    private const string DefaultSmokeUiUnlockPasswordEnvironmentVariable = "MONICA_SMOKE_UI_UNLOCK_PASSWORD";

    internal readonly record struct SmokeUiViewportSize(double Width, double Height);

    // Every smoke flag starts with the same token, so an automated run is recognizable before any of its
    // individual checks are read. Nothing here is a product switch: it says a machine is measuring this
    // process, and a measurement must not add a network round trip to every host it walks past.
    internal static bool IsSmokeUiRun(string[]? args) =>
        args is { Length: > 0 } && Array.Exists(
            args,
            arg => arg.StartsWith("--smoke-ui", StringComparison.OrdinalIgnoreCase));

    // Asking for the picture path by name is what puts a run back online: a measurement must not pay a
    // round trip per host, but a screenshot of those pictures cannot show one it never fetched.
    internal static bool AllowsSmokeUiWebsiteIcons(string[]? args) =>
        HasSmokeUiFlag(args, "--smoke-ui-website-icons");

    internal static string? GetSmokeUiUnlockPassword(string[]? args)
    {
        var passwordEnvironmentVariable = GetSmokeUiArgument(args, "--smoke-ui-unlock-env");
        if (!string.IsNullOrWhiteSpace(passwordEnvironmentVariable))
        {
            return Environment.GetEnvironmentVariable(passwordEnvironmentVariable.Trim());
        }

        if (HasSmokeUiFlag(args, "--smoke-ui-unlock-env"))
        {
            return Environment.GetEnvironmentVariable(DefaultSmokeUiUnlockPasswordEnvironmentVariable);
        }

        return GetSmokeUiArgument(args, "--smoke-ui-unlock", allowOptionLikeValue: true);
    }

    internal static SmokeUiViewportSize? GetSmokeUiViewportSize(string[]? args)
    {
        var width = GetSmokeUiCount(args, "--smoke-ui-width");
        var height = GetSmokeUiCount(args, "--smoke-ui-height");
        if (width <= 0 && height <= 0)
        {
            return null;
        }

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return new SmokeUiViewportSize(width, height);
    }

    private static string? GetSmokeUiArgument(
        string[]? args,
        string optionName,
        bool allowOptionLikeValue = false)
    {
        if (args is null)
        {
            return null;
        }

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.Ordinal))
            {
                var value = args[index + 1];
                if (!allowOptionLikeValue && IsSmokeUiOptionToken(value))
                {
                    return null;
                }

                return value;
            }
        }

        return null;
    }

    private static bool IsSmokeUiOptionToken(string value) =>
        value.StartsWith("--", StringComparison.Ordinal);

    private static string? GetSmokeUiSection(string[]? args) =>
        GetSmokeUiArgument(args, "--smoke-ui-section");

    private static int GetSmokeUiSelectPasswordCount(string[]? args)
    {
        return GetSmokeUiCount(args, "--smoke-ui-select-passwords");
    }

    private static bool HasSmokeUiFlag(string[]? args, string optionName)
    {
        return args?.Any(arg => string.Equals(arg, optionName, StringComparison.Ordinal)) == true;
    }

    private static int GetSmokeUiOpenNoteCount(string[]? args)
    {
        return GetSmokeUiCount(args, "--smoke-ui-open-notes");
    }

    private static int GetSmokeUiNoteLongLineCount(string[]? args)
    {
        return GetSmokeUiCount(args, "--smoke-ui-note-long-lines");
    }

    private static string? GetSmokeUiNoteMode(string[]? args) =>
        GetSmokeUiArgument(args, "--smoke-ui-note-mode");

    private static string? GetSmokeUiTheme(string[]? args) =>
        GetSmokeUiArgument(args, "--smoke-ui-theme");

    private static int GetSmokeUiCount(string[]? args, string optionName)
    {
        if (args is null)
        {
            return 0;
        }

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.Ordinal) &&
                int.TryParse(args[index + 1], out var count))
            {
                return Math.Max(0, count);
            }
        }

        return 0;
    }

    private static void QueueSmokeUiUnlock(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow mainWindow,
        MainWindowViewModel viewModel,
        DesktopIntegrationCoordinator desktopIntegration,
        string password,
        string? smokeSection,
        int smokePasswordSelectionCount,
        int smokeOpenNoteCount,
        string? smokeNoteMode,
        int smokeNoteLongLineCount,
        string? smokeTheme,
        string? smokeScreenshotDirectory,
        int smokeVaultLoadDelayMilliseconds,
        int smokeMaxVaultLoadMilliseconds,
        bool smokeH04ListInteractions,
        bool smokeNoteEditorChecks,
        bool smokeOtherPagesChecks,
        bool smokeKeyboardChecks,
        bool smokeExitAfterChecks)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MonicaAppDataPaths.OverrideEnvironmentVariable)))
        {
            AppDiagnostics.Info("--smoke-ui-unlock requires MONICA_APPDATA_DIR.");
            if (smokeExitAfterChecks)
            {
                desktop.Shutdown(2);
            }

            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            var smokeSuccess = true;
            await Task.Delay(300);
            await viewModel.InitializeAsync();
            viewModel.SmokeVaultLoadDelayMilliseconds = smokeVaultLoadDelayMilliseconds;
            viewModel.MasterPassword = password;
            await viewModel.UnlockCommand.ExecuteAsync(null);
            var vaultReady = await WaitForSmokeVaultReadyAsync(viewModel, TimeSpan.FromSeconds(30));
            smokeSuccess &= vaultReady;
            // Settings keep landing asynchronously after InitializeAsync returns, and each apply
            // re-writes the theme, so overriding earlier would only be undone before the capture.
            ApplySmokeUiTheme(viewModel, smokeTheme);
            AppDiagnostics.Info(
                $"Smoke UI vault ready result. success={vaultReady}, " +
                $"loadMs={viewModel.LastVaultLoadDurationMilliseconds}, passwords={viewModel.Passwords.Count}");
            if (smokeMaxVaultLoadMilliseconds > 0)
            {
                var loadWithinBudget = vaultReady &&
                    viewModel.LastVaultLoadDurationMilliseconds > 0 &&
                    viewModel.LastVaultLoadDurationMilliseconds <= smokeMaxVaultLoadMilliseconds;
                smokeSuccess &= loadWithinBudget;
                AppDiagnostics.Info(
                    $"Smoke UI vault load budget result. success={loadWithinBudget}, " +
                    $"actualMs={viewModel.LastVaultLoadDurationMilliseconds}, maxMs={smokeMaxVaultLoadMilliseconds}");
            }

            if (HasSmokeUiFlag(Environment.GetCommandLineArgs(), "--smoke-ui-status-notice"))
            {
                smokeSuccess &= await RunSmokeUiStatusNoticeRetirementAsync(viewModel);
            }

            if (!string.IsNullOrWhiteSpace(smokeSection) &&
                viewModel.SelectSectionCommand.CanExecute(smokeSection))
            {
                viewModel.SelectSectionCommand.Execute(smokeSection);
                AppDiagnostics.Info($"Smoke UI section selected. section={smokeSection}");
            }

            if (smokePasswordSelectionCount > 0)
            {
                smokeSuccess &= await RunSmokeUiPasswordSelectionsAsync(viewModel, smokePasswordSelectionCount);
            }

            if (smokeOpenNoteCount > 0)
            {
                await RunSmokeUiOpenNotesAsync(viewModel, smokeOpenNoteCount);
            }

            ApplySmokeUiLongNoteContent(viewModel, smokeNoteLongLineCount);
            ApplySmokeUiNoteMode(viewModel, smokeNoteMode);
            if (smokeNoteEditorChecks)
            {
                var success = await mainWindow.RunSmokeUiNoteEditorChecksAsync();
                smokeSuccess &= success;
                AppDiagnostics.Info(
                    $"Smoke UI note editor checks result. success={success}, status={viewModel.StatusMessage}");
            }

            if (smokeH04ListInteractions)
            {
                smokeSuccess &= await RunSmokeUiH04ListInteractionsAsync(viewModel);
            }

            if (smokeOtherPagesChecks)
            {
                var success = await mainWindow.RunSmokeUiOtherPagesChecksAsync();
                smokeSuccess &= success;
                AppDiagnostics.Info(
                    $"Smoke UI other pages checks result. success={success}, status={viewModel.StatusMessage}");
            }

            if (smokeKeyboardChecks)
            {
                var success = await mainWindow.RunSmokeUiKeyboardChecksAsync();
                smokeSuccess &= success;
                AppDiagnostics.Info(
                    $"Smoke UI keyboard checks result. success={success}, status={viewModel.StatusMessage}");
            }

            if (HasSmokeUiFlag(Environment.GetCommandLineArgs(), "--smoke-ui-batch-select"))
            {
                // The same command the header chip and Ctrl+A raise, so this proves the published
                // build reaches the bulk state — and leaves it on screen for the capture below.
                viewModel.SelectAllVaultRowsCommand.Execute(null);
                var batchSelected = viewModel.HasVaultBatchSelection && viewModel.VaultBatchCount > 0;
                smokeSuccess &= batchSelected;
                AppDiagnostics.Info(
                    $"Smoke UI batch selection result. success={batchSelected}, count={viewModel.VaultBatchCount}");
            }

            if (!string.IsNullOrWhiteSpace(smokeScreenshotDirectory))
            {
                var success = await mainWindow.RunSmokeUiOtherPagesScreenshotsAsync(smokeScreenshotDirectory);
                smokeSuccess &= success;
                AppDiagnostics.Info(
                    $"Smoke UI other pages screenshots result. success={success}, directory={smokeScreenshotDirectory}");
            }

            var keepassProbePath = GetSmokeUiArgument(
                Environment.GetCommandLineArgs(), "--smoke-ui-keepass-file");
            if (!string.IsNullOrWhiteSpace(keepassProbePath))
            {
                var keepassPassword = GetSmokeUiArgument(
                    Environment.GetCommandLineArgs(), "--smoke-ui-keepass-password");
                var probeSuccess = await RunSmokeUiKeePassMemoryProbeAsync(
                    viewModel,
                    keepassProbePath,
                    keepassPassword);
                smokeSuccess &= probeSuccess;
            }

            var keepassEditPath = GetSmokeUiArgument(
                Environment.GetCommandLineArgs(), "--smoke-ui-keepass-edit");
            if (!string.IsNullOrWhiteSpace(keepassEditPath))
            {
                var keepassPassword = GetSmokeUiArgument(
                    Environment.GetCommandLineArgs(), "--smoke-ui-keepass-password");
                var editShotSuccess = await mainWindow.RunSmokeUiKeePassEditShotAsync(
                    keepassEditPath,
                    keepassPassword ?? "",
                    smokeScreenshotDirectory);
                smokeSuccess &= editShotSuccess;
            }

            var keepassManagePath = GetSmokeUiArgument(
                Environment.GetCommandLineArgs(), "--smoke-ui-keepass-manage");
            if (!string.IsNullOrWhiteSpace(keepassManagePath))
            {
                var keepassPassword = GetSmokeUiArgument(
                    Environment.GetCommandLineArgs(), "--smoke-ui-keepass-password");
                var manageShotSuccess = await mainWindow.RunSmokeUiKeePassManageShotAsync(
                    keepassManagePath,
                    keepassPassword ?? "",
                    smokeScreenshotDirectory);
                smokeSuccess &= manageShotSuccess;
            }

            var keepassSearchPath = GetSmokeUiArgument(
                Environment.GetCommandLineArgs(), "--smoke-ui-keepass-search");
            if (!string.IsNullOrWhiteSpace(keepassSearchPath))
            {
                var keepassPassword = GetSmokeUiArgument(
                    Environment.GetCommandLineArgs(), "--smoke-ui-keepass-password");
                // The default hits the address field of every fixture entry, which is the case the folded
                // tree cannot show at all until something is typed.
                var keepassQuery = GetSmokeUiArgument(
                    Environment.GetCommandLineArgs(), "--smoke-ui-keepass-search-query") ?? "example.com";
                var searchShotSuccess = await mainWindow.RunSmokeUiKeePassSearchShotAsync(
                    keepassSearchPath,
                    keepassPassword ?? "",
                    keepassQuery,
                    smokeScreenshotDirectory);
                smokeSuccess &= searchShotSuccess;
            }

            if (HasSmokeUiFlag(Environment.GetCommandLineArgs(), "--smoke-ui-keepass-create"))
            {
                // No vault argument: this frame is the form a person reaches with nothing open yet, and the
                // password only has to be something to type, so it reuses the fixture's own literal.
                var keepassPassword = GetSmokeUiArgument(
                    Environment.GetCommandLineArgs(), "--smoke-ui-keepass-password");
                if (string.IsNullOrEmpty(keepassPassword))
                {
                    AppDiagnostics.Info(
                        "Smoke UI KeePass create shot failed. reason=no-password-argument");
                    smokeSuccess = false;
                }
                else
                {
                    smokeSuccess &= await mainWindow.RunSmokeUiKeePassCreateShotAsync(
                        keepassPassword,
                        smokeScreenshotDirectory);
                }
            }

            var keepassRecentPath = GetSmokeUiArgument(
                Environment.GetCommandLineArgs(), "--smoke-ui-keepass-recent");
            if (!string.IsNullOrWhiteSpace(keepassRecentPath))
            {
                var keepassPassword = GetSmokeUiArgument(
                    Environment.GetCommandLineArgs(), "--smoke-ui-keepass-password");
                if (string.IsNullOrEmpty(keepassPassword))
                {
                    AppDiagnostics.Info(
                        "Smoke UI KeePass recent shot failed. reason=no-password-argument");
                    smokeSuccess = false;
                }
                else
                {
                    smokeSuccess &= await mainWindow.RunSmokeUiKeePassRecentShotAsync(
                        keepassRecentPath,
                        keepassPassword,
                        smokeScreenshotDirectory);
                }
            }

            var keepassHandoffPath = GetSmokeUiArgument(
                Environment.GetCommandLineArgs(), "--smoke-ui-keepass-handoff");
            if (!string.IsNullOrWhiteSpace(keepassHandoffPath))
            {
                var keepassPassword = GetSmokeUiArgument(
                    Environment.GetCommandLineArgs(), "--smoke-ui-keepass-password");
                if (string.IsNullOrEmpty(keepassPassword))
                {
                    AppDiagnostics.Info(
                        "Smoke UI KeePass handoff shot failed. reason=no-password-argument");
                    smokeSuccess = false;
                }
                else
                {
                    smokeSuccess &= await mainWindow.RunSmokeUiKeePassHandoffShotAsync(
                        keepassHandoffPath,
                        keepassPassword,
                        smokeScreenshotDirectory);
                }
            }

            // Last of the KeePass frames on purpose: this one leaves its revert staged as unsaved edits,
            // and every frame after it would otherwise have to open over them.
            var keepassHistoryPath = GetSmokeUiArgument(
                Environment.GetCommandLineArgs(), "--smoke-ui-keepass-history");
            if (!string.IsNullOrWhiteSpace(keepassHistoryPath))
            {
                var keepassPassword = GetSmokeUiArgument(
                    Environment.GetCommandLineArgs(), "--smoke-ui-keepass-password");
                if (string.IsNullOrEmpty(keepassPassword))
                {
                    AppDiagnostics.Info(
                        "Smoke UI KeePass history shot failed. reason=no-password-argument");
                    smokeSuccess = false;
                }
                else
                {
                    smokeSuccess &= await mainWindow.RunSmokeUiKeePassHistoryShotAsync(
                        keepassHistoryPath,
                        keepassPassword,
                        smokeScreenshotDirectory);
                }
            }

            if (HasSmokeUiFlag(Environment.GetCommandLineArgs(), "--smoke-ui-autotype"))
            {
                smokeSuccess &= await RunSmokeUiAutoTypeProbeAsync(viewModel, desktopIntegration);
            }

            var lockCycleSuccess = true;
            if (HasSmokeUiFlag(Environment.GetCommandLineArgs(), "--smoke-ui-lock-after-checks"))
            {
                lockCycleSuccess = await RunSmokeUiPostLockMemorySampleAsync(viewModel, password);
                smokeSuccess &= lockCycleSuccess;
            }

            AppDiagnostics.Info(
                $"Smoke UI release gate completed. success={smokeSuccess}, " +
                $"loadMs={viewModel.LastVaultLoadDurationMilliseconds}, passwords={viewModel.Passwords.Count}, " +
                $"notes={viewModel.NoteItems.Count}, totp={viewModel.TotpItems.Count}, wallet={viewModel.WalletItems.Count}");
            if (smokeExitAfterChecks)
            {
                desktop.Shutdown(smokeSuccess ? 0 : 1);
            }
        }, DispatcherPriority.Background);
    }

    private const int LockedSettleIntervalSeconds = 5;
    private const int LockedSettleRounds = 10;
    private const int LockedSettleTailRounds = 5;

    private static async Task<bool> RunSmokeUiPostLockMemorySampleAsync(
        MainWindowViewModel viewModel,
        string password)
    {
        ReportSmokeUiMemory(viewModel, "unlocked");
        var expectedPasswords = viewModel.Passwords.Count;
        var expectedNotes = viewModel.NoteItems.Count;
        var expectedTotp = viewModel.TotpItems.Count;
        var expectedWallet = viewModel.WalletItems.Count;
        viewModel.LockCommand.Execute(null);
        var locked = await WaitForSmokeConditionAsync(() => !viewModel.IsUnlocked, TimeSpan.FromSeconds(10));
        // The lock handler schedules its own blocking compaction one second later; give it time to
        // land before reading anything. Every sample below compacts first, like the KeePass probe, so
        // a rooted leak still shows up - it is the only thing that can survive a full compaction.
        await Task.Delay(2500);
        CompactSmokeUiMemory();
        var maxLockedMemoryMb = GetSmokeUiCount(
            Environment.GetCommandLineArgs(), "--smoke-ui-max-memory-mb");
        // A locked private-bytes budget is a claim about what the shell keeps, but the locked process
        // is not readable the moment it locks. Measured on the shipped build, after the phases above
        // pushed this process past 300 MB, the number decayed for half a minute while the thread pool
        // shed workers - 123.8/120.2/120.8/119.3/121.2 and 121.3/123.4/117.2/115.7/118.2/113.4/113.7
        // at five second intervals, two runs of the same binary - while the managed heap stayed level
        // at 25-28 MB. A single early sample therefore graded whichever transient it landed on, and
        // the gate went red on runs where nothing had changed. A stop-when-two-readings-agree rule
        // does not fix it either: with jitter of this size two samples agree by luck (the first run
        // above "converged" at 121.2). So sample a fixed window past the decay and judge the median
        // of its second half, which is a number the next run can be expected to reproduce.
        var settleSamples = new List<double>();
        for (var round = 0; round < LockedSettleRounds; round++)
        {
            if (round > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(LockedSettleIntervalSeconds));
            }

            CompactSmokeUiMemory();
            settleSamples.Add(ReportSmokeUiMemory(viewModel, $"locked-settling-{round + 1}"));
        }

        var tail = settleSamples.Skip(settleSamples.Count - LockedSettleTailRounds).ToList();
        tail.Sort();
        var settledPrivateMb = tail[tail.Count / 2];
        AppDiagnostics.Info(
            $"Smoke UI locked settle result. rounds={settleSamples.Count}, " +
            $"intervalSeconds={LockedSettleIntervalSeconds}, " +
            $"trajectory={string.Join("/", settleSamples.ConvertAll(sample => sample.ToString("F1")))}, " +
            $"tailRounds={LockedSettleTailRounds}, tailMinMB={tail[0]:F1}, " +
            $"tailMedianMB={settledPrivateMb:F1}, tailMaxMB={tail[^1]:F1}");
        var memoryWithinBudget = maxLockedMemoryMb <= 0 || settledPrivateMb <= maxLockedMemoryMb;
        if (maxLockedMemoryMb > 0)
        {
            AppDiagnostics.Info(
                $"Smoke UI memory budget result. success={memoryWithinBudget}, " +
                $"lockedPrivateMB={settledPrivateMb:F1}, maxMB={maxLockedMemoryMb}");
        }

        // The shell caches are released on lock, so re-unlocking must rebuild them from the vault.
        viewModel.MasterPassword = password;
        await viewModel.UnlockCommand.ExecuteAsync(null);
        var restored = await WaitForSmokeConditionAsync(
            () => viewModel.IsUnlocked &&
                  viewModel.Passwords.Count >= expectedPasswords &&
                  viewModel.NoteItems.Count >= expectedNotes &&
                  viewModel.TotpItems.Count >= expectedTotp &&
                  viewModel.WalletItems.Count >= expectedWallet,
            TimeSpan.FromSeconds(20));
        AppDiagnostics.Info(
            $"Smoke UI lock cycle result. locked={locked}, reUnlocked={restored}, " +
            $"passwords={viewModel.Passwords.Count}/{expectedPasswords}, " +
            $"notes={viewModel.NoteItems.Count}/{expectedNotes}, " +
            $"totp={viewModel.TotpItems.Count}/{expectedTotp}, " +
            $"wallet={viewModel.WalletItems.Count}/{expectedWallet}");
        return locked && restored && memoryWithinBudget;
    }

    private static async Task<bool> RunSmokeUiKeePassMemoryProbeAsync(
        MainWindowViewModel viewModel,
        string filePath,
        string? password)
    {
        // No console harness can answer whether the shipped process gives the decrypted model back,
        // so this runs the same service the app runs and reports private bytes at every stage. Growth
        // is measured against this process's own baseline, and the file bytes stay alive for as long
        // as the session does - the import page holds the picked file the same way.
        var args = Environment.GetCommandLineArgs();
        var maxGrowthMb = GetSmokeUiCount(args, "--smoke-ui-keepass-max-growth-mb");
        var streamDetails = HasSmokeUiFlag(args, "--smoke-ui-keepass-stream-details");
        // Earlier smoke phases leave uncollected garbage behind, so compact both ends of the
        // measurement or the growth number would just be reporting the other phases' debris.
        CompactSmokeUiMemory();
        var baselineMb = ReportSmokeUiMemory(viewModel, "keepass-before");
        try
        {
            var openStopwatch = System.Diagnostics.Stopwatch.StartNew();
            byte[]? content = await File.ReadAllBytesAsync(filePath);
            var fileBytesMb = content.Length / 1048576d;
            var details = 0;
            var streamMs = 0L;
            var streamedMb = 0d;
            var session = await new KeePassVaultService().OpenAsync(
                content,
                Path.GetFileName(filePath),
                password);
            openStopwatch.Stop();
            var entryCount = session.EntryCount;
            var groupCount = session.GroupCount;
            var openMb = ReportSmokeUiMemory(viewModel, "keepass-open");
            if (streamDetails)
            {
                var streamStopwatch = System.Diagnostics.Stopwatch.StartNew();
                await foreach (var detail in session.ReadDetailsAsync())
                {
                    details++;
                }

                streamStopwatch.Stop();
                streamMs = streamStopwatch.ElapsedMilliseconds;
                streamedMb = ReportSmokeUiMemory(viewModel, "keepass-streamed");
            }

            session.Dispose();
            content = null;
            var releasedMb = ReportSmokeUiMemory(viewModel, "keepass-released");
            CompactSmokeUiMemory();
            var collectedMb = ReportSmokeUiMemory(viewModel, "keepass-collected");
            var growthMb = collectedMb - baselineMb;
            var withinBudget = maxGrowthMb <= 0 || growthMb <= maxGrowthMb;
            AppDiagnostics.Info(
                $"Smoke UI KeePass probe result. success={withinBudget}, file={Path.GetFileName(filePath)}, " +
                $"fileMB={fileBytesMb:F2}, entries={entryCount}, groups={groupCount}, " +
                $"details={details}, openMs={openStopwatch.ElapsedMilliseconds}, streamMs={streamMs}, " +
                $"baselineMB={baselineMb:F1}, openMB={openMb:F1}, streamedMB={streamedMb:F1}, " +
                $"releasedMB={releasedMb:F1}, collectedMB={collectedMb:F1}, growthMB={growthMb:F1}, " +
                $"maxGrowthMB={maxGrowthMb}");
            return withinBudget;
        }
        catch (Exception error)
        {
            AppDiagnostics.Error(
                $"Smoke UI KeePass probe failed. file={Path.GetFileName(filePath)}",
                error);
            return false;
        }
    }

    // Whether the shipped build really hands keystrokes to a foreign window is not something an
    // interface test can answer: RegisterHotKey, the message pump, the foreground-window lookup and
    // SendInput all only exist on a desktop. This flips the setting on through the same path the
    // settings page uses, confirms the desktop accepted the gesture, and then waits for a key press
    // driven from outside the process. The external observer — not this log — decides what landed in
    // the target, so no credential material is ever written here.
    private static async Task<bool> RunSmokeUiAutoTypeProbeAsync(
        MainWindowViewModel viewModel,
        DesktopIntegrationCoordinator desktopIntegration)
    {
        var args = Environment.GetCommandLineArgs();
        var timeoutSeconds = GetSmokeUiCount(args, "--smoke-ui-autotype-timeout");
        if (timeoutSeconds <= 0)
        {
            timeoutSeconds = 120;
        }

        viewModel.AutoTypeEnabled = true;
        var armed = await WaitForSmokeConditionAsync(
            () => desktopIntegration.IsAutoTypeHotkeyRegistered,
            TimeSpan.FromSeconds(15));
        AppDiagnostics.Info(
            $"Smoke UI auto type armed. armed={armed}, " +
            $"gesture={viewModel.AutoTypeHotkey}, " +
            $"registrationError={viewModel.AutoTypeRegistrationError.Length > 0}");
        var pressed = await WaitForSmokeConditionAsync(
            () => viewModel.LastAutoTypeOutcome != MainWindowViewModel.AutoTypeOutcome.None,
            TimeSpan.FromSeconds(timeoutSeconds));
        // Surfacing the list is a step, not an answer, so the probe keeps watching while the popup is
        // up; an external harness filters and confirms, and only the terminal outcome decides.
        var pickerSurfaced = pressed && IsAutoTypePickerOutcome(viewModel.LastAutoTypeOutcome);
        if (pickerSurfaced)
        {
            var pickerState = viewModel.LastAutoTypeOutcome;
            await WaitForSmokeConditionAsync(
                () => viewModel.LastAutoTypeOutcome != pickerState || !viewModel.IsAutoTypePickerOpen,
                TimeSpan.FromSeconds(timeoutSeconds));
        }

        var success = armed && pressed &&
            viewModel.LastAutoTypeOutcome == MainWindowViewModel.AutoTypeOutcome.Typed;
        AppDiagnostics.Info(
            $"Smoke UI auto type result. success={success}, armed={armed}, pressed={pressed}, " +
            $"pickerSurfaced={pickerSurfaced}, pickerOpen={viewModel.IsAutoTypePickerOpen}, " +
            $"outcome={viewModel.LastAutoTypeOutcome}, matches={viewModel.LastAutoTypeMatches.Count}");
        return success;
    }

    private static bool IsAutoTypePickerOutcome(MainWindowViewModel.AutoTypeOutcome outcome) =>
        outcome is MainWindowViewModel.AutoTypeOutcome.PickerForMatches
            or MainWindowViewModel.AutoTypeOutcome.PickerForAllEntries;

    private static void CompactSmokeUiMemory()
    {
        // A private-bytes sample only means "what this process still holds" once the heap has been
        // compacted; otherwise it reports whatever the GC had not bothered to collect yet. The recipe is
        // the product's own lock-time shedding rather than a stronger one only the probe knows about, so
        // the gate grades the compaction a locked user actually gets.
        MainWindowViewModel.CompactShellMemory();
        GC.WaitForPendingFinalizers();
        MainWindowViewModel.CompactShellMemory();
    }

    private static double ReportSmokeUiMemory(MainWindowViewModel viewModel, string stage)
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var workingSetMb = Environment.WorkingSet / 1048576d;
        var privateMb = process.PrivateMemorySize64 / 1048576d;
        var peakWorkingSetMb = process.PeakWorkingSet64 / 1048576d;
        var managedMb = GC.GetTotalMemory(forceFullCollection: false) / 1048576d;
        // `managedHeapMB` is live object bytes, which is not what the process still holds: the runtime
        // keeps segments it committed while the unlocked shell was filling the heap. Until the locked
        // floor is split into "committed by the GC" and "not the GC's", the 87-100MB of private bytes
        // outside the live heap has no name, and the run-to-run step cannot be attributed to anything.
        var gcInfo = GC.GetGCMemoryInfo();
        var gcCommittedMb = gcInfo.TotalCommittedBytes / 1048576d;
        var gcHeapMb = gcInfo.HeapSizeBytes / 1048576d;
        AppDiagnostics.Info(
            $"Smoke UI memory. stage={stage}, unlocked={viewModel.IsUnlocked}, " +
            $"workingSetMB={workingSetMb:F1}, privateMB={privateMb:F1}, " +
            $"peakWorkingSetMB={peakWorkingSetMb:F1}, managedHeapMB={managedMb:F1}, " +
            $"gcCommittedMB={gcCommittedMb:F1}, gcHeapMB={gcHeapMb:F1}, " +
            $"nonGcPrivateMB={privateMb - gcCommittedMb:F1}, " +
            $"serverGC={GCSettings.IsServerGC}, cpuCount={Environment.ProcessorCount}, " +
            $"handles={process.HandleCount}, threads={process.Threads.Count}");
        return privateMb;
    }

    private static void ApplySmokeUiViewportSize(MainWindow mainWindow, SmokeUiViewportSize? smokeViewportSize)
    {
        if (smokeViewportSize is not { } viewportSize)
        {
            return;
        }

        mainWindow.Width = Math.Max(mainWindow.MinWidth, viewportSize.Width);
        mainWindow.Height = Math.Max(mainWindow.MinHeight, viewportSize.Height);
        AppDiagnostics.Info(
            $"Smoke UI viewport applied. width={mainWindow.Width}, height={mainWindow.Height}");
    }

    private static void ApplySmokeUiTheme(MainWindowViewModel viewModel, string? smokeTheme)
    {
        if (string.IsNullOrWhiteSpace(smokeTheme))
        {
            return;
        }

        var normalizedTheme = smokeTheme.Trim().ToLowerInvariant() switch
        {
            "light" => "light",
            "dark" => "dark",
            "highcontrast" => "high-contrast",
            "high-contrast" => "high-contrast",
            "contrast" => "high-contrast",
            "default" => "system",
            "system" => "system",
            _ => ""
        };

        if (string.IsNullOrWhiteSpace(normalizedTheme))
        {
            AppDiagnostics.Info($"Smoke UI theme ignored. theme={smokeTheme}");
            return;
        }

        viewModel.SettingsTheme = normalizedTheme;
        AppDiagnostics.Info($"Smoke UI theme applied. theme={normalizedTheme}");
    }

    private static void ApplySmokeUiLongNoteContent(MainWindowViewModel viewModel, int requestedLineCount)
    {
        if (requestedLineCount <= 0)
        {
            return;
        }

        if (viewModel.SelectedNoteTab is null)
        {
            AppDiagnostics.Info("Smoke UI long note ignored. reason=no-selected-note-tab");
            return;
        }

        var lineTarget = Math.Clamp(requestedLineCount, 12, 500);
        var builder = new System.Text.StringBuilder();
        var lines = 0;
        void AppendLine(string value = "")
        {
            builder.AppendLine(value);
            lines++;
        }

        AppendLine("# Smoke long Markdown note");
        AppendLine();
        AppendLine("This deterministic note exercises wrapping, line numbers, preview rendering, split mode, tables, lists, code, and links.");
        AppendLine();
        AppendLine("## Checklist");
        AppendLine("- [x] Editor content starts at the same x position as preview content.");
        AppendLine("- [x] Line numbers stay outside the text column.");
        AppendLine("- [ ] Long lines wrap without horizontal layout drift.");
        AppendLine();
        AppendLine("## Table");
        AppendLine("| Area | Expected behavior |");
        AppendLine("| --- | --- |");
        AppendLine("| Tabs | Fit the viewport and keep actions visible |");
        AppendLine("| Editor | Keep focus chrome invisible and text aligned |");
        AppendLine("| Preview | Render Markdown without oversized headings |");
        AppendLine();
        AppendLine("## Code");
        AppendLine("```csharp");
        AppendLine("var layout = new NoteWorkspaceLayout(mode, viewportWidth);");
        AppendLine("layout.AssertNoOverflow();");
        AppendLine("```");
        AppendLine();

        var paragraph = 1;
        while (lines < lineTarget)
        {
            AppendLine($"### Section {paragraph}");
            AppendLine($"Paragraph {paragraph} contains a deliberately long sentence so the editor can prove that wrapping remains stable at small widths without creating a horizontal scrollbar or shifting the preview column.");
            AppendLine($"- Nested thought {paragraph}.1");
            AppendLine($"- Nested thought {paragraph}.2 with `inline code` and a [local link](https://example.invalid/monica-smoke).");
            AppendLine();
            paragraph++;
        }

        viewModel.NoteIsMarkdown = true;
        viewModel.NoteTitle = $"Smoke long Markdown note ({lineTarget} lines)";
        viewModel.NoteContent = builder.ToString();
        AppDiagnostics.Info(
            $"Smoke UI long note applied. requestedLines={requestedLineCount}, targetLines={lineTarget}, actualLines={lines}");
    }

    private static void ApplySmokeUiNoteMode(MainWindowViewModel viewModel, string? smokeNoteMode)
    {
        if (string.IsNullOrWhiteSpace(smokeNoteMode))
        {
            return;
        }

        var normalizedMode = smokeNoteMode.Trim().ToLowerInvariant();
        viewModel.NoteIsMarkdown = true;
        switch (normalizedMode)
        {
            case "edit":
                viewModel.NotePreviewMode = false;
                viewModel.NoteSplitPreviewMode = false;
                break;
            case "preview":
                viewModel.NoteSplitPreviewMode = false;
                viewModel.NotePreviewMode = true;
                break;
            case "split":
                viewModel.NotePreviewMode = false;
                viewModel.NoteSplitPreviewMode = true;
                break;
            default:
                AppDiagnostics.Info($"Smoke UI note mode ignored. mode={smokeNoteMode}");
                return;
        }

        AppDiagnostics.Info(
            $"Smoke UI note mode applied. mode={normalizedMode}, " +
            $"preview={viewModel.NotePreviewMode}, split={viewModel.NoteSplitPreviewMode}");
    }

    private static async Task RunSmokeUiOpenNotesAsync(MainWindowViewModel viewModel, int requestedCount)
    {
        var vaultReady = await WaitForSmokeVaultReadyAsync(viewModel, TimeSpan.FromSeconds(10));
        var availableNotes = viewModel.FilteredNoteItems;
        var entries = availableNotes
            .Take(Math.Min(requestedCount, availableNotes.Count))
            .ToArray();
        AppDiagnostics.Info(
            $"Smoke UI note tabs open started. requested={requestedCount}, vaultReady={vaultReady}, " +
            $"available={availableNotes.Count}, count={entries.Length}");

        foreach (var entry in entries)
        {
            viewModel.OpenNoteCommand.Execute(entry);
            await Task.Delay(20);
            AppDiagnostics.Info(
                $"Smoke UI note tab opened. id={entry.Id}, title={entry.Title}, " +
                $"openTabs={viewModel.OpenNoteTabs.Count}, selected={viewModel.SelectedNoteTab?.Title}");
        }
    }

    private static async Task<bool> RunSmokeUiPasswordSelectionsAsync(MainWindowViewModel viewModel, int requestedCount)
    {
        var success = true;
        var vaultReady = await WaitForSmokeVaultReadyAsync(viewModel, TimeSpan.FromSeconds(10));
        success &= vaultReady;
        var availablePasswords = viewModel.Passwords;
        var entries = availablePasswords
            .Take(Math.Min(requestedCount, availablePasswords.Count))
            .ToArray();
        success &= requestedCount <= 0 || entries.Length > 0;
        AppDiagnostics.Info(
            $"Smoke UI password selection started. requested={requestedCount}, vaultReady={vaultReady}, " +
            $"available={availablePasswords.Count}, count={entries.Length}");

        foreach (var entry in entries)
        {
            success &= await SelectSmokePasswordAsync(viewModel, entry, "sequence");
        }

        var edgeEntry = availablePasswords.FirstOrDefault(
            entry => entry.Title.StartsWith("Smoke Edge Long Password", StringComparison.Ordinal));
        if (edgeEntry is not null && entries.LastOrDefault()?.Id != edgeEntry.Id)
        {
            success &= await SelectSmokePasswordAsync(viewModel, edgeEntry, "edge");
        }

        AppDiagnostics.Info(
            $"Smoke UI password selection completed. success={success}, requested={requestedCount}, selected={entries.Length}");
        return success;
    }

    private static async Task<bool> SelectSmokePasswordAsync(
        MainWindowViewModel viewModel,
        PasswordEntry entry,
        string reason)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        viewModel.SelectedPassword = entry;
        AppDiagnostics.Info(
            $"Smoke UI password selection setter completed in {stopwatch.ElapsedMilliseconds} ms. " +
            $"id={entry.Id}, reason={reason}");
        var detailsReady = await WaitForSmokeSelectedPasswordDetailsAsync(viewModel, entry.Id, TimeSpan.FromSeconds(3));
        AppDiagnostics.Info(
            $"Smoke UI password selection details {(detailsReady ? "ready" : "timeout")} in {stopwatch.ElapsedMilliseconds} ms. " +
            $"id={entry.Id}, reason={reason}, hasCurrent={viewModel.HasCurrentSelectedPasswordDetails}");
        return detailsReady;
    }

    // The status bar's self-clearing is driven by a dispatcher timer, and a dispatcher timer is the
    // one thing a headless test cannot show ticking. This is that proof, on the build that ships.
    // It runs before every other phase and raises nothing but a filter reset, so the shell stays on
    // the page it landed on and the memory readings after this point measure the same set.
    private static async Task<bool> RunSmokeUiStatusNoticeRetirementAsync(MainWindowViewModel viewModel)
    {
        // Whatever the load sequence left in the bar is a standing line - a title or a prompt the
        // user still has to act on - and those must never be put on the retirement clock.
        var standingHeld = viewModel.StatusMessage;
        var standingArmed = viewModel.IsStatusNoticePending;

        viewModel.ClearTotpFiltersCommand.Execute(null);
        var raised = !string.IsNullOrWhiteSpace(viewModel.StatusMessage);
        var armedForNotice = viewModel.IsStatusNoticePending;
        var retired = await WaitForSmokeConditionAsync(
            () => string.IsNullOrWhiteSpace(viewModel.StatusMessage),
            TimeSpan.FromSeconds(15));

        var success = raised && armedForNotice && retired && !standingArmed;
        AppDiagnostics.Info(
            $"Smoke UI status notice retirement result. success={success}, raised={raised}, " +
            $"armedForNotice={armedForNotice}, retired={retired}, standingArmed={standingArmed}, " +
            $"standingLength={standingHeld.Length}");
        return success;
    }

    private static async Task<bool> RunSmokeUiH04ListInteractionsAsync(MainWindowViewModel viewModel)
    {
        var failures = new List<string>();

        void Check(string name, bool condition, string detail = "")
        {
            if (condition)
            {
                AppDiagnostics.Info($"Smoke UI H04 check passed. check={name}, {detail}");
                return;
            }

            failures.Add(name);
            AppDiagnostics.Info($"Smoke UI H04 check failed. check={name}, {detail}");
        }

        try
        {
            var vaultReady = await WaitForSmokeVaultReadyAsync(viewModel, TimeSpan.FromSeconds(10));
            Check("vault-ready", vaultReady, $"passwords={viewModel.Passwords.Count}");
            viewModel.ClearVaultFiltersCommand.Execute(null);

            viewModel.SelectSectionCommand.Execute("Totp");
            await Task.Delay(50);
            var totp = viewModel.TotpItems.FirstOrDefault();
            Check("totp-row-present", totp is not null, $"count={viewModel.TotpItems.Count}");
            if (totp is not null)
            {
                viewModel.SelectedTotpItem = totp;
                await Task.Delay(50);
                Check(
                    "totp-details-selected",
                    viewModel.HasSelectedTotpItem && viewModel.SelectedTotpDetails?.Item.Id == totp.Id,
                    $"selected={viewModel.SelectedTotpItem?.Title}");
                viewModel.ToggleTotpSelectionCommand.Execute(totp);
                Check(
                    "totp-batch-selection",
                    viewModel.HasSelectedTotpItems && viewModel.SelectedTotpCount == 1,
                    $"selectedCount={viewModel.SelectedTotpCount}");
                viewModel.ClearTotpSelectionCommand.Execute(null);
                Check(
                    "totp-clear-selection",
                    !viewModel.HasSelectedTotpItems && viewModel.SelectedTotpCount == 0,
                    $"selectedCount={viewModel.SelectedTotpCount}");
            }

            viewModel.SelectSectionCommand.Execute("Cards");
            await Task.Delay(50);
            var wallets = viewModel.WalletItems.ToArray();
            Check("wallet-rows-present", wallets.Length >= 2, $"count={wallets.Length}");
            if (wallets.Length > 0)
            {
                viewModel.SelectedWalletItem = wallets[0];
                await Task.Delay(50);
                Check(
                    "wallet-details-selected",
                    viewModel.HasSelectedWalletItem && viewModel.SelectedWalletDetails?.Item.Id == wallets[0].Id,
                    $"selected={viewModel.SelectedWalletItem?.Title}");
                if (wallets.Length > 1)
                {
                    viewModel.ShowWalletDetailsCommand.Execute(wallets[1]);
                    await Task.Delay(50);
                    Check(
                        "wallet-row-command-selects-details",
                        viewModel.SelectedWalletItem?.Id == wallets[1].Id &&
                        viewModel.SelectedWalletDetails?.Item.Id == wallets[1].Id,
                        $"selected={viewModel.SelectedWalletItem?.Title}");
                }

                viewModel.ToggleWalletSelectionCommand.Execute(wallets[0]);
                Check(
                    "wallet-batch-selection",
                    viewModel.HasSelectedWalletItems && viewModel.SelectedWalletCount == 1,
                    $"selectedCount={viewModel.SelectedWalletCount}");
                viewModel.ClearWalletSelectionCommand.Execute(null);
                Check(
                    "wallet-clear-selection",
                    !viewModel.HasSelectedWalletItems && viewModel.SelectedWalletCount == 0,
                    $"selectedCount={viewModel.SelectedWalletCount}");
            }

            viewModel.SelectSectionCommand.Execute("Archive");
            viewModel.ArchiveSearchText = "Smoke";
            await Task.Delay(50);
            var archived = viewModel.FilteredArchivedPasswords.ToArray();
            Check(
                "archive-filtered-row-present",
                archived.Any(item => string.Equals(item.Title, "Smoke Archived Account", StringComparison.Ordinal)),
                $"count={archived.Length}");

            viewModel.SelectSectionCommand.Execute("RecycleBin");
            viewModel.RecycleBinSearchText = "Smoke";
            await Task.Delay(50);
            var deleted = viewModel.FilteredDeletedPasswords.ToArray();
            Check(
                "recycle-filtered-row-present",
                deleted.Any(item => string.Equals(item.Title, "Smoke Deleted Account", StringComparison.Ordinal)),
                $"count={deleted.Length}");
            viewModel.ClearArchiveSearchCommand.Execute(null);
            viewModel.ClearRecycleBinSearchCommand.Execute(null);

            viewModel.SelectSectionCommand.Execute("Timeline");
            var timelineReady = await WaitForSmokeConditionAsync(
                () => viewModel.TimelineEntries.Count > 0,
                TimeSpan.FromSeconds(3));
            Check("timeline-rows-present", timelineReady, $"count={viewModel.TimelineEntries.Count}");

            viewModel.SelectSectionCommand.Execute("SecurityAnalysis");
            viewModel.RefreshSecurityAnalysis();
            await Task.Delay(50);
            Check(
                "security-summary-present",
                viewModel.SecuritySummaryItems.Count >= 3,
                $"summary={viewModel.SecuritySummaryItems.Count}");
            Check(
                "security-issue-list-present",
                viewModel.SecurityIssueItems.Count > 0,
                $"issues={viewModel.SecurityIssueItems.Count}");
        }
        catch (Exception ex)
        {
            failures.Add("exception");
            AppDiagnostics.Error("Smoke UI H04 list interactions failed", ex);
        }

        AppDiagnostics.Info(
            $"Smoke UI H04 list interactions completed. success={failures.Count == 0}, " +
            $"failureCount={failures.Count}, failures={string.Join(",", failures)}");
        return failures.Count == 0;
    }

    private static async Task<bool> WaitForSmokeVaultReadyAsync(MainWindowViewModel viewModel, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (viewModel.IsUnlocked && !viewModel.IsLoadingVault && viewModel.Passwords.Count > 0)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    private static async Task<bool> WaitForSmokeConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }

    private static async Task<bool> WaitForSmokeSelectedPasswordDetailsAsync(
        MainWindowViewModel viewModel,
        long entryId,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (viewModel.SelectedPassword?.Id == entryId &&
                viewModel.SelectedPasswordDetails?.Entry.Id == entryId &&
                viewModel.HasCurrentSelectedPasswordDetails &&
                !viewModel.IsLoadingSelectedPasswordDetails)
            {
                return true;
            }

            await Task.Delay(25);
        }

        return false;
    }
}
