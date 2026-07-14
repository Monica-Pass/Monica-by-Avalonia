using CommunityToolkit.Mvvm.ComponentModel;

namespace Monica.App.ViewModels;

public sealed partial class PasswordEditorViewModel
{
    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _websiteLines = "";

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _passwordLines = "";

    [ObservableProperty]
    private bool _isPasswordVisible;

    [ObservableProperty]
    private int _generatorLength = 24;

    [ObservableProperty]
    private bool _generatorIncludeUppercase = true;

    [ObservableProperty]
    private bool _generatorIncludeLowercase = true;

    [ObservableProperty]
    private bool _generatorIncludeNumbers = true;

    [ObservableProperty]
    private bool _generatorIncludeSymbols = true;

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private string _authenticatorKey = "";

    [ObservableProperty]
    private string _appPackageName = "";

    [ObservableProperty]
    private string _appName = "";

    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _phone = "";

    [ObservableProperty]
    private string _addressLine = "";

    [ObservableProperty]
    private string _city = "";

    [ObservableProperty]
    private string _state = "";

    [ObservableProperty]
    private string _zipCode = "";

    [ObservableProperty]
    private string _country = "";

    [ObservableProperty]
    private string _creditCardNumber = "";

    [ObservableProperty]
    private string _creditCardHolder = "";

    [ObservableProperty]
    private string _creditCardExpiry = "";

    [ObservableProperty]
    private string _creditCardCvv = "";

    [ObservableProperty]
    private string _passkeyBindings = "";

    [ObservableProperty]
    private string _sshKeyData = "";

    [ObservableProperty]
    private string _ssoProvider = "";

    [ObservableProperty]
    private string _wifiMetadata = "";

    [ObservableProperty]
    private string _customFieldsText = "";

    [ObservableProperty]
    private string _customIconValue = "";

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private PasswordCategoryChoice? _selectedCategory;

    [ObservableProperty]
    private PasswordLoginTypeChoice? _selectedLoginType;

    [ObservableProperty]
    private BoundNoteChoice? _selectedBoundNote;

    [ObservableProperty]
    private CustomIconTypeChoice? _selectedCustomIconType;
}
