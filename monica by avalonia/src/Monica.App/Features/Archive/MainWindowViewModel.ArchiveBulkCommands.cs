using CommunityToolkit.Mvvm.Input;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task UnarchiveSelectedArchivedPasswordsAsync()
    {
        var selected = ArchivedPasswords.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        var handledGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in selected)
        {
            if (handledGroups.Add(BuildSiblingGroupKey(entry)))
            {
                await UnarchivePasswordAsync(entry);
            }
        }

        ClearArchivedPasswordSelection();
        StatusMessage = _localization.Format("UnarchivedSelectedPasswordsFormat", selected.Length);
    }
}
