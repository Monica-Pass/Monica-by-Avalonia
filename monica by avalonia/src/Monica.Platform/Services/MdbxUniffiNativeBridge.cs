using System.Collections;
using System.Reflection;
using Monica.Core.Models;
using Monica.Data.Mdbx;

namespace Monica.Platform.Services;

public sealed class MdbxUniffiNativeBridge : IMdbxNativeBridge
{
    private const string GeneratedMethodsTypeName = "Monica.Mdbx.Ffi.MdbxFfi";
    private readonly Type? _methodsType = FindType(GeneratedMethodsTypeName);
    private readonly bool _isNativeLibraryAvailable = CanLoadNativeLibrary();

    public bool IsAvailable => _methodsType is not null && _isNativeLibraryAvailable;

    public Task<IMdbxNativeVault> CreateVaultAsync(string path, string password, string deviceId, MdbxTigaMode mode, CancellationToken cancellationToken = default)
    {
        var methods = RequireMethodsType();
        var ffiMode = ConvertTigaMode(methods.Assembly, mode);
        var vault = Invoke(methods, null, ["CreateVaultWithTigaMode", "create_vault_with_tiga_mode"], path, password, deviceId, ffiMode);
        return Task.FromResult<IMdbxNativeVault>(new MdbxUniffiNativeVault(vault));
    }

    public Task<IMdbxNativeVault> OpenVaultAsync(string path, string password, string deviceId, CancellationToken cancellationToken = default)
    {
        var methods = RequireMethodsType();
        var vault = Invoke(methods, null, ["OpenVault", "open_vault"], path, password, deviceId);
        return Task.FromResult<IMdbxNativeVault>(new MdbxUniffiNativeVault(vault));
    }

    private Type RequireMethodsType() =>
        _methodsType ?? throw new InvalidOperationException("Generated MDBX UniFFI C# bindings were not found.");

    private static object ConvertTigaMode(Assembly assembly, MdbxTigaMode mode)
    {
        var enumType = assembly.GetType("Monica.Mdbx.Ffi.MdbxTigaMode")
            ?? throw new InvalidOperationException("Generated MDBX UniFFI Tiga mode enum was not found.");
        return Enum.Parse(enumType, mode.ToString(), ignoreCase: true);
    }

    private sealed class MdbxUniffiNativeVault(object vault) : IMdbxNativeVault, IDisposable
    {
        public Task<MdbxNativeVaultInfo> GetInfoAsync(CancellationToken cancellationToken = default)
        {
            var info = Invoke(vault.GetType(), vault, ["Info", "info"]);
            return Task.FromResult(new MdbxNativeVaultInfo(
                GetString(info, "VaultId", "vaultId", "vault_id"),
                GetString(info, "DeviceId", "deviceId", "device_id")));
        }

        public Task<MdbxNativeProjectRecord> CreateProjectAsync(string title, CancellationToken cancellationToken = default)
        {
            var project = Invoke(vault.GetType(), vault, ["CreateProject", "create_project"], title);
            return Task.FromResult(ToProject(project));
        }

        public Task<IReadOnlyList<MdbxNativeProjectRecord>> ListProjectsAsync(bool includeDeleted, CancellationToken cancellationToken = default)
        {
            var projects = Invoke(vault.GetType(), vault, ["ListProjects", "list_projects"], includeDeleted);
            return Task.FromResult<IReadOnlyList<MdbxNativeProjectRecord>>(AsEnumerable(projects).Select(ToProject).ToList());
        }

        public Task<MdbxNativeEntryRecord> CreateEntryAsync(string projectId, string entryType, string title, string payloadJson, CancellationToken cancellationToken = default)
        {
            var entry = Invoke(vault.GetType(), vault, ["CreateEntry", "create_entry"], projectId, entryType, title, payloadJson);
            return Task.FromResult(ToEntry(entry));
        }

        public Task<IReadOnlyList<MdbxNativeEntryRecord>> ListEntriesAsync(string projectId, string? entryType = null, CancellationToken cancellationToken = default)
        {
            var entries = Invoke(vault.GetType(), vault, ["ListEntries", "list_entries"], projectId, entryType);
            return Task.FromResult<IReadOnlyList<MdbxNativeEntryRecord>>(AsEnumerable(entries).Select(ToEntry).ToList());
        }

        public Task<IReadOnlyList<MdbxNativeEntryRecord>> ListDeletedEntriesAsync(string projectId, string? entryType = null, CancellationToken cancellationToken = default)
        {
            var entries = Invoke(vault.GetType(), vault, ["ListDeletedEntries", "list_deleted_entries"], projectId, entryType);
            return Task.FromResult<IReadOnlyList<MdbxNativeEntryRecord>>(AsEnumerable(entries).Select(ToEntry).ToList());
        }

        public Task<MdbxNativeEntryRecord> UpdateEntryAsync(string projectId, string entryId, string entryType, string title, string payloadJson, CancellationToken cancellationToken = default)
        {
            var entry = Invoke(vault.GetType(), vault, ["UpdateEntry", "update_entry"], projectId, entryId, entryType, title, payloadJson);
            return Task.FromResult(ToEntry(entry));
        }

        public Task DeleteEntryAsync(string projectId, string entryId, CancellationToken cancellationToken = default)
        {
            Invoke(vault.GetType(), vault, ["DeleteEntry", "delete_entry"], projectId, entryId);
            return Task.CompletedTask;
        }

        public Task<MdbxNativeEntryRecord> RestoreEntryAsync(string projectId, string entryId, CancellationToken cancellationToken = default)
        {
            var entry = Invoke(vault.GetType(), vault, ["RestoreEntry", "restore_entry"], projectId, entryId);
            return Task.FromResult(ToEntry(entry));
        }

        public void Dispose()
        {
            if (vault is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static MdbxNativeProjectRecord ToProject(object project) => new(
        GetString(project, "ProjectId", "projectId", "project_id"),
        GetString(project, "Title", "title"),
        GetBool(project, "Deleted", "deleted"));

    private static MdbxNativeEntryRecord ToEntry(object entry) => new(
        GetString(entry, "EntryId", "entryId", "entry_id"),
        GetString(entry, "ProjectId", "projectId", "project_id"),
        GetString(entry, "EntryType", "entryType", "entry_type"),
        GetString(entry, "Title", "title"),
        GetString(entry, "PayloadJson", "payloadJson", "payload_json"),
        GetBool(entry, "Deleted", "deleted"));

    private static object Invoke(Type type, object? target, IReadOnlyList<string> methodNames, params object?[] args)
    {
        foreach (var name in methodNames)
        {
            var method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .FirstOrDefault(candidate => candidate.Name.Equals(name, StringComparison.Ordinal) && candidate.GetParameters().Length == args.Length);
            if (method is null)
            {
                continue;
            }

            return method.Invoke(target, args)
                ?? throw new InvalidOperationException($"MDBX UniFFI method '{name}' returned null.");
        }

        throw new MissingMethodException(type.FullName, string.Join("/", methodNames));
    }

    private static IEnumerable<object> AsEnumerable(object value)
    {
        if (value is not IEnumerable enumerable)
        {
            throw new InvalidOperationException($"Expected MDBX UniFFI list, got {value.GetType().FullName}.");
        }

        foreach (var item in enumerable)
        {
            if (item is not null)
            {
                yield return item;
            }
        }
    }

    private static string GetString(object source, params string[] names) =>
        Convert.ToString(GetValue(source, names), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private static bool GetBool(object source, params string[] names) =>
        Convert.ToBoolean(GetValue(source, names), System.Globalization.CultureInfo.InvariantCulture);

    private static object? GetValue(object source, IReadOnlyList<string> names)
    {
        var type = source.GetType();
        foreach (var name in names)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property is not null)
            {
                return property.GetValue(source);
            }

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field is not null)
            {
                return field.GetValue(source);
            }
        }

        throw new MissingMemberException(type.FullName, string.Join("/", names));
    }

    private static Type? FindType(string fullName) =>
        AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType(fullName, throwOnError: false))
            .FirstOrDefault(type => type is not null);

    private static bool CanLoadNativeLibrary()
    {
        if (!System.Runtime.InteropServices.NativeLibrary.TryLoad("mdbx_ffi", out var handle))
        {
            return false;
        }

        System.Runtime.InteropServices.NativeLibrary.Free(handle);
        return true;
    }
}
