param(
    [Parameter(Mandatory = $true)]
    [string] $MdbxRepo,

    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'osx-x64', 'osx-arm64', 'linux-x64', 'linux-arm64')]
    [string] $RuntimeIdentifier,

    [string] $OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$manifestPath = Join-Path $PSScriptRoot 'native-source.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
$buildRoot = Join-Path $projectRoot 'artifacts/mdbx-native'
$sourceRoot = Join-Path $buildRoot "source-$manifestHash"
$preparedMarker = Join-Path $sourceRoot '.monica-prepared'

$target = switch ($RuntimeIdentifier) {
    'win-x64' { 'x86_64-pc-windows-msvc' }
    'osx-x64' { 'x86_64-apple-darwin' }
    'osx-arm64' { 'aarch64-apple-darwin' }
    'linux-x64' { 'x86_64-unknown-linux-gnu' }
    'linux-arm64' { 'aarch64-unknown-linux-gnu' }
}
$libraryName = if ($RuntimeIdentifier -like 'win-*') {
    'mdbx_ffi.dll'
} elseif ($RuntimeIdentifier -like 'osx-*') {
    'libmdbx_ffi.dylib'
} else {
    'libmdbx_ffi.so'
}

# Patches are byte-preserved in Git and checked before use. No checkout, reset or
# patch operation is performed against the caller's source repository.
foreach ($patch in $manifest.patches) {
    $patchPath = Join-Path $PSScriptRoot $patch.path
    if ((Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $patch.sha256) {
        throw "MDBX patch checksum mismatch: $($patch.path)"
    }
}

if (-not (Test-Path -LiteralPath $preparedMarker)) {
    if (Test-Path -LiteralPath $sourceRoot) {
        throw "Incomplete MDBX staging directory '$sourceRoot'; remove it after inspecting the failed build."
    }

    New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
    $archive = Join-Path $buildRoot "source-$manifestHash.zip"
    & git -C $MdbxRepo archive --format=zip "--output=$archive" $manifest.commit
    if ($LASTEXITCODE -ne 0) { throw 'Unable to archive the pinned MDBX source commit.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $sourceRoot

    # A temporary repository makes patch path resolution independent of the outer
    # Avalonia repository. The staged source lives under ignored build artifacts.
    & git -C $sourceRoot init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Unable to prepare the isolated MDBX source.' }
    foreach ($patch in $manifest.patches) {
        & git -C $sourceRoot apply (Join-Path $PSScriptRoot $patch.path)
        if ($LASTEXITCODE -ne 0) { throw "Unable to apply MDBX patch '$($patch.path)'." }
    }

    Set-Content -LiteralPath $preparedMarker -Value $manifestHash -Encoding utf8NoBOM
}

# Match the Android provenance after LF normalization, including the overlapping
# patches. A partially applied or changed source must never become a runtime asset.
foreach ($file in $manifest.finalOverlayFiles.PSObject.Properties) {
    $path = Join-Path $sourceRoot $file.Name
    $normalized = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
    $bytes = [Text.Encoding]::UTF8.GetBytes($normalized)
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    if ($hash -ne $file.Value) {
        throw "MDBX source does not match Android provenance: $($file.Name)"
    }
}

& rustup target add $target
if ($LASTEXITCODE -ne 0) { throw "Unable to install Rust target '$target'." }
$targetRoot = Join-Path $buildRoot 'target'
& cargo build --manifest-path (Join-Path $sourceRoot 'Cargo.toml') --locked --package mdbx-ffi --release --target $target --target-dir $targetRoot
if ($LASTEXITCODE -ne 0) { throw "MDBX native build failed for '$RuntimeIdentifier'." }

$library = Join-Path $targetRoot "$target/release/$libraryName"
if (-not (Test-Path -LiteralPath $library)) { throw "MDBX did not produce '$libraryName'." }
$runtimeDirectory = if ($OutputDirectory) { $OutputDirectory } else {
    Join-Path $projectRoot "src/Monica.Platform/Mdbx/runtimes/$RuntimeIdentifier"
}
New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
Copy-Item -LiteralPath $library -Destination (Join-Path $runtimeDirectory $libraryName) -Force
Write-Host "MDBX prepared for $RuntimeIdentifier from $($manifest.commit) plus verified Android patches."
