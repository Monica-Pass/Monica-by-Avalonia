using System.Reflection;
using Avalonia;
using FluentIcons.Common;
using Monica.App.Controls;
using Monica.App.Features.Vault;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Xunit;

namespace Monica.Tests;

public class VaultTreeModelTests
{
    [Theory]
    [InlineData(PasswordLoginType.Password, VaultEntryKind.Password)]
    [InlineData(PasswordLoginType.Sso, VaultEntryKind.Sso)]
    [InlineData(PasswordLoginType.Wifi, VaultEntryKind.Wifi)]
    [InlineData(PasswordLoginType.SshKey, VaultEntryKind.SshKey)]
    [InlineData(PasswordLoginType.Barcode, VaultEntryKind.Barcode)]
    public void Every_password_login_type_renders_as_a_tree_leaf(PasswordLoginType loginType, VaultEntryKind expected)
    {
        Assert.Equal(expected, VaultEntryKinds.FromPassword(new PasswordEntry { LoginType = loginType }));
    }

    [Theory]
    [InlineData(VaultItemType.Note, VaultEntryKind.Note)]
    [InlineData(VaultItemType.Totp, VaultEntryKind.Totp)]
    [InlineData(VaultItemType.BankCard, VaultEntryKind.BankCard)]
    [InlineData(VaultItemType.Document, VaultEntryKind.Document)]
    [InlineData(VaultItemType.BillingAddress, VaultEntryKind.BillingAddress)]
    [InlineData(VaultItemType.PaymentAccount, VaultEntryKind.PaymentAccount)]
    public void Every_secure_item_type_renders_as_a_tree_leaf(VaultItemType itemType, VaultEntryKind expected)
    {
        Assert.Equal(expected, VaultEntryKinds.FromSecureItem(new SecureItem { ItemType = itemType }));
    }

    [Fact]
    public void Password_typed_secure_items_produce_no_leaf()
    {
        // Passwords live in their own table; a secure item carrying that type would double-list.
        Assert.Null(VaultEntryKinds.FromSecureItem(new SecureItem { ItemType = VaultItemType.Password }));
    }

    [Fact]
    public void Both_vault_enums_are_covered_by_the_display_kind()
    {
        var loginTypes = Enum.GetValues<PasswordLoginType>()
            .Select(type => VaultEntryKinds.FromPassword(new PasswordEntry { LoginType = type }));
        var secureTypes = Enum.GetValues<VaultItemType>()
            .Select(type => VaultEntryKinds.FromSecureItem(new SecureItem { ItemType = type }))
            .Where(kind => kind is not null)
            .Select(kind => kind!.Value);

        var uncovered = Enum.GetValues<VaultEntryKind>()
            .Except(loginTypes)
            .Except(secureTypes)
            .ToArray();

        Assert.Empty(uncovered);
    }

    [Fact]
    public void Every_display_kind_has_a_glyph_and_a_label()
    {
        foreach (var kind in Enum.GetValues<VaultEntryKind>())
        {
            Assert.NotEqual(default, VaultEntryKinds.SymbolFor(kind));
            Assert.False(string.IsNullOrWhiteSpace(VaultEntryKinds.LabelFor(kind)));
        }
    }

    [Fact]
    public void Row_keys_survive_a_password_and_a_secure_item_sharing_an_id()
    {
        const long sharedId = 42;

        var password = VaultTreeKey.Password(sharedId);
        var secureItem = VaultTreeKey.SecureItem(sharedId);
        var folder = VaultTreeKey.Folder("42");

        Assert.NotEqual(password, secureItem);
        Assert.NotEqual(password, folder);
        Assert.NotEqual(secureItem, folder);

        var keys = new[] { password, secureItem, folder }.Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(3, keys.Length);
    }

    [Fact]
    public void Folder_rows_declare_themselves_as_folders_to_the_tree()
    {
        IVaultTreeRow row = new PasswordFolderFilterChoice(Id: 1, Name: "Work", Count: 2, SelectionKey: "f:1");

        Assert.False(row.IsEntryRow);
        Assert.Equal(VaultTreeRowKind.Folder, row.RowKind);
        Assert.Equal(Symbol.Folder, row.EntrySymbol);
        Assert.Equal("", row.EntryDetail);
        Assert.Equal(new Thickness(0, 0, 0, 0), row.Indent);
    }

    [Fact]
    public void Tree_rows_indent_by_one_step_per_level()
    {
        Assert.Equal(new Thickness(0, 0, 0, 0), FolderTreeLayout.IndentFor(0));
        Assert.Equal(new Thickness(32, 0, 0, 0), FolderTreeLayout.IndentFor(2));
        Assert.Equal(new Thickness(0, 0, 0, 0), FolderTreeLayout.IndentFor(-1));
    }

    [Fact]
    public void Shared_controls_expose_nothing_from_the_vault_model()
    {
        // The tree control renders presentation primitives only. Letting a storage enum through
        // here would mean a second tree per feature, which is what the 库 page exists to avoid.
        var offenders = typeof(VaultFolderTree).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "Monica.App.Controls")
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(member => (type, member)))
            .Where(pair => SignatureTypes(pair.member)
                .Any(outer => Flattened(outer).Any(IsVaultModelType)))
            .Select(pair => $"{pair.type.Name}.{pair.member.Name}")
            .ToArray();

        Assert.Empty(offenders);
    }

    private static IEnumerable<TypeInfo> SignatureTypes(MemberInfo member) => member switch
    {
        PropertyInfo property => [property.PropertyType.GetTypeInfo()],
        FieldInfo field => [field.FieldType.GetTypeInfo()],
        MethodInfo method => method.GetParameters()
            .Select(parameter => parameter.ParameterType.GetTypeInfo())
            .Append(method.ReturnType.GetTypeInfo())
            .ToArray(),
        _ => []
    };

    /// A control may hand out a collection of model objects as easily as a model object itself.
    private static IEnumerable<Type> Flattened(Type type)
    {
        yield return type;

        if (type.IsArray)
        {
            foreach (var inner in Flattened(type.GetElementType()!))
            {
                yield return inner;
            }
        }

        foreach (var argument in type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes)
        {
            foreach (var inner in Flattened(argument))
            {
                yield return inner;
            }
        }
    }

    private static bool IsVaultModelType(Type type) =>
        type.Namespace == "Monica.Core.Models" ||
        type.Namespace?.StartsWith("Monica.Core.Models.", StringComparison.Ordinal) == true;
}
