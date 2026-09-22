using Monica.App.Services;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class AutoTypeMatcherTests
{
    [Theory]
    [InlineData("https://github.com", "Sign in to GitHub · GitHub", true)]
    [InlineData("https://github.com", "Gmail - mail.google.com", false)]
    [InlineData("https://github.com", "github.com - Confirmation required", true)]
    [InlineData("https://github.com", "accounts.github.com - SSO", true)]
    [InlineData("https://github.com", "github.com.phishing.test - Sign in", false)]
    [InlineData("https://example.com", "example.com.attacker.test", false)]
    [InlineData("https://example.com", "accounts.example.com", true)]
    [InlineData("github.com", "GitHub Desktop", true)]
    [InlineData("", "GitHub", false)]
    [InlineData("https://github.com", "", false)]
    public void Match_resolves_a_title_to_at_most_the_entry_it_covers(string website, string windowText, bool expected)
    {
        var entry = Entry("GitHub", website, "octocat", "hunter2");

        var matches = AutoTypeMatcher.Match([entry], windowText);

        Assert.Equal(expected, matches.Count == 1);
    }

    [Fact]
    public void Match_ignores_deleted_and_archived_entries()
    {
        var live = Entry("GitHub", "https://github.com", "octocat", "hunter2");
        var deleted = Entry("GitHub Trash", "https://github.com", "ghost", "gone");
        deleted.IsDeleted = true;
        var archived = Entry("GitHub Old", "https://github.com", "old", "stale");
        archived.IsArchived = true;

        var matches = AutoTypeMatcher.Match([live, deleted, archived], "github.com - Sign in");

        Assert.Same(live, Assert.Single(matches));
    }

    [Fact]
    public void Match_skips_entries_with_nothing_to_type()
    {
        var empty = Entry("GitHub", "https://github.com", "", "");
        var populated = Entry("GitHub Work", "https://github.com", "work", "work-secret");

        var matches = AutoTypeMatcher.Match([empty, populated], "GitHub Desktop");

        Assert.Same(populated, Assert.Single(matches));
    }

    [Theory]
    [InlineData("Sign in to GitHub · GitHub", "")]
    [InlineData("Monica 2.0 release notes", "")]
    [InlineData("Vault - v1.2beta - edit", "")]
    [InlineData("Gmail - mail.google.com", "mail.google.com")]
    [InlineData("example.com.attacker.test", "example.com.attacker.test")]
    public void Collect_host_tokens_keeps_only_host_shaped_runs(string windowText, string expected)
    {
        var hosts = AutoTypeMatcher.CollectHostTokens(windowText);

        Assert.Equal(expected.Length == 0 ? 0 : 1, hosts.Count);
        if (expected.Length > 0)
        {
            Assert.Equal(expected, hosts[0]);
        }
    }

    [Fact]
    public void BuildTokens_sends_username_tab_password_and_never_enter()
    {
        var tokens = AutoTypeMatcher.BuildTokens(Entry("GitHub", "https://github.com", "octocat", "hunter2"));

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
    [InlineData("octocat", "hunter2", 3)]
    [InlineData("", "", 0)]
    public void BuildTokens_emits_only_what_the_entry_carries(string username, string password, int expected)
    {
        Assert.Equal(expected, AutoTypeMatcher.BuildTokens(Entry("GitHub", "", username, password)).Count);
    }

    private static PasswordEntry Entry(string title, string website, string username, string password) =>
        new()
        {
            Title = title,
            Website = website,
            Username = username,
            Password = password
        };
}
