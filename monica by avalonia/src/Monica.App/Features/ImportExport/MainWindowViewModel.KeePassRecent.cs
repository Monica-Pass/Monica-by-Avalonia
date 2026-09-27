using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

/// <summary>
/// One remembered database as the KeePass page shows it. The file's location is deliberately absent
/// except when two remembered files share a name, because the row is a thing to open rather than a
/// path to read, and a screen full of backslashes hides which vault is which.
/// </summary>
public sealed record KeePassRecentVaultRow
{
    public required string Path { get; init; }

    public required string Label { get; init; }

    public required string DirectoryText { get; init; }

    public required string LastOpenedText { get; init; }

    public required bool IsFileMissing { get; init; }

    public bool ShowsLastOpened => !IsFileMissing && LastOpenedText.Length > 0;

    public bool ShowsLocation => IsFileMissing || DirectoryText.Length > 0;
}

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeePassRecentVaults))]
    private IReadOnlyList<KeePassRecentVaultRow> _keePassRecentVaultRows = [];

    /// <summary>
    /// The section is not shown when it holds nothing. Two buttons that always work sit above it, and
    /// a line saying "nothing here yet" under them would only repeat what the absence already says.
    /// </summary>
    public bool HasKeePassRecentVaults => KeePassRecentVaultRows.Count > 0;

    /// <summary>
    /// Reads the remembered files out of settings and asks the disk about each one right now. A vault
    /// that is not where it was last unlocked stays listed and says so: a database on a drive that is
    /// not mounted, or a file the user moved on purpose, is not a row to throw away.
    /// </summary>
    private void RefreshKeePassRecentVaults()
    {
        var ordered = KeePassRecentVaultRegistry.Ordered(_settingsService.Current.KeePassRecentVaults);
        var repeatedNames = ordered
            .GroupBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = new List<KeePassRecentVaultRow>(ordered.Count);
        foreach (var item in ordered)
        {
            var exists = File.Exists(item.Path);
            // A row the user can no longer open is the row where saying which folder it came from
            // earns its keep, and so is a name two rows share.
            rows.Add(new KeePassRecentVaultRow
            {
                Path = item.Path,
                Label = item.DisplayName,
                DirectoryText = exists && !repeatedNames.Contains(item.DisplayName)
                    ? ""
                    : System.IO.Path.GetDirectoryName(item.Path) ?? "",
                LastOpenedText = FormatKeePassRecentTimestamp(item.LastOpenedAtUtc),
                IsFileMissing = !exists
            });
        }

        KeePassRecentVaultRows = rows;
    }

    private string FormatKeePassRecentTimestamp(string timestamp) =>
        DateTimeOffset.TryParseExact(
            timestamp,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var opened)
            ? opened.ToLocalTime().ToString("g", _localization.Culture)
            : "";

    /// <summary>
    /// A remembered row goes through the door every other file already uses: it names the file, the
    /// open form appears, and the master password is typed again. Nothing here unlocks anything on its
    /// own, and no secret of any kind is kept about the file - only where it is and when it was last
    /// opened. That is the whole difference between a recent list and a stored password.
    /// </summary>
    [RelayCommand]
    private async Task OpenKeePassRecentVaultAsync(KeePassRecentVaultRow? row)
    {
        if (row is not null)
        {
            await StageKeePassFileForOpenAsync(row.Path);
        }
    }

    [RelayCommand]
    private void ForgetKeePassRecentVault(KeePassRecentVaultRow? row)
    {
        if (row is null)
        {
            return;
        }

        UpdateSettings(settings => KeePassRecentVaultRegistry.Forget(settings.KeePassRecentVaults, row.Path));
        RefreshKeePassRecentVaults();
        SetStatusNotice("KeePassRecentRemovedFormat", row.Label);
    }

    /// <summary>
    /// Called where a database proved it unlocks - not where a file was picked - so a list of files
    /// this machine has actually opened is what the page offers.
    /// </summary>
    private void RememberKeePassVault(KeePassVaultSession? session)
    {
        if (session?.SourcePath is not { } path)
        {
            return;
        }

        UpdateSettings(settings => KeePassRecentVaultRegistry.Remember(
            settings.KeePassRecentVaults,
            path,
            displayName: null,
            DateTimeOffset.UtcNow));
        RefreshKeePassRecentVaults();
    }
}
