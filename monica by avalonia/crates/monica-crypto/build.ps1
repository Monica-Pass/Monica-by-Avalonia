param(
    [ValidateSet('debug', 'release')]
    [string] $Configuration = 'release',

    [string] $RuntimeIdentifier = ''
)

$ErrorActionPreference = 'Stop'

$crateRoot = $PSScriptRoot
$cargoToml = Join-Path $crateRoot 'Cargo.toml'

if (-not (Test-Path -LiteralPath $cargoToml)) {
    throw "Cargo.toml not found at $cargoToml"
}

if (-not $RuntimeIdentifier) {
    $RuntimeIdentifier = if ($env:NETCoreSdkPortableRuntimeIdentifier) {
        $env:NETCoreSdkPortableRuntimeIdentifier
    } else {
        'win-x64'
    }
}

Write-Host "Building monica-crypto ($Configuration, $RuntimeIdentifier)..."
$cargoArgs = @('build', '--manifest-path', $cargoToml)
if ($Configuration -eq 'release') {
    $cargoArgs += '--release'
}

& cargo @cargoArgs
if ($LASTEXITCODE -ne 0) {
    throw "cargo build failed with exit code $LASTEXITCODE"
}

$targetDir = if ($Configuration -eq 'release') { 'release' } else { 'debug' }
$builtArtifact = Join-Path $crateRoot "target\$targetDir"

$solutionRoot = Split-Path -Parent (Split-Path -Parent $crateRoot)
$destinationDir = Join-Path $solutionRoot "src\Monica.Core\Crypto\runtimes\$RuntimeIdentifier"
New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null

$sourceName = if ($RuntimeIdentifier -like 'win-*') {
    'monica_crypto.dll'
} elseif ($RuntimeIdentifier -like 'osx-*') {
    'libmonica_crypto.dylib'
} else {
    'libmonica_crypto.so'
}

$sourcePath = Join-Path $builtArtifact $sourceName
if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Built artifact not found at $sourcePath"
}

$destinationPath = Join-Path $destinationDir $sourceName
Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
Write-Host "monica-crypto artifact copied to $destinationPath"
