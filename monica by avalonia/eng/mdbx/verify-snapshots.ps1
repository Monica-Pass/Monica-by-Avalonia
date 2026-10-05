#Requires -Version 7.6
param([string]$AppDirectory = (Join-Path $PSScriptRoot '../../src/Monica.App/bin/Release/net10.0'))

$ErrorActionPreference = 'Stop'
$appPath = [IO.Path]::GetFullPath($AppDirectory)
$products = @('Monica.Core.dll', 'Monica.Data.dll', 'Monica.Platform.dll', 'Microsoft.Data.Sqlite.dll')
foreach ($name in $products) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $appPath $name))
}
if ([OperatingSystem]::IsWindows()) {
    [void][Runtime.InteropServices.NativeLibrary]::Load((Join-Path $appPath 'runtimes/win-x64/native/e_sqlite3.dll'))
}
# Compile the verifier in memory. No test DLL is generated, loaded, restored or renamed.
$references = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)
$references += $products | ForEach-Object { Join-Path $appPath $_ }
# Microsoft.Data.Sqlite targets .NET 8 while the product and pwsh host run on .NET 10.
Add-Type -Path (Join-Path $PSScriptRoot 'SnapshotVerification.cs') -ReferencedAssemblies $references -CompilerOptions '/nowarn:1701'

$fixtureParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixturePrefix = $fixtureParent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$fixturePath = [IO.Path]::GetFullPath((Join-Path $fixtureParent ('monica-snapshot-verification-' + [Guid]::NewGuid().ToString('N'))))
if (-not $fixturePath.StartsWith($fixturePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Fixture path escaped the temporary directory.'
}
[void][IO.Directory]::CreateDirectory($fixturePath)
try {
    [void][SnapshotVerification]::RunAsync($fixturePath).GetAwaiter().GetResult()
} catch {
    # Native errors can include confidential payloads. Print only fixed check names and type names.
    [Console]::Error.WriteLine('Snapshot verification failed at check: ' + [SnapshotVerification]::CurrentCheck)
    $cause = $_.Exception
    while ($null -ne $cause) {
        $causeType = [object].GetMethod('GetType').Invoke($cause, @())
        [Console]::Error.WriteLine($causeType.Name)
        if ($cause -is [Monica.Data.Mdbx.MdbxSnapshotException]) {
            [Console]::Error.WriteLine('Snapshot reason: ' + $cause.ReasonCode)
        }
        $cause = [Exception].GetProperty('InnerException').GetValue($cause)
    }
    exit 1
} finally {
    [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools()
    # Delete only this invocation's fixture, after verifying its resolved absolute path again.
    if ($fixturePath.StartsWith($fixturePrefix, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($fixturePath).StartsWith('monica-snapshot-verification-', [StringComparison]::Ordinal)) {
        [IO.Directory]::Delete($fixturePath, $true)
    }
}
