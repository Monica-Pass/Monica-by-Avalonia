using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void ResetNoteEditor()
    {
        NoteTitle = "";
        NoteContent = "";
        NoteTagsText = "";
        SelectedNoteCategory = FindNoteCategoryChoice(null);
        NoteIsMarkdown = true;
        NotePreviewMode = false;
        NoteSplitPreviewMode = false;
        NoteIsFavorite = false;
    }
}
