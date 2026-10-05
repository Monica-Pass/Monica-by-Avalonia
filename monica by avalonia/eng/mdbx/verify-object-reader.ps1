#Requires -Version 7.6
param(
    [string]$AppDirectory = (Join-Path $PSScriptRoot '../../src/Monica.App/bin/Release/net10.0'),
    [ValidateRange(0, 20000)]
    [int]$PerformanceEntryCount = 0
)

$ErrorActionPreference = 'Stop'
$appPath = [IO.Path]::GetFullPath($AppDirectory)
foreach ($name in @('CommunityToolkit.Mvvm.dll', 'Monica.Core.dll', 'Monica.Data.dll', 'Monica.Platform.dll', 'Monica.App.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $appPath $name))
}
if ([OperatingSystem]::IsWindows()) {
    # pwsh does not apply Monica.App.deps.json runtime-native probing paths.
    [void][Runtime.InteropServices.NativeLibrary]::Load((Join-Path $appPath 'runtimes/win-x64/native/e_sqlite3.dll'))
}

# Exercise the shipped product assemblies and native runtime directly. No test assembly is built,
# loaded or restored. Random fixture credentials never enter argv or output; fixture metadata
# stays in this invocation's temporary directory, which is removed at the end.
function Assert-Check([bool]$condition, [string]$name) {
    if (-not $condition) { throw "Object reader check failed: $name" }
    [Console]::WriteLine("PASS $name")
}

function Assert-Rejected([scriptblock]$action, [string]$name, [type]$expectedType, [string]$expectedReason = '') {
    $rejected = $false
    try { & $action } catch {
        $cause = $_.Exception
        while ($null -ne $cause) {
            if ($expectedType.IsInstanceOfType($cause)) {
                $reason = if ($cause.PSObject.Properties.Name -contains 'Outcome') { $cause.Outcome } else { $cause.ReasonCode }
                $rejected = $expectedReason -eq '' -or $reason -eq $expectedReason
                break
            }
            $cause = [Exception].GetProperty('InnerException').GetValue($cause)
        }
    }
    Assert-Check $rejected $name
}

$fixtureParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixturePath = [IO.Path]::GetFullPath((Join-Path $fixtureParent ('monica-object-reader-' + [Guid]::NewGuid().ToString('N'))))
$fixturePrefix = $fixtureParent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $fixturePath.StartsWith($fixturePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Fixture path escaped the temporary directory.'
}
[void][IO.Directory]::CreateDirectory($fixturePath)
$vaultPath = Join-Path $fixturePath 'fixture.mdbx'
$credential = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$native = $null
$store = $null
$adapter = $null
$readOnly = $null
try {
    $native = [Monica.Mdbx.Ffi.MdbxFfi]::CreateVaultWithTigaMode(
        $vaultPath, $credential, 'object-reader-fixture', [Monica.Mdbx.Ffi.MdbxTigaMode]::Multi)
    $project = $native.CreateProject('Object reader fixture')
    $unknownPayload = '{"empty":"","false":false,"null":null,"integer":9007199254740993,"nested":{"array":[null,true,"\u4e2d\u6587"]}}'
    $unknown = $native.CreateObject($project.ProjectId, 'com.example.recovery-kit.v9', 'Unknown fixture', $unknownPayload, 9)
    $future = $native.CreateObject($project.ProjectId, 'login', 'Future login', '{"kind":"password","room_id":201,"password_plain":"future"}', 2)
    $alternate = $native.CreateObject($project.ProjectId, 'com.example.alternate.v1', 'Alternate fixture', '{}', 1)
    $alias = $native.CreateObject($project.ProjectId, 'ssh-key', 'Legacy native alias', '{}', 1)
    $known = $native.CreateObject($project.ProjectId, 'login', 'Known fixture', '{"kind":"password","room_id":101,"username":"fixture","password_plain":"known"}', 1)
    $auditBefore = @($native.ListSecurityAuditEvents(100)).Count
    $native.Dispose()
    $native = $null

    $bridge = [Monica.Platform.Services.MdbxUniffiNativeBridge]::new()
    Assert-Check $bridge.IsAvailable 'native-runtime-available'
    $database = [Monica.Core.Models.LocalMdbxDatabase]::new()
    $database.Id = 1
    $database.FilePath = $vaultPath
    $database.EncryptedPassword = $credential
    $store = [Monica.Data.Mdbx.MdbxVaultStore]::new($bridge, $null, $null)
    $none = [Threading.CancellationToken]::None
    $descriptors = $store.GetUnknownEntriesAsync($database, $false, $none).GetAwaiter().GetResult()
    Assert-Check ($descriptors.Count -eq 4) 'unknown-future-variant-and-alias-discovered'
    Assert-Check (-not [Monica.Data.Mdbx.MdbxObjectReadPolicy]::Supports('LOGIN', 1)) 'type-recognition-is-case-sensitive'
    Assert-Check (-not ($descriptors[0].PSObject.Properties.Name -contains 'PayloadJson')) 'descriptors-have-no-payload'
    Assert-Check (@($descriptors | Where-Object EntryId -eq $future.ObjectId)[0].PayloadSchemaVersion -eq 2) 'future-payload-version-retained'

    $native = [Monica.Mdbx.Ffi.MdbxFfi]::OpenVault($vaultPath, $credential, 'object-reader-fixture')
    Assert-Check (@($native.ListSecurityAuditEvents(100)).Count -eq $auditBefore) 'metadata-browse-adds-no-audit'
    $native.Dispose()
    $native = $null

    $detail = $store.ReadUnknownEntryAsync($database, $unknown.ObjectId, $project.ProjectId, $none).GetAwaiter().GetResult()
    $json = [Text.Json.JsonDocument]::Parse($detail.PayloadJson)
    try {
        Assert-Check ($json.RootElement.GetProperty('integer').GetRawText() -eq '9007199254740993') 'large-integer-preserved'
        Assert-Check ($json.RootElement.GetProperty('empty').GetString() -eq '') 'empty-string-preserved'
        Assert-Check ($json.RootElement.GetProperty('null').ValueKind -eq [Text.Json.JsonValueKind]::Null) 'null-preserved'
        Assert-Check ($json.RootElement.GetProperty('false').GetRawText() -eq 'false') 'boolean-preserved'
        Assert-Check ($json.RootElement.GetProperty('nested').GetProperty('array')[2].GetString() -eq ([char]0x4e2d + [string][char]0x6587)) 'nested-unicode-preserved'
    } finally { $json.Dispose() }

    $passwords = $store.GetPasswordsAsync($database, $false, $false, $none).GetAwaiter().GetResult()
    Assert-Check ($passwords.Count -eq 1 -and $passwords[0].Id -eq 101) 'unsupported-objects-excluded-from-passwords'
    foreach ($record in @($unknown, $future, $alternate, $alias)) {
        $forged = [Monica.Core.Models.PasswordEntry]::new()
        $forged.Id = 999
        $forged.Title = 'Must not save'
        $forged.MdbxFolderId = $record.ObjectId
        Assert-Rejected { [void]$store.SavePasswordAsync($database, $forged, $none).GetAwaiter().GetResult() } ('password-write-refused-' + $record.ObjectTypeId) ([Monica.Data.Mdbx.MdbxVaultReadOnlyException]) 'object-type-or-version'
    }

    $adapter = $bridge.OpenVaultAsync($vaultPath, $credential, 'object-reader-fixture', $none).GetAwaiter().GetResult()
    Assert-Rejected { [void]$adapter.MoveEntryAsync($project.ProjectId, $future.ObjectId, $project.ProjectId, $none).GetAwaiter().GetResult() } 'future-object-move-refused' ([Monica.Data.Mdbx.MdbxVaultReadOnlyException]) 'object-type-or-version'
    Assert-Rejected { $adapter.DeleteEntryAsync($project.ProjectId, $unknown.ObjectId, $none).GetAwaiter().GetResult() } 'unknown-object-delete-refused' ([Monica.Data.Mdbx.MdbxVaultReadOnlyException]) 'object-type-or-version'
    $adapter.DeleteEntryAsync($project.ProjectId, $known.ObjectId, $none).GetAwaiter().GetResult()
    $deleted = $store.GetPasswordsAsync($database, $true, $false, $none).GetAwaiter().GetResult()
    Assert-Check ($deleted.Count -eq 1 -and $deleted[0].IsDeleted) 'known-recycle-bin-still-readable'
    $adapter.RestoreEntryAsync($project.ProjectId, $known.ObjectId, $none).GetAwaiter().GetResult() | Out-Null

    $decision = [Monica.Data.Mdbx.MdbxNativeAccessDecision]::ReadOnly('fixture', 'fixture')
    $readOnly = [Monica.Data.Mdbx.MdbxReadOnlyNativeVault]::new($adapter, $decision)
    Assert-Check ($readOnly.ListObjectSummariesAsync($project.ProjectId, $false, $none).GetAwaiter().GetResult().Count -eq 5) 'read-only-metadata-available'
    Assert-Rejected { [void]$readOnly.RevealObjectAsync($unknown.ObjectId, 4194304, $none).GetAwaiter().GetResult() } 'read-only-disclosure-refused' ([Monica.Data.Mdbx.MdbxObjectDisclosureException]) 'read-only-session'
    $readOnly.Dispose()
    $readOnly = $null
    $adapter = $null

    $metadataFactory = [Monica.Data.SqliteConnectionFactory]::new((Join-Path $fixturePath 'metadata.db'))
    $migrator = [Monica.Data.DatabaseMigrator]::new($metadataFactory)
    $inner = [Monica.Data.Repositories.MonicaRepository]::new($metadataFactory, $migrator, $null, $null)
    $database.IsDefault = $true
    $database.Id = 0
    $database.StorageLocation = [Monica.Core.Models.MdbxStorageLocation]::Internal
    $database.SourceType = 'LOCAL'
    [void]$inner.SaveMdbxDatabaseAsync($database, $none).GetAwaiter().GetResult()
    $repository = [Monica.Data.Repositories.MdbxBackedMonicaRepository]::new($inner, $store, $null)
    Assert-Rejected { [void]$repository.ClearVaultDataAsync([Monica.Core.Models.VaultClearScope]::All, $none).GetAwaiter().GetResult() } 'incomplete-clear-all-refused' ([Monica.Data.Mdbx.MdbxVaultReadOnlyException]) 'unsupported-vault-objects'
    $exporter = [Monica.App.Services.VaultOperations.MonicaJsonExportUseCase]::new($repository, [Monica.Core.ImportExport.ImportExportService]::new())
    Assert-Rejected { [void]$exporter.ExecuteAsync([Monica.Core.Models.PasswordEntry[]]@(), [Monica.Core.Models.SecureItem[]]@(), [Monica.Core.Models.Category[]]@(), $null, $null, $none).GetAwaiter().GetResult() } 'incomplete-json-backup-refused' ([Monica.Data.Mdbx.MdbxVaultReadOnlyException]) 'unsupported-vault-objects'

    $native = [Monica.Mdbx.Ffi.MdbxFfi]::OpenVault($vaultPath, $credential, 'object-reader-fixture')
    $after = $native.GetObject($project.ProjectId, $unknown.ObjectId)
    Assert-Check ($after.ObjectTypeId -eq $unknown.ObjectTypeId -and $after.PayloadSchemaVersion -eq 9 -and $after.PayloadJson -eq $detail.PayloadJson) 'unknown-object-remains-unchanged'
    $oversized = $native.CreateObject($project.ProjectId, 'com.example.large.v1', 'Large fixture', ('{"value":"' + ('x' * 4194304) + '"}'), 1)
    $native.Dispose()
    $native = $null
    Assert-Rejected { [void]$store.ReadUnknownEntryAsync($database, $oversized.ObjectId, $project.ProjectId, $none).GetAwaiter().GetResult() } 'four-mib-limit-enforced' ([Monica.Data.Mdbx.MdbxObjectDisclosureException]) 'payload-too-large'

    $cts = [Threading.CancellationTokenSource]::new()
    try {
        $cts.Cancel()
        Assert-Rejected { [void]$store.GetUnknownEntriesAsync($database, $false, $cts.Token).GetAwaiter().GetResult() } 'cancellation-enforced' ([OperationCanceledException])
    } finally { $cts.Dispose() }

    $native = [Monica.Mdbx.Ffi.MdbxFfi]::OpenVault($vaultPath, $credential, 'object-reader-fixture')
    $pageProject = $native.CreateProject('Paged fixture')
    for ($index = 0; $index -lt 205; $index++) {
        [void]$native.CreateObject($pageProject.ProjectId, 'com.example.paged.v1', ('Paged ' + $index), '{}', 1)
    }
    $native.Dispose()
    $native = $null
    $adapter = $bridge.OpenVaultAsync($vaultPath, $credential, 'object-reader-fixture', $none).GetAwaiter().GetResult()
    Assert-Check ($adapter.ListObjectSummariesAsync($pageProject.ProjectId, $false, $none).GetAwaiter().GetResult().Count -eq 205) 'summary-pagination-crosses-two-pages'
    $adapter.Dispose()
    $adapter = $null
    if ($PerformanceEntryCount -gt 0) {
        $native = [Monica.Mdbx.Ffi.MdbxFfi]::OpenVault($vaultPath, $credential, 'object-reader-fixture')
        $performanceProject = $native.CreateProject('Performance fixture')
        [Monica.Mdbx.Ffi.MdbxWriteCommand[]]$commands = for ($index = 0; $index -lt $PerformanceEntryCount; $index++) {
            $payload = '{"kind":"password","room_id":' + (10000 + $index) + ',"password_plain":"fixture"}'
            [Monica.Mdbx.Ffi.MdbxWriteCommand+CreateEntry]::new([Guid]::NewGuid().ToString(), $performanceProject.ProjectId, 'login', ('Performance ' + $index), $payload)
        }
        $batchSize = [int][Monica.Mdbx.Ffi.MdbxFfi]::DefaultWriteOperationLimits().MaxCommands
        for ($offset = 0; $offset -lt $commands.Count; $offset += $batchSize) {
            $last = [Math]::Min($offset + $batchSize, $commands.Count) - 1
            [void]$native.ExecuteWriteOperation([Guid]::NewGuid().ToString(), 'object-reader-performance-fixture', $commands[$offset..$last])
        }
        $native.Dispose()
        $native = $null
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $loaded = $store.GetPasswordsAsync($database, $false, $false, $none).GetAwaiter().GetResult()
        $timer.Stop()
        Assert-Check ($loaded.Count -eq $PerformanceEntryCount + 1) 'native-business-load-count'
        [Console]::WriteLine('Native business load milliseconds: ' + [Math]::Round($timer.Elapsed.TotalMilliseconds, 1))
    }
    [Console]::WriteLine('Object reader verification passed.')
} catch {
    # Never include exception messages: native exceptions can contain raw object values.
    [Console]::Error.WriteLine('Object reader verification failed at line ' + $_.InvocationInfo.ScriptLineNumber)
    $cause = $_.Exception
    while ($null -ne $cause) {
        $causeType = [object].GetMethod('GetType').Invoke($cause, @())
        [Console]::Error.WriteLine($causeType.Name)
        $cause = [Exception].GetProperty('InnerException').GetValue($cause)
    }
    exit 1
} finally {
    if ($null -ne $readOnly) { $readOnly.Dispose(); $adapter = $null }
    if ($null -ne $adapter) { $adapter.Dispose() }
    if ($null -ne $store) { $store.Dispose() }
    if ($null -ne $native) { $native.Dispose() }
    if ('Microsoft.Data.Sqlite.SqliteConnection' -as [type]) { [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
    $credential = $null
    # Recheck the absolute target before removing this single invocation's temporary fixture.
    if ($fixturePath.StartsWith($fixturePrefix, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($fixturePath).StartsWith('monica-object-reader-', [StringComparison]::Ordinal)) {
        [IO.Directory]::Delete($fixturePath, $true)
    }
}
