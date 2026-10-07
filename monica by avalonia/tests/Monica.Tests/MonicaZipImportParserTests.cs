using System.IO.Compression;
using System.Text;
using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.Tests;

public sealed class MonicaZipImportParserTests
{
    [Fact]
    public void Imports_android_database_archive_items_categories_and_attachment()
    {
        var bytes = BuildArchive(("passwords/password_7.json", """
            {"id":7,"title":"Android login","username":"alice","password":"secret","loginType":"API_KEY","categoryName":"Work","customFields":[{"title":"Tenant","value":"prod","isProtected":true}]}
            """),
            ("notes/item_9.json", """
            {"id":9,"title":"A note","itemType":"NOTE","itemData":"{\"content\":\"hello\"}","categoryName":"Work"}
            """),
            ("attachments_portable/attachments_portable.json", """
            {"version":2,"entries":[{"parentPasswordId":7,"fileName":"otp.txt","mimeType":"text/plain","sizeBytes":5,"payloadPath":"attachments_portable/attachment_0.bin","createdAt":1700000000000,"updatedAt":1700000000000}]}
            """),
            ("attachments_portable/attachment_0.bin", "hello"),
            ("database_export.json", "{\"version\":1}"));

        var package = MonicaZipImportParser.Import(bytes);

        var password = Assert.Single(package.Passwords);
        Assert.Equal("Android login", password.Title);
        Assert.Equal(PasswordLoginType.ApiKey, password.LoginType);
        var category = Assert.Single(package.Categories);
        Assert.Equal("Work", category.Name);
        Assert.Equal(category.Id, password.CategoryId);
        Assert.Equal(category.Id, Assert.Single(package.SecureItems).CategoryId);
        Assert.Equal("prod", Assert.Single(Assert.Single(package.PasswordCustomFields).Fields).Value);
        var attachment = Assert.Single(Assert.Single(package.PasswordAttachments).Attachments);
        Assert.Equal("otp.txt", attachment.Metadata.FileName);
        Assert.Equal("hello", Encoding.UTF8.GetString(Convert.FromBase64String(attachment.ContentBase64)));
    }

    [Fact]
    public void Rejects_zip_slip_entry()
    {
        var bytes = BuildArchive(("../passwords/password_1.json", "{}"));
        Assert.Throws<MonicaJsonImportException>(() => MonicaZipImportParser.Import(bytes));
    }

    private static byte[] BuildArchive(params (string Name, string Content)[] entries)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return output.ToArray();
    }
}
