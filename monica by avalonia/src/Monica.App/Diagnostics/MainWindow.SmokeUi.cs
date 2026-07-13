using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    private sealed record SmokePageLayoutCheck(
        string Section,
        string[] RequiredClasses,
        string[] RequiredVisibleClasses);

    public async Task<bool> RunSmokeUiKeyboardChecksAsync()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(RunSmokeUiKeyboardChecksAsync);
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            AppDiagnostics.Info("Smoke UI keyboard checks failed. reason=no-view-model");
            return false;
        }

        var failures = new List<string>();
        void Check(string name, bool condition, string detail = "")
        {
            if (condition)
            {
                AppDiagnostics.Info($"Smoke UI keyboard check passed. check={name}, {detail}");
                return;
            }

            failures.Add(name);
            AppDiagnostics.Info($"Smoke UI keyboard check failed. check={name}, {detail}");
        }

        try
        {
            var vaultReady = await WaitForSmokeWindowConditionAsync(
                () => viewModel.IsUnlocked && !viewModel.IsLoadingVault && viewModel.Passwords.Count > 0,
                TimeSpan.FromSeconds(10));
            Check("vault-ready", vaultReady, $"passwords={viewModel.Passwords.Count}");

            viewModel.SelectSectionCommand.Execute("Passwords");
            await Task.Delay(50);
            PasswordVaultView.FocusSearch();
            Check("password-search-focus", PasswordVaultView.IsSearchFocused, $"section={viewModel.SelectedSection}");

            viewModel.PasswordSearchText = "Smoke";
            await Task.Delay(50);
            Check("password-filter-active", viewModel.HasPasswordFilters, $"summary={viewModel.PasswordFilterSummaryText}");
            if (viewModel.ClearPasswordFiltersCommand.CanExecute(null))
            {
                viewModel.ClearPasswordFiltersCommand.Execute(null);
            }

            await Task.Delay(50);
            Check(
                "password-escape-clear-filters",
                !viewModel.HasPasswordFilters && string.IsNullOrWhiteSpace(viewModel.PasswordSearchText),
                $"search='{viewModel.PasswordSearchText}'");

            var visiblePasswords = viewModel.FilteredPasswords.ToArray();
            Check("password-list-has-rows", visiblePasswords.Length >= 2, $"count={visiblePasswords.Length}");
            if (visiblePasswords.Length >= 2)
            {
                viewModel.SelectedPassword = visiblePasswords[0];
                SelectAdjacentPassword(viewModel, 1);
                await Task.Delay(50);
                Check(
                    "password-arrow-select-next",
                    viewModel.SelectedPassword?.Id == visiblePasswords[1].Id,
                    $"selected={viewModel.SelectedPassword?.Title}");

                var detailsReady = await WaitForSmokeWindowConditionAsync(
                    () => viewModel.SelectedPasswordDetails?.Entry.Id == viewModel.SelectedPassword?.Id &&
                          viewModel.HasCurrentSelectedPasswordDetails &&
                          !viewModel.IsLoadingSelectedPasswordDetails,
                    TimeSpan.FromSeconds(3));
                Check("password-details-ready", detailsReady, $"selected={viewModel.SelectedPassword?.Title}");
                PasswordVaultView.FocusDetails();
                Check("password-enter-focus-details", PasswordVaultView.IsDetailFocused, $"hasDetails={viewModel.HasCurrentSelectedPasswordDetails}");
                Check(
                    "password-delete-command-available",
                    viewModel.SelectedPassword is not null &&
                    viewModel.DeletePasswordCommand.CanExecute(viewModel.SelectedPassword),
                    $"selected={viewModel.SelectedPassword?.Title}");
            }

            viewModel.SelectSectionCommand.Execute("Notes");
            await Task.Delay(50);
            if (viewModel.OpenNoteTabs.Count < 2)
            {
                viewModel.AddNoteCommand.Execute(null);
                viewModel.AddNoteCommand.Execute(null);
            }

            Check("note-tabs-available", viewModel.OpenNoteTabs.Count >= 2, $"tabs={viewModel.OpenNoteTabs.Count}");
            if (viewModel.OpenNoteTabs.Count >= 2)
            {
                viewModel.SelectedNoteTab = viewModel.OpenNoteTabs[0];
                if (viewModel.SelectNextNoteTabCommand.CanExecute(null))
                {
                    viewModel.SelectNextNoteTabCommand.Execute(null);
                }

                await Task.Delay(50);
                Check(
                    "note-ctrl-pagedown-next-tab",
                    viewModel.SelectedNoteTab == viewModel.OpenNoteTabs[1],
                    $"selected={viewModel.SelectedNoteTab?.Title}");
                if (viewModel.SelectPreviousNoteTabCommand.CanExecute(null))
                {
                    viewModel.SelectPreviousNoteTabCommand.Execute(null);
                }

                await Task.Delay(50);
                Check(
                    "note-ctrl-pageup-previous-tab",
                    viewModel.SelectedNoteTab == viewModel.OpenNoteTabs[0],
                    $"selected={viewModel.SelectedNoteTab?.Title}");
            }

            await NoteWorkspaceView.RunKeyboardSmokeChecksAsync(Check);

            viewModel.SelectSectionCommand.Execute("Generator");
            await Task.Delay(50);
            viewModel.GeneratedPassword = "";
            var generatorNewExecuted = TryExecuteCurrentSectionNewCommand(viewModel);
            await Task.Delay(50);
            Check(
                "ctrl-n-generator-generates",
                generatorNewExecuted && !string.IsNullOrWhiteSpace(viewModel.GeneratedPassword),
                $"generatedLength={viewModel.GeneratedPassword?.Length ?? 0}");

            viewModel.SelectSectionCommand.Execute("Totp");
            await Task.Delay(50);
            Check(
                "ctrl-n-totp-action-available",
                viewModel.AddTotpCommand.CanExecute(null),
                $"section={viewModel.SelectedSection}");

            viewModel.SelectSectionCommand.Execute("Cards");
            await Task.Delay(50);
            Check(
                "ctrl-n-wallet-action-available",
                viewModel.AddWalletItemCommand.CanExecute(null),
                $"section={viewModel.SelectedSection}");

            viewModel.SelectSectionCommand.Execute("Mdbx");
            await Task.Delay(50);
            Check(
                "ctrl-n-mdbx-action-available",
                viewModel.CreateMdbxVaultCommand.CanExecute(null),
                $"section={viewModel.SelectedSection}");
        }
        catch (Exception ex)
        {
            failures.Add("exception");
            AppDiagnostics.Error("Smoke UI keyboard checks failed", ex);
        }

        var success = failures.Count == 0;
        AppDiagnostics.Info(
            $"Smoke UI keyboard checks completed. success={success}, " +
            $"failureCount={failures.Count}, failures={string.Join(",", failures)}");
        return success;
    }

    public async Task<bool> RunSmokeUiOtherPagesChecksAsync()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(RunSmokeUiOtherPagesChecksAsync);
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            AppDiagnostics.Info("Smoke UI other pages checks failed. reason=no-view-model");
            return false;
        }

        var pages = new[]
        {
            new SmokePageLayoutCheck(
                "Totp",
                ["totpAccountList"],
                ["totpCodeConsole", "totpInspector"]),
            new SmokePageLayoutCheck(
                "Cards",
                ["walletItemList"],
                ["walletWorkbench", "walletInspector"]),
            new SmokePageLayoutCheck(
                "Generator",
                [],
                ["generatorResultPanel", "generatorOptionsPanel"]),
            new SmokePageLayoutCheck(
                "Archive",
                ["archiveRecoveryList"],
                ["archiveFilterRail", "archiveRecoveryPanel"]),
            new SmokePageLayoutCheck(
                "RecycleBin",
                ["recycleQueueList"],
                ["recycleFilterRail", "recycleRiskPanel"]),
            new SmokePageLayoutCheck(
                "Timeline",
                ["timelineEventStream"],
                ["timelineFilterRail", "timelineInspector"]),
            new SmokePageLayoutCheck(
                "Mdbx",
                ["mdbxWorkingCopyList", "mdbxPivotButton"],
                ["mdbxSourceRail", "mdbxWorkbench"]),
            new SmokePageLayoutCheck(
                "DatabaseManagement",
                ["databaseSourceList", "databasePivotButton"],
                ["databaseSourceRail", "databaseWorkbench"])
        };

        var failures = new List<string>();
        void Check(string name, bool condition, string detail = "")
        {
            if (condition)
            {
                AppDiagnostics.Info($"Smoke UI other page check passed. check={name}, {detail}");
                return;
            }

            failures.Add(name);
            AppDiagnostics.Info($"Smoke UI other page check failed. check={name}, {detail}");
        }

        try
        {
            var viewportReady = await WaitForSmokeWindowConditionAsync(
                () => Bounds.Width >= MinWidth && Bounds.Height >= MinHeight,
                TimeSpan.FromSeconds(3));
            Check(
                "viewport-ready",
                viewportReady,
                $"bounds={Bounds.Width:0}x{Bounds.Height:0}, min={MinWidth:0}x{MinHeight:0}");

            foreach (var page in pages)
            {
                viewModel.SelectSectionCommand.Execute(page.Section);
                await Task.Delay(80);
                Check(
                    $"{page.Section}-selected",
                    string.Equals(viewModel.SelectedSection, page.Section, StringComparison.OrdinalIgnoreCase),
                    $"selected={viewModel.SelectedSection}");

                foreach (var className in page.RequiredClasses)
                {
                    Check(
                        $"{page.Section}-class-{className}",
                        HasControlClass(className),
                        $"class={className}");
                }

                foreach (var className in page.RequiredVisibleClasses)
                {
                    Check(
                        $"{page.Section}-visible-{className}",
                        HasVisibleControlClass(className),
                        $"class={className}");
                }

                Check(
                    $"{page.Section}-no-visible-workspacePageHeader",
                    !HasVisibleControlClass("workspacePageHeader"),
                    "legacyHeader=workspacePageHeader");
            }
        }
        catch (Exception ex)
        {
            failures.Add("exception");
            AppDiagnostics.Error("Smoke UI other pages checks failed", ex);
        }

        var success = failures.Count == 0;
        AppDiagnostics.Info(
            $"Smoke UI other pages checks completed. success={success}, " +
            $"failureCount={failures.Count}, failures={string.Join(",", failures)}");
        return success;
    }

    public async Task<bool> RunSmokeUiOtherPagesScreenshotsAsync(string screenshotDirectory)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => RunSmokeUiOtherPagesScreenshotsAsync(screenshotDirectory));
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            AppDiagnostics.Info("Smoke UI other pages screenshots failed. reason=no-view-model");
            return false;
        }

        var failures = new List<string>();
        var sections = new[]
        {
            "Totp",
            "Cards",
            "Generator",
            "Archive",
            "RecycleBin",
            "Timeline",
            "Mdbx",
            "DatabaseManagement"
        };

        try
        {
            Directory.CreateDirectory(screenshotDirectory);
            foreach (var section in sections)
            {
                viewModel.SelectSectionCommand.Execute(section);
                await Task.Delay(150);

                var fileName = $"{section}_{Math.Max(1, (int)Math.Round(Bounds.Width))}x{Math.Max(1, (int)Math.Round(Bounds.Height))}.png";
                var path = Path.Combine(screenshotDirectory, fileName);
                if (!await SaveSmokeScreenshotAsync(path))
                {
                    failures.Add(section);
                    AppDiagnostics.Info($"Smoke UI screenshot failed. section={section}, path={path}");
                    continue;
                }

                AppDiagnostics.Info($"Smoke UI screenshot saved. section={section}, path={path}");
            }
        }
        catch (Exception ex)
        {
            failures.Add("exception");
            AppDiagnostics.Error("Smoke UI other pages screenshots failed", ex);
        }

        var success = failures.Count == 0;
        AppDiagnostics.Info(
            $"Smoke UI other pages screenshots completed. success={success}, " +
            $"failureCount={failures.Count}, failures={string.Join(",", failures)}, directory={screenshotDirectory}");
        return success;
    }

    private static string FormatSmokeLogValue(string? value) =>
        (value ?? "").Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private async Task<bool> SaveSmokeScreenshotAsync(string path)
    {
        var width = Math.Max(1, (int)Math.Round(Bounds.Width));
        var height = Math.Max(1, (int)Math.Round(Bounds.Height));
        if (width < 1 || height < 1)
        {
            return false;
        }

        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(this);
        bitmap.Save(path);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    private bool HasControlClass(string className) =>
        this.GetVisualDescendants()
            .OfType<Control>()
            .Any(control => control.Classes.Contains(className));

    private bool HasVisibleControlClass(string className) =>
        this.GetVisualDescendants()
            .OfType<Control>()
            .Any(control => control.Classes.Contains(className) && IsControlEffectivelyVisible(control));

    private static bool IsControlEffectivelyVisible(Control control)
    {
        for (var current = control as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Control currentControl && !currentControl.IsVisible)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> WaitForSmokeWindowConditionAsync(Func<bool> condition, TimeSpan timeout)
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
}
