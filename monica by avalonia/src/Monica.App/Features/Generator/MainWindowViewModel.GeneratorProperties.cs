using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const int MaxGeneratorHistoryItems = 8;
    private const string GeneratorModeRandom = "random";
    private const string GeneratorModePassphrase = "passphrase";
    private const string GeneratorModePin = "pin";
    private const string GeneratorModeUsername = "username";
    private const string GeneratorTemplateBalanced = "balanced";
    private const string GeneratorTemplateMaximum = "maximum";
    private const string GeneratorTemplateMemorable = "memorable";
    private const string GeneratorTemplatePin = "pin";
    private const string GeneratorTemplateUsername = "username";
    private const string SimilarGeneratorCharacters = "0OolI1|`";

    private bool _isApplyingGeneratorTemplate;

    private static readonly string[] GeneratorPassphraseWords =
    [
        "amber", "atlas", "brisk", "cedar", "cinder", "cobalt", "coral", "delta",
        "ember", "falcon", "frost", "harbor", "ivory", "juniper", "kinetic", "linen",
        "meadow", "meteor", "nebula", "onyx", "orchid", "pixel", "quartz", "ripple",
        "saffron", "signal", "silver", "summit", "tundra", "velvet", "violet", "willow"
    ];

    public ObservableCollection<SettingsChoice> GeneratorModeOptions { get; } = [];
    public ObservableCollection<SettingsChoice> GeneratorTemplateOptions { get; } = [];
    public ObservableCollection<GeneratorHistoryItem> GeneratedPasswordHistory { get; } = [];

    [ObservableProperty]
    private string _generatedPassword = "";

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
    private bool _generatorExcludeSimilarCharacters;

    [ObservableProperty]
    private string _generatorMode = GeneratorModeRandom;

    [ObservableProperty]
    private string _generatorTemplate = GeneratorTemplateBalanced;

    [ObservableProperty]
    private int _generatorWordCount = 4;

    public Thickness GeneratorResultPanelPadding => IsOtherWorkspaceCompact
        ? new Thickness(18)
        : new Thickness(24);
    public Thickness GeneratorOptionsPanelPadding => IsOtherWorkspaceCompact
        ? new Thickness(14)
        : new Thickness(18);
    public double GeneratorOptionsSpacing => IsOtherWorkspaceCompact ? 12 : 18;
    public double GeneratorCheckboxSpacing => IsOtherWorkspaceCompact ? 6 : 10;
    public double GeneratorPasswordBoxMinHeight => IsOtherWorkspaceCompact ? 96 : 170;
    public double GeneratorHistoryPanelMaxHeight => IsOtherWorkspaceCompact ? 78 : 104;
    public bool ShowGeneratorStrengthSummaryCard => !IsOtherWorkspaceCompact;

    public string GeneratorLengthText => _localization.Format("GeneratorLengthFormat", GeneratorLength);
    public string GeneratorWordCountText => _localization.Format("GeneratorWordCountFormat", GeneratorWordCount);
    public int GeneratorLengthMinimum => GeneratorMode == GeneratorModePin ? 3 : 4;
    public int GeneratorLengthMaximum => GeneratorMode == GeneratorModePin ? 9 : 128;
    public bool IsGeneratorPassphraseMode => GeneratorMode == GeneratorModePassphrase;
    public bool ShowGeneratorCharacterOptions => GeneratorMode == GeneratorModeRandom;
    public bool ShowGeneratorUsernameOptions => GeneratorMode == GeneratorModeUsername;
    public bool ShowGeneratorLengthOptions => GeneratorMode is not GeneratorModePassphrase;
    public bool ShowGeneratorWordCountOptions => GeneratorMode == GeneratorModePassphrase;
    public bool HasGeneratedPasswordHistory => GeneratedPasswordHistory.Count > 0;
    public bool CanGeneratePassword => GeneratorMode != GeneratorModeRandom ||
        GeneratorIncludeUppercase || GeneratorIncludeLowercase || GeneratorIncludeNumbers || GeneratorIncludeSymbols;
    public bool CanCopyGeneratedPassword => CanGeneratePassword && !string.IsNullOrEmpty(GeneratedPassword);
    public bool HasGeneratorValidationError => !CanGeneratePassword;
    public string GeneratorValidationMessage => HasGeneratorValidationError
        ? _localization.Get("GeneratorSelectCharacterType")
        : _localization.Get("GeneratorReady");
    public string SelectedGeneratorModeLabel => FindChoiceLabel(GeneratorModeOptions, GeneratorMode);
    public string SelectedGeneratorTemplateLabel => FindChoiceLabel(GeneratorTemplateOptions, GeneratorTemplate);

    public SettingsChoice? SelectedGeneratorModeOption
    {
        get => GeneratorModeOptions.FirstOrDefault(item => Equals(item.Value, GeneratorMode));
        set
        {
            if (value?.Value is string mode)
            {
                GeneratorMode = mode;
            }
        }
    }

    public SettingsChoice? SelectedGeneratorTemplateOption
    {
        get => GeneratorTemplateOptions.FirstOrDefault(item => Equals(item.Value, GeneratorTemplate));
        set
        {
            if (value?.Value is string template)
            {
                GeneratorTemplate = template;
            }
        }
    }

    public string GeneratorStrategySummaryText => GeneratorMode switch
    {
        GeneratorModePassphrase => _localization.Format(
            "GeneratorStrategyPassphraseFormat",
            SelectedGeneratorModeLabel,
            GeneratorWordCount),
        GeneratorModePin => _localization.Format(
            "GeneratorStrategyLengthFormat",
            SelectedGeneratorModeLabel,
            GeneratorLength),
        _ => _localization.Format(
            "GeneratorStrategyLengthFormat",
            SelectedGeneratorModeLabel,
            GeneratorLength)
    };

    public string GeneratedPasswordStrengthText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(GeneratedPassword))
            {
                return _localization.Get("GeneratorNoPassword");
            }

            var strength = _passwordGenerator.Analyze(GeneratedPassword);
            return _localization.Format(
                "GeneratedPasswordStrengthFormat",
                PasswordStrengthLocalization.Label(_localization, strength.Label),
                strength.Score,
                PasswordStrengthLocalization.Warnings(_localization, strength.Warnings));
        }
    }

    partial void OnGeneratedPasswordChanged(string value)
    {
        OnPropertyChanged(nameof(GeneratedPasswordStrengthText));
        OnPropertyChanged(nameof(CanCopyGeneratedPassword));
        CopyGeneratedPasswordCommand.NotifyCanExecuteChanged();
    }

    partial void OnGeneratorLengthChanged(int value)
    {
        GeneratorLength = Math.Clamp(value, GeneratorLengthMinimum, GeneratorLengthMaximum);
        HandleGeneratorOptionsChanged();
    }

    partial void OnGeneratorWordCountChanged(int value)
    {
        GeneratorWordCount = Math.Clamp(value, 1, 20);
        HandleGeneratorOptionsChanged();
    }

    partial void OnGeneratorModeChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            GeneratorMode = GeneratorModeRandom;
            return;
        }

        if (_isApplyingGeneratorTemplate)
        {
            GeneratorLength = Math.Clamp(GeneratorLength, GeneratorLengthMinimum, GeneratorLengthMaximum);
            return;
        }

        _isApplyingGeneratorTemplate = true;
        try
        {
            GeneratorLength = Math.Clamp(GeneratorLength, GeneratorLengthMinimum, GeneratorLengthMaximum);
        }
        finally
        {
            _isApplyingGeneratorTemplate = false;
        }

        HandleGeneratorOptionsChanged();
    }

    partial void OnGeneratorTemplateChanged(string value)
    {
        ApplyGeneratorTemplate(value);
    }

    partial void OnGeneratorIncludeUppercaseChanged(bool value) => HandleGeneratorOptionsChanged();
    partial void OnGeneratorIncludeLowercaseChanged(bool value) => HandleGeneratorOptionsChanged();
    partial void OnGeneratorIncludeNumbersChanged(bool value) => HandleGeneratorOptionsChanged();
    partial void OnGeneratorIncludeSymbolsChanged(bool value) => HandleGeneratorOptionsChanged();
    partial void OnGeneratorExcludeSimilarCharactersChanged(bool value) => HandleGeneratorOptionsChanged();

    private void HandleGeneratorOptionsChanged()
    {
        if (_isApplyingGeneratorTemplate)
        {
            return;
        }

        RaiseGeneratorState();
        if (!CanGeneratePassword)
        {
            GeneratedPassword = "";
            return;
        }

        RefreshGeneratedPasswordFromOptions();
    }

    private void RaiseGeneratorState()
    {
        OnPropertyChanged(nameof(GeneratorLengthText));
        OnPropertyChanged(nameof(GeneratorWordCountText));
        OnPropertyChanged(nameof(GeneratorLengthMinimum));
        OnPropertyChanged(nameof(GeneratorLengthMaximum));
        OnPropertyChanged(nameof(IsGeneratorPassphraseMode));
        OnPropertyChanged(nameof(ShowGeneratorCharacterOptions));
        OnPropertyChanged(nameof(ShowGeneratorUsernameOptions));
        OnPropertyChanged(nameof(ShowGeneratorLengthOptions));
        OnPropertyChanged(nameof(ShowGeneratorWordCountOptions));
        OnPropertyChanged(nameof(SelectedGeneratorModeLabel));
        OnPropertyChanged(nameof(SelectedGeneratorTemplateLabel));
        OnPropertyChanged(nameof(SelectedGeneratorModeOption));
        OnPropertyChanged(nameof(SelectedGeneratorTemplateOption));
        OnPropertyChanged(nameof(GeneratorStrategySummaryText));
        OnPropertyChanged(nameof(GeneratedPasswordStrengthText));
        OnPropertyChanged(nameof(CanGeneratePassword));
        OnPropertyChanged(nameof(CanCopyGeneratedPassword));
        OnPropertyChanged(nameof(HasGeneratorValidationError));
        OnPropertyChanged(nameof(GeneratorValidationMessage));
        GeneratePasswordCommand.NotifyCanExecuteChanged();
        CopyGeneratedPasswordCommand.NotifyCanExecuteChanged();
    }
}
