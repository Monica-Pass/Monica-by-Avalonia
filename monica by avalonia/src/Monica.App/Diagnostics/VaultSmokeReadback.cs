using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data.Repositories;

namespace Monica.App;

/// <summary>
/// Reading back the seeded vault and writing a throwaway entry proves the data layer end to end: a build
/// that silently drops Dapper statements has to fail here rather than ship a green artifact. The probe runs
/// against both repositories because the canonical vault answers most metadata reads from MDBX, so a broken
/// SQLite statement only surfaces when the plain repository is exercised directly.
/// </summary>
internal static class VaultSmokeReadback
{
    public static async Task<bool> VerifyAsync(
        IMonicaRepository canonicalRepository,
        IMonicaRepository sqliteRepository,
        CryptoService crypto)
    {
        var failures = new List<string>();
        var passwords = await canonicalRepository.GetPasswordsAsync(includeDeleted: true, includeArchived: true);
        var seededPasswords = passwords.Count(item => item.Title.StartsWith("Smoke ", StringComparison.Ordinal));
        var notes = await canonicalRepository.GetSecureItemsAsync(VaultItemType.Note, includeDeleted: true);
        var seededNotes = notes.Count(item => item.Title.StartsWith("Smoke ", StringComparison.Ordinal));
        var categories = await canonicalRepository.GetCategoriesAsync();
        var attachmentOwners = await canonicalRepository.GetAttachmentOwnerIdsAsync("PASSWORD");

        if (seededPasswords < 26)
        {
            failures.Add($"seeded passwords read back as {seededPasswords}, expected at least 26");
        }

        if (seededNotes < 13)
        {
            failures.Add($"seeded notes read back as {seededNotes}, expected at least 13");
        }

        if (categories.Count < 6)
        {
            failures.Add($"seeded categories read back as {categories.Count}, expected at least 6");
        }

        if (attachmentOwners.Count < 5)
        {
            failures.Add($"seeded attachment owners read back as {attachmentOwners.Count}, expected at least 5");
        }

        if ((await canonicalRepository.GetOperationLogsAsync(500)).Count < 24)
        {
            failures.Add("seeded operation logs did not read back");
        }

        await RunWriteProbeAsync(canonicalRepository, crypto, "canonical", failures);
        await RunWriteProbeAsync(sqliteRepository, crypto, "sqlite", failures);

        if (failures.Count == 0)
        {
            Console.WriteLine(
                $"Vault smoke readback passed. passwords={seededPasswords} notes={seededNotes} " +
                $"categories={categories.Count} attachmentOwners={attachmentOwners.Count}");
            return true;
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine($"Vault smoke readback failed: {failure}");
        }

        return false;
    }

    private static async Task RunWriteProbeAsync(
        IMonicaRepository repository,
        CryptoService crypto,
        string label,
        List<string> failures)
    {
        try
        {
            await VerifyWriteProbeAsync(repository, crypto, label, failures);
        }
        catch (Exception ex)
        {
            failures.Add($"[{label}] the probe threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task VerifyWriteProbeAsync(
        IMonicaRepository repository,
        CryptoService crypto,
        string label,
        List<string> failures)
    {
        void AddFailure(string message) => failures.Add($"[{label}] {message}");

        var probe = new PasswordEntry
        {
            Title = "Smoke Roundtrip Probe",
            Username = "probe@smoke.local",
            Password = crypto.EncryptString("roundtrip-probe-secret")
        };
        var probeId = await repository.SavePasswordAsync(probe);
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = probeId,
            FileName = "smoke-roundtrip.txt",
            ContentType = "text/plain",
            StoragePath = $"secure_attachments/smoke-roundtrip-{probeId}.txt",
            SizeBytes = 11
        };
        var probeAttachmentId = await repository.SaveAttachmentAsync(attachment, "monica-smoke"u8.ToArray());
        await repository.ReplaceCustomFieldsAsync(probeId,
        [
            new CustomField { EntryId = probeId, Title = "smoke-roundtrip probe", Value = "probe-value", SortOrder = 0 }
        ]);
        await repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = probeId,
            Password = crypto.EncryptString("roundtrip-probe-previous")
        });
        var baselineLogCount = (await repository.GetOperationLogsAsync(500)).Count;
        await repository.LogAsync(new OperationLog
        {
            ItemType = "PASSWORD",
            ItemId = probeId,
            ItemTitle = probe.Title,
            OperationType = "CREATE",
            DeviceName = Environment.MachineName
        });
        var logsAfterProbe = (await repository.GetOperationLogsAsync(500)).Count;

        var reloaded = (await repository.GetPasswordsAsync(includeDeleted: true))
            .FirstOrDefault(item => item.Id == probeId);
        if (reloaded is null || reloaded.Username != probe.Username)
        {
            AddFailure("the probe password did not read back through the repository");
        }
        else if (Unprotect(crypto, reloaded.Password) != "roundtrip-probe-secret")
        {
            AddFailure("the probe password decrypted to the wrong value");
        }

        var probeFields = await repository.GetCustomFieldsAsync(probeId);
        if (probeFields.Count != 1 || probeFields[0].Value != "probe-value")
        {
            AddFailure($"the probe custom field read back as {probeFields.Count} entries");
        }

        var batchedFields = await repository.GetCustomFieldsByEntryIdsAsync([probeId]);
        if (!batchedFields.TryGetValue(probeId, out var batchedFieldList) || batchedFieldList.Count != 1)
        {
            AddFailure("the probe custom field did not read back through the batched lookup");
        }

        var probeAttachments = await repository.GetAttachmentsAsync("PASSWORD", probeId);
        if (probeAttachments.Count != 1)
        {
            AddFailure($"the probe attachment read back as {probeAttachments.Count} entries");
        }
        else if (repository.PersistsAttachmentContent)
        {
            var content = await repository.TryReadAttachmentContentAsync(probeAttachments[0]);
            if (content is null || !content.AsSpan().SequenceEqual("monica-smoke"u8))
            {
                AddFailure("the probe attachment content did not read back");
            }
        }

        var batchedAttachments = await repository.GetAttachmentsByOwnerIdsAsync("PASSWORD", [probeId]);
        if (!batchedAttachments.TryGetValue(probeId, out var batchedAttachmentList) || batchedAttachmentList.Count != 1)
        {
            AddFailure("the probe attachment did not read back through the batched lookup");
        }

        var probeHistory = await repository.GetPasswordHistoryAsync(probeId);
        if (probeHistory.Count != 1)
        {
            AddFailure($"the probe password history read back as {probeHistory.Count} entries");
        }

        if (logsAfterProbe <= baselineLogCount)
        {
            AddFailure("the probe operation log did not read back");
        }

        var ownersWithProbe = await repository.GetAttachmentOwnerIdsAsync("PASSWORD");
        if (!ownersWithProbe.Contains(probeId))
        {
            AddFailure("the probe attachment owner id did not read back");
        }

        var metadata = await repository.SearchPasswordMetadataAsync("smoke-roundtrip");
        if (!metadata.CustomFieldMatchIds.Contains(probeId))
        {
            AddFailure("the probe custom field did not match a metadata search");
        }

        if (!metadata.AttachmentMatchIds.Contains(probeId))
        {
            AddFailure("the probe attachment did not match a metadata search");
        }

        await repository.DeleteAttachmentAsync(probeAttachmentId, attachment);
        await repository.ClearPasswordHistoryAsync(probeId);
        await repository.DeletePasswordPermanentlyAsync(probeId);
        if ((await repository.GetPasswordsAsync()).Any(item => item.Id == probeId))
        {
            AddFailure("the probe password survived permanent deletion");
        }
    }

    // The canonical store decrypts on read, so a repository value is either the payload or its plaintext.
    private static string Unprotect(CryptoService crypto, string value)
    {
        try
        {
            return crypto.DecryptString(value);
        }
        catch (FormatException)
        {
            return value;
        }
    }
}
