using Monica.App.Services;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// The sequence is what a user now writes by hand, so these pin both halves: what the default sends
/// (it must stay the flow Monica shipped with, which never presses Enter) and what a broken one refuses
/// to send.
/// </summary>
public sealed class AutoTypeSequenceTests
{
    [Fact]
    public void Default_sequence_sends_username_tab_password_and_never_enter()
    {
        Assert.True(TryBuild(
            AutoTypeSequenceParser.DefaultTemplate,
            "octocat",
            "hunter2",
            out var tokens));

        Assert.Collection(
            tokens,
            token =>
            {
                Assert.Equal(AutoTypeTokenKind.Text, token.Kind);
                Assert.Equal("octocat", token.Value);
            },
            token => Assert.Equal(AutoTypeTokenKind.Tab, token.Kind),
            token =>
            {
                Assert.Equal(AutoTypeTokenKind.Text, token.Kind);
                Assert.Equal("hunter2", token.Value);
            });
        Assert.DoesNotContain(tokens, token => token.Kind == AutoTypeTokenKind.Enter);
    }

    [Theory]
    [InlineData("octocat", "", 1)]
    [InlineData("", "hunter2", 1)]
    [InlineData("   ", "hunter2", 1)]
    [InlineData("octocat", "hunter2", 3)]
    [InlineData("", "", 0)]
    public void Default_sequence_emits_only_what_the_entry_carries(string username, string password, int expected)
    {
        Assert.True(TryBuild(AutoTypeSequenceParser.DefaultTemplate, username, password, out var tokens));

        Assert.Equal(expected, tokens.Count);
    }

    [Fact]
    public void A_field_the_entry_does_not_carry_takes_the_tabs_after_it_out_of_the_sequence()
    {
        // The tab is what walks from the username box to the password box. Left in when there is no
        // username, it would move the caret off the field the user had already clicked into.
        Assert.True(TryBuild("{USERNAME}{TAB}{TAB}{PASSWORD}", "", "hunter2", out var tokens));

        var token = Assert.Single(tokens);
        Assert.Equal(AutoTypeTokenKind.Text, token.Kind);
        Assert.Equal("hunter2", token.Value);
    }

    [Fact]
    public void A_missing_password_leaves_the_username_without_a_trailing_tab()
    {
        Assert.True(TryBuild("{USERNAME}{TAB}{PASSWORD}", "octocat", "", out var tokens));

        var token = Assert.Single(tokens);
        Assert.Equal("octocat", token.Value);
    }

    [Fact]
    public void Enter_is_sent_only_because_the_sequence_asks_for_it()
    {
        Assert.True(TryBuild("{USERNAME}{TAB}{PASSWORD}{ENTER}", "octocat", "hunter2", out var tokens));

        Assert.Equal(AutoTypeTokenKind.Enter, tokens[^1].Kind);
        Assert.Equal(3, tokens.Count(token => token.Kind != AutoTypeTokenKind.Enter));
    }

    [Fact]
    public void A_sequence_can_send_only_one_of_the_two_fields()
    {
        Assert.True(TryBuild("{PASSWORD}", "octocat", "hunter2", out var tokens));

        var token = Assert.Single(tokens);
        Assert.Equal("hunter2", token.Value);
    }

    [Fact]
    public void Literal_text_between_tokens_is_typed_as_it_is()
    {
        Assert.True(TryBuild("{USERNAME}@mail{TAB}{PASSWORD}", "octocat", "hunter2", out var tokens));

        Assert.Collection(
            tokens,
            token => Assert.Equal("octocat", token.Value),
            token => Assert.Equal("@mail", token.Value),
            token => Assert.Equal(AutoTypeTokenKind.Tab, token.Kind),
            token => Assert.Equal("hunter2", token.Value));
    }

    [Fact]
    public void A_delay_carries_the_milliseconds_the_sequence_declared()
    {
        Assert.True(TryBuild("{USERNAME}{DELAY:250}{TAB}{PASSWORD}", "octocat", "hunter2", out var tokens));

        var delay = Assert.Single(tokens, token => token.Kind == AutoTypeTokenKind.Delay);
        Assert.Equal(250, delay.DelayMilliseconds);
    }

    [Theory]
    [InlineData("{username}{tab}{password}")]
    [InlineData("{USERNAME}{Tab}{Password}")]
    public void Token_spelling_is_case_insensitive(string template)
    {
        Assert.True(TryBuild(template, "octocat", "hunter2", out var tokens));

        Assert.Equal(3, tokens.Count);
    }

    [Theory]
    [InlineData("", "AutoTypeSequenceErrorEmpty")]
    [InlineData("{}", "AutoTypeSequenceErrorUnknownFormat")]
    [InlineData("{capslock}", "AutoTypeSequenceErrorUnknownFormat")]
    [InlineData("{USERNAME", "AutoTypeSequenceErrorUnterminatedFormat")]
    [InlineData("pass{DELAY:200", "AutoTypeSequenceErrorUnterminatedFormat")]
    [InlineData("{DELAY}", "AutoTypeSequenceErrorUnknownFormat")]
    [InlineData("{DELAY:}", "AutoTypeSequenceErrorBadDelayFormat")]
    [InlineData("{DELAY:soon}", "AutoTypeSequenceErrorBadDelayFormat")]
    [InlineData("{DELAY:5001}", "AutoTypeSequenceErrorBadDelayFormat")]
    [InlineData("{DELAY:-1}", "AutoTypeSequenceErrorBadDelayFormat")]
    public void An_unusable_sequence_is_refused_with_a_reason(string template, string expectedKey)
    {
        Assert.False(AutoTypeSequenceParser.TryParse(template, out var steps, out var error));

        Assert.Empty(steps);
        Assert.Equal(expectedKey, error!.MessageKey);
    }

    [Fact]
    public void The_refusal_quotes_the_token_as_it_was_typed()
    {
        Assert.False(AutoTypeSequenceParser.TryParse("{CapsLock}", out _, out var error));

        Assert.Equal("{CapsLock}", error!.Argument);
    }

    [Fact]
    public void A_brace_with_no_token_after_it_is_just_text()
    {
        Assert.True(TryBuild("{USERNAME}}", "octocat", "hunter2", out var tokens));

        Assert.Equal("}", tokens[1].Value);
    }

    [Fact]
    public void A_refused_sequence_yields_no_partial_keystrokes()
    {
        Assert.False(TryBuild("{USERNAME}{CAPSLOCK}{PASSWORD}", "octocat", "hunter2", out var tokens, out var error));

        Assert.Empty(tokens);
        Assert.Equal("AutoTypeSequenceErrorUnknownFormat", error!.MessageKey);
        Assert.Equal("{CAPSLOCK}", error.Argument);
    }

    [Fact]
    public void A_sequence_longer_than_the_setting_holds_is_refused_before_parsing()
    {
        var template = new string('a', AutoTypeSequenceParser.MaxTemplateLength + 1);

        Assert.False(AutoTypeSequenceParser.TryParse(template, out _, out var error));

        Assert.Equal("AutoTypeSequenceErrorTooLong", error!.MessageKey);
    }

    private static bool TryBuild(
        string template,
        string username,
        string password,
        out IReadOnlyList<AutoTypeToken> tokens,
        out AutoTypeSequenceError? error) =>
        AutoTypeSequenceParser.TryBuild(template, username, password, out tokens, out error);

    private static bool TryBuild(
        string template,
        string username,
        string password,
        out IReadOnlyList<AutoTypeToken> tokens) =>
        TryBuild(template, username, password, out tokens, out _);
}
