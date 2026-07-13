using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void RefreshChoiceLabels()
    {
        ReplaceOptions(LanguageOptions,
            new("system", _localization.GetLanguageName("system")),
            new("en-US", _localization.GetLanguageName("en-US")),
            new("zh-CN", _localization.GetLanguageName("zh-CN")));

        ReplaceOptions(ThemeOptions,
            new("system", _localization.Get("SystemDefault")),
            new("light", _localization.Get("Light")),
            new("dark", _localization.Get("Dark")),
            new("high-contrast", _localization.Get("HighContrast")));

        ReplaceOptions(StartupSectionOptions,
            new("Passwords", _localization.Passwords),
            new("Notes", _localization.SecureNotes),
            new("Totp", _localization.Totp),
            new("Cards", _localization.Cards),
            new("Generator", _localization.Generator),
            new("Archive", _localization.Archive),
            new("RecycleBin", _localization.RecycleBin),
            new("SecurityAnalysis", _localization.SecurityAnalysis),
            new("Timeline", _localization.Timeline),
            new("Mdbx", _localization.Get("MdbxVaults")),
            new("DatabaseManagement", _localization.DatabaseManagement),
            new("Sync", _localization.SyncAndBackup),
            new("Settings", _localization.Settings));

        ReplaceOptions(AutoLockMinuteOptions,
            new(1, _localization.Format("MinuteFormat", 1)),
            new(5, _localization.Format("MinuteFormat", 5)),
            new(15, _localization.Format("MinuteFormat", 15)),
            new(30, _localization.Format("MinuteFormat", 30)),
            new(60, _localization.Format("MinuteFormat", 60)));

        ReplaceOptions(ClipboardSecondOptions,
            new(10, _localization.Format("SecondFormat", 10)),
            new(30, _localization.Format("SecondFormat", 30)),
            new(60, _localization.Format("SecondFormat", 60)),
            new(120, _localization.Format("SecondFormat", 120)));

        ReplaceOptions(ConflictStrategyOptions,
            new("ask", _localization.Get("AskEveryTime")),
            new("local-wins", _localization.Get("LocalWins")),
            new("remote-wins", _localization.Get("RemoteWins")));

        ReplaceOptions(PasswordSortOptions,
            new("updated-desc", _localization.Get("SortUpdated")),
            new("title-asc", _localization.Get("SortTitle")),
            new("website-asc", _localization.Get("SortWebsite")),
            new("username-asc", _localization.Get("SortUsername")),
            new("created-desc", _localization.Get("SortCreated")),
            new("favorites-first", _localization.Get("SortFavorites")));

        RefreshGeneratorChoiceLabels();

        ReplaceOptions(
            SecurityQuestionOptions,
            _securityQuestionService.PredefinedQuestions
                .Select(question => new SettingsChoice(question.Id, question.Text))
                .ToArray());

        RaiseFilteredPasswordsChanged();
    }

    private static void ReplaceOptions(ObservableCollection<SettingsChoice> target, params SettingsChoice[] choices)
    {
        target.Clear();
        foreach (var choice in choices)
        {
            target.Add(choice);
        }
    }

    private static string FindChoiceLabel(IEnumerable<SettingsChoice> choices, object value)
    {
        var choice = choices.FirstOrDefault(item => Equals(item.Value, value));
        return choice?.Label ?? Convert.ToString(value, CultureInfo.CurrentCulture) ?? "";
    }

    private void RaiseAboutText()
    {
        OnPropertyChanged(nameof(AboutTitle));
        OnPropertyChanged(nameof(AboutDescription));
        OnPropertyChanged(nameof(AppVersionLabel));
        OnPropertyChanged(nameof(GitHubRepositoryLabel));
        OnPropertyChanged(nameof(OpenRepositoryText));
        OnPropertyChanged(nameof(RepositoryUrlText));
        OnPropertyChanged(nameof(AppVersionText));
    }

    private void RaiseDangerZoneText()
    {
        OnPropertyChanged(nameof(DangerZoneTitle));
        OnPropertyChanged(nameof(DangerZoneDescription));
        OnPropertyChanged(nameof(ClearVaultDataTitle));
        OnPropertyChanged(nameof(ClearVaultDataDescription));
        OnPropertyChanged(nameof(ClearPasswordsOnlyText));
        OnPropertyChanged(nameof(ClearSecureItemsOnlyText));
        OnPropertyChanged(nameof(ClearAllVaultDataText));
        OnPropertyChanged(nameof(ClearVaultConfirmationInstructionText));
    }

    private void RaiseMasterPasswordMaintenanceText()
    {
        OnPropertyChanged(nameof(ChangeMasterPasswordTitle));
        OnPropertyChanged(nameof(ChangeMasterPasswordDescription));
        OnPropertyChanged(nameof(CurrentMasterPasswordText));
        OnPropertyChanged(nameof(NewMasterPasswordText));
        OnPropertyChanged(nameof(ConfirmNewMasterPasswordText));
        OnPropertyChanged(nameof(ChangeMasterPasswordActionText));
    }

    private void RaiseSecurityRecoveryText()
    {
        OnPropertyChanged(nameof(SecurityRecoveryTitle));
        OnPropertyChanged(nameof(SecurityRecoveryDescription));
        OnPropertyChanged(nameof(SecurityRecoveryStatusText));
        OnPropertyChanged(nameof(SecurityRecoveryEnabledText));
        OnPropertyChanged(nameof(SecurityQuestion1Text));
        OnPropertyChanged(nameof(SecurityQuestion2Text));
        OnPropertyChanged(nameof(SecurityQuestionAnswerText));
        OnPropertyChanged(nameof(CustomSecurityQuestionText));
        OnPropertyChanged(nameof(SaveSecurityQuestionsText));
        OnPropertyChanged(nameof(ResetMasterPasswordTitle));
        OnPropertyChanged(nameof(ResetMasterPasswordDescription));
        OnPropertyChanged(nameof(ResetMasterPasswordActionText));
        OnPropertyChanged(nameof(SecurityRecoveryQuestion1PromptText));
        OnPropertyChanged(nameof(SecurityRecoveryQuestion2PromptText));
        OnPropertyChanged(nameof(CanResetMasterPasswordWithSecurityQuestions));
        OnPropertyChanged(nameof(CanRunResetMasterPassword));
    }

    private string LocalizeVaultClearScope(VaultClearScope scope) => scope switch
    {
        VaultClearScope.Passwords => _localization.Get("ClearPasswordsOnly"),
        VaultClearScope.SecureItems => _localization.Get("ClearSecureItemsOnly"),
        _ => _localization.Get("ClearAllVaultData")
    };

    private string GetSecurityQuestionText(int questionId, string customText) =>
        questionId == SecurityQuestionService.CustomQuestionId
            ? customText.Trim()
            : _securityQuestionService.GetQuestion(questionId).Text;

    private static string GetAppVersionText()
    {
        var assembly = typeof(MainWindowViewModel).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString()
            : informationalVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            return "V0.0.0";
        }

        var metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            version = version[..metadataIndex];
        }

        return version.StartsWith('V') || version.StartsWith('v')
            ? version
            : $"V{version}";
    }
}
