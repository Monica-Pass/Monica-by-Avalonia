using System.Collections.ObjectModel;
using System.Globalization;

namespace Monica.App.ViewModels;

internal static class SettingsChoices
{
    public static void ReplaceOptions(ObservableCollection<SettingsChoice> target, params SettingsChoice[] choices)
    {
        if (target.Count == choices.Length &&
            target.Zip(choices).All(pair => Equals(pair.First.Value, pair.Second.Value)))
        {
            for (var index = 0; index < choices.Length; index++)
            {
                target[index].Label = choices[index].Label;
            }

            return;
        }

        target.Clear();
        foreach (var choice in choices)
        {
            target.Add(choice);
        }
    }

    public static string FindChoiceLabel(IEnumerable<SettingsChoice> choices, object value)
    {
        var choice = choices.FirstOrDefault(item => Equals(item.Value, value));
        return choice?.Label ?? Convert.ToString(value, CultureInfo.CurrentCulture) ?? "";
    }
}
