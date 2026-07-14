namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void ApplyGeneratorTemplate(string value)
    {
        _isApplyingGeneratorTemplate = true;
        try
        {
            ApplyGeneratorTemplateValues(value);
        }
        finally
        {
            _isApplyingGeneratorTemplate = false;
        }

        RaiseGeneratorState();
        RefreshGeneratedPasswordFromOptions();
    }

    private void ApplyGeneratorTemplateValues(string value)
    {
        switch (value)
        {
            case GeneratorTemplateMaximum:
                SetGeneratorOptions(GeneratorModeRandom, 32, 4, true, true, true, true, false);
                break;
            case GeneratorTemplateMemorable:
                SetGeneratorOptions(GeneratorModePassphrase, 24, 4, false, true, true, false, true);
                break;
            case GeneratorTemplatePin:
                SetGeneratorOptions(GeneratorModePin, 6, 4, false, false, true, false, false);
                break;
            case GeneratorTemplateUsername:
                SetGeneratorOptions(GeneratorModeUsername, 18, 2, false, true, true, false, true);
                break;
            default:
                SetGeneratorOptions(GeneratorModeRandom, 24, 4, true, true, true, true, false);
                break;
        }
    }

    private void SetGeneratorOptions(
        string mode,
        int length,
        int wordCount,
        bool uppercase,
        bool lowercase,
        bool numbers,
        bool symbols,
        bool excludeSimilar)
    {
        GeneratorMode = mode;
        GeneratorLength = length;
        GeneratorWordCount = wordCount;
        GeneratorIncludeUppercase = uppercase;
        GeneratorIncludeLowercase = lowercase;
        GeneratorIncludeNumbers = numbers;
        GeneratorIncludeSymbols = symbols;
        GeneratorExcludeSimilarCharacters = excludeSimilar;
    }

    private void RefreshGeneratorChoiceLabels()
    {
        ReplaceOptions(GeneratorModeOptions,
            new(GeneratorModeRandom, _localization.Get("GeneratorModeRandom")),
            new(GeneratorModePassphrase, _localization.Get("GeneratorModePassphrase")),
            new(GeneratorModePin, _localization.Get("GeneratorModePin")),
            new(GeneratorModeUsername, _localization.Get("GeneratorModeUsername")));

        ReplaceOptions(GeneratorTemplateOptions,
            new(GeneratorTemplateBalanced, _localization.Get("GeneratorTemplateBalanced")),
            new(GeneratorTemplateMaximum, _localization.Get("GeneratorTemplateMaximum")),
            new(GeneratorTemplateMemorable, _localization.Get("GeneratorTemplateMemorable")),
            new(GeneratorTemplatePin, _localization.Get("GeneratorTemplatePin")),
            new(GeneratorTemplateUsername, _localization.Get("GeneratorTemplateUsername")));

        RaiseGeneratorState();
    }

    private void RefreshGeneratorLocalizedState() => RaiseGeneratorState();
}
