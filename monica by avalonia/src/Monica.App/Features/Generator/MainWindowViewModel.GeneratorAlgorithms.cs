using System.Globalization;
using System.Security.Cryptography;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string CreateGeneratedPasswordValue() => GeneratorMode switch
    {
        GeneratorModePassphrase => GeneratePassphrase(),
        GeneratorModePin => GenerateFromAlphabet("0123456789", GeneratorLength),
        GeneratorModeUsername => GenerateUsername(),
        _ => GenerateRandomPasswordValue()
    };

    private string GenerateRandomPasswordValue()
    {
        var groups = BuildGeneratorCharacterGroups(
            GeneratorIncludeUppercase,
            GeneratorIncludeLowercase,
            GeneratorIncludeNumbers,
            GeneratorIncludeSymbols,
            GeneratorExcludeSimilarCharacters);
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

        return GeneratorIncludeSymbols ? result + PickCharacter("!@#$%?") : result;
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
        return value.Length <= GeneratorLength ? value : value[..GeneratorLength].TrimEnd('.');
    }

    private static string GenerateFromAlphabet(string alphabet, int length)
    {
        var chars = new char[Math.Max(1, length)];
        for (var index = 0; index < chars.Length; index++)
        {
            chars[index] = PickCharacter(alphabet);
        }

        return new string(chars);
    }

    private static string GenerateFromGroups(IReadOnlyList<string> groups, int length)
    {
        var required = groups.Select(PickCharacter).ToList();
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
}
