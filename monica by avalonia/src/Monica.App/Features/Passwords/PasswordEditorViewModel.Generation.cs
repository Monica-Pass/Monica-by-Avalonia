using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;

namespace Monica.App.ViewModels;

public sealed partial class PasswordEditorViewModel
{
    public char PasswordMaskChar => IsPasswordVisible ? '\0' : '*';
    public string TogglePasswordVisibilityLabel => IsPasswordVisible ? L.Get("HidePassword") : L.Get("ShowPassword");

    public string PasswordEditorStrengthText
    {
        get
        {
            var rows = GetPasswordRows();
            if (rows.Count == 0)
            {
                return L.Get("GeneratorNoPassword");
            }

            var strength = _passwordGenerator.Analyze(rows[0]);
            return L.Format(
                "GeneratedPasswordStrengthFormat",
                PasswordStrengthLocalization.Label(L, strength.Label),
                strength.Score,
                PasswordStrengthLocalization.Warnings(L, strength.Warnings));
        }
    }

    public int PasswordEditorStrengthValue
    {
        get
        {
            var rows = GetPasswordRows();
            return rows.Count == 0 ? 0 : _passwordGenerator.Analyze(rows[0]).Score * 20;
        }
    }

    public string PasswordRowCountText => L.Format("PasswordRowCountFormat", GetPasswordRows().Count);
    public string GeneratorLengthText => L.Format("GeneratorLengthFormat", GeneratorLength);
    public bool IsCustomIconValueEnabled => SelectedCustomIconType?.Value is "SIMPLE_ICON" or "UPLOADED";
    public string CustomIconValueWatermark => SelectedCustomIconType?.Value == "UPLOADED"
        ? L.Get("CustomIconUploadedHint")
        : L.Get("CustomIconSimpleHint");

    [RelayCommand]
    private void GeneratePassword()
    {
        var rows = GetPasswordRows().ToList();
        if (rows.Count == 0)
        {
            rows.Add(GenerateEditorPassword());
        }
        else
        {
            rows[0] = GenerateEditorPassword();
        }

        PasswordLines = string.Join(Environment.NewLine, rows);
    }

    [RelayCommand]
    private void AddGeneratedPasswordRow()
    {
        var rows = GetPasswordRows().ToList();
        rows.Add(GenerateEditorPassword());
        PasswordLines = string.Join(Environment.NewLine, rows);
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordVisible = !IsPasswordVisible;
    }

    partial void OnPasswordLinesChanged(string value)
    {
        RaisePasswordEditorState();
        ClearCorrectedPasswordValidation();
    }

    partial void OnSelectedCustomIconTypeChanged(CustomIconTypeChoice? value)
    {
        if (value?.Value == "NONE")
        {
            CustomIconValue = "";
        }

        OnPropertyChanged(nameof(IsCustomIconValueEnabled));
        OnPropertyChanged(nameof(CustomIconValueWatermark));
    }

    partial void OnIsPasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordMaskChar));
        OnPropertyChanged(nameof(TogglePasswordVisibilityLabel));
    }

    partial void OnGeneratorLengthChanged(int value)
    {
        GeneratorLength = Math.Clamp(value, 8, 128);
        OnPropertyChanged(nameof(GeneratorLengthText));
    }

    private string GenerateEditorPassword() =>
        _passwordGenerator.GeneratePassword(
            GeneratorLength,
            GeneratorIncludeUppercase,
            GeneratorIncludeLowercase,
            GeneratorIncludeNumbers,
            GeneratorIncludeSymbols);

    private void RaisePasswordEditorState()
    {
        OnPropertyChanged(nameof(PasswordEditorStrengthText));
        OnPropertyChanged(nameof(PasswordEditorStrengthValue));
        OnPropertyChanged(nameof(PasswordRowCountText));
    }
}
