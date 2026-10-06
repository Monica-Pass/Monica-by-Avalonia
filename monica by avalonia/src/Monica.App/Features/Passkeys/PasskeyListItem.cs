using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Passkeys;

namespace Monica.App.ViewModels;

/// <summary>A presentation projection containing no private-key reference or material.</summary>
public sealed partial class PasskeyListItem : ObservableObject
{
    public PasskeyListItem(PasskeyEntry entry, ILocalizationService localization)
    {
        Id = entry.Id;
        RpId = entry.RpId;
        RpName = entry.RpName;
        UserName = entry.UserName;
        UserDisplayName = entry.UserDisplayName;
        CredentialId = entry.CredentialId;
        Mode = entry.PasskeyMode;
        Transport = entry.Transports;
        CreatedAt = entry.CreatedAt;
        LastUsedAt = entry.LastUsedAt;
        UseCount = entry.UseCount;
        IsDiscoverable = entry.IsDiscoverable;
        RefreshLocalization(localization);
    }

    public long Id { get; }
    public string Mode { get; }
    public bool IsDiscoverable { get; }
    private DateTimeOffset CreatedAt { get; }
    private DateTimeOffset LastUsedAt { get; }
    private int UseCount { get; }
    public bool IsPlatformManaged => Mode is PasskeyModes.WindowsHello or PasskeyModes.MacOsAuthenticationServices
        or PasskeyModes.AndroidCredentialManager or PasskeyModes.LinuxFido2 or PasskeyModes.Legacy;
    public string Title => string.IsNullOrWhiteSpace(RpName) ? RpId : RpName;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] private string _rpId = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] private string _rpName = "";
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _userDisplayName = "";
    [ObservableProperty] private string _credentialId = "";
    [ObservableProperty] private string _transport = "";
    [ObservableProperty] private string _ownerText = "";
    [ObservableProperty] private string _createdText = "";
    [ObservableProperty] private string _lastUsedText = "";
    [ObservableProperty] private string _discoverableText = "";

    public void RefreshLocalization(ILocalizationService localization)
    {
        OwnerText = Mode switch
        {
            PasskeyModes.WindowsHello => "Windows Hello",
            PasskeyModes.MacOsAuthenticationServices => "macOS AuthenticationServices",
            PasskeyModes.AndroidCredentialManager => "Android Credential Manager",
            PasskeyModes.LinuxFido2 => "Linux FIDO2",
            PasskeyModes.BitwardenCompatible or PasskeyModes.KeePassCompatible => localization.Get("PasskeyOwnerMonica"),
            _ => localization.Get("PasskeyOwnerOther")
        };
        CreatedText = CreatedAt.ToLocalTime().ToString("g", localization.Culture);
        LastUsedText = UseCount == 0 ? localization.Get("PasskeyNotUsed") : LastUsedAt.ToLocalTime().ToString("g", localization.Culture);
        DiscoverableText = localization.Get(IsDiscoverable ? "PasskeyDiscoverable" : "PasskeyNotDiscoverable");
    }

    internal bool Matches(string query) => RpId.Contains(query, StringComparison.OrdinalIgnoreCase)
        || RpName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || UserName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || UserDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);

    internal void ClearSensitiveState()
    {
        RpId = RpName = UserName = UserDisplayName = CredentialId = Transport = "";
        OwnerText = CreatedText = LastUsedText = DiscoverableText = "";
    }
}
