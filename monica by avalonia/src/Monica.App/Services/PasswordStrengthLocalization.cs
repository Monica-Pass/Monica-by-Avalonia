using Monica.Core.Services;

namespace Monica.App.Services;

internal static class PasswordStrengthLocalization
{
    public static string Label(ILocalizationService localization, string label) => label switch
    {
        "Excellent" => localization.Get("PasswordStrengthExcellent"),
        "Strong" => localization.Get("PasswordStrengthStrong"),
        "Fair" => localization.Get("PasswordStrengthFair"),
        "Weak" => localization.Get("PasswordStrengthWeak"),
        "Very weak" => localization.Get("PasswordStrengthVeryWeak"),
        _ => label
    };

    public static string Summarize(ILocalizationService localization, PasswordStrengthResult strength)
    {
        var label = Label(localization, strength.Label);
        var warnings = Warnings(localization, strength.Warnings);

        return warnings.Length == 0
            ? localization.Format("GeneratedPasswordStrengthOnlyFormat", label, strength.Score)
            : localization.Format("GeneratedPasswordStrengthFormat", label, strength.Score, warnings);
    }

    public static string Warnings(ILocalizationService localization, IEnumerable<string> warnings) =>
        string.Join(" ", warnings.Select(warning => Warning(localization, warning)));

    private static string Warning(ILocalizationService localization, string warning) => warning switch
    {
        "Password is shorter than 12 characters." => localization.Get("PasswordStrengthWarningShort"),
        "Use both upper and lower case letters." => localization.Get("PasswordStrengthWarningMixedCase"),
        "Add numbers." => localization.Get("PasswordStrengthWarningNumbers"),
        "Add symbols." => localization.Get("PasswordStrengthWarningSymbols"),
        _ => warning
    };
}
