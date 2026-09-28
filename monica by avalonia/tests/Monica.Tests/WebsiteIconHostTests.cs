using Monica.App.Controls;
using Monica.App.Features.Vault;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.Tests;

public class WebsiteIconHostTests
{
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("  example.com  ", "example.com")]
    [InlineData("WWW.Example.COM", "example.com")]
    [InlineData("https://www.example.com/login?x=1", "example.com")]
    [InlineData("http://sub.example.co.uk/", "sub.example.co.uk")]
    [InlineData("https://example.com:8443/x", "example.com")]
    // Only the authority is asked about: whatever a parser leaves after it, a path or a fragment, is
    // decoration and can never point the request at a second destination.
    [InlineData("https://cdn.example.com/@evil", "cdn.example.com")]
    [InlineData("https://example.com/../evil", "example.com")]
    [InlineData("xn--80ak6aa92e.com", "xn--80ak6aa92e.com")]
    public void A_website_field_yields_the_host_the_row_asks_for(string website, string expected)
    {
        Assert.True(WebsiteIconCache.TryExtractHost(website, out var host));
        Assert.Equal(expected, host);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("example com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("ftp://example.com")]
    [InlineData("https://user:pass@example.com")]
    [InlineData("https://@example.com")]
    [InlineData("192.168.0.1")]
    [InlineData("http://[::1]/")]
    [InlineData("not a host at all")]
    [InlineData("https://exa mple.com")]
    [InlineData("-leading.com")]
    [InlineData("trailing-.com")]
    [InlineData("too..many.com")]
    [InlineData("example.com.")]
    public void Anything_that_is_not_one_host_is_refused_before_a_request_is_built(string website)
    {
        Assert.False(WebsiteIconCache.TryExtractHost(website, out var host));
        Assert.Equal("", host);
    }

    [Fact]
    public void An_overlong_website_is_refused_without_being_parsed()
    {
        Assert.False(WebsiteIconCache.TryExtractHost(new string('a', 300) + ".com", out _));
    }

    [Fact]
    public void A_library_row_exposes_only_its_passwords_host()
    {
        var withWebsite = new VaultTreeEntryRow
        {
            Kind = VaultEntryKind.Password,
            Label = "Row A",
            Indent = default,
            Password = new PasswordEntry { Title = "Row A", Website = "https://www.example.com/account" }
        };
        var note = new VaultTreeEntryRow
        {
            Kind = VaultEntryKind.Note,
            Label = "Row B",
            Indent = default,
            Item = new SecureItem { Title = "Row B", ItemType = VaultItemType.Note }
        };

        Assert.Equal("example.com", withWebsite.WebsiteIconHost);
        Assert.Null(note.WebsiteIconHost);
    }

    [Fact]
    public void A_website_the_row_cannot_use_leaves_the_host_empty()
    {
        var row = new VaultTreeEntryRow
        {
            Kind = VaultEntryKind.Password,
            Label = "Row C",
            Indent = default,
            Password = new PasswordEntry { Title = "Row C", Website = "internal" }
        };

        Assert.Null(row.WebsiteIconHost);
    }

    [Fact]
    public void A_folder_row_never_asks_for_a_picture()
    {
        IVaultTreeRow row = new VaultTreeFolderRow
        {
            Path = "Work",
            Label = "Work",
            Indent = default,
            HasChildren = true,
            IsExpanded = true
        };

        Assert.Null(row.WebsiteIconHost);
    }
}
