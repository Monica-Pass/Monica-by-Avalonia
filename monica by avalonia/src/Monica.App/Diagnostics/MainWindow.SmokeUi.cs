using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Monica.App.Features.Authenticator;
using Monica.App.Features.Notes;
using Monica.App.Features.Passwords;
using Monica.App.Features.Vault;
using Monica.App.Features.Wallet;
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

            var library = VaultWorkspaceView;
            viewModel.SelectSectionCommand.Execute("Passwords");
            await Task.Delay(50);
            library.FocusSearch();
            Check("library-search-focus", library.IsSearchFocused, $"section={viewModel.SelectedSection}");

            var allPasswordRows = viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>().ToList();
            Check("library-tree-has-rows", allPasswordRows.Count >= 2, $"count={allPasswordRows.Count}");

            // A search term copied from a row that is really on screen keeps the narrowing
            // assertion true for any seeded vault instead of only for one naming scheme.
            var needle = allPasswordRows.FirstOrDefault()?.Label ?? "";
            viewModel.VaultSearchText = needle;
            await Task.Delay(150);
            var searchedRows = viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>().ToList();
            Check(
                "library-search-narrows",
                needle.Length > 0 && searchedRows.Count >= 1 && searchedRows.Count < allPasswordRows.Count,
                $"needle='{needle}', all={allPasswordRows.Count}, searched={searchedRows.Count}");

            bool PressEscape()
            {
                var args = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Source = library,
                    Key = Key.Escape
                };
                library.TryHandleShortcut(viewModel, args);
                return args.Handled;
            }

            Check(
                "library-escape-clears-search",
                PressEscape() && !viewModel.HasVaultSearchText,
                $"search='{viewModel.VaultSearchText}'");

            viewModel.VaultFavoritesOnly = true;
            await Task.Delay(150);
            var favoriteRows = viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>().ToList();
            Check(
                "library-favorites-only",
                favoriteRows.Count == viewModel.Passwords.Count(entry => entry.IsFavorite) &&
                favoriteRows.All(row => row.Password?.IsFavorite == true),
                $"favoriteRows={favoriteRows.Count}");

            if (favoriteRows.Count > 0)
            {
                viewModel.SelectedVaultRow = favoriteRows[0];
                await Task.Delay(80);
                Check(
                    "library-selection-opens-password-editor",
                    library.MountedSurface is PasswordDetailPaneView,
                    $"surface={library.MountedSurface?.GetType().Name ?? "none"}");

                Check(
                    "library-escape-closes-selection",
                    PressEscape() && viewModel.SelectedVaultRow is null && viewModel.VaultFavoritesOnly,
                    $"selected={viewModel.SelectedVaultRow?.Label}");

                Check(
                    "library-escape-clears-favorites",
                    PressEscape() && !viewModel.VaultFavoritesOnly,
                    $"favoritesOnly={viewModel.VaultFavoritesOnly}");
            }

            viewModel.VaultSearchText = "";
            viewModel.VaultFavoritesOnly = false;
            await Task.Delay(150);
            var visibleRows = viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>().ToList();
            if (visibleRows.Count >= 2)
            {
                viewModel.SelectedVaultRow = visibleRows[0];
                library.SelectAdjacentEntry(viewModel, 1);
                await Task.Delay(50);
                Check(
                    "library-arrow-select-next",
                    viewModel.SelectedVaultRow?.Key == visibleRows[1].Key,
                    $"selected={viewModel.SelectedVaultRow?.Label}");

                var detailsReady = await WaitForSmokeWindowConditionAsync(
                    () => viewModel.SelectedPasswordDetails?.Entry.Id == viewModel.SelectedPassword?.Id &&
                          viewModel.HasCurrentSelectedPasswordDetails &&
                          !viewModel.IsLoadingSelectedPasswordDetails,
                    TimeSpan.FromSeconds(3));
                Check("library-details-ready", detailsReady, $"selected={viewModel.SelectedPassword?.Title}");
                Check(
                    "library-delete-command-available",
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

            await VaultWorkspaceView.RunNoteEditorKeyboardSmokeChecksAsync(viewModel, Check);

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
                "Generator",
                [],
                ["generatorResultPanel", "generatorOptionsPanel"]),
            new SmokePageLayoutCheck(
                "Archive",
                ["archiveRecoveryList"],
                ["archiveFilterRail", "archiveInspector"]),
            new SmokePageLayoutCheck(
                "RecycleBin",
                ["recycleQueueList"],
                ["recycleFilterRail", "recycleInspector"]),
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

            // The four vault sections are presets of the one library page, so what proves a preset is
            // the slice it filters to and the surface a row of that slice opens — not a page shell.
            var libraryPresets = new (string Section, VaultEntryGroup Group, Type? Surface)[]
            {
                ("Vault", VaultEntryGroup.All, null),
                ("Passwords", VaultEntryGroup.Passwords, typeof(PasswordDetailPaneView)),
                ("Notes", VaultEntryGroup.Notes, typeof(NoteEditorView)),
                ("Totp", VaultEntryGroup.Totp, typeof(AuthenticatorCodeConsoleView)),
                ("Cards", VaultEntryGroup.Cards, typeof(WalletWorkbenchView))
            };

            foreach (var preset in libraryPresets)
            {
                viewModel.SelectSectionCommand.Execute(preset.Section);
                await Task.Delay(80);
                var libraries = this.GetVisualDescendants().OfType<VaultWorkspaceView>().ToList();
                Check(
                    $"library-{preset.Section}-one-instance",
                    libraries.Count == 1 && libraries[0].IsVisible,
                    $"instances={libraries.Count}");
                Check(
                    $"library-{preset.Section}-group",
                    viewModel.VaultGroup == preset.Group,
                    $"group={viewModel.VaultGroup}");

                var filter = new VaultTreeFilter(preset.Group);
                var rows = viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>().ToList();
                Check(
                    $"library-{preset.Section}-rows-in-group",
                    rows.Count > 0 && rows.All(row => filter.Matches(row.Kind)),
                    $"rows={rows.Count}");

                viewModel.SelectedVaultRow = null;
                await Task.Delay(30);
                if (rows.Count > 0)
                {
                    viewModel.SelectedVaultRow = rows[0];
                    await Task.Delay(80);
                    var mounted = libraries[0].MountedSurface;
                    Check(
                        $"library-{preset.Section}-surface",
                        mounted is not null && (preset.Surface?.IsInstanceOfType(mounted) ?? true),
                        $"surface={mounted?.GetType().Name ?? "none"}");
                }
            }

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
        var frameHashes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var sections = new[]
        {
            "Vault",
            "Passwords",
            "Notes",
            "Totp",
            "Cards",
            "Generator",
            "Archive",
            "RecycleBin",
            "SecurityAnalysis",
            "Timeline",
            "Mdbx",
            "DatabaseManagement",
            "Settings"
        };

        try
        {
            Directory.CreateDirectory(screenshotDirectory);
            var captureLive = await WaitForLiveSmokeCaptureAsync(viewModel);
            if (!captureLive)
            {
                failures.Add("capture-stale");
            }

            // One frozen capture means the harness is broken, not that 13 screens are: writing the
            // sections the compositor never repainted would only bury the reason under duplicates.
            foreach (var section in captureLive ? sections : Array.Empty<string>())
            {
                viewModel.SelectSectionCommand.Execute(section);
                var settled = await WaitForSmokeWindowConditionAsync(
                    () => viewModel.IsUnlocked &&
                          string.Equals(viewModel.SelectedSection, section, StringComparison.OrdinalIgnoreCase),
                    TimeSpan.FromSeconds(3));
                if (!settled)
                {
                    failures.Add($"{section}:not-settled");
                    AppDiagnostics.Info(
                        $"Smoke UI screenshot skipped. section={section}, reason=section-not-settled, " +
                        $"selected={viewModel.SelectedSection}, unlocked={viewModel.IsUnlocked}");
                    continue;
                }

                await Task.Delay(150);

                // With no row selected a preset shows only the open hint, so the gate would never see
                // an editing surface at all: open the first entry the preset has.
                if (section is "Vault" or "Passwords" or "Notes" or "Totp" or "Cards")
                {
                    viewModel.SelectedVaultRow = viewModel.VaultTreeRows.FirstOrDefault(row => row.IsEntryRow);
                    await Task.Delay(150);
                }

                var fileName = $"{section}_{Math.Max(1, (int)Math.Round(Bounds.Width))}x{Math.Max(1, (int)Math.Round(Bounds.Height))}.png";
                var path = Path.Combine(screenshotDirectory, fileName);
                if (!await SaveSmokeScreenshotAsync(path))
                {
                    failures.Add(section);
                    AppDiagnostics.Info($"Smoke UI screenshot failed. section={section}, path={path}");
                    continue;
                }

                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                if (frameHashes.TryGetValue(hash, out var twins))
                {
                    failures.Add($"{section}:duplicate-of-{twins[0]}");
                    twins.Add(section);
                    AppDiagnostics.Info(
                        $"Smoke UI screenshot duplicated. section={section}, sameFrameAs={string.Join(",", twins)}");
                }
                else
                {
                    frameHashes[hash] = new List<string> { section };
                    AppDiagnostics.Info($"Smoke UI screenshot saved. section={section}, path={path}, sha256={hash[..12]}");
                }
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
            $"failureCount={failures.Count}, failures={string.Join(",", failures)}, " +
            $"distinctFrames={frameHashes.Count}, directory={screenshotDirectory}");
        return success;
    }

    private static string FormatSmokeLogValue(string? value) =>
        (value ?? "").Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private async Task<bool> SaveSmokeScreenshotAsync(string path)
    {
        var frame = await CaptureSmokeFrameAsync();
        if (frame is null || frame.Length == 0)
        {
            return false;
        }

        File.WriteAllBytes(path, frame);
        return new FileInfo(path).Length > 0;
    }

    /// <summary>
    /// Proves the capture is repainting before 13 files depend on it. Measured once in seven real runs
    /// every section saved the same byte-identical pre-unlock frame while the log said the vault was
    /// unlocked, and nothing downstream could tell a frozen harness from 13 screens that genuinely
    /// look alike. The mechanism never got pinned down; this gate only refuses to lie about it.
    /// </summary>
    private async Task<bool> WaitForLiveSmokeCaptureAsync(MainWindowViewModel viewModel)
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            ShowFromDesktopIntegration();
            var before = await CaptureSmokeFrameAsync();
            viewModel.SelectSectionCommand.Execute("Settings");
            await Task.Delay(250);
            var after = await CaptureSmokeFrameAsync();
            if (before is not null && after is not null && !before.SequenceEqual(after))
            {
                AppDiagnostics.Info($"Smoke UI capture liveness proved. attempt={attempt}");
                return true;
            }

            AppDiagnostics.Info(
                $"Smoke UI capture did not repaint. attempt={attempt}, " +
                $"beforeBytes={before?.Length ?? 0}, afterBytes={after?.Length ?? 0}");
            await Task.Delay(400);
        }

        return false;
    }

    private async Task<byte[]?> CaptureSmokeFrameAsync()
    {
        var width = Math.Max(1, (int)Math.Round(Bounds.Width));
        var height = Math.Max(1, (int)Math.Round(Bounds.Height));
        if (width < 1 || height < 1)
        {
            return null;
        }

        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        // Each frame holds a native backing surface, and the run captures one per section, so the
        // bitmap has to go back — otherwise the screenshots inflate the memory this gate measures.
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        // Rendering the Window itself captures its top-level drawing group; rendering Content skips
        // that group. Which one the stale frame came out of is not established (the freeze reproduces
        // about once in seven runs), so WaitForLiveSmokeCaptureAsync is what actually guards this.
        bitmap.Render((Visual)Content!);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
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
