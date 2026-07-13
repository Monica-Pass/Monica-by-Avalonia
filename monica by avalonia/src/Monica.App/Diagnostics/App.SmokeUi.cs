using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Data;

namespace Monica.App;

public partial class App
{
    private const string DefaultSmokeUiUnlockPasswordEnvironmentVariable = "MONICA_SMOKE_UI_UNLOCK_PASSWORD";

    internal readonly record struct SmokeUiViewportSize(double Width, double Height);

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
            viewModel.StatusMessage = "--smoke-ui-unlock requires MONICA_APPDATA_DIR.";
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
            ApplySmokeUiTheme(viewModel, smokeTheme);
            viewModel.SmokeVaultLoadDelayMilliseconds = smokeVaultLoadDelayMilliseconds;
            viewModel.MasterPassword = password;
            await viewModel.UnlockCommand.ExecuteAsync(null);
            var vaultReady = await WaitForSmokeVaultReadyAsync(viewModel, TimeSpan.FromSeconds(30));
            smokeSuccess &= vaultReady;
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

            if (!string.IsNullOrWhiteSpace(smokeScreenshotDirectory))
            {
                var success = await mainWindow.RunSmokeUiOtherPagesScreenshotsAsync(smokeScreenshotDirectory);
                smokeSuccess &= success;
                AppDiagnostics.Info(
                    $"Smoke UI other pages screenshots result. success={success}, directory={smokeScreenshotDirectory}");
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
        var availablePasswords = viewModel.FilteredPasswords;
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
            viewModel.SearchText = "";

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
            viewModel.SearchText = "Smoke";
            await Task.Delay(50);
            var archived = viewModel.FilteredArchivedPasswords.ToArray();
            Check(
                "archive-filtered-row-present",
                archived.Any(item => string.Equals(item.Title, "Smoke Archived Account", StringComparison.Ordinal)),
                $"count={archived.Length}");

            viewModel.SelectSectionCommand.Execute("RecycleBin");
            await Task.Delay(50);
            var deleted = viewModel.FilteredDeletedPasswords.ToArray();
            Check(
                "recycle-filtered-row-present",
                deleted.Any(item => string.Equals(item.Title, "Smoke Deleted Account", StringComparison.Ordinal)),
                $"count={deleted.Length}");
            viewModel.SearchText = "";

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
