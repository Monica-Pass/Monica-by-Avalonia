param(
    [Parameter(Mandatory = $true)]
    [string] $Project,

    [ValidateSet('jit', 'aot')]
    [string] $Mode = 'aot',

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
    throw @"
iOS project '$Project' was not found.
Add an Avalonia iOS host project or update IOS_PROJECT in the workflow.
The current desktop project targets net10.0 and cannot produce an IPA by itself.
"@
}

if ($Mode -eq 'jit') {
    Write-Warning 'iOS device IPAs cannot use JIT. This build keeps the requested jit label but uses the iOS device publish pipeline.'
}

$outputDir = Join-Path 'artifacts' (Join-Path 'mobile' (Join-Path 'ios' $Mode))

if (Test-Path -LiteralPath $outputDir) {
    Remove-Item -LiteralPath $outputDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

dotnet publish $Project `
    --configuration Release `
    --framework net10.0-ios `
    --runtime ios-arm64 `
    /p:ContinuousIntegrationBuild=true `
    /p:ArchiveOnBuild=true `
    /p:BuildIpa=true `
    /p:Version=$Version `
    /p:VersionPrefix=$VersionPrefix `
    /p:PackageVersion=$PackageVersion `
    /p:AssemblyVersion=$AssemblyVersion `
    /p:FileVersion=$FileVersion `
    /p:InformationalVersion=$InformationalVersion

$ipaFiles = Get-ChildItem -Path (Split-Path -Parent $Project) -Recurse -Filter '*.ipa'
if (-not $ipaFiles) {
    throw "iOS publish completed, but no IPA file was found under '$Project'. Check signing and provisioning settings."
}

foreach ($ipa in $ipaFiles) {
    Copy-Item -LiteralPath $ipa.FullName -Destination $outputDir -Force
}
