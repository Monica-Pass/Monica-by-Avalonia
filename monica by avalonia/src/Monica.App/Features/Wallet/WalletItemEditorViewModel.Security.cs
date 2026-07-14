using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Monica.App.ViewModels;

public sealed partial class WalletItemEditorViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentNumberMaskChar))]
    [NotifyPropertyChangedFor(nameof(DocumentNumberVisibilityLabel))]
    private bool _isDocumentNumberVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardNumberMaskChar))]
    [NotifyPropertyChangedFor(nameof(CardNumberVisibilityLabel))]
    private bool _isCardNumberVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CvvMaskChar))]
    [NotifyPropertyChangedFor(nameof(CvvVisibilityLabel))]
    private bool _isCvvVisible;

    public char DocumentNumberMaskChar => IsDocumentNumberVisible ? '\0' : '*';
    public char CardNumberMaskChar => IsCardNumberVisible ? '\0' : '*';
    public char CvvMaskChar => IsCvvVisible ? '\0' : '*';
    public string DocumentNumberVisibilityLabel => VisibilityLabel(IsDocumentNumberVisible);
    public string CardNumberVisibilityLabel => VisibilityLabel(IsCardNumberVisible);
    public string CvvVisibilityLabel => VisibilityLabel(IsCvvVisible);

    [RelayCommand]
    private void ToggleDocumentNumberVisibility() => IsDocumentNumberVisible = !IsDocumentNumberVisible;

    [RelayCommand]
    private void ToggleCardNumberVisibility() => IsCardNumberVisible = !IsCardNumberVisible;

    [RelayCommand]
    private void ToggleCvvVisibility() => IsCvvVisible = !IsCvvVisible;

    private string VisibilityLabel(bool isVisible) =>
        L.Get(isVisible ? "HideSensitiveField" : "ShowSensitiveField");

    partial void OnCardNumberChanged(string value)
    {
        var sanitized = new string(value.Where(character => char.IsDigit(character) || character == ' ').ToArray());
        if (!string.Equals(value, sanitized, StringComparison.Ordinal))
        {
            CardNumber = sanitized;
        }
    }

    partial void OnCvvChanged(string value)
    {
        var sanitized = new string(value.Where(char.IsDigit).Take(4).ToArray());
        if (!string.Equals(value, sanitized, StringComparison.Ordinal))
        {
            Cvv = sanitized;
        }
    }
}
