param(
    [ValidateSet('ci', 'release')]
    [string] $Mode = 'ci',

    [string] $ReleaseVersion = ''
)

$ErrorActionPreference = 'Stop'

function Write-OutputValue([string] $Name, [string] $Value) {
    Write-Host "$Name=$Value"
    if ($env:GITHUB_OUTPUT) {
        Add-Content -Path $env:GITHUB_OUTPUT -Value "$Name=$Value"
    }
}

function Get-ShortSha {
    $sha = ''
    if ($env:GITHUB_SHA) {
        $sha = $env:GITHUB_SHA
    } else {
        $sha = (git rev-parse HEAD).Trim()
    }

    if ($sha.Length -gt 7) {
        return $sha.Substring(0, 7)
    }

    return $sha
}

function Normalize-Version([string] $Value) {
    $normalized = $Value.Trim()
    if ($normalized.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
        $normalized = $normalized.Substring(1)
    }

    if ($normalized -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
        throw "Version '$Value' must look like 1.2.3, 1.2.3-preview.1, or v1.2.3."
    }

    return $normalized
}

function Get-BaseVersion {
    $props = Join-Path $PWD 'Directory.Build.props'
    if (-not (Test-Path -LiteralPath $props)) {
        return '0.1.0'
    }

    [xml] $xml = Get-Content -LiteralPath $props -Raw
    $versionPrefix = $xml.Project.PropertyGroup.VersionPrefix
    if ($versionPrefix) {
        return (Normalize-Version $versionPrefix)
    }

    $version = $xml.Project.PropertyGroup.Version
    if ($version) {
        return (Normalize-Version $version)
    }

    return '0.1.0'
}

$shortSha = Get-ShortSha
$runNumber = if ($env:GITHUB_RUN_NUMBER) { $env:GITHUB_RUN_NUMBER } else { '0' }

if ($Mode -eq 'release') {
    $version = Normalize-Version $ReleaseVersion
} else {
    $baseVersion = Get-BaseVersion
    $version = "$baseVersion-ci.$runNumber"
}

$versionPrefix = ($version -split '[-+]')[0]
$assemblyVersion = "$versionPrefix.0"
$fileVersion = "$versionPrefix.0"
$packageVersion = $version
$informationalVersion = "$version+$shortSha"
$tag = "v$version"

Write-OutputValue 'version' $version
Write-OutputValue 'version_prefix' $versionPrefix
Write-OutputValue 'package_version' $packageVersion
Write-OutputValue 'assembly_version' $assemblyVersion
Write-OutputValue 'file_version' $fileVersion
Write-OutputValue 'informational_version' $informationalVersion
Write-OutputValue 'short_sha' $shortSha
Write-OutputValue 'tag' $tag
