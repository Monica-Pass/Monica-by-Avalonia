param(
    [Parameter(Mandatory = $true)]
    [string] $Project,

    [Parameter(Mandatory = $true)]
    [string] $RuntimeIdentifier,

    [ValidateSet('jit', 'aot')]
    [string] $Mode = 'jit',

    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $VersionPrefix,

    [Parameter(Mandatory = $true)]
    [string] $PackageVersion,

    [Parameter(Mandatory = $true)]
    [string] $AssemblyVersion,

    [Parameter(Mandatory = $true)]
    [string] $FileVersion,

    [Parameter(Mandatory = $true)]
    [string] $InformationalVersion
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Project)) {
    throw "Desktop project '$Project' was not found."
}

$publishAot = if ($Mode -eq 'aot') { 'true' } else { 'false' }
$publishReadyToRun = if ($Mode -eq 'jit') { 'true' } else { 'false' }
$publishDir = Join-Path 'artifacts' (Join-Path 'publish' (Join-Path $RuntimeIdentifier $Mode))

# MDBX is maintained in the sibling Monica-Pass/Mdbx repository. CI checks it out into
# `mdbx` beside this repository; local developers commonly keep it at the same sibling
# location. Build the native library for the selected RID before dotnet publish so macOS
# and Linux artifacts carry the same vault engine as Windows instead of silently shipping
# a vault client that cannot open a database.
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$mdbxCandidates = @(
    (Join-Path (Split-Path -Parent $projectRoot) 'mdbx'),
    (Join-Path $projectRoot '..\mdbx'),
    (Join-Path $projectRoot '..\..\mdbx')
) | Select-Object -Unique
$mdbxRepo = $mdbxCandidates |
    Where-Object { Test-Path -LiteralPath (Join-Path $_ 'Cargo.toml') } |
    Select-Object -First 1

if ($mdbxRepo) {
    $mdbxLibraryName = if ($RuntimeIdentifier -like 'win-*') {
        'mdbx_ffi.dll'
    } elseif ($RuntimeIdentifier -like 'osx-*') {
        'libmdbx_ffi.dylib'
    } else {
        'libmdbx_ffi.so'
    }

    Push-Location $mdbxRepo
    try {
        cargo build --package mdbx-ffi --release
        if ($LASTEXITCODE -ne 0) {
            throw "cargo build -p mdbx-ffi failed for $RuntimeIdentifier."
        }
    } finally {
        Pop-Location
    }

    $mdbxLibrary = Join-Path $mdbxRepo (Join-Path 'target' (Join-Path 'release' $mdbxLibraryName))
    if (-not (Test-Path -LiteralPath $mdbxLibrary)) {
        throw "MDBX native library '$mdbxLibraryName' was not produced for $RuntimeIdentifier."
    }

    $runtimeDirectory = Join-Path $projectRoot (Join-Path 'src\Monica.Platform\Mdbx\runtimes' $RuntimeIdentifier)
    New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
    $runtimeLibrary = Join-Path $runtimeDirectory $mdbxLibraryName
    if (-not (Test-Path -LiteralPath $runtimeLibrary)) {
        Copy-Item -LiteralPath $mdbxLibrary -Destination $runtimeLibrary -Force
        Write-Host "MDBX native library prepared: $RuntimeIdentifier/$mdbxLibraryName"
    } else {
        Write-Host "MDBX native library already present: $RuntimeIdentifier/$mdbxLibraryName"
    }
} else {
    Write-Host 'MDBX source repository was not found; using any checked-in native runtime asset.'
}

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

$rustBuild = Join-Path $PSScriptRoot '..\..\crates\monica-crypto\build.ps1'
if (Test-Path -LiteralPath $rustBuild) {
    & $rustBuild -Configuration release -RuntimeIdentifier $RuntimeIdentifier
}

dotnet publish $Project `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishDir `
    /p:ContinuousIntegrationBuild=true `
    /p:MonicaNativeRid=$RuntimeIdentifier `
    /p:PublishAot=$publishAot `
    /p:PublishReadyToRun=$publishReadyToRun `
    /p:PublishSingleFile=false `
    /p:Version=$Version `
    /p:VersionPrefix=$VersionPrefix `
    /p:PackageVersion=$PackageVersion `
    /p:AssemblyVersion=$AssemblyVersion `
    /p:FileVersion=$FileVersion `
    /p:InformationalVersion=$InformationalVersion

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for $RuntimeIdentifier/$Mode with exit code $LASTEXITCODE."
}

$resolved = (Resolve-Path -LiteralPath $publishDir).Path
Write-Host "Published $RuntimeIdentifier $Mode to $resolved"

if ($env:GITHUB_OUTPUT) {
    Add-Content -Path $env:GITHUB_OUTPUT -Value "publish_dir=$publishDir"
}
