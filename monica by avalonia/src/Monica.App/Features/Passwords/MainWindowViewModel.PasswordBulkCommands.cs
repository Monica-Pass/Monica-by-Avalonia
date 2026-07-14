using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task FavoriteSelectedPasswordsAsync()
    {
        var selected = Passwords.Where(item => item.IsSelected).ToArray();
        foreach (var entry in selected)
        {
            if (!entry.IsFavorite)
            {
                entry.IsFavorite = true;
                await _repository.SavePasswordAsync(entry);
                await LogOperationAsync(new OperationLog
                {
                    ItemType = "PASSWORD",
                    ItemId = entry.Id,
                    ItemTitle = entry.Title,
                    OperationType = "FAVORITE",
                    DeviceName = Environment.MachineName
                });
            }
        }

        UpdatePasswordSelectionsInBatch(() =>
        {
            foreach (var entry in selected)
            {
                entry.IsSelected = false;
            }
        });

        RaiseFilteredPasswordsChanged();
        InvalidateSecurityAnalysis();
        StatusMessage = _localization.Format("FavoritedPasswordCountFormat", selected.Length);
    }

    [RelayCommand]
    private async Task DeleteSelectedPasswordsAsync()
    {
        var selected = Passwords.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        if (!await _confirmationDialogService.ConfirmAsync(
            _localization.Get("DeleteSelectedPasswordsConfirmationTitle"),
            _localization.Format("DeleteSelectedPasswordsConfirmationMessageFormat", selected.Length),
            _localization.Get("MoveToRecycleBin"),
            _localization.Cancel))
        {
            return;
        }

        UpdatePasswordSelectionsInBatch(() =>
        {
            foreach (var entry in selected)
            {
                entry.IsSelected = false;
            }
        });

        var handled = new HashSet<long>();
        foreach (var entry in selected)
        {
            if (!handled.Add(entry.Id))
            {
                continue;
            }

            var siblings = GetPasswordSiblings(entry).ToArray();
            foreach (var sibling in siblings)
            {
                handled.Add(sibling.Id);
            }

            await DeletePasswordGroupAsync(entry, siblings, updateStatus: false);
        }

        RefreshPasswordSelectionStateFromPasswords();
        StatusMessage = _localization.Format("MovedSelectedPasswordsToRecycleBinFormat", selected.Length);
    }

    [RelayCommand]
    private async Task ArchiveSelectedPasswordsAsync()
    {
        var selected = Passwords.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        var handled = new HashSet<long>();
        UpdatePasswordSelectionsInBatch(() =>
        {
            foreach (var entry in selected)
            {
                entry.IsSelected = false;
            }
        });

        foreach (var entry in selected)
        {
            if (!handled.Add(entry.Id))
            {
                continue;
            }

            var siblings = GetPasswordSiblings(entry).ToArray();
            foreach (var sibling in siblings)
            {
                handled.Add(sibling.Id);
            }

            await ArchivePasswordGroupAsync(entry, siblings, updateStatus: false);
        }

        RefreshPasswordSelectionStateFromPasswords();
        StatusMessage = _localization.Format("ArchivedSelectedPasswordsFormat", selected.Length);
    }

    [RelayCommand]
    private async Task MoveSelectedPasswordsToCategoryAsync()
    {
        var selected = Passwords.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        var currentCategoryId = selected
            .Select(item => item.CategoryId)
            .Distinct()
            .Count() == 1
                ? selected[0].CategoryId
                : null;
        var choice = await _categoryPickerDialogService.ShowAsync(Categories.ToList(), currentCategoryId);
        if (choice is null)
        {
            return;
        }

        var handled = new HashSet<long>();
        foreach (var entry in selected)
        {
            if (!handled.Add(entry.Id))
            {
                continue;
            }

            var siblings = GetPasswordSiblings(entry).ToArray();
            foreach (var sibling in siblings)
            {
                handled.Add(sibling.Id);
                sibling.CategoryId = choice.Id;
                await _repository.SavePasswordAsync(sibling);
                await SynchronizeBoundTotpAsync(sibling);
                await LogOperationAsync(new OperationLog
                {
                    ItemType = "PASSWORD",
                    ItemId = sibling.Id,
                    ItemTitle = sibling.Title,
                    OperationType = "MOVE_CATEGORY",
                    DeviceName = Environment.MachineName
                });
            }
        }

        UpdatePasswordSelectionsInBatch(() =>
        {
            foreach (var entry in selected)
            {
                entry.IsSelected = false;
            }
        });

        RefreshBoundTotpPresentation(selected);
        RefreshPasswordFolderFilters(choice.Id);
        RaiseFilteredPasswordsChanged();
        StatusMessage = _localization.Format("MovedSelectedPasswordsToFolderFormat", selected.Length, choice.Name);
    }
    [RelayCommand]
    private async Task StackSelectedPasswordsAsync()
    {
        var selected = Passwords
            .Where(item => item.IsSelected)
            .OrderBy(item => item.Id == 0 ? long.MaxValue : item.Id)
            .ToArray();
        if (selected.Length < 2)
        {
            return;
        }

        var replicaGroupId = selected
            .Select(item => item.ReplicaGroupId)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? $"manual-{Guid.NewGuid():N}";
        UpdatePasswordSelectionsInBatch(() =>
        {
            foreach (var entry in selected)
            {
                entry.IsSelected = false;
            }
        });

        foreach (var entry in selected)
        {
            entry.ReplicaGroupId = replicaGroupId;
            await _repository.SavePasswordAsync(entry);
            await LogOperationAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = entry.Id,
                ItemTitle = entry.Title,
                OperationType = "STACK",
                DeviceName = Environment.MachineName
            });
        }

        RaiseFilteredPasswordsChanged();
        InvalidateSecurityAnalysis();
        StatusMessage = _localization.Format("StackedPasswordCountFormat", selected.Length);
    }

}
