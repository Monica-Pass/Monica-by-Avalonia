using System.Globalization;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void GeneratePassword()
    {
        GeneratedPassword = GeneratorMode switch
        {
            GeneratorModePassphrase => GeneratePassphrase(),
            GeneratorModePin => GenerateFromAlphabet("0123456789", GeneratorLength),
            GeneratorModeUsername => GenerateUsername(),
            _ => GenerateRandomPasswordValue()
        };
        AddGeneratedPasswordHistory(GeneratedPassword);
        StatusMessage = _localization.Get("GeneratedPassword");
    }

    [RelayCommand]
    private void ResetGenerator()
    {
        GeneratorTemplate = GeneratorTemplateBalanced;
        ApplyGeneratorTemplate(GeneratorTemplateBalanced);
    }

    [RelayCommand]
    private void UseGeneratedPasswordHistoryItem(GeneratorHistoryItem? item)
    {
        if (item is null)
        {
            return;
        }

        GeneratedPassword = item.Value;
        StatusMessage = _localization.Get("GeneratedPasswordRestoredFromHistory");
    }

    [RelayCommand]
    private async Task CopyGeneratedPasswordHistoryItemAsync(GeneratorHistoryItem? item)
    {
        if (item is null)
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(item.Value);
        StatusMessage = _localization.Get("CopiedGeneratedPassword");
    }

    [RelayCommand]
    private async Task CopyGeneratedPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(GeneratedPassword))
        {
            GeneratePassword();
        }

        await _clipboardService.SetSensitiveTextAsync(GeneratedPassword);
        StatusMessage = _localization.Get("CopiedGeneratedPassword");
    }

    private string GenerateRandomPasswordValue()
    {
        if (!GeneratorExcludeSimilarCharacters)
        {
            return _passwordGenerator.GeneratePassword(
                GeneratorLength,
                GeneratorIncludeUppercase,
                GeneratorIncludeLowercase,
                GeneratorIncludeNumbers,
                GeneratorIncludeSymbols);
        }

        var groups = BuildGeneratorCharacterGroups(
            GeneratorIncludeUppercase,
            GeneratorIncludeLowercase,
            GeneratorIncludeNumbers,
            GeneratorIncludeSymbols,
            excludeSimilar: true);
        return GenerateFromGroups(groups, GeneratorLength);
    }

    private string GeneratePassphrase()
    {
        var words = Enumerable
            .Range(0, GeneratorWordCount)
            .Select(_ => GeneratorPassphraseWords[RandomNumberGenerator.GetInt32(GeneratorPassphraseWords.Length)])
            .ToList();

        var result = string.Join("-", words);
        if (GeneratorIncludeNumbers)
        {
            result += RandomNumberGenerator.GetInt32(10, 100).ToString(CultureInfo.InvariantCulture);
        }

        if (GeneratorIncludeSymbols)
        {
            result += PickCharacter("!@#$%?");
        }

        return result;
    }

    private string GenerateUsername()
    {
        var words = Enumerable
            .Range(0, 2)
            .Select(_ => GeneratorPassphraseWords[RandomNumberGenerator.GetInt32(GeneratorPassphraseWords.Length)])
            .ToArray();
        var suffix = GeneratorIncludeNumbers
            ? RandomNumberGenerator.GetInt32(100, 1000).ToString(CultureInfo.InvariantCulture)
            : "";
        var value = $"{words[0]}.{words[1]}{suffix}";

        if (value.Length <= GeneratorLength)
        {
            return value;
        }

        return value[..GeneratorLength].TrimEnd('.');
    }

    private static string GenerateFromAlphabet(string alphabet, int length)
    {
        if (string.IsNullOrEmpty(alphabet))
        {
            alphabet = "abcdefghijklmnopqrstuvwxyz";
        }

        var chars = new char[Math.Max(1, length)];
        for (var index = 0; index < chars.Length; index++)
        {
            chars[index] = PickCharacter(alphabet);
        }

        return new string(chars);
    }

    private static string GenerateFromGroups(IReadOnlyList<string> groups, int length)
    {
        if (groups.Count == 0)
        {
            groups = ["abcdefghijklmnopqrstuvwxyz"];
        }

        var required = groups
            .Select(PickCharacter)
            .ToList();
        var alphabet = string.Concat(groups);
        while (required.Count < length)
        {
            required.Add(PickCharacter(alphabet));
        }

        for (var index = required.Count - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (required[index], required[swapIndex]) = (required[swapIndex], required[index]);
        }

        return new string(required.Take(length).ToArray());
    }

    private static char PickCharacter(string alphabet) =>
        alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    private static IReadOnlyList<string> BuildGeneratorCharacterGroups(
        bool includeUppercase,
        bool includeLowercase,
        bool includeNumbers,
        bool includeSymbols,
        bool excludeSimilar)
    {
        var groups = new List<string>(4);
        AddGeneratorGroup(groups, "ABCDEFGHIJKLMNOPQRSTUVWXYZ", includeUppercase, excludeSimilar);
        AddGeneratorGroup(groups, "abcdefghijklmnopqrstuvwxyz", includeLowercase, excludeSimilar);
        AddGeneratorGroup(groups, "0123456789", includeNumbers, excludeSimilar);
        AddGeneratorGroup(groups, "!@#$%^&*()-_=+[]{};:,.?", includeSymbols, excludeSimilar);
        return groups;
    }

    private static void AddGeneratorGroup(List<string> groups, string alphabet, bool include, bool excludeSimilar)
    {
        if (!include)
        {
            return;
        }

        var value = excludeSimilar
            ? new string(alphabet.Where(character => !SimilarGeneratorCharacters.Contains(character)).ToArray())
            : alphabet;
        if (!string.IsNullOrEmpty(value))
        {
            groups.Add(value);
        }
    }

    private void AddGeneratedPasswordHistory(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var existing = GeneratedPasswordHistory.FirstOrDefault(item => item.Value == value);
        if (existing is not null)
        {
            GeneratedPasswordHistory.Remove(existing);
        }

        var strength = _passwordGenerator.Analyze(value);
        GeneratedPasswordHistory.Insert(0, new GeneratorHistoryItem(
            value,
            SelectedGeneratorModeLabel,
            PasswordStrengthLocalization.Label(_localization, strength.Label),
            DateTimeOffset.Now.ToString("HH:mm", CultureInfo.CurrentCulture)));

        while (GeneratedPasswordHistory.Count > MaxGeneratorHistoryItems)
        {
            GeneratedPasswordHistory.RemoveAt(GeneratedPasswordHistory.Count - 1);
        }

        OnPropertyChanged(nameof(HasGeneratedPasswordHistory));
    }

    private void ApplyGeneratorTemplate(string value)
    {
        switch (value)
        {
            case GeneratorTemplateMaximum:
                GeneratorMode = GeneratorModeRandom;
                GeneratorLength = 32;
                GeneratorWordCount = 4;
                GeneratorIncludeUppercase = true;
                GeneratorIncludeLowercase = true;
                GeneratorIncludeNumbers = true;
                GeneratorIncludeSymbols = true;
                GeneratorExcludeSimilarCharacters = false;
                break;
            case GeneratorTemplateMemorable:
                GeneratorMode = GeneratorModePassphrase;
                GeneratorLength = 24;
                GeneratorWordCount = 4;
                GeneratorIncludeUppercase = false;
                GeneratorIncludeLowercase = true;
                GeneratorIncludeNumbers = true;
                GeneratorIncludeSymbols = false;
                GeneratorExcludeSimilarCharacters = true;
                break;
            case GeneratorTemplatePin:
                GeneratorMode = GeneratorModePin;
                GeneratorLength = 6;
                GeneratorWordCount = 4;
                GeneratorIncludeUppercase = false;
                GeneratorIncludeLowercase = false;
                GeneratorIncludeNumbers = true;
                GeneratorIncludeSymbols = false;
                GeneratorExcludeSimilarCharacters = false;
                break;
            case GeneratorTemplateUsername:
                GeneratorMode = GeneratorModeUsername;
                GeneratorLength = 18;
                GeneratorWordCount = 2;
                GeneratorIncludeUppercase = false;
                GeneratorIncludeLowercase = true;
                GeneratorIncludeNumbers = true;
                GeneratorIncludeSymbols = false;
                GeneratorExcludeSimilarCharacters = true;
                break;
            default:
                GeneratorMode = GeneratorModeRandom;
                GeneratorLength = 24;
                GeneratorWordCount = 4;
                GeneratorIncludeUppercase = true;
                GeneratorIncludeLowercase = true;
                GeneratorIncludeNumbers = true;
                GeneratorIncludeSymbols = true;
                GeneratorExcludeSimilarCharacters = false;
                break;
        }
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
