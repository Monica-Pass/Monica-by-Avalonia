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
            StatusMessage = "无法打开此引用";
            return;
        }

        try
        {
            await _externalLinkService.OpenAsync(uri);
            StatusMessage = $"已打开 {uri.Host}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开引用失败：{ex.Message}";
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
        StatusMessage = "已复制引用";
    }
}
