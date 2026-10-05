#Requires -Version 7.6
param(
    [string]$AppDirectory = (Join-Path $PSScriptRoot '../../src/Monica.App/bin/Release/net10.0'),
    [string]$HeadlessAssembly = (Join-Path $PSScriptRoot '../../tests/Monica.UiTests/bin/Release/net10.0/Avalonia.Headless.dll'),
    [string]$ImageDirectory = (Join-Path $PSScriptRoot '../../artifacts/mdbx-local-snapshots')
)
$ErrorActionPreference = 'Stop'
$appPath = [IO.Path]::GetFullPath($AppDirectory)
$fixtureParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixturePrefix = $fixtureParent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$fixturePath = [IO.Path]::GetFullPath((Join-Path $fixtureParent ('monica-local-snapshot-verification-' + [Guid]::NewGuid().ToString('N'))))
if (-not $fixturePath.StartsWith($fixturePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture path escaped the temporary directory.' }
$previousAppData = $env:MONICA_APPDATA_DIR
[void][IO.Directory]::CreateDirectory($fixturePath)
$env:MONICA_APPDATA_DIR = Join-Path $fixturePath 'appdata'
$verificationStage = 'loading-product'
try {
    # Only managed product dependencies plus Avalonia's third-party headless backend are referenced.
    # Neither Monica.Tests.dll nor Monica.UiTests.dll is loaded or generated.
    $managed = @(Get-ChildItem -LiteralPath $appPath -Filter '*.dll' -File | Where-Object {
        if ($_.Name -in @('Monica.Tests.dll', 'Monica.UiTests.dll')) { return $false }
        try { [void][Reflection.AssemblyName]::GetAssemblyName($_.FullName); $true } catch { $false }
    })
    foreach ($name in @('CommunityToolkit.Mvvm.dll', 'Monica.Core.dll', 'Monica.Data.dll', 'Monica.Platform.dll', 'Monica.App.dll')) {
        [void][Reflection.Assembly]::LoadFrom((Join-Path $appPath $name))
    }
    [void][Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($HeadlessAssembly))
    if ([OperatingSystem]::IsWindows()) {
        [void][Runtime.InteropServices.NativeLibrary]::Load((Join-Path $appPath 'runtimes/win-x64/native/e_sqlite3.dll'))
        foreach ($nativeName in @('libSkiaSharp.dll', 'libHarfBuzzSharp.dll')) {
            $nativeArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
            $nativeAsset = Join-Path $appPath ('runtimes/win-' + $nativeArchitecture + '/native/' + $nativeName)
            if (Test-Path -LiteralPath $nativeAsset) { [void][Runtime.InteropServices.NativeLibrary]::Load($nativeAsset) }
        }
    }
    $references = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)
    $references += $managed | Where-Object { -not (Test-Path -LiteralPath (Join-Path $PSHOME ('ref/' + $_.Name))) } | Select-Object -ExpandProperty FullName
    $references += [IO.Path]::GetFullPath($HeadlessAssembly)
    $verificationStage = 'compilation'
    Add-Type -Path (Join-Path $PSScriptRoot 'LocalSnapshotVerification.cs') -ReferencedAssemblies $references -CompilerOptions '/nowarn:1701'
    $verificationStage = 'product-ui-workflow'
    [LocalSnapshotVerification]::ConfigureDependencies($appPath)
    [LocalSnapshotVerification]::Run($fixturePath, [IO.Path]::GetFullPath($ImageDirectory))
} catch {
    $check = if ('LocalSnapshotVerification' -as [type]) { [LocalSnapshotVerification]::CurrentCheck } else { 'initialization' }
    [Console]::Error.WriteLine('Local snapshot verification failed at check: ' + $check)
    [Console]::Error.WriteLine('Verifier stage: ' + $verificationStage)
    if ($verificationStage -eq 'compilation') {
        # Compilation diagnostics describe the static verifier source, never fixture credentials.
        [Console]::Error.WriteLine($_.Exception.Message)
    }
    $cause = $_.Exception
    while ($null -ne $cause) {
        [Console]::Error.WriteLine([object].GetMethod('GetType').Invoke($cause, @()).Name)
        if ($cause -is [TypeInitializationException]) { [Console]::Error.WriteLine('Initializer: ' + $cause.TypeName) }
        if ($cause -is [IO.FileNotFoundException] -and $cause.FileName -match '^[A-Za-z0-9.]+, Version=') {
            [Console]::Error.WriteLine('Missing assembly: ' + $cause.FileName)
        }
        if ($cause -is [Monica.Data.Mdbx.MdbxSnapshotException]) { [Console]::Error.WriteLine('Snapshot reason: ' + $cause.ReasonCode) }
        $cause = [Exception].GetProperty('InnerException').GetValue($cause)
    }
    exit 1
} finally {
    if ('Monica.App.App' -as [type]) {
        $diagnostics = [Monica.App.App].Assembly.GetType('Monica.App.AppDiagnostics')
        [void]$diagnostics.GetMethod('FlushForShutdown', [Reflection.BindingFlags]'NonPublic,Static').Invoke($null, @())
    }
    $env:MONICA_APPDATA_DIR = $previousAppData
    if ('Microsoft.Data.Sqlite.SqliteConnection' -as [type]) { [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
    if ($fixturePath.StartsWith($fixturePrefix, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($fixturePath).StartsWith('monica-local-snapshot-verification-', [StringComparison]::Ordinal)) {
        [IO.Directory]::Delete($fixturePath, $true)
    }
}
