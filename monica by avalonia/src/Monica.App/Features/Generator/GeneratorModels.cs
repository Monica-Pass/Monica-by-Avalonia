using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Monica.App.ViewModels;

public sealed partial class GeneratorHistoryItem : ObservableObject
{
    private const string MaskedDisplayValue = "••••••••••";

    public GeneratorHistoryItem(string value, string modeLabel, string strengthText, string createdAtText)
    {
        Value = value;
        ModeLabel = modeLabel;
        StrengthText = strengthText;
        CreatedAtText = createdAtText;
    }

    public string Value { get; private set; }
    public string ModeLabel { get; }
    public string StrengthText { get; }
    public string CreatedAtText { get; }
    public string DisplayValue => string.IsNullOrEmpty(Value)
        ? ""
        : IsRevealed ? Value : MaskedDisplayValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    private bool _isRevealed;

    [RelayCommand]
    private void ToggleVisibility()
    {
        if (!string.IsNullOrEmpty(Value))
        {
            IsRevealed = !IsRevealed;
        }
    }

    public void ClearSensitiveState()
    {
        Value = "";
        IsRevealed = false;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(DisplayValue));
    }
}
