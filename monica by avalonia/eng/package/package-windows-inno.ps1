param(
    [Parameter(Mandatory = $true)]
    [string] $PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,

    [Parameter(Mandatory = $true)]
    [string] $Version,

    [ValidateSet('jit', 'aot')]
    [string] $Mode = 'jit'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PublishDirectory)) {
    throw "Publish directory '$PublishDirectory' was not found."
}

$isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
$isccPath = if ($isccCommand) { $isccCommand.Source } else { '' }
if (-not $isccPath) {
    $candidate = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    if (Test-Path -LiteralPath $candidate) {
        $isccPath = (Get-Item -LiteralPath $candidate).FullName
    }
}

if (-not $isccPath) {
    throw 'Inno Setup compiler ISCC.exe was not found.'
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$resolvedPublish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$resolvedOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
$installerBaseName = "Monica-$Version-windows-x64-$Mode-setup"
$scriptPath = Join-Path $OutputDirectory "$installerBaseName.iss"
$iconPath = Join-Path $resolvedPublish 'Assets\AppIcon.ico'

if (-not (Test-Path -LiteralPath $iconPath)) {
    $iconPath = Join-Path $PWD 'src\Monica.App\Assets\AppIcon.ico'
}

$script = @"
#define AppName "Monica"
#define AppVersion "$Version"
#define AppPublisher "Monica"
#define AppExeName "Monica.App.exe"
#define AppMode "$Mode"

[Setup]
AppId={{6A925696-FA45-4E34-9E39-7F740BB43D42}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Monica
DefaultGroupName=Monica
DisableProgramGroupPage=yes
OutputDir=$resolvedOutput
OutputBaseFilename=$installerBaseName
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=$iconPath

[Files]
Source: "$resolvedPublish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Monica"; Filename: "{app}\{#AppExeName}"
Name: "{commondesktop}\Monica"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
; Keep the desktop shortcut enabled by default. Users can still clear this optional task in
; the installer, but a normal install must provide the desktop entry reported by the issue.
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Registry]
; The application's own name for a KeePass database, and the one command Windows runs to open one. The
; path goes in quotes because a double-clicked file may have spaces in it, and the whole thing is quoted
; the way Inno expects so {app} still expands at install time.
Root: HKA; Subkey: "Software\Classes\Monica.KeePassDatabase"; ValueType: string; ValueName: ""; ValueData: "KeePass Database"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Monica.KeePassDatabase\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Assets\AppIcon.ico"
Root: HKA; Subkey: "Software\Classes\Monica.KeePassDatabase\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""
; Offered, never forced: writing a default for .kdbx or its UserChoice is not this installer's to do
; (Windows hashes UserChoice and rejects foreign writes), and a vault program that takes over an
; extension nobody asked it to take is one people stop trusting. Adding a ProgID here is what puts
; "Monica" in the right-click Open with list.
Root: HKA; Subkey: "Software\Classes\.kdbx\OpenWithProgids"; ValueType: string; ValueName: "Monica.KeePassDatabase"; ValueData: ""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Monica"; Flags: nowait postinstall skipifsilent
"@

Set-Content -LiteralPath $scriptPath -Value $script -Encoding UTF8
& $isccPath $scriptPath
# $ErrorActionPreference does not act on a native exit code, so without this check a failed
# compile would leave the CI "Build installer" step green with no installer produced.
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $resolvedOutput "$installerBaseName.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Inno Setup reported success but produced no installer at $installerPath."
}

Write-Host ("Installer built: {0} ({1:N1} MB)" -f `
    $installerPath, ((Get-Item -LiteralPath $installerPath).Length / 1MB))
