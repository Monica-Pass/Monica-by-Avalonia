param(
    [string]$MdbxRepo = (Join-Path $PSScriptRoot "..\..\..\..\mdbx"),
    [string]$Configuration = "debug",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

# The generated bindings must come from the same mdbx-ffi source the Android main
# repo ships (see Monica for Android/mdbx-engine/MDBX3_RUNTIME_PROVENANCE.json).
# Regenerating against a divergent tree silently breaks the shared vault format.
$workspace = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$mdbx = (Resolve-Path $MdbxRepo).Path
$configurationName = if ($Configuration -ieq "release") { "release" } else { "debug" }
$libraryName = if ($IsWindows) { "mdbx_ffi.dll" } elseif ($IsMacOS) { "libmdbx_ffi.dylib" } else { "libmdbx_ffi.so" }
$libraryPath = Join-Path (Join-Path $mdbx "target") (Join-Path $configurationName $libraryName)

if (-not $SkipBuild) {
    Push-Location $mdbx
    try {
        & cargo build -p mdbx-ffi
        if ($LASTEXITCODE -ne 0) { throw "cargo build -p mdbx-ffi failed" }
    } finally {
        Pop-Location
    }
}

if (-not (Test-Path $libraryPath)) {
    throw "Native library not found at '$libraryPath'. Build mdbx-ffi first or pass -Configuration."
}

$bindgen = Get-Command "uniffi-bindgen-cs" -ErrorAction SilentlyContinue
if ($null -eq $bindgen) {
    throw "uniffi-bindgen-cs was not found on PATH. Install with: cargo install uniffi-bindgen-cs --locked"
}

$outDir = Join-Path $workspace "src\Monica.Platform\Mdbx\Generated"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

& $bindgen.Source --library --config (Join-Path $PSScriptRoot "uniffi.toml") --no-format --out-dir $outDir $libraryPath
if ($LASTEXITCODE -ne 0) { throw "uniffi-bindgen-cs failed" }

Copy-Item -Path $libraryPath -Destination (Join-Path $outDir $libraryName) -Force
Write-Host "Generated bindings and copied $libraryName to $outDir"
