using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Monica.App.Controls;
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

    /// <summary>
    /// One frame of the KeePass edit form out of the shipped binary. The rest of the matrix covers the
    /// pages a section name reaches; this surface sits behind a native file dialog, so without a seam
    /// like it nobody has looked at how the form actually lays out. Counts and byte sizes are logged,
    /// never a title or a secret.
    /// </summary>
    public async Task<bool> RunSmokeUiKeePassEditShotAsync(
        string vaultPath,
        string password,
        string? screenshotDirectory)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(
                () => RunSmokeUiKeePassEditShotAsync(vaultPath, password, screenshotDirectory));
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            AppDiagnostics.Info("Smoke UI KeePass edit shot failed. reason=no-view-model");
            return false;
        }

        try
        {
            var keepassTab = await RealizeSmokeKeePassImportTabAsync("edit shot");
            if (keepassTab is null)
            {
                return false;
            }

            var state = await viewModel.SmokeShowKeePassEditorAsync(vaultPath, password);
            await Task.Delay(250);

            var frame = await CaptureSmokeFrameAsync();
            var frameBytes = frame?.Length ?? 0;
            var wantedFile = !string.IsNullOrWhiteSpace(screenshotDirectory);
            var written = false;
            var fileName = "";
            if (wantedFile && frameBytes > 0)
            {
                Directory.CreateDirectory(screenshotDirectory!);
                fileName = $"KeePassEdit_{Math.Max(1, (int)Math.Round(Bounds.Width))}x" +
                    $"{Math.Max(1, (int)Math.Round(Bounds.Height))}.png";
                var path = Path.Combine(screenshotDirectory!, fileName);
                File.WriteAllBytes(path, frame!);
                written = new FileInfo(path).Length > 0;
            }

            var success = state.DatabaseOpened &&
                state.EditorShown &&
                state.EntryRows > 0 &&
                keepassTab.IsSelected &&
                frameBytes > 0 &&
                (!wantedFile || written);
            AppDiagnostics.Info(
                $"Smoke UI KeePass edit shot result. success={success}, opened={state.DatabaseOpened}, " +
                $"editor={state.EditorShown}, treeRows={state.TreeRows}, entryRows={state.EntryRows}, " +
                $"vaultBytes={state.FileBytes}, tabSelected={keepassTab.IsSelected}, " +
                $"frameBytes={frameBytes}, written={written}, file={fileName}");
            return success;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Smoke UI KeePass edit shot failed", ex);
            return false;
        }
    }

    /// <summary>
    /// The row-management surface as two frames: a folder added to the tree, an entry filed into it and
    /// that entry then recycled, with the unsaved-changes notice lit and the file on disk still the one
    /// that was opened - and then the new-entry form itself, which the tree frame cannot show at all.
    /// Both halves are the point: the tree growing and shrinking proves the commands run against the
    /// browsed database, the file being untouched proves nothing was saved to get there, and the form
    /// being on screen proves a draft is something a person can type into.
    /// </summary>
    public async Task<bool> RunSmokeUiKeePassManageShotAsync(
        string vaultPath,
        string password,
        string? screenshotDirectory)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(
                () => RunSmokeUiKeePassManageShotAsync(vaultPath, password, screenshotDirectory));
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            AppDiagnostics.Info("Smoke UI KeePass manage shot failed. reason=no-view-model");
            return false;
        }

        try
        {
            var keepassTab = await RealizeSmokeKeePassImportTabAsync("manage shot");
            if (keepassTab is null)
            {
                return false;
            }

            var state = await viewModel.SmokeShowKeePassEditorManagementAsync(vaultPath, password);
            await Task.Delay(250);

            var frame = await CaptureSmokeFrameAsync();
            var frameBytes = frame?.Length ?? 0;
            var wantedFile = !string.IsNullOrWhiteSpace(screenshotDirectory);
            var written = false;
            var fileName = "";
            if (wantedFile && frameBytes > 0)
            {
                Directory.CreateDirectory(screenshotDirectory!);
                fileName = $"KeePassManage_{Math.Max(1, (int)Math.Round(Bounds.Width))}x" +
                    $"{Math.Max(1, (int)Math.Round(Bounds.Height))}.png";
                var path = Path.Combine(screenshotDirectory!, fileName);
                File.WriteAllBytes(path, frame!);
                written = new FileInfo(path).Length > 0;
            }

            // The frame above is the tree, so it cannot show the half of this surface that matters most
            // when a row is added: the form. A draft used to be reported as opened by a view model flag
            // while the column hosting it stayed collapsed, which is the difference between filing an
            // entry and staring at an empty panel - so the shipped binary is asked whether the fields are
            // on screen with room to type in, and a second frame keeps the answer inspectable.
            viewModel.NewKeePassEntryCommand.Execute(null);
            await Task.Delay(250);
            var formPane = this.GetVisualDescendants()
                .OfType<StackPanel>()
                .FirstOrDefault(control => control.Name == "KeePassEntryEditorPane");
            var formTitleBox = this.GetVisualDescendants()
                .OfType<TextBox>()
                .FirstOrDefault(control => control.Name == "KeePassEditTitleBox");
            var formOnScreen = formPane is { IsVisible: true } &&
                formPane.Bounds.Width > 0 &&
                formPane.Bounds.Height > 0 &&
                formTitleBox is { IsVisible: true } &&
                formTitleBox.Bounds.Width > 0 &&
                formTitleBox.Bounds.Height > 0;
            var formWritten = false;
            var formFileName = "";
            if (wantedFile && formOnScreen)
            {
                var formFrame = await CaptureSmokeFrameAsync();
                if (formFrame is { Length: > 0 })
                {
                    formFileName = $"KeePassManageForm_{Math.Max(1, (int)Math.Round(Bounds.Width))}x" +
                        $"{Math.Max(1, (int)Math.Round(Bounds.Height))}.png";
                    var formPath = Path.Combine(screenshotDirectory!, formFileName);
                    File.WriteAllBytes(formPath, formFrame);
                    formWritten = new FileInfo(formPath).Length > 0;
                }
            }

            viewModel.CancelKeePassEntryEditCommand.Execute(null);
            await Task.Delay(150);

            var success = state.DatabaseOpened &&
                state.FolderShown &&
                state.DraftShown &&
                state.EntryShown &&
                state.UnsavedNoticeShown &&
                state.BinShown &&
                state.EntryInBin &&
                state.BinDeleteSplit &&
                state.EntryRestored &&
                state.RecycleBinEmptied &&
                formOnScreen &&
                keepassTab.IsSelected &&
                frameBytes > 0 &&
                (!wantedFile || written) &&
                (!wantedFile || formWritten);
            AppDiagnostics.Info(
                $"Smoke UI KeePass manage shot result. success={success}, opened={state.DatabaseOpened}, " +
                $"folderAdded={state.FolderShown}, draftOpened={state.DraftShown}, " +
                $"draftFormOnScreen={formOnScreen}, " +
                $"entryAdded={state.EntryShown}, unsavedNotice={state.UnsavedNoticeShown}, " +
                $"binShown={state.BinShown}, entryInBin={state.EntryInBin}, " +
                $"binDeleteSplit={state.BinDeleteSplit}, " +
                $"entryRestoredOutOfBin={state.EntryRestored}, recycleBinEmptied={state.RecycleBinEmptied}, " +
                $"treeRows={state.TreeRows}, folderRows={state.FolderRows}, entryRows={state.EntryRows}, " +
                $"vaultBytes={state.FileBytes}, tabSelected={keepassTab.IsSelected}, " +
                $"frameBytes={frameBytes}, written={written}, file={fileName}, " +
                $"formFile={formFileName}");
            return success;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Smoke UI KeePass manage shot failed", ex);
            return false;
        }
    }

    /// <summary>
    /// One frame of the searched tree. The query is typed into the shipped control's own text box instead
    /// of being set on the view model, because the binding is the part only an installed binary has. The
    /// tree's rendered text is scanned for the fixture's protected values and reported as one boolean, so
    /// a build that ever paints a secret goes red rather than printing it.
    /// </summary>
    public async Task<bool> RunSmokeUiKeePassSearchShotAsync(
        string vaultPath,
        string password,
        string query,
        string? screenshotDirectory)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(
                () => RunSmokeUiKeePassSearchShotAsync(vaultPath, password, query, screenshotDirectory));
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            AppDiagnostics.Info("Smoke UI KeePass search shot failed. reason=no-view-model");
            return false;
        }

        try
        {
            var keepassTab = await RealizeSmokeKeePassImportTabAsync("search shot");
            if (keepassTab is null)
            {
                return false;
            }

            var opened = await viewModel.SmokeOpenKeePassForSearchAsync(vaultPath, password);
            await Task.Delay(250);

            var field = this.GetVisualDescendants()
                .OfType<SearchField>()
                .FirstOrDefault(control => control.Name == "KeePassSearchField");
            var boxOnScreen = field is { IsVisible: true } && field.Bounds.Width > 0 && field.Bounds.Height > 0;
            if (boxOnScreen)
            {
                field!.InnerSearchBox!.Text = query;
            }

            await Task.Delay(250);
            var rows = viewModel.KeePassTreeRowsPublic;
            var flat = rows.Count > 0 && rows.All(row => row.IsEntryRow);
            // With the indentation gone the subtitle is the only place a folder can appear, and a hit
            // that cannot say where it came from is a hit the user has to go find by hand.
            var everyHitSaysWhere = rows.Count > 0 && rows.All(row =>
                row.ShowsGroupPath && !string.IsNullOrEmpty(row.EntryDetail));
            var summary = viewModel.KeePassSearchSummaryText;
            var painted = field is not null
                ? this.GetVisualDescendants()
                    .OfType<VaultFolderTree>()
                    .FirstOrDefault(control => control.Name == "KeePassBrowseTree")
                    ?.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(text => text.Text ?? "")
                    .ToList() ?? []
                : [];
            var paintedSecretFree = !painted.Any(text =>
                text.Contains("secret-", StringComparison.Ordinal) ||
                text.Contains("ticket-", StringComparison.Ordinal) ||
                text.Contains("otpauth", StringComparison.Ordinal));

            var frame = await CaptureSmokeFrameAsync();
            var frameBytes = frame?.Length ?? 0;
            var wantedFile = !string.IsNullOrWhiteSpace(screenshotDirectory);
            var written = false;
            var fileName = "";
            if (wantedFile && frameBytes > 0)
            {
                Directory.CreateDirectory(screenshotDirectory!);
                fileName = $"KeePassSearch_{Math.Max(1, (int)Math.Round(Bounds.Width))}x" +
                    $"{Math.Max(1, (int)Math.Round(Bounds.Height))}.png";
                var path = Path.Combine(screenshotDirectory!, fileName);
                File.WriteAllBytes(path, frame!);
                written = new FileInfo(path).Length > 0;
            }

            // The query has to be put down again in the same session: a flat list you cannot leave is a
            // broken browser no matter how good the frame looks.
            field?.InnerClearButton?.Command?.Execute(null);
            await Task.Delay(250);
            var backToHierarchy = viewModel.KeePassSearchText.Length == 0 &&
                viewModel.KeePassTreeRowsPublic.Count(row => row.IsEntryRow is false) == opened.FolderRows;

            var success = opened.DatabaseOpened &&
                boxOnScreen &&
                flat &&
                everyHitSaysWhere &&
                summary.Length > 0 &&
                !summary.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                paintedSecretFree &&
                backToHierarchy &&
                keepassTab.IsSelected &&
                frameBytes > 0 &&
                (!wantedFile || written);
            AppDiagnostics.Info(
                $"Smoke UI KeePass search shot result. success={success}, opened={opened.DatabaseOpened}, " +
                $"boxOnScreen={boxOnScreen}, treeRowsBefore={opened.TreeRows}, " +
                $"folderRowsBefore={opened.FolderRows}, flatRows={rows.Count}, " +
                $"everyHitSaysWhere={everyHitSaysWhere}, summaryChars={summary.Length}, " +
                $"echoesQuery={summary.Contains(query, StringComparison.OrdinalIgnoreCase)}, " +
                $"paintedTexts={painted.Count}, paintedSecretFree={paintedSecretFree}, " +
                $"backToHierarchy={backToHierarchy}, vaultBytes={opened.FileBytes}, " +
                $"tabSelected={keepassTab.IsSelected}, frameBytes={frameBytes}, " +
                $"written={written}, file={fileName}");
            return success;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Smoke UI KeePass search shot failed", ex);
            return false;
        }
    }

    /// <summary>
    /// One frame of the new-database form out of the shipped binary, with the master password typed into
    /// the controls' own text boxes instead of set on the view model - the mask and the two-way binding are
    /// exactly the parts that only exist once the XAML has been applied. Nothing is written to disk here:
    /// the create command asks the operating system where the file goes, and a gate that had to dismiss a
    /// native dialog on the way to red is not a gate. What this does prove is that the entry point is
    /// reachable with no database open, that both fields stay masked, that the button refuses a mismatch,
    /// and that backing out of the form takes the typed text off the screen with it.
    /// </summary>
    public async Task<bool> RunSmokeUiKeePassCreateShotAsync(
        string password,
        string? screenshotDirectory)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(
                () => RunSmokeUiKeePassCreateShotAsync(password, screenshotDirectory));
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            AppDiagnostics.Info("Smoke UI KeePass create shot failed. reason=no-view-model");
            return false;
        }

        try
        {
            var keepassTab = await RealizeSmokeKeePassImportTabAsync("create shot");
            if (keepassTab is null)
            {
                return false;
            }

            await Task.Delay(250);
            T? Find<T>(string name)
                where T : Control =>
                this.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);

            var entryButton = Find<Button>("NewKeePassVaultButton");
            var entryOnScreen = entryButton is { IsVisible: true, IsEnabled: true } &&
                entryButton.Bounds.Width > 0 &&
                entryButton.Bounds.Height > 0;
            var collapsedBefore = Find<StackPanel>("KeePassCreateForm") is { IsVisible: false };

            entryButton?.Command?.Execute(null);
            await Task.Delay(250);

            var form = Find<StackPanel>("KeePassCreateForm");
            var passwordBox = Find<TextBox>("KeePassCreatePasswordBox");
            var confirmBox = Find<TextBox>("KeePassCreateConfirmPasswordBox");
            var mismatch = Find<TextBlock>("KeePassCreatePasswordMismatchText");
            var createButton = Find<Button>("CreateKeePassVaultButton");
            var cancelButton = Find<Button>("CancelKeePassVaultCreateButton");

            var formOnScreen = form is { IsVisible: true } &&
                form.Bounds.Width > 0 &&
                form.Bounds.Height > 0;
            var boxesOnScreen = passwordBox is { IsVisible: true } &&
                passwordBox.Bounds.Width > 0 &&
                confirmBox is { IsVisible: true } &&
                confirmBox.Bounds.Width > 0;
            var masked = passwordBox?.PasswordChar == '*' && confirmBox?.PasswordChar == '*';
            var darkUntilTyped = createButton is { IsVisible: true, IsEnabled: false };

            if (passwordBox is not null && confirmBox is not null)
            {
                passwordBox.Text = password;
                confirmBox.Text = password + "x";
            }

            await Task.Delay(250);
            var mismatchShown = mismatch is { IsVisible: true } && !string.IsNullOrEmpty(mismatch.Text);
            var darkWhileMismatching = createButton is { IsEnabled: false };

            if (confirmBox is not null)
            {
                confirmBox.Text = password;
            }

            await Task.Delay(250);
            var mismatchGone = mismatch is { IsVisible: false };
            var readyToCreate = createButton is { IsEnabled: true };

            var painted = form is not null
                ? form.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? "").ToList()
                : [];
            var paintedSecretFree = password.Length == 0 ||
                !painted.Any(text => text.Contains(password, StringComparison.Ordinal));

            var frame = await CaptureSmokeFrameAsync();
            var frameBytes = frame?.Length ?? 0;
            var wantedFile = !string.IsNullOrWhiteSpace(screenshotDirectory);
            var written = false;
            var fileName = "";
            if (wantedFile && frameBytes > 0)
            {
                Directory.CreateDirectory(screenshotDirectory!);
                fileName = $"KeePassCreate_{Math.Max(1, (int)Math.Round(Bounds.Width))}x" +
                    $"{Math.Max(1, (int)Math.Round(Bounds.Height))}.png";
                var path = Path.Combine(screenshotDirectory!, fileName);
                File.WriteAllBytes(path, frame!);
                written = new FileInfo(path).Length > 0;
            }

            cancelButton?.Command?.Execute(null);
            await Task.Delay(250);
            var wipedOnCancel = form is { IsVisible: false } &&
                string.IsNullOrEmpty(passwordBox?.Text) &&
                string.IsNullOrEmpty(confirmBox?.Text) &&
                viewModel.KeePassCreatePassword.Length == 0 &&
                viewModel.KeePassCreateConfirmation.Length == 0;

            var success = entryOnScreen &&
                collapsedBefore &&
                formOnScreen &&
                boxesOnScreen &&
                masked &&
                darkUntilTyped &&
                mismatchShown &&
                darkWhileMismatching &&
                mismatchGone &&
                readyToCreate &&
                paintedSecretFree &&
                wipedOnCancel &&
                keepassTab.IsSelected &&
                frameBytes > 0 &&
                (!wantedFile || written);
            AppDiagnostics.Info(
                $"Smoke UI KeePass create shot result. success={success}, entryOnScreen={entryOnScreen}, " +
                $"collapsedBefore={collapsedBefore}, formOnScreen={formOnScreen}, " +
                $"boxesOnScreen={boxesOnScreen}, masked={masked}, darkUntilTyped={darkUntilTyped}, " +
                $"mismatchShown={mismatchShown}, darkWhileMismatching={darkWhileMismatching}, " +
                $"mismatchGone={mismatchGone}, readyToCreate={readyToCreate}, " +
                $"typedChars={password.Length}, paintedTexts={painted.Count}, " +
                $"paintedSecretFree={paintedSecretFree}, wipedOnCancel={wipedOnCancel}, " +
                $"tabSelected={keepassTab.IsSelected}, frameBytes={frameBytes}, " +
                $"written={written}, file={fileName}");
            return success;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Smoke UI KeePass create shot failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Walks the remembered-database list on the shipped binary. The tab keeps that list to itself, so the
    /// only thing the view model seam does here is open a file; every claim is then read off the screen or
    /// driven through a painted control - the row is named by its file and not its folder, the row is still
    /// there after the database is closed, tapping it brings the masked master-password prompt back, a row
    /// whose file has gone says so and refuses the prompt, and the small dismiss control takes it off the
    /// list. The list is emptied through that same dismiss control first, so a machine that has run this
    /// frame before still starts from nothing. The master password is only ever looked for in the painted
    /// text and reported as one boolean.
    /// </summary>
    public async Task<bool> RunSmokeUiKeePassRecentShotAsync(
        string vaultPath,
        string password,
        string? screenshotDirectory)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(
                () => RunSmokeUiKeePassRecentShotAsync(vaultPath, password, screenshotDirectory));
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            AppDiagnostics.Info("Smoke UI KeePass recent shot failed. reason=no-view-model");
            return false;
        }

        // The gone-row half of the frame moves the file out of the way, and a run that threw halfway would
        // otherwise leave the fixture unrecognisable to the gate that reads its exit code.
        var movedAside = vaultPath + ".moved-aside";
        var fileIsMoved = false;
        try
        {
            var keepassTab = await RealizeSmokeKeePassImportTabAsync("recent shot");
            if (keepassTab is null)
            {
                return false;
            }

            await Task.Delay(250);
            T? Find<T>(string name)
                where T : Control =>
                this.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);
            List<string> Painted(Control? root) =>
                root is null
                    ? new List<string>()
                    : root.GetVisualDescendants()
                        .OfType<TextBlock>()
                        .Where(text => text.IsEffectivelyVisible)
                        .Select(text => text.Text ?? "")
                        .ToList();

            var emptiedThroughTheControl = false;
            for (var pass = 0; pass < 24; pass++)
            {
                var remembered = Find<Button>("ForgetKeePassRecentVaultButton");
                if (remembered is null)
                {
                    emptiedThroughTheControl = true;
                    break;
                }

                remembered.Command?.Execute(remembered.CommandParameter);
                await Task.Delay(120);
            }

            var beforeSection = Find<StackPanel>("KeePassRecentSection");
            var listHiddenBefore = beforeSection is null || !beforeSection.IsEffectivelyVisible;

            var opened = await viewModel.SmokeOpenKeePassForRecentAsync(vaultPath, password);
            await Task.Delay(250);

            var fileName = Path.GetFileName(vaultPath);
            var directory = Path.GetDirectoryName(vaultPath) ?? "";
            var section = Find<StackPanel>("KeePassRecentSection");
            var rowButton = Find<Button>("OpenKeePassRecentVaultButton");
            var row = rowButton?.CommandParameter as KeePassRecentVaultRow;
            var rowOnScreen = section is { IsVisible: true } &&
                section.Bounds.Width > 0 &&
                section.Bounds.Height > 0 &&
                rowButton is { IsVisible: true } &&
                rowButton.Bounds.Width > 0 &&
                rowButton.Bounds.Height > 0;
            var painted = Painted(section);
            var namedByFile = painted.Any(text => text == fileName);
            var noFolderShown = !painted.Any(text => text.Contains(directory, StringComparison.Ordinal));
            var paintedSecretFree = !painted.Any(text => text.Contains(password, StringComparison.Ordinal));
            var saysWhenItWasOpened = row is { ShowsLastOpened: true, IsFileMissing: false } &&
                row.LastOpenedText.Length > 0;

            var frame = await CaptureSmokeFrameAsync();
            var frameBytes = frame?.Length ?? 0;
            var wantedFile = !string.IsNullOrWhiteSpace(screenshotDirectory);
            var written = false;
            var shotName = "";
            if (wantedFile && frameBytes > 0)
            {
                Directory.CreateDirectory(screenshotDirectory!);
                shotName = $"KeePassRecent_{Math.Max(1, (int)Math.Round(Bounds.Width))}x" +
                    $"{Math.Max(1, (int)Math.Round(Bounds.Height))}.png";
                var path = Path.Combine(screenshotDirectory!, shotName);
                File.WriteAllBytes(path, frame!);
                written = new FileInfo(path).Length > 0;
            }

            await viewModel.ResetKeePassImportCommand.ExecuteAsync(null);
            await Task.Delay(250);
            // Closing the database is not the same as being told it is gone: the row outlives the session.
            var rowSurvivesClose = Painted(Find<StackPanel>("KeePassRecentSection"))
                .Any(text => text == fileName);

            rowButton = Find<Button>("OpenKeePassRecentVaultButton");
            rowButton?.Command?.Execute(rowButton?.CommandParameter);
            await Task.Delay(250);
            var prompt = Find<TextBox>("KeePassImportPasswordBox");
            var promptCameBack = prompt is { IsEffectivelyVisible: true };
            var promptMaskedAndEmpty = prompt?.PasswordChar == '*' &&
                (prompt.Text is null || prompt.Text.Length == 0);
            var fileNameRestored = viewModel.KeePassSelectedFileName == fileName;

            await viewModel.ResetKeePassImportCommand.ExecuteAsync(null);
            await Task.Delay(150);
            File.Move(vaultPath, movedAside);
            fileIsMoved = true;
            viewModel.SelectedSyncPage = "Export";
            await Task.Delay(150);
            // Leaving and returning is what asks the disk again, and the walk back has to re-pick the tab
            // the page swap dropped.
            keepassTab = await RealizeSmokeKeePassImportTabAsync("recent shot") ?? keepassTab;
            await Task.Delay(250);
            var missingText = Find<TextBlock>("KeePassRecentFileMissingText");
            var goneRowSaysWhereItLived = missingText is { IsEffectivelyVisible: true } &&
                !string.IsNullOrEmpty(missingText.Text) &&
                Painted(Find<StackPanel>("KeePassRecentSection"))
                    .Any(text => text.Contains(directory, StringComparison.Ordinal));

            rowButton = Find<Button>("OpenKeePassRecentVaultButton");
            rowButton?.Command?.Execute(rowButton?.CommandParameter);
            await Task.Delay(250);
            var goneRowRefusedTheForm = viewModel.IsStatusMessageFailure &&
                Find<TextBox>("KeePassImportPasswordBox")?.IsEffectivelyVisible != true;

            File.Move(movedAside, vaultPath, overwrite: true);
            fileIsMoved = false;

            var dismiss = Find<Button>("ForgetKeePassRecentVaultButton");
            dismiss?.Command?.Execute(dismiss.CommandParameter);
            await Task.Delay(250);
            var listHiddenAfterDismiss = Find<Button>("ForgetKeePassRecentVaultButton") is null &&
                Find<StackPanel>("KeePassRecentSection") is not { IsEffectivelyVisible: true };

            var success = emptiedThroughTheControl &&
                listHiddenBefore &&
                opened &&
                rowOnScreen &&
                namedByFile &&
                noFolderShown &&
                paintedSecretFree &&
                saysWhenItWasOpened &&
                rowSurvivesClose &&
                promptCameBack &&
                promptMaskedAndEmpty &&
                fileNameRestored &&
                goneRowSaysWhereItLived &&
                goneRowRefusedTheForm &&
                listHiddenAfterDismiss &&
                keepassTab.IsSelected &&
                frameBytes > 0 &&
                (!wantedFile || written);
            AppDiagnostics.Info(
                $"Smoke UI KeePass recent shot result. success={success}, " +
                $"emptiedThroughTheControl={emptiedThroughTheControl}, " +
                $"listHiddenBefore={listHiddenBefore}, opened={opened}, rowOnScreen={rowOnScreen}, " +
                $"namedByFile={namedByFile}, noFolderShown={noFolderShown}, " +
                $"paintedSecretFree={paintedSecretFree}, saysWhenItWasOpened={saysWhenItWasOpened}, " +
                $"rowSurvivesClose={rowSurvivesClose}, promptCameBack={promptCameBack}, " +
                $"promptMaskedAndEmpty={promptMaskedAndEmpty}, fileNameRestored={fileNameRestored}, " +
                $"goneRowSaysWhereItLived={goneRowSaysWhereItLived}, " +
                $"goneRowRefusedTheForm={goneRowRefusedTheForm}, " +
                $"listHiddenAfterDismiss={listHiddenAfterDismiss}, " +
                $"paintedTexts={painted.Count}, tabSelected={keepassTab.IsSelected}, " +
                $"frameBytes={frameBytes}, written={written}, file={shotName}");
            return success;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Smoke UI KeePass recent shot failed", ex);
            return false;
        }
        finally
        {
            if (fileIsMoved && File.Exists(movedAside))
            {
                File.Move(movedAside, vaultPath, overwrite: true);
            }
        }
    }

    /// <summary>
    /// Walks the window to the opened-database tab both KeePass shots capture from. Returns the tab so
    /// a caller can prove it stayed selected in the frame it captured, or null after logging why it
    /// could not get there.
    /// </summary>
    private async Task<TabItem?> RealizeSmokeKeePassImportTabAsync(string reasonPrefix)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return null;
        }

        viewModel.SelectSectionCommand.Execute("Sync");
        viewModel.SelectedSyncPage = "Import";
        ShowFromDesktopIntegration();
        await Task.Delay(250);

        var tabs = this.GetVisualDescendants()
            .OfType<TabControl>()
            .FirstOrDefault(control => control.Name == "ImportSourceTabs");
        var keepassTab = tabs?.Items
            .OfType<TabItem>()
            .FirstOrDefault(item => item.Name == "KeePassImportTab");
        if (tabs is null || keepassTab is null)
        {
            AppDiagnostics.Info(
                $"Smoke UI KeePass {reasonPrefix} failed. reason=tabs-not-realized, " +
                $"tabsFound={tabs is not null}, keepassTabFound={keepassTab is not null}");
            return null;
        }

        tabs.SelectedItem = keepassTab;
        return keepassTab;
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
