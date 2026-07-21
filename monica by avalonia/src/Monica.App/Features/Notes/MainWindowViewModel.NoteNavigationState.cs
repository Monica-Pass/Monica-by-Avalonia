using CommunityToolkit.Mvvm.ComponentModel;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly HashSet<string> _collapsedNoteFolderKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _collapsedNoteTagKeys = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNoteFolderNavigation))]
    [NotifyPropertyChangedFor(nameof(IsNoteTagNavigation))]
    private string _noteNavigationMode = "Folders";

    public bool IsNoteFolderNavigation => string.Equals(NoteNavigationMode, "Folders", StringComparison.Ordinal);
    public bool IsNoteTagNavigation => !IsNoteFolderNavigation;
}
