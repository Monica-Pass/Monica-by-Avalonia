namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void RefreshNoteCategoryOptions()
    {
        var selectedCategoryId = SelectedNoteCategory?.Id;
        var wasLoading = _isLoadingNoteEditor;
        _isLoadingNoteEditor = true;
        try
        {
            ReplaceItems(
                NoteCategoryOptions,
                PasswordCategoryChoice.BuildOptions(Categories, _localization.Get("NoFolder")));
            SelectedNoteCategory = FindNoteCategoryChoice(selectedCategoryId);
        }
        finally
        {
            _isLoadingNoteEditor = wasLoading;
        }
    }

    private PasswordCategoryChoice? FindNoteCategoryChoice(long? categoryId) =>
        NoteCategoryOptions.FirstOrDefault(choice => choice.Id == categoryId) ??
        NoteCategoryOptions.FirstOrDefault();
}
