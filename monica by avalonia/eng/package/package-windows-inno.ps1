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
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=$iconPath

[Files]
Source: "$resolvedPublish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Monica"; Filename: "{app}\{#AppExeName}"
Name: "{commondesktop}\Monica"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Monica"; Flags: nowait postinstall skipifsilent
"@

Set-Content -LiteralPath $scriptPath -Value $script -Encoding UTF8
& $isccPath $scriptPath
