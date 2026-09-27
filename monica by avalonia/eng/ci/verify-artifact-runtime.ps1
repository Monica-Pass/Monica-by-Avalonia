param(
    [Parameter(Mandatory = $true)]
    [string] $PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string] $RuntimeIdentifier,

    [ValidateSet('jit', 'aot')]
    [string] $Mode = 'jit',

    [string] $MasterPassword = 'CiRuntime!2026',

    # Background budget agreed for the locked/minimized process, in private bytes.
    [int] $MaxLockedMemoryMb = 120,

    # Size of the .kdbx the artifact writes for itself, and the private bytes it may still be
    # holding after the session is disposed. Measured growth is ~5 MB at 20,000 entries; a
    # retained decrypted graph would show tens of MB, so this catches the regression it is for.
    [int] $KeePassProbeEntries = 20000,
    [int] $KeePassProbeGroups = 20,
    [int] $MaxKeePassGrowthMb = 24,

    [int] $UiTimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'

# The password the artifact writes its own databases with. It is a fixture, but it is the only thing
# that unlocks every file this run creates, so it is treated as a secret everywhere it can be seen.
$keepassFixturePassword = 'keepass-smoke-fixture-not-a-secret'

# This gate's console is the CI job log: it gets uploaded, kept and pasted around by people who never
# touched the vault. Every step starts by echoing the command it is about to run, and six of those
# commands carry a password as plain argv - measured on the previous version of this file, one run
# printed the master password and the KeePass fixture password 6 times in total. Only arguments equal
# to a registered secret disappear; paths, counts and flags stay readable.
$secretArguments = @($MasterPassword, $keepassFixturePassword)

function Protect-CommandEcho {
    param([string[]] $Arguments)

    return (@($Arguments | ForEach-Object {
        if ($secretArguments -contains $_) { '[redacted]' } else { $_ }
    }) -join ' ')
}

$exeName = if ($RuntimeIdentifier -like 'win-*') { 'Monica.App.exe' } else { 'Monica.App' }
$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$exePath = Join-Path $publishRoot $exeName

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Published artifact '$exePath' was not found."
}

# The engine is what turns "no entries" into a real answer, so the file the platform can dlopen has
# to be inside the artifact before anything is smoked: a linux-x64 build once shipped a Windows PE
# here, opened no vault, and still reported a passing runtime smoke.
$nativeLibraryName = if ($RuntimeIdentifier -like 'win-*') {
    'mdbx_ffi.dll'
} elseif ($RuntimeIdentifier -like 'osx-*') {
    'libmdbx_ffi.dylib'
} else {
    'libmdbx_ffi.so'
}

if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $nativeLibraryName))) {
    throw "Published artifact for $RuntimeIdentifier carries no $nativeLibraryName, so it cannot open a vault."
}

$nativeCryptoName = if ($RuntimeIdentifier -like 'win-*') {
    'monica_crypto.dll'
} elseif ($RuntimeIdentifier -like 'osx-*') {
    'libmonica_crypto.dylib'
} else {
    'libmonica_crypto.so'
}

if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $nativeCryptoName))) {
    throw "Published artifact for $RuntimeIdentifier carries no $nativeCryptoName, so it cannot derive keys."
}

$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$runRoot = Join-Path $tempRoot ("monica-runtime-smoke-{0}-{1}" -f $RuntimeIdentifier, [guid]::NewGuid().ToString('N'))
$logDirectory = Join-Path $runRoot 'logs'
$vaultDirectory = Join-Path $runRoot 'vault'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $vaultDirectory | Out-Null
$databasePath = Join-Path $vaultDirectory 'vault.db'

function Write-AppLogEvidence {
    param(
        [string] $Label,
        [string] $AppLogPath
    )

    if (-not $AppLogPath -or -not (Test-Path -LiteralPath $AppLogPath)) {
        return
    }

    Write-Host "--- $Label app log ---"
    Get-Content -LiteralPath $AppLogPath |
        Select-String -SimpleMatch 'check failed', 'budget result', 'release gate completed', 'lock cycle result' |
        ForEach-Object { Write-Host ($_.Line -replace '^\[[^\]]+\]\s*', '') }
}

function Invoke-ArtifactCommand {
    param(
        [string] $Label,
        [string[]] $Arguments,
        [int] $TimeoutSeconds = 0,
        # A windowed run reports through runtime.log rather than stdout, so point at it to
        # keep the evidence when the process exits nonzero.
        [string] $AppLogPath = ''
    )

    Write-Host "::group=$Label"
    Write-Host "exec: $exePath $(Protect-CommandEcho -Arguments $Arguments)"

    # The apphost is a GUI-subsystem executable: `& exe` would not wait for it and would
    # leave $LASTEXITCODE unset, so a broken artifact could report a green run.
    $stdoutPath = Join-Path $logDirectory "$Label.out.txt"
    $stderrPath = Join-Path $logDirectory "$Label.err.txt"
    # A string array splits any argument containing a space; quote each argument explicitly.
    $commandLine = (@($Arguments | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }) -join ' ')
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $exePath
    $startInfo.Arguments = $commandLine
    $startInfo.WorkingDirectory = Split-Path -Parent $exePath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    # Start-Process -PassThru can hand back an object whose ExitCode is null, which then reads
    # as a failed step; Process::Start keeps the real exit code.
    [System.Diagnostics.Process]$process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "$Label never ran: no process was started for $RuntimeIdentifier/$Mode."
    }

    # Draining the pipes concurrently is what keeps a chatty child from blocking on the pipe buffer.
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $finished = if ($TimeoutSeconds -gt 0) {
        $process.WaitForExit($TimeoutSeconds * 1000)
    } else {
        $process.WaitForExit()
        $true
    }
    if (-not $finished) {
        $process.Kill($true)
        Write-AppLogEvidence -Label $Label -AppLogPath $AppLogPath
        throw "$Label did not finish within $TimeoutSeconds seconds for $RuntimeIdentifier/$Mode."
    }

    $stdout = $stdoutTask.Result
    $stderr = $stderrTask.Result
    Set-Content -LiteralPath $stdoutPath -Value $stdout -Encoding UTF8
    Set-Content -LiteralPath $stderrPath -Value $stderr -Encoding UTF8
    foreach ($line in (($stdout + [Environment]::NewLine + $stderr) -split "`r?`n")) {
        if ($line.Trim().Length -gt 0) { Write-Host $line }
    }
    Write-Host "::endgroup::"

    $exitCode = $process.ExitCode
    if ($exitCode -ne 0) {
        Write-AppLogEvidence -Label $Label -AppLogPath $AppLogPath
        throw "$Label failed for $RuntimeIdentifier/$Mode with exit code $exitCode. Output: $(($stdout + $stderr).Trim())"
    }

    return ($stdout + $stderr)
}

try {
    # Evidence is worthless if the failing run's directory disappears with it.
    $keepRunRoot = $true
    Write-Host "Runtime smoke for $RuntimeIdentifier/$Mode using $exePath"

    $initOutput = Invoke-ArtifactCommand -Label 'init-empty-smoke-vault' -Arguments @('--init-empty-smoke-vault', $MasterPassword, $databasePath)
    if ($initOutput -notmatch 'Empty smoke vault initialized') {
        throw 'init-empty-smoke-vault did not report success.'
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

    Write-Host ("CANONICAL VAULT passed. rid={0} mode={1} native={2} canonicalVaultFiles={3}" -f `
        $RuntimeIdentifier, $Mode, $nativeLibraryName, $mdbxFiles.Count)

    # The windowed smoke is the only gate that drives the shipped shell end to end and samples
    # memory once locked. Its runner needs an interactive session, which Linux and macOS do not have.
    if ($RuntimeIdentifier -notlike 'win-*') {
        Write-Host ("UI SMOKE not covered for {0}: that runner has no desktop session." -f $RuntimeIdentifier)
        $keepRunRoot = $false
        return
    }

    $uiAppData = Join-Path $runRoot 'ui-appdata'
    New-Item -ItemType Directory -Force -Path $uiAppData | Out-Null
    $env:MONICA_APPDATA_DIR = $uiAppData
    $uiLog = Join-Path $uiAppData 'runtime.log'
    try {
        $null = Invoke-ArtifactCommand -Label 'ui-init-empty-smoke-vault' -Arguments @('--init-empty-smoke-vault', $MasterPassword)
        $uiSeed = Invoke-ArtifactCommand -Label 'ui-seed-smoke-vault' -Arguments @('--seed-smoke-vault', $MasterPassword)
        if ($uiSeed -notmatch 'Smoke vault seeded') {
            throw 'ui-seed-smoke-vault did not report success.'
        }

        # The memory answer has to come from inside the shipped process, so the artifact writes the
        # database it will open rather than carrying a fixture nobody can re-derive or resize.
        $keepassDirectory = Join-Path $runRoot 'keepass'
        New-Item -ItemType Directory -Force -Path $keepassDirectory | Out-Null
        $keepassPath = Join-Path $keepassDirectory 'probe.kdbx'
        $keepassSeed = Invoke-ArtifactCommand -Label 'ui-seed-smoke-keepass-vault' -Arguments @(
            '--seed-smoke-keepass-vault', $keepassPath, $keepassFixturePassword,
            "$KeePassProbeEntries", "$KeePassProbeGroups")
        if ($keepassSeed -notmatch 'Smoke KeePass vault seeded') {
            throw 'ui-seed-smoke-keepass-vault did not report success.'
        }

        # The probe database is deliberately huge because it measures retention; a frame does not need
        # 20,000 rows to be painted, and rebuilding that tree for every screenshot would only make the
        # shot slow enough that someone turns it off. This one is sized like a screen.
        $keepassShotPath = Join-Path $keepassDirectory 'shots.kdbx'
        $keepassShotSeed = Invoke-ArtifactCommand -Label 'ui-seed-smoke-keepass-vault-shot' -Arguments @(
            '--seed-smoke-keepass-vault', $keepassShotPath, $keepassFixturePassword,
            '12', '3')
        if ($keepassShotSeed -notmatch 'Smoke KeePass vault seeded') {
            throw 'ui-seed-smoke-keepass-vault-shot did not report success.'
        }

        # The remembered-list frame moves its own file out of the way to paint the row's gone state, so it
        # gets a database of its own rather than borrowing one a later frame still has to read.
        $keepassRecentPath = Join-Path $keepassDirectory 'remembered.kdbx'
        $keepassRecentSeed = Invoke-ArtifactCommand -Label 'ui-seed-smoke-keepass-vault-recent' -Arguments @(
            '--seed-smoke-keepass-vault', $keepassRecentPath, $keepassFixturePassword,
            '4', '2')
        if ($keepassRecentSeed -notmatch 'Smoke KeePass vault seeded') {
            throw 'ui-seed-smoke-keepass-vault-recent did not report success.'
        }

        # The handoff frame is read by a *second* copy of the executable, so it needs a file that no other
        # frame has unlocked, moved aside or left a remembered row behind for.
        $keepassHandoffPath = Join-Path $keepassDirectory 'handed.kdbx'
        $keepassHandoffSeed = Invoke-ArtifactCommand -Label 'ui-seed-smoke-keepass-vault-handoff' -Arguments @(
            '--seed-smoke-keepass-vault', $keepassHandoffPath, $keepassFixturePassword,
            '3', '2')
        if ($keepassHandoffSeed -notmatch 'Smoke KeePass vault seeded') {
            throw 'ui-seed-smoke-keepass-vault-handoff did not report success.'
        }

        $null = Invoke-ArtifactCommand -Label 'smoke-ui' -TimeoutSeconds $UiTimeoutSeconds -AppLogPath $uiLog -Arguments @(
            '--smoke-ui-unlock', $MasterPassword,
            '--smoke-ui-width', '1280',
            '--smoke-ui-height', '800',
            '--smoke-ui-h04-lists',
            '--smoke-ui-note-editor-checks',
            '--smoke-ui-other-pages-checks',
            '--smoke-ui-keyboard-checks',
            '--smoke-ui-status-notice',
            '--smoke-ui-max-vault-load-ms', '4000',
            '--smoke-ui-max-memory-mb', "$MaxLockedMemoryMb",
            '--smoke-ui-keepass-file', $keepassPath,
            '--smoke-ui-keepass-password', $keepassFixturePassword,
            '--smoke-ui-keepass-stream-details',
            '--smoke-ui-keepass-max-growth-mb', "$MaxKeePassGrowthMb",
            '--smoke-ui-keepass-edit', $keepassShotPath,
            '--smoke-ui-keepass-manage', $keepassShotPath,
            '--smoke-ui-keepass-search', $keepassShotPath,
            '--smoke-ui-keepass-search-query', 'example.com',
            '--smoke-ui-keepass-history', $keepassShotPath,
            '--smoke-ui-keepass-create',
            '--smoke-ui-keepass-recent', $keepassRecentPath,
            '--smoke-ui-keepass-handoff', $keepassHandoffPath,
            '--smoke-ui-lock-after-checks',
            '--smoke-ui-exit-after-checks'
        )

        if (-not (Test-Path -LiteralPath $uiLog)) {
            throw "smoke-ui produced no runtime log at $uiLog."
        }

        $gateLines = @(Get-Content -LiteralPath $uiLog | Select-String -SimpleMatch `
            'release gate completed', 'budget result', 'check failed', 'lock cycle result',
            'KeePass probe', 'status notice retirement', 'locked settle result',
            'KeePass edit shot', 'KeePass manage shot', 'KeePass search shot',
            'KeePass create shot', 'KeePass recent shot', 'KeePass handoff shot',
            'KeePass history shot')
        foreach ($line in $gateLines) { Write-Host ($line.Line -replace '^\[[^\]]+\]\s*', '') }
        $gateLine = $gateLines | Where-Object { $_.Line -match 'release gate completed' } | Select-Object -Last 1
        if ($null -eq $gateLine) {
            throw 'smoke-ui produced no release gate line.'
        }

        if ($gateLine.Line -notmatch 'success=True') {
            throw "smoke-ui release gate reported failure: $($gateLine.Line)"
        }

        # A probe that silently stopped running would leave the gate green, so its own line is
        # required and reported with the UI smoke verdict.
        $keepassLine = @($gateLines | Where-Object { $_.Line -match 'KeePass probe result' }) | Select-Object -Last 1
        if ($null -eq $keepassLine) {
            throw "smoke-ui produced no KeePass probe line for $keepassPath."
        }

        if ($keepassLine.Line -notmatch 'success=True') {
            throw "KeePass memory probe reported failure: $($keepassLine.Line)"
        }

        # The edit form, the row commands, the search box and the list of saved versions are reachable only
        # behind a native file dialog, so these in-process frames are the whole proof that the shipped binary
        # draws them - the search one also holds the only check that a rendered row never carries a protected
        # value, and the create one is the only frame that ever shows the new-database form. A run where any
        # of them stopped painting would otherwise leave the gate green.
        foreach ($shot in @('KeePass edit shot', 'KeePass manage shot', 'KeePass search shot', 'KeePass create shot', 'KeePass recent shot', 'KeePass history shot')) {
            $shotLine = @($gateLines | Where-Object { $_.Line -match "$shot result" }) | Select-Object -Last 1
            if ($null -eq $shotLine) {
                throw "smoke-ui produced no $shot result line."
            }

            if ($shotLine.Line -notmatch 'success=True') {
                throw "$shot reported failure: $($shotLine.Line)"
            }
        }

        # The manage shot's own success flag already requires the new-entry form to be painted, but the
        # flag is what this gate exists to catch going soft, so name the field: a draft that is opened in
        # the view model and never drawn has to stop the run here rather than be logged and passed.
        $manageLine = @($gateLines | Where-Object { $_.Line -match 'KeePass manage shot result' }) | Select-Object -Last 1
        if ($manageLine.Line -notmatch 'draftFormOnScreen=True') {
            throw "KeePass manage shot did not paint the new-entry form: $($manageLine.Line)"
        }

        # The recycle bin's two exits are on the same surface and are the moves a person cannot undo by
        # closing the file, so they are named here too: a build where restore leaves the row in the bin,
        # or where emptying leaves the folder behind, stops the run instead of logging a flag nobody reads.
        if ($manageLine.Line -notmatch 'entryRestoredOutOfBin=True') {
            throw "KeePass manage shot did not restore the recycled entry: $($manageLine.Line)"
        }

        if ($manageLine.Line -notmatch 'recycleBinEmptied=True') {
            throw "KeePass manage shot did not empty the recycle bin folder: $($manageLine.Line)"
        }

        # A version list that is only ever built in the view model is the failure this frame exists for, so
        # the two things a person actually needs are named: the row and its restore button are painted inside
        # the window after the pane is scrolled to them, and pressing one puts the entry back to the shape it
        # was opened with. Neither is a flag the aggregate verdict can go soft on.
        $historyLine = @($gateLines | Where-Object { $_.Line -match 'KeePass history shot result' }) | Select-Object -Last 1
        if ($historyLine.Line -notmatch 'listOnScreen=True' -or $historyLine.Line -notmatch 'restoreOnScreen=True' `
            -or $historyLine.Line -notmatch 'restoreButtons=1') {
            throw "KeePass history shot did not paint the version row and its restore button: $($historyLine.Line)"
        }

        if ($historyLine.Line -notmatch 'reverted=True') {
            throw "KeePass history shot did not put the entry back to the version it restored: $($historyLine.Line)"
        }

        # The create form is the only path that can produce a .kdbx, and its three promises are all
        # things a unit test cannot make: the master password is never drawn in the clear, disagreeing
        # confirmations keep the command dark, and cancelling wipes what was typed. Each is named so a
        # build that quietly stopped honouring one of them fails here instead of only in the log.
        $createLine = @($gateLines | Where-Object { $_.Line -match 'KeePass create shot result' }) | Select-Object -Last 1
        if ($createLine.Line -notmatch 'masked=True') {
            throw "KeePass create shot drew the master password in the clear: $($createLine.Line)"
        }

        if ($createLine.Line -notmatch 'mismatchShown=True' -or $createLine.Line -notmatch 'darkWhileMismatching=True') {
            throw "KeePass create shot let a mismatched confirmation through: $($createLine.Line)"
        }

        if ($createLine.Line -notmatch 'readyToCreate=True') {
            throw "KeePass create shot never armed create once the passwords agreed: $($createLine.Line)"
        }

        if ($createLine.Line -notmatch 'paintedSecretFree=True') {
            throw "KeePass create shot painted a typed secret somewhere it should not appear: $($createLine.Line)"
        }

        if ($createLine.Line -notmatch 'wipedOnCancel=True') {
            throw "KeePass create shot left the typed passwords behind after cancel: $($createLine.Line)"
        }

        # The remembered list is the one KeePass surface that survives a person leaving the page, so its
        # own promises are named here rather than left to one aggregate flag: a row is a file name and not
        # a path, it never carries the master password, tapping it returns a blank masked prompt instead of
        # a stored one, a file that has gone is still listed and says where it lived, and dismissing a row
        # takes the section away with it.
        $recentLine = @($gateLines | Where-Object { $_.Line -match 'KeePass recent shot result' }) | Select-Object -Last 1
        if ($recentLine.Line -notmatch 'namedByFile=True' -or $recentLine.Line -notmatch 'noFolderShown=True') {
            throw "KeePass recent shot did not paint the row as a file name: $($recentLine.Line)"
        }

        if ($recentLine.Line -notmatch 'paintedSecretFree=True') {
            throw "KeePass recent shot painted the master password: $($recentLine.Line)"
        }

        if ($recentLine.Line -notmatch 'rowSurvivesClose=True' -or $recentLine.Line -notmatch 'promptMaskedAndEmpty=True') {
            throw "KeePass recent shot did not hand the master password prompt back: $($recentLine.Line)"
        }

        if ($recentLine.Line -notmatch 'goneRowSaysWhereItLived=True' -or $recentLine.Line -notmatch 'goneRowRefusedTheForm=True') {
            throw "KeePass recent shot misreported a database that is no longer there: $($recentLine.Line)"
        }

        if ($recentLine.Line -notmatch 'listHiddenAfterDismiss=True') {
            throw "KeePass recent shot left the remembered list up after its last row was dismissed: $($recentLine.Line)"
        }

        # A second copy of the executable is a real process here, so this is the only frame that asks the
        # shipped binary the question a double-click actually poses. Its promises are named one by one
        # because each is a thing a later change could quietly drop: the file reaches the screen, the window
        # that was out of sight comes back, the master password is asked for rather than filled in, nothing
        # is decrypted for arriving on a command line, and no secret is painted.
        $handoffLine = @($gateLines | Where-Object { $_.Line -match 'KeePass handoff shot result' }) | Select-Object -Last 1
        if ($handoffLine.Line -notmatch 'namedTheFile=True' -or $handoffLine.Line -notmatch 'cameBack=True') {
            throw "KeePass handoff shot did not bring the file and the window back: $($handoffLine.Line)"
        }

        if ($handoffLine.Line -notmatch 'askedForTheKey=True' -or $handoffLine.Line -notmatch 'promptMaskedAndEmpty=True') {
            throw "KeePass handoff shot did not hand back a blank masked master-password prompt: $($handoffLine.Line)"
        }

        if ($handoffLine.Line -notmatch 'stayedShut=True') {
            throw "KeePass handoff shot opened a database from a command-line path: $($handoffLine.Line)"
        }

        if ($handoffLine.Line -notmatch 'listUntouched=True') {
            throw "KeePass handoff shot wrote to the remembered list without unlocking anything: $($handoffLine.Line)"
        }

        if ($handoffLine.Line -notmatch 'paintedSecretFree=True') {
            throw "KeePass handoff shot painted the master password: $($handoffLine.Line)"
        }

        # Same reason: the dispatcher timer that retires status acknowledgements only exists in a
        # running app, so a build where it stopped ticking has to be caught here.
        $noticeLine = @($gateLines | Where-Object { $_.Line -match 'status notice retirement result' }) | Select-Object -Last 1
        if ($null -eq $noticeLine) {
            throw 'smoke-ui produced no status notice retirement probe line.'
        }

        if ($noticeLine.Line -notmatch 'success=True') {
            throw "status notice retirement probe reported failure: $($noticeLine.Line)"
        }

        Write-Host ("UI SMOKE passed. rid={0} mode={1} lockedBudgetMB={2}" -f $RuntimeIdentifier, $Mode, $MaxLockedMemoryMb)
    }
    finally {
        Remove-Item Env:MONICA_APPDATA_DIR -ErrorAction SilentlyContinue
    }

    Write-Host ("RUNTIME SMOKE passed. rid={0} mode={1}" -f $RuntimeIdentifier, $Mode)
    $keepRunRoot = $false
}
finally {
    if (Test-Path -LiteralPath $runRoot) {
        if ($keepRunRoot) {
            Write-Host "RUNTIME SMOKE evidence kept at $runRoot"
        } else {
            Remove-Item -LiteralPath $runRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
