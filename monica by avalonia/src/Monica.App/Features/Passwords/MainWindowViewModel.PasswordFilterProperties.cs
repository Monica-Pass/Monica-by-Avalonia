using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public string PasswordCountText => _localization.Format("PasswordCountFormat", Passwords.Count);

    public string SelectPasswordItemsText => _localization.Get("SelectPasswordItems");

    public string SelectAllVisiblePasswordsText => _localization.Get("SelectAllVisiblePasswords");

    public string RetryPasswordDetailsText => _localization.Get("RetryPasswordDetails");

    public string SelectedPasswordTitle => SelectedPassword?.Title ?? _localization.Get("PasswordDetails");

    public string SelectedPasswordSubtitle => SelectedPassword is null
        ? PasswordCountText
        : BuildPasswordSubtitle(SelectedPassword);

    public string SelectedPasswordSourceText => SelectedPassword is null
        ? ""
        : SelectedPassword.IsMdbxEntry
            ? "MDBX"
            : SelectedPassword.IsKeePassEntry
                ? "KeePass"
                : SelectedPassword.IsBitwardenEntry
                    ? "Bitwarden"
                    : "Local";
}
