using Monica.Platform.Services;

namespace Monica.App.Services;

internal enum AutoTypeStepKind
{
    Text,
    UserName,
    Password,
    Tab,
    Enter,
    Delay
}

internal readonly record struct AutoTypeStep(
    AutoTypeStepKind Kind,
    string Text = "",
    int DelayMilliseconds = 0);

/// <summary>
/// Why a configured sequence cannot be used, carrying the localization key and the offending text so
/// the settings page can name the token instead of saying "invalid".
/// </summary>
internal sealed record AutoTypeSequenceError(string MessageKey, string Argument)
{
    public static AutoTypeSequenceError Empty { get; } = new("AutoTypeSequenceErrorEmpty", "");
    public static AutoTypeSequenceError TooLong { get; } = new("AutoTypeSequenceErrorTooLong", "");

    public static AutoTypeSequenceError Unterminated(string token) =>
        new("AutoTypeSequenceErrorUnterminatedFormat", token);

    public static AutoTypeSequenceError Unknown(string token) => new("AutoTypeSequenceErrorUnknownFormat", token);

    public static AutoTypeSequenceError BadDelay(string token) => new("AutoTypeSequenceErrorBadDelayFormat", token);
}

/// <summary>
/// Turns the sequence text a user configures into the steps to send. The token spelling follows the one
/// Android shows in its KeePass screen ("For example: {USERNAME}{TAB}{PASSWORD}{ENTER}"), which is also
/// the spelling a .kdbx auto-type field carries — the difference is that Monica actually consumes it,
/// while Android only stores the string back into the file.
/// </summary>
internal static class AutoTypeSequenceParser
{
    // The shape of the hard-coded flow this replaced: username, tab, password, and never Enter, because
    // submitting a live form stays the user's own action until they ask for it in the sequence.
    public const string DefaultTemplate = "{USERNAME}{TAB}{PASSWORD}";

    public const int MaxTemplateLength = 256;

    private const string UserNameToken = "{USERNAME}";
    private const string PasswordToken = "{PASSWORD}";
    private const string TabToken = "{TAB}";
    private const string EnterToken = "{ENTER}";
    private const string DelayToken = "{DELAY:";

    public static bool TryParse(
        string? template,
        out IReadOnlyList<AutoTypeStep> steps,
        out AutoTypeSequenceError? error)
    {
        steps = [];
        error = null;
        var parsed = new List<AutoTypeStep>();
        var text = template ?? "";
        if (text.Length > MaxTemplateLength)
        {
            error = AutoTypeSequenceError.TooLong;
            return false;
        }

        var literalStart = -1;
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] == '{')
            {
                var close = text.IndexOf('}', index);
                if (close < 0)
                {
                    error = AutoTypeSequenceError.Unterminated(text[index..]);
                    return false;
                }

                FlushLiteral(parsed, text, literalStart, index);
                literalStart = -1;
                var token = text[index..(close + 1)];
                if (!TryParseToken(token, out var step, out error))
                {
                    steps = [];
                    return false;
                }

                parsed.Add(step);
                index = close + 1;
                continue;
            }

            if (literalStart < 0)
            {
                literalStart = index;
            }

            index++;
        }

        FlushLiteral(parsed, text, literalStart, text.Length);
        if (parsed.Count == 0)
        {
            error = AutoTypeSequenceError.Empty;
            return false;
        }

        steps = parsed;
        return true;
    }

    /// <summary>
    /// Resolves a sequence against one entry. A field the entry does not carry is skipped, and so is any
    /// tab next to it: that tab exists only to walk between two values, so leaving it in would move the
    /// caret off the field the user had already clicked into.
    /// </summary>
    public static bool TryBuild(
        string? template,
        string? userName,
        string? password,
        out IReadOnlyList<AutoTypeToken> tokens,
        out AutoTypeSequenceError? error)
    {
        tokens = [];
        if (!TryParse(template, out var steps, out error))
        {
            return false;
        }

        var dropped = new bool[steps.Count];
        for (var index = 0; index < steps.Count; index++)
        {
            dropped[index] = steps[index].Kind is AutoTypeStepKind.UserName or AutoTypeStepKind.Password &&
                string.IsNullOrWhiteSpace(ValueOf(steps[index], userName, password));
        }

        var built = new List<AutoTypeToken>(steps.Count);
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            if (step.Kind == AutoTypeStepKind.Tab)
            {
                if (!IsNextToDroppedField(steps, dropped, index))
                {
                    built.Add(AutoTypeToken.Tab);
                }

                continue;
            }

            if (dropped[index])
            {
                continue;
            }

            built.Add(step.Kind switch
            {
                AutoTypeStepKind.UserName => AutoTypeToken.Text(userName ?? ""),
                AutoTypeStepKind.Password => AutoTypeToken.Text(password ?? ""),
                AutoTypeStepKind.Enter => AutoTypeToken.Enter,
                AutoTypeStepKind.Delay => AutoTypeToken.Delay(step.DelayMilliseconds),
                _ => AutoTypeToken.Text(step.Text)
            });
        }

        tokens = built;
        return true;
    }

    private static string? ValueOf(AutoTypeStep step, string? userName, string? password) => step.Kind switch
    {
        AutoTypeStepKind.UserName => userName,
        AutoTypeStepKind.Password => password,
        _ => null
    };

    /// <summary>
    /// Looks past a whole run of tabs in both directions, so consecutive tabs around the same missing
    /// field leave together rather than one of them surviving.
    /// </summary>
    private static bool IsNextToDroppedField(
        IReadOnlyList<AutoTypeStep> steps,
        bool[] dropped,
        int index)
    {
        var left = index - 1;
        while (left >= 0 && steps[left].Kind == AutoTypeStepKind.Tab)
        {
            left--;
        }

        if (left >= 0 && dropped[left])
        {
            return true;
        }

        var right = index + 1;
        while (right < steps.Count && steps[right].Kind == AutoTypeStepKind.Tab)
        {
            right++;
        }

        return right < steps.Count && dropped[right];
    }

    private static bool TryParseToken(
        string token,
        out AutoTypeStep step,
        out AutoTypeSequenceError? error)
    {
        step = default;
        error = null;
        var name = token.ToUpperInvariant();
        switch (name)
        {
            case UserNameToken:
                step = new AutoTypeStep(AutoTypeStepKind.UserName);
                return true;
            case PasswordToken:
                step = new AutoTypeStep(AutoTypeStepKind.Password);
                return true;
            case TabToken:
                step = new AutoTypeStep(AutoTypeStepKind.Tab);
                return true;
            case EnterToken:
                step = new AutoTypeStep(AutoTypeStepKind.Enter);
                return true;
        }

        if (!name.StartsWith(DelayToken, StringComparison.Ordinal) || !name.EndsWith("}", StringComparison.Ordinal))
        {
            error = AutoTypeSequenceError.Unknown(token);
            return false;
        }

        var argument = token[(DelayToken.Length)..^1];
        if (!int.TryParse(argument, out var milliseconds) ||
            milliseconds is < 0 or > AutoTypeLimits.MaxDelayMilliseconds)
        {
            error = AutoTypeSequenceError.BadDelay(token);
            return false;
        }

        step = new AutoTypeStep(AutoTypeStepKind.Delay, DelayMilliseconds: milliseconds);
        return true;
    }

    private static void FlushLiteral(List<AutoTypeStep> steps, string text, int start, int end)
    {
        if (start < 0 || end <= start)
        {
            return;
        }

        steps.Add(new AutoTypeStep(AutoTypeStepKind.Text, text[start..end]));
    }
}
