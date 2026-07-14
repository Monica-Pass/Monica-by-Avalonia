param(
    [string] $Solution = 'Monica.slnx',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $SkipRestore,

    [string] $Version = '',
    [string] $VersionPrefix = '',
    [string] $PackageVersion = '',
    [string] $AssemblyVersion = '',
    [string] $FileVersion = '',
    [string] $InformationalVersion = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(Mandatory = $true)]
        [string[]] $CommandArguments
    )

    Write-Host "> $FilePath $($CommandArguments -join ' ')"
    & $FilePath @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$FilePath' failed with exit code $LASTEXITCODE."
    }
}

function Assert-NoTrackedGeneratedArtifacts {
    $trackedFiles = @(& git ls-files)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect tracked files.'
    }

    $forbiddenPattern = '(^|/)(\.codex-tasks|artifacts|publish)(/|$)|(^|/)_screenshot\.png$'
    $forbiddenFiles = @($trackedFiles | Where-Object { $_ -match $forbiddenPattern })
    if ($forbiddenFiles.Count -gt 0) {
        throw "Generated or private files are tracked:`n$($forbiddenFiles -join "`n")"
    }
}

function Get-VulnerabilityCount {
    param([object] $Value)

    if ($null -eq $Value) {
        return 0
    }

    if ($Value -is [System.Array]) {
        $count = 0
        foreach ($item in $Value) {
            $count += Get-VulnerabilityCount $item
        }

        return $count
    }

    if ($Value -isnot [System.Management.Automation.PSCustomObject]) {
        return 0
    }

    $count = 0
    foreach ($property in $Value.PSObject.Properties) {
        if ($property.Name -eq 'vulnerabilities') {
            $count += @($property.Value).Count
        } else {
            $count += Get-VulnerabilityCount $property.Value
        }
    }

    return $count
}

function Assert-NoVulnerablePackages {
    $auditOutput = @(& dotnet list $Solution package --vulnerable --include-transitive --format json)
    if ($LASTEXITCODE -ne 0) {
        throw 'NuGet vulnerability audit failed.'
    }

    $audit = ($auditOutput -join "`n") | ConvertFrom-Json
    $vulnerabilityCount = Get-VulnerabilityCount $audit
    if ($vulnerabilityCount -gt 0) {
        throw "NuGet vulnerability audit found $vulnerabilityCount vulnerable package entries."
    }

    Write-Host 'NuGet vulnerability audit passed.'
}

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
Push-Location $projectRoot
try {
    if (-not (Test-Path -LiteralPath $Solution)) {
        throw "Solution '$Solution' was not found in '$projectRoot'."
    }

    Assert-NoTrackedGeneratedArtifacts

    if (-not $SkipRestore) {
        Invoke-CheckedCommand dotnet @('restore', $Solution)
    }

    Invoke-CheckedCommand dotnet @('format', $Solution, '--verify-no-changes', '--no-restore')
    Assert-NoVulnerablePackages

    $versionProperties = [ordered]@{
        Version = $Version
        VersionPrefix = $VersionPrefix
        PackageVersion = $PackageVersion
        AssemblyVersion = $AssemblyVersion
        FileVersion = $FileVersion
        InformationalVersion = $InformationalVersion
    }
    $buildArguments = @(
        'build',
        $Solution,
        '--configuration',
        $Configuration,
        '--no-restore',
        '--warnaserror',
        '/p:ContinuousIntegrationBuild=true'
    )
    foreach ($property in $versionProperties.GetEnumerator()) {
        if (-not [string]::IsNullOrWhiteSpace($property.Value)) {
            $buildArguments += "/p:$($property.Key)=$($property.Value)"
        }
    }

    Invoke-CheckedCommand dotnet $buildArguments
    Invoke-CheckedCommand dotnet @(
        'test',
        'tests/Monica.Tests/Monica.Tests.csproj',
        '--configuration',
        $Configuration,
        '--no-restore',
        '--no-build',
        '--logger',
        'trx',
        '--results-directory',
        'TestResults/Monica.Tests'
    )
    Invoke-CheckedCommand dotnet @(
        'test',
        'tests/Monica.UiTests/Monica.UiTests.csproj',
        '--configuration',
        $Configuration,
        '--no-restore',
        '--no-build',
        '--logger',
        'trx',
        '--results-directory',
        'TestResults/Monica.UiTests'
    )

    Write-Host 'Commercial release verification passed.'
} finally {
    Pop-Location
}
