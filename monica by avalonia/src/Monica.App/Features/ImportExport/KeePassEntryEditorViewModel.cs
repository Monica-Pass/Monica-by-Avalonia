using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Platform.Services;

namespace Monica.App.Features.ImportExport;

/// <summary>
/// Edit form for one entry of an opened .kdbx. It carries the fields a KeePass client edits in place
/// plus the entry's custom fields and attachments exactly as they were read, so saving rewrites what
/// the user changed and leaves everything the form cannot express - custom fields, attachments,
/// history, AutoType - byte-for-byte as the file had them.
/// </summary>
public sealed partial class KeePassEntryEditorViewModel : ObservableObject
{
    private readonly KeePassEntryDetail _detail;

    public KeePassEntryEditorViewModel(KeePassEntryDetail detail)
    {
        _detail = detail;
        EntryUuid = detail.Row.EntryUuid;
        GroupPath = detail.Row.GroupPath;
        _title = detail.Row.Title;
        _userName = detail.Row.UserName;
        _password = detail.Password;
        _url = detail.Row.Url;
        _notes = detail.Notes;
        _authenticatorKey = detail.AuthenticatorKey;
    }

    public string EntryUuid { get; }

    public string GroupPath { get; }

    /// <summary>
    /// Custom fields are shown so the user knows they exist, but the edit hands the original list
    /// straight back. Removing a field the form cannot re-create would be a quieter kind of data loss.
    /// </summary>
    public IReadOnlyList<KeePassCustomField> CustomFields => _detail.CustomFields;

    public IReadOnlyList<KeePassAttachmentRow> Attachments => _detail.Row.Attachments;

    public bool HasAttachments => Attachments.Count > 0;

    public bool HasCustomFields => CustomFields.Count > 0;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _url = "";

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private string _authenticatorKey = "";

    [ObservableProperty]
    private bool _isPasswordVisible;

    public KeePassEntryEdit ToEdit() => new(
        EntryUuid,
        Title,
        UserName,
        Password,
        Url,
        Notes,
        AuthenticatorKey,
        CustomFields);
}
