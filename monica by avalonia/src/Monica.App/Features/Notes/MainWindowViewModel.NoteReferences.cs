using CommunityToolkit.Mvvm.Input;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{

    private bool CanOpenNoteReference(NoteReferenceItem? item) =>
        CanOpenExternalLinks && TryCreateExternalReferenceUri(item?.Target, out _);

    [RelayCommand(CanExecute = nameof(CanOpenNoteReference))]
    private async Task OpenNoteReferenceAsync(NoteReferenceItem? item)
    {
        if (!TryCreateExternalReferenceUri(item?.Target, out var uri))
        {
            StatusMessage = _localization.Get("ReferenceCannotOpen");
            return;
        }

        try
        {
            await _externalLinkService.OpenAsync(uri);
            StatusMessage = _localization.Format("OpenedReferenceFormat", uri.Host);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Opening secure note reference failed", ex);
            StatusMessage = _localization.Get("OpenReferenceFailed");
        }
    }

    [RelayCommand]
    private async Task CopyNoteReferenceAsync(NoteReferenceItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Target))
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(item.Target);
        StatusMessage = _localization.Get("CopiedReference");
    }
}
