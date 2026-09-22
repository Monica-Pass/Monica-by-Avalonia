param(
    [Parameter(Mandatory = $true)]
    [string] $InputDirectory,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,

    [Parameter(Mandatory = $true)]
    [string] $PackageName
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $InputDirectory)) {
    throw "Input directory '$InputDirectory' was not found."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$isWindowsRid = $PackageName -match 'win-'
$extension = if ($isWindowsRid) { '.zip' } else { '.tar.gz' }
$packagePath = Join-Path $OutputDirectory "$PackageName$extension"

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

$tar = Get-Command tar -ErrorAction SilentlyContinue
$archiveExitCode = 0
if ($tar) {
    if ($isWindowsRid) {
        tar -a -cf $packagePath -C $InputDirectory .
    } else {
        tar -czf $packagePath -C $InputDirectory .
    }
    $archiveExitCode = $LASTEXITCODE
} elseif ($isWindowsRid) {
    Compress-Archive -Path (Join-Path $InputDirectory '*') -DestinationPath $packagePath -Force
} else {
    throw 'tar was not found and is required to create tar.gz packages.'
}

# A native tar that exits nonzero does not trip $ErrorActionPreference, so without these checks a
# CI upload could carry a missing or truncated package.
if ($tar -and $archiveExitCode -ne 0) {
    throw "tar failed to create $packagePath with exit code $archiveExitCode."
}

if (-not (Test-Path -LiteralPath $packagePath)) {
    throw "Packaging reported success but produced no package at $packagePath."
}
if ((Get-Item -LiteralPath $packagePath).Length -eq 0) {
    throw "Package $packagePath is empty."
}

if ($env:GITHUB_OUTPUT) {
    Add-Content -Path $env:GITHUB_OUTPUT -Value "package_path=$packagePath"
}

Write-Host ("Created {0} ({1:N1} MB)" -f $packagePath, ((Get-Item -LiteralPath $packagePath).Length / 1MB))
