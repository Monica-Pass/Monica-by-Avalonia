param(
    [Parameter(Mandatory = $true)]
    [string] $PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string] $RuntimeIdentifier,

    [ValidateSet('jit', 'aot')]
    [string] $Mode = 'jit',

    [string] $MasterPassword = 'CiRuntime!2026'
)

$ErrorActionPreference = 'Stop'

$exeName = if ($RuntimeIdentifier -like 'win-*') { 'Monica.App.exe' } else { 'Monica.App' }
$exePath = Join-Path (Resolve-Path -LiteralPath $PublishDirectory).Path $exeName

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Published artifact '$exePath' was not found."
}

$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$runRoot = Join-Path $tempRoot ("monica-runtime-smoke-{0}-{1}" -f $RuntimeIdentifier, [guid]::NewGuid().ToString('N'))
$logDirectory = Join-Path $runRoot 'logs'
$vaultDirectory = Join-Path $runRoot 'vault'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $vaultDirectory | Out-Null
$databasePath = Join-Path $vaultDirectory 'vault.db'

function Invoke-ArtifactCommand {
    param(
        [string] $Label,
        [string[]] $Arguments
    )

    Write-Host "::group=$Label"
    Write-Host "exec: $exePath $($Arguments -join ' ')"

    # The apphost is a GUI-subsystem executable: `& exe` would not wait for it and would
    # leave $LASTEXITCODE unset, so a broken artifact could report a green run.
    $stdoutPath = Join-Path $logDirectory "$Label.out.txt"
    $stderrPath = Join-Path $logDirectory "$Label.err.txt"
    # A string array splits any argument containing a space; quote each argument explicitly.
    $commandLine = (@($Arguments | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }) -join ' ')
    $process = Start-Process -FilePath $exePath -ArgumentList $commandLine -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    $stdout = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Raw } else { '' }
    $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { '' }
    foreach ($line in (($stdout + [Environment]::NewLine + $stderr) -split "`r?`n")) {
        if ($line.Trim().Length -gt 0) { Write-Host $line }
    }
    Write-Host "::endgroup::"

    if ($null -eq $process) {
        throw "$Label never ran: no process was started for $RuntimeIdentifier/$Mode."
    }

    if ($process.ExitCode -ne 0) {
        throw "$Label failed for $RuntimeIdentifier/$Mode with exit code $($process.ExitCode). Output: $((($stdout + $stderr) -join ' | ').Trim())"
    }

    return ($stdout + $stderr)
}

try {
    $requiresNativeMdbx = $RuntimeIdentifier -like 'win-*'
    Write-Host "Runtime smoke for $RuntimeIdentifier/$Mode using $exePath"

    $initOutput = Invoke-ArtifactCommand -Label 'init-empty-smoke-vault' -Arguments @('--init-empty-smoke-vault', $MasterPassword, $databasePath)
    if ($initOutput -notmatch 'Empty smoke vault initialized') {
        throw 'init-empty-smoke-vault did not report success.'
    }

    if (-not $requiresNativeMdbx) {
        # Only mdbx_ffi.dll is shipped today; canonical vault seeding cannot run elsewhere.
        Write-Warning "SKIPPED canonical MDBX seeding for ${RuntimeIdentifier}: no native MDBX library is published for this runtime."
        $null = Invoke-ArtifactCommand -Label 'smoke-vault' -Arguments @('--smoke-vault', $databasePath, $MasterPassword, 'definitely-wrong-password')
        Write-Host ("RUNTIME SMOKE partial. rid={0} mode={1} covered=sqlite+dapper+crypto skipped=canonical-mdbx-vault" -f $RuntimeIdentifier, $Mode)
        return
    }

    $seedOutput = Invoke-ArtifactCommand -Label 'seed-smoke-vault' -Arguments @('--seed-smoke-vault', $MasterPassword, $databasePath)
    if ($seedOutput -notmatch 'Smoke vault seeded') {
        throw 'seed-smoke-vault did not report success.'
    }

    $null = Invoke-ArtifactCommand -Label 'smoke-vault' -Arguments @('--smoke-vault', $databasePath, $MasterPassword, 'definitely-wrong-password')

    $mdbxFiles = @(Get-ChildItem -LiteralPath (Join-Path $vaultDirectory 'mdbx') -File -ErrorAction SilentlyContinue)
    if ($mdbxFiles.Count -eq 0) {
        throw 'Canonical MDBX vault files were not created next to the SQLite database.'
    }

    Write-Host ("RUNTIME SMOKE passed. rid={0} mode={1} canonicalVaultFiles={2}" -f $RuntimeIdentifier, $Mode, $mdbxFiles.Count)
}
finally {
    if (Test-Path -LiteralPath $runRoot) {
        Remove-Item -LiteralPath $runRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
