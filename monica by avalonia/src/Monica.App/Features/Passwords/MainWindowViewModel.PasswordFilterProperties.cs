using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public string PasswordCountText => _localization.Format("PasswordCountFormat", Passwords.Count);
    public string PasswordListStatusText => HasPasswordFilters
        ? _localization.Format("PasswordFilteredStatusFormat", FilteredPasswords.Count, Passwords.Count)
        : PasswordCountText;
    public bool HasFilteredPasswordRows => FilteredPasswordRows.Count > 0;
    public bool HasPasswordSearchText => !string.IsNullOrEmpty(PasswordSearchText);
    public bool ShowAddPasswordInEmptyState => Passwords.Count == 0;
    public bool ShowClearPasswordFiltersInEmptyState => Passwords.Count > 0 && HasPasswordFilters;
    public string PasswordEmptyStateText => ShowClearPasswordFiltersInEmptyState
        ? _localization.Get("PasswordNoFilteredResults")
        : _localization.Get("PasswordEmptyHint");
    public string SelectPasswordItemsText => _localization.Get("SelectPasswordItems");
    public string SelectAllVisiblePasswordsText => _localization.Get("SelectAllVisiblePasswords");
    public string SelectedPasswordCountText => _localization.Format("SelectedPasswordCountFormat", SelectedPasswordCount);
    public string BackToPasswordListText => _localization.Get("BackToPasswordList");
    public string RetryPasswordDetailsText => _localization.Get("RetryPasswordDetails");
    public string SelectedPasswordTitle => SelectedPassword?.Title ?? _localization.Get("PasswordDetails");
    public string SelectedPasswordSubtitle => SelectedPassword is null
        ? PasswordCountText
        : BuildPasswordSubtitle(SelectedPassword);
    public string SelectedPasswordSourceText => SelectedPassword is null
        ? ""
        : SelectedPassword.IsMdbxEntry
            ? "MDBX"
            : SelectedPassword.IsKeePassEntry
                ? "KeePass"
                : SelectedPassword.IsBitwardenEntry
                    ? "Bitwarden"
                    : "Local";
    public bool HasPasswordFilters =>
        !string.IsNullOrWhiteSpace(PasswordSearchText) ||
        QuickFilterFavorite ||
        QuickFilter2Fa ||
        QuickFilterNotes ||
        QuickFilterPasskey ||
        QuickFilterBoundNote ||
        QuickFilterUncategorized ||
        QuickFilterLocalOnly ||
        QuickFilterAttachments ||
        (SelectedPasswordFolderFilter is not null &&
            !string.Equals(SelectedPasswordFolderFilter.SelectionKey, "system:all", StringComparison.OrdinalIgnoreCase));
    public string ClearPasswordFiltersText => _localization.Get("ClearPasswordFilters");
    public string ClearPasswordSearchText => _localization.Get("ClearPasswordSearch");
    public string PasswordSearchHelpText => _localization.Get("PasswordSearchHelp");
    public string PasswordFilterSummaryText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(PasswordSearchText))
            {
                parts.Add(PasswordSearchText.Trim());
            }

            if (!string.Equals(SelectedPasswordFolderFilter?.SelectionKey, "system:all", StringComparison.OrdinalIgnoreCase) &&
                SelectedPasswordFolderFilter is not null)
            {
                parts.Add(SelectedPasswordFolderFilter.FolderDisplayName);
            }

            if (QuickFilterFavorite)
            {
                parts.Add(_localization.Get("QuickFilterFavorite"));
            }

            if (QuickFilter2Fa)
            {
                parts.Add(_localization.Get("QuickFilter2Fa"));
            }

            if (QuickFilterNotes)
            {
                parts.Add(_localization.Get("QuickFilterNotes"));
            }

            if (QuickFilterPasskey)
            {
                parts.Add(_localization.Get("QuickFilterPasskey"));
            }

            if (QuickFilterBoundNote)
            {
                parts.Add(_localization.Get("QuickFilterBoundNote"));
            }

            if (QuickFilterUncategorized)
            {
                parts.Add(_localization.Get("QuickFilterUncategorized"));
            }

            if (QuickFilterLocalOnly)
            {
                parts.Add(_localization.Get("QuickFilterLocalOnly"));
            }

            if (QuickFilterAttachments)
            {
                parts.Add(_localization.Get("QuickFilterAttachments"));
            }

            return parts.Count == 0
                ? _localization.Get("AllFolders")
                : string.Join(" / ", parts);
        }
    }
}
