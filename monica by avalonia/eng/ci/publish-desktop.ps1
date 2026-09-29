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
