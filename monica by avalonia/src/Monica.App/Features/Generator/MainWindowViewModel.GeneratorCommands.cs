using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanGeneratePassword))]
    private void GeneratePassword()
    {
        RegeneratePassword(addToHistory: true);
        SetStatusNotice("GeneratedPassword");
    }

    [RelayCommand]
    private void ResetGenerator()
    {
        if (GeneratorTemplate == GeneratorTemplateBalanced)
        {
            ApplyGeneratorTemplate(GeneratorTemplateBalanced);
        }
        else
        {
            GeneratorTemplate = GeneratorTemplateBalanced;
        }
    }

    [RelayCommand(CanExecute = nameof(HasGeneratedPasswordHistory))]
    private void ClearGeneratedPasswordHistory()
    {
        ClearGeneratedPasswordHistorySecrets();
        SetStatusNotice("GeneratedPasswordHistoryCleared");
    }

    [RelayCommand]
    private void UseGeneratedPasswordHistoryItem(GeneratorHistoryItem? item)
    {
        if (item is null || string.IsNullOrEmpty(item.Value))
        {
            return;
        }

        GeneratedPassword = item.Value;
        SetStatusNotice("GeneratedPasswordRestoredFromHistory");
    }

    [RelayCommand]
    private async Task CopyGeneratedPasswordHistoryItemAsync(GeneratorHistoryItem? item)
    {
        if (item is null || string.IsNullOrEmpty(item.Value))
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(item.Value);
        SetStatusNotice("CopiedGeneratedPassword");
    }

    [RelayCommand(CanExecute = nameof(CanCopyGeneratedPassword))]
    private async Task CopyGeneratedPasswordAsync()
    {
        await _clipboardService.SetSensitiveTextAsync(GeneratedPassword);
        SetStatusNotice("CopiedGeneratedPassword");
    }

    private void EnsureGeneratedPassword()
    {
        if (string.IsNullOrEmpty(GeneratedPassword) && CanGeneratePassword)
        {
            RegeneratePassword(addToHistory: false);
        }
    }

    private void RefreshGeneratedPasswordFromOptions()
    {
        if (!string.IsNullOrEmpty(GeneratedPassword))
        {
            RegeneratePassword(addToHistory: false);
        }
    }

    private void RegeneratePassword(bool addToHistory)
    {
        GeneratedPassword = CreateGeneratedPasswordValue();
        if (addToHistory)
        {
            AddGeneratedPasswordHistory(GeneratedPassword);
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
            existing.ClearSensitiveState();
        }

        var strength = _passwordGenerator.Analyze(value);
        GeneratedPasswordHistory.Insert(0, new GeneratorHistoryItem(
            value,
            SelectedGeneratorModeLabel,
            PasswordStrengthLocalization.Label(_localization, strength.Label),
            DateTimeOffset.Now.ToString("HH:mm", CultureInfo.CurrentCulture)));

        while (GeneratedPasswordHistory.Count > MaxGeneratorHistoryItems)
        {
            var removed = GeneratedPasswordHistory[^1];
            GeneratedPasswordHistory.RemoveAt(GeneratedPasswordHistory.Count - 1);
            removed.ClearSensitiveState();
        }

        RaiseGeneratedPasswordHistoryState();
    }

    private void RaiseGeneratedPasswordHistoryState()
    {
        OnPropertyChanged(nameof(HasGeneratedPasswordHistory));
        ClearGeneratedPasswordHistoryCommand.NotifyCanExecuteChanged();
    }

    private void ClearGeneratedPasswordHistorySecrets()
    {
        foreach (var item in GeneratedPasswordHistory)
        {
            item.ClearSensitiveState();
        }

        GeneratedPasswordHistory.Clear();
        RaiseGeneratedPasswordHistoryState();
    }

}
