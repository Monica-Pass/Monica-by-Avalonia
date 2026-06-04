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
if ($tar) {
    if ($isWindowsRid) {
        tar -a -cf $packagePath -C $InputDirectory .
    } else {
        tar -czf $packagePath -C $InputDirectory .
    }
} elseif ($isWindowsRid) {
    Compress-Archive -Path (Join-Path $InputDirectory '*') -DestinationPath $packagePath -Force
} else {
    throw 'tar was not found and is required to create tar.gz packages.'
}

if ($env:GITHUB_OUTPUT) {
    Add-Content -Path $env:GITHUB_OUTPUT -Value "package_path=$packagePath"
}

Write-Host "Created $packagePath"
