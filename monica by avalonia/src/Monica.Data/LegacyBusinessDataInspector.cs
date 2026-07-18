namespace Monica.Data;

public sealed record LegacyBusinessDataInspection(
    int PasswordCount,
    int SecureItemCount,
    int CategoryCount,
    int CustomFieldCount,
    int PasswordHistoryCount,
    int AttachmentCount)
{
    public static LegacyBusinessDataInspection Empty { get; } = new(0, 0, 0, 0, 0, 0);

    public bool HasData => TotalCount > 0;
    public int TotalCount => PasswordCount + SecureItemCount + CategoryCount + CustomFieldCount + PasswordHistoryCount + AttachmentCount;
    public string NoticeSignature => HasData
        ? FormattableString.Invariant(
            $"legacy-sqlite-v1:{PasswordCount}:{SecureItemCount}:{CategoryCount}:{CustomFieldCount}:{PasswordHistoryCount}:{AttachmentCount}")
        : "";
}

public interface ILegacyBusinessDataInspector
{
    Task<LegacyBusinessDataInspection> InspectAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Counts legacy SQLite business rows without decrypting or importing them.
/// The result is informational and never mutates MDBX.
/// </summary>
public sealed class LegacyBusinessDataInspector(
    ISqliteConnectionFactory connectionFactory,
    IDatabaseMigrator migrator) : ILegacyBusinessDataInspector
{
    public async Task<LegacyBusinessDataInspection> InspectAsync(CancellationToken cancellationToken = default)
    {
        await migrator.MigrateAsync(cancellationToken);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM password_entries),
                (SELECT COUNT(*) FROM secure_items),
                (SELECT COUNT(*) FROM categories),
                (SELECT COUNT(*) FROM custom_fields),
                (SELECT COUNT(*) FROM password_history_entries),
                (SELECT COUNT(*) FROM attachments);
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return LegacyBusinessDataInspection.Empty;
        }

        return new LegacyBusinessDataInspection(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5));
    }
}
