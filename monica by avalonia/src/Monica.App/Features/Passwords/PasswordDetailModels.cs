using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public enum PasswordAttachmentAddOutcome
{
    Added,
    Cancelled,
    VaultLocked,
    TooLarge,
    Failed
}

public readonly record struct PasswordAttachmentAddResult(
    PasswordAttachmentAddOutcome Outcome,
    Attachment? Attachment = null,
    string StatusText = "");

public enum PasswordAttachmentSaveOutcome
{
    Saved,
    Cancelled,
    AuthorizationFailed,
    ContentUnavailable,
    Failed
}

public readonly record struct PasswordAttachmentSaveResult(PasswordAttachmentSaveOutcome Outcome);

public sealed partial class PasswordDetailGroup : ObservableObject
{
    public PasswordDetailGroup(string title, bool isExpanded, IReadOnlyList<PasswordDetailField> fields)
    {
        Title = title;
        _isExpanded = isExpanded;
        Fields = fields;
    }

    public string Title { get; }
    public IReadOnlyList<PasswordDetailField> Fields { get; }
    public IReadOnlyList<PasswordDetailField> VisibleFields => IsExpanded ? Fields : [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleFields))]
    private bool _isExpanded;

    internal void ClearSensitiveState()
    {
        foreach (var field in Fields)
        {
            field.ClearSensitiveState();
        }

        IsExpanded = false;
    }
}

public sealed partial class PasswordDetailField(
    string label,
    string displayValue,
    string copyValue,
    bool canCopy = true,
    bool isSensitive = false) : ObservableObject
{
    private const string HiddenSensitiveValue = "************";
    private string _showLabel = "";
    private string _hideLabel = "";

    public string Label { get; } = label;
    public string DisplayValue { get; private set; } = displayValue;
    public string CopyValue { get; private set; } = copyValue;
    public bool CanCopy { get; private set; } = canCopy;
    public bool IsSensitive { get; } = isSensitive;
    public bool CanToggleVisibility => IsSensitive && CanCopy && !string.IsNullOrWhiteSpace(DisplayValue);
    public string DisplayText => CanToggleVisibility && !IsVisible ? HiddenSensitiveValue : DisplayValue;
    public string VisibilityActionLabel => IsVisible ? _hideLabel : _showLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    [NotifyPropertyChangedFor(nameof(VisibilityActionLabel))]
    private bool _isVisible;

    internal void ConfigureVisibilityLabels(string showLabel, string hideLabel)
    {
        _showLabel = showLabel;
        _hideLabel = hideLabel;
        OnPropertyChanged(nameof(VisibilityActionLabel));
    }

    internal void ClearSensitiveState()
    {
        DisplayValue = "";
        CopyValue = "";
        CanCopy = false;
        IsVisible = false;
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(CopyValue));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(CanToggleVisibility));
        OnPropertyChanged(nameof(DisplayText));
    }
}

public sealed class PasswordAttachmentItem(ILocalizationService localization, Attachment attachment)
{
    public Attachment Attachment { get; private set; } = attachment;
    public string FileName => Attachment.FileName;
    public string DisplayValue => BuildAttachmentDisplayValue(localization, Attachment);

    internal void ClearSensitiveState() => Attachment = new Attachment();

    private static string BuildAttachmentDisplayValue(ILocalizationService localization, Attachment attachment)
    {
        var values = new[]
        {
            FormatAttachmentSize(attachment.SizeBytes),
            attachment.ContentType
        }.Where(value => !string.IsNullOrWhiteSpace(value));
        return string.Join(" - ", values);
    }

    private static string FormatAttachmentSize(long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            return "";
        }

        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)sizeBytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{sizeBytes} {units[unit]}" : $"{size:0.#} {units[unit]}";
    }
}

public sealed partial class PasswordHistoryItemViewModel : ObservableObject
{
    private readonly string _showLabel;
    private readonly string _hideLabel;

    public PasswordHistoryItemViewModel(ILocalizationService localization, PasswordHistoryDisplayItem source, bool isLatest)
    {
        Entry = source.Entry;
        Password = source.DisplayPassword;
        CanCopy = source.CanCopy;
        IsLatest = isLatest;
        LastUsedText = localization.Format("PasswordHistoryLastUsedFormat", source.Entry.LastUsedAt.ToString("g", localization.Culture));
        _showLabel = localization.Get("ShowPassword");
        _hideLabel = localization.Get("HidePassword");
    }

    public PasswordHistoryEntry Entry { get; private set; }
    public string Password { get; private set; }
    public bool CanCopy { get; private set; }
    public bool IsLatest { get; }
    public string LastUsedText { get; private set; }
    public string DisplayPassword => IsVisible ? Password : new string('*', Math.Clamp(Password.Length, 8, 24));
    public string VisibilityActionLabel => IsVisible ? _hideLabel : _showLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayPassword))]
    [NotifyPropertyChangedFor(nameof(VisibilityActionLabel))]
    private bool _isVisible;

    internal void ClearSensitiveState()
    {
        Entry = new PasswordHistoryEntry();
        Password = "";
        CanCopy = false;
        LastUsedText = "";
        IsVisible = false;
        OnPropertyChanged(nameof(Entry));
        OnPropertyChanged(nameof(Password));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(LastUsedText));
        OnPropertyChanged(nameof(DisplayPassword));
    }
}
