param(
    [Parameter(Mandatory = $true)]
    [string] $Project,

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
    throw @"
Android project '$Project' was not found.
Add an Avalonia Android host project or update ANDROID_PROJECT in the workflow.
The current desktop project targets net10.0 and cannot produce an APK by itself.
"@
}

$runAot = if ($Mode -eq 'aot') { 'true' } else { 'false' }
$outputDir = Join-Path 'artifacts' (Join-Path 'mobile' (Join-Path 'android' $Mode))

if (Test-Path -LiteralPath $outputDir) {
    Remove-Item -LiteralPath $outputDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

dotnet publish $Project `
    --configuration Release `
    --framework net10.0-android `
    /p:ContinuousIntegrationBuild=true `
    /p:AndroidPackageFormat=apk `
    /p:RunAOTCompilation=$runAot `
    /p:Version=$Version `
    /p:VersionPrefix=$VersionPrefix `
    /p:PackageVersion=$PackageVersion `
    /p:AssemblyVersion=$AssemblyVersion `
    /p:FileVersion=$FileVersion `
    /p:InformationalVersion=$InformationalVersion

$apkFiles = Get-ChildItem -Path (Split-Path -Parent $Project) -Recurse -Filter '*.apk'
if (-not $apkFiles) {
    throw "Android publish completed, but no APK file was found under '$Project'."
}

foreach ($apk in $apkFiles) {
    Copy-Item -LiteralPath $apk.FullName -Destination $outputDir -Force
}
