using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class PasswordDetailViewModel : ObservableObject, IDisposable
{
    private readonly IClipboardService _clipboardService;
    private Func<PasswordEntry, Task<PasswordAttachmentAddResult>>? _addAttachment;
    private Func<Attachment, Task<PasswordAttachmentSaveResult>>? _saveAttachment;
    private Func<Attachment, Task<bool>>? _deleteAttachment;
    private Func<PasswordHistoryEntry, Task<bool>>? _deletePasswordHistory;
    private Func<long, Task<bool>>? _clearPasswordHistory;

    public PasswordDetailViewModel(
        ILocalizationService localization,
        IClipboardService clipboardService,
        ICryptoService cryptoService,
        ITotpService totpService,
        PasswordEntry entry,
        IReadOnlyList<PasswordEntry> siblings,
        Category? category,
        SecureItem? boundNote,
        IReadOnlyList<Attachment> attachments,
        IReadOnlyList<CustomField> customFields,
        IReadOnlyList<PasswordHistoryDisplayItem>? passwordHistory = null,
        Func<PasswordEntry, Task<PasswordAttachmentAddResult>>? addAttachment = null,
        Func<Attachment, Task<PasswordAttachmentSaveResult>>? saveAttachment = null,
        Func<Attachment, Task<bool>>? deleteAttachment = null,
        Func<PasswordHistoryEntry, Task<bool>>? deletePasswordHistory = null,
        Func<long, Task<bool>>? clearPasswordHistory = null)
    {
        L = localization;
        _clipboardService = clipboardService;
        _addAttachment = addAttachment;
        _saveAttachment = saveAttachment;
        _deleteAttachment = deleteAttachment;
        _deletePasswordHistory = deletePasswordHistory;
        _clearPasswordHistory = clearPasswordHistory;
        Entry = entry;
        DialogTitle = localization.Get("PasswordDetails");
        Title = entry.Title;
        Subtitle = string.Join(" - ", new[] { entry.Username, entry.Website }.Where(value => !string.IsNullOrWhiteSpace(value)));
        Initial = entry.AvatarText;
        PasswordHistoryDescription = localization.Get("PasswordHistoryDescription");

        InitializeBarcodePreview(cryptoService, entry);

        foreach (var group in BuildGroups(
            localization,
            cryptoService,
            totpService,
            entry,
            NormalizeSiblings(entry, siblings),
            category,
            boundNote,
            attachments,
            customFields))
        {
            Groups.Add(group);
        }

        foreach (var attachment in attachments
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Select(item => new PasswordAttachmentItem(localization, item)))
        {
            Attachments.Add(attachment);
        }

        SetPasswordHistory(passwordHistory ?? []);
    }

    public ILocalizationService L { get; }
    public PasswordEntry Entry { get; private set; }
    public string DialogTitle { get; }
    public string Title { get; private set; }
    public string Subtitle { get; private set; }
    public string Initial { get; private set; }
    public string CopyLabel => L.Get("Copy");
    public string AddAttachmentActionLabel => L.Get(IsAddingAttachment ? "AddingAttachment" : "AddAttachment");
    public string SaveAttachmentLabel => L.Get("SaveAttachment");
    public string DeleteLabel => L.Get("Delete");
    public string PasswordHistoryTitle => L.Get("PasswordHistory");
    public string PasswordHistoryDescription { get; }
    public string LatestLabel => L.Get("PasswordHistoryLatest");
    public string ClearPasswordHistoryLabel => L.Get("ClearPasswordHistory");
    public bool HasPasswordHistory => PasswordHistory.Count > 0;
    public bool HasAttachments => Attachments.Count > 0;
    public bool IsBarcode => Entry.LoginType == PasswordLoginType.Barcode;
    public string BarcodePayload { get; private set; } = "";
    public bool HasBarcodePayload => IsBarcode && !string.IsNullOrWhiteSpace(BarcodePayload);
    public bool HasBarcodePreview => BarcodeImage is not null;
    public bool IsQrBarcodeMode => BarcodeMode == BarcodePreviewMode.QrCode;
    public bool IsCode128BarcodeMode => BarcodeMode == BarcodePreviewMode.Code128;
    public string BarcodeModeLabel => L.Get("BarcodeRenderMode");
    public string BarcodeQrLabel => L.Get("BarcodeRenderQr");
    public string BarcodeCode128Label => L.Get("BarcodeRenderCode128");
    public string BarcodePayloadLabel => L.Get("BarcodePayload");
    public string BarcodeRenderFailedLabel => L.Get("BarcodeRenderFailed");
    public Avalonia.Media.Imaging.Bitmap? BarcodeImage { get; private set; }
    public BarcodePreviewMode BarcodeMode { get; private set; } = BarcodePreviewMode.QrCode;
    public bool IsSensitiveStateCleared { get; private set; }
    public ObservableCollection<PasswordDetailGroup> Groups { get; } = [];
    public ObservableCollection<PasswordAttachmentItem> Attachments { get; } = [];
    public ObservableCollection<PasswordHistoryItemViewModel> PasswordHistory { get; } = [];

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddAttachmentActionLabel))]
    private bool _isAddingAttachment;

    partial void OnIsAddingAttachmentChanged(bool value) =>
        AddAttachmentCommand.NotifyCanExecuteChanged();

    public void SetPasswordHistory(IReadOnlyList<PasswordHistoryDisplayItem> passwordHistory)
    {
        ClearPasswordHistoryPresentation();
        if (IsSensitiveStateCleared)
        {
            return;
        }

        foreach (var item in passwordHistory.Select((item, index) => new PasswordHistoryItemViewModel(L, item, index == 0)))
        {
            PasswordHistory.Add(item);
        }

        OnPropertyChanged(nameof(HasPasswordHistory));
    }

    public void ClearSensitiveState()
    {
        if (IsSensitiveStateCleared)
        {
            return;
        }

        foreach (var group in Groups)
        {
            group.ClearSensitiveState();
        }

        foreach (var attachment in Attachments)
        {
            attachment.ClearSensitiveState();
        }

        ClearPasswordHistoryPresentation();
        Groups.Clear();
        Attachments.Clear();
        Entry = new PasswordEntry();
        Title = "";
        Subtitle = "";
        Initial = "";
        StatusText = "";
        BarcodePayload = "";
        BarcodeImage?.Dispose();
        BarcodeImage = null;
        _addAttachment = null;
        IsAddingAttachment = false;
        _saveAttachment = null;
        _deleteAttachment = null;
        _deletePasswordHistory = null;
        _clearPasswordHistory = null;
        IsSensitiveStateCleared = true;
        OnPropertyChanged(nameof(Entry));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(Initial));
        OnPropertyChanged(nameof(IsSensitiveStateCleared));
        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(IsBarcode));
        OnPropertyChanged(nameof(BarcodePayload));
        OnPropertyChanged(nameof(HasBarcodePayload));
        OnPropertyChanged(nameof(HasBarcodePreview));
        OnPropertyChanged(nameof(BarcodeImage));
        AddAttachmentCommand.NotifyCanExecuteChanged();
    }

    public void Dispose() => ClearSensitiveState();

    private void ClearPasswordHistoryPresentation()
    {
        foreach (var item in PasswordHistory)
        {
            item.ClearSensitiveState();
        }

        PasswordHistory.Clear();
        OnPropertyChanged(nameof(HasPasswordHistory));
    }
}
