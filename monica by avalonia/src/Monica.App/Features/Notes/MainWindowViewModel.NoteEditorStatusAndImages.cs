using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly TimeSpan NoteImagePreviewRefreshDelay = TimeSpan.FromMilliseconds(250);
    private CancellationTokenSource? _noteImagePreviewRefreshCts;

    public void UpdateNoteEditorStatus(int caretIndex, int selectionStart, int selectionEnd)
    {
        var text = NoteContent ?? "";
        caretIndex = Math.Clamp(caretIndex, 0, text.Length);
        selectionStart = Math.Clamp(selectionStart, 0, text.Length);
        selectionEnd = Math.Clamp(selectionEnd, 0, text.Length);
        var (line, column) = GetNoteCaretPosition(caretIndex);

        NoteCaretLine = line;
        NoteCaretColumn = column;
        NoteSelectedCharacterCount = Math.Abs(selectionEnd - selectionStart);
        if (!_isLoadingNoteEditor && SelectedNoteTab is not null)
        {
            SelectedNoteTab.DraftSelectionStart = selectionStart;
            SelectedNoteTab.DraftSelectionEnd = selectionEnd;
        }
    }

    private void QueueNoteImagePreviewRefresh(string content)
    {
        CancelNoteImagePreviewRefresh();
        if (string.IsNullOrEmpty(content))
        {
            Interlocked.Increment(ref _noteImagePreviewVersion);
            ReplaceNoteImagePreviews([]);
            return;
        }

        var ownerId = SelectedNoteTab?.Source?.Id ?? SelectedNote?.Id ?? 0;
        var dispatcher = Dispatcher.CurrentDispatcher;
        var cts = new CancellationTokenSource();
        _noteImagePreviewRefreshCts = cts;
        _ = RefreshNoteImagePreviewsAfterDelayAsync(content, ownerId, dispatcher, cts);
    }

    private async Task RefreshNoteImagePreviewsAfterDelayAsync(
        string content,
        long ownerId,
        Dispatcher dispatcher,
        CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(NoteImagePreviewRefreshDelay, cts.Token).ConfigureAwait(false);
            await RefreshNoteImagePreviewsAsync(
                content,
                ownerId,
                dispatcher,
                cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            AppDiagnostics.Error("Note image preview refresh failed", exception);
        }
        finally
        {
            if (ReferenceEquals(_noteImagePreviewRefreshCts, cts))
            {
                _noteImagePreviewRefreshCts = null;
            }

            cts.Dispose();
        }
    }

    private void CancelNoteImagePreviewRefresh()
    {
        var cts = _noteImagePreviewRefreshCts;
        if (cts is null)
        {
            return;
        }

        _noteImagePreviewRefreshCts = null;
        cts.Cancel();
    }

    private async Task RefreshNoteImagePreviewsAsync(
        string content,
        long ownerId,
        Dispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var version = Interlocked.Increment(ref _noteImagePreviewVersion);
        cancellationToken.ThrowIfCancellationRequested();
        var imagePaths = NoteContentCodec.ExtractInlineImageIds(content)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (imagePaths.Length == 0)
        {
            await PublishNoteImagePreviewsAsync(
                [],
                version,
                dispatcher,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var previews = new List<NoteImagePreviewItem>();
        try
        {
            foreach (var imagePath in imagePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var attachment = CreateNoteImageAttachment(imagePath, ownerId);
                    var contentBytes = await _repository.TryReadAttachmentContentAsync(
                        attachment,
                        cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (contentBytes is null || contentBytes.Length == 0)
                    {
                        continue;
                    }

                    using var stream = new MemoryStream(contentBytes);
                    previews.Add(new NoteImagePreviewItem(
                        imagePath,
                        BuildNoteImagePreviewName(imagePath, previews.Count + 1),
                        FormatByteSize(contentBytes.LongLength),
                        new Bitmap(stream)));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AppDiagnostics.Error($"Note image preview failed for {imagePath}", ex);
                }
            }

            await PublishNoteImagePreviewsAsync(
                previews,
                version,
                dispatcher,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            DisposeNoteImagePreviews(previews);
            throw;
        }
    }

    private async Task PublishNoteImagePreviewsAsync(
        IReadOnlyList<NoteImagePreviewItem> previews,
        int version,
        Dispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (version == _noteImagePreviewVersion)
            {
                ReplaceNoteImagePreviews(previews);
            }
            else
            {
                DisposeNoteImagePreviews(previews);
            }
        });
    }

    private Attachment CreateNoteImageAttachment(string imagePath, long ownerId)
    {
        return new Attachment
        {
            OwnerType = "SECURE_ITEM",
            OwnerId = ownerId,
            FileName = BuildNoteImagePreviewName(imagePath, 0),
            ContentType = InferImageContentType(imagePath),
            StoragePath = imagePath,
            SizeBytes = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private void ReplaceNoteImagePreviews(IReadOnlyList<NoteImagePreviewItem> previews)
    {
        DisposeNoteImagePreviews(NoteImagePreviewItems);

        ReplaceItems(NoteImagePreviewItems, previews);
        OnPropertyChanged(nameof(NoteImagePreviewCount));
        OnPropertyChanged(nameof(HasNoteImagePreviewItems));
    }

    private static void DisposeNoteImagePreviews(IEnumerable<NoteImagePreviewItem> previews)
    {
        foreach (var preview in previews)
        {
            preview.Image.Dispose();
        }
    }

    private string BuildNoteImagePreviewName(string imagePath, int fallbackIndex)
    {
        var normalized = imagePath.Trim();
        if (normalized.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
        {
            return fallbackIndex > 0
                ? $"MDBX {_localization.Format("NoteImageNumberFormat", fallbackIndex)}"
                : $"MDBX {_localization.Get("NoteImage")}";
        }

        var fileName = Path.GetFileName(normalized.Replace('\\', '/'));
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        return fallbackIndex > 0
            ? _localization.Format("NoteImageNumberFormat", fallbackIndex)
            : _localization.Get("NoteImage");
    }

}
