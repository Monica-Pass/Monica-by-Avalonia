using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed record LocalizedPlatformIntegrationCapability(
    string Key,
    string Title,
    string Description,
    string Status,
    string UnsupportedReason,
    PlatformFeatureStatus StatusValue)
{
    public bool HasUnsupportedReason => !string.IsNullOrWhiteSpace(UnsupportedReason);
    public bool IsUsable => StatusValue is PlatformFeatureStatus.Available or PlatformFeatureStatus.DesktopEquivalent;
}

public sealed class LocalizedPlatformCapability : ObservableObject
{
    private readonly Action<string, bool> _setFeatureEnabled;
    private readonly string _enabledText;
    private readonly string _disabledText;
    private bool _isEnabled;

    public LocalizedPlatformCapability(
        string key,
        string title,
        string description,
        string status,
        bool isEnabled,
        bool canToggle,
        string enabledText,
        string disabledText,
        string unsupportedReason,
        Action<string, bool> setFeatureEnabled)
    {
        Key = key;
        Title = title;
        Description = description;
        Status = status;
        CanToggle = canToggle;
        UnsupportedReason = unsupportedReason;
        _enabledText = enabledText;
        _disabledText = disabledText;
        _setFeatureEnabled = setFeatureEnabled;
        _isEnabled = canToggle && isEnabled;
    }

    public string Key { get; }
    public string Title { get; }
    public string Description { get; }
    public string Status { get; }
    public bool CanToggle { get; }
    public string UnsupportedReason { get; }
    public bool HasUnsupportedReason => !string.IsNullOrWhiteSpace(UnsupportedReason);
    public string ToggleStatus => IsEnabled ? _enabledText : _disabledText;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            var normalizedValue = CanToggle && value;
            if (!SetProperty(ref _isEnabled, normalizedValue))
            {
                return;
            }

            _setFeatureEnabled(Key, normalizedValue);
            OnPropertyChanged(nameof(ToggleStatus));
        }
    }
}
