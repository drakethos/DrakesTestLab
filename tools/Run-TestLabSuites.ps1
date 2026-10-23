<#
.SYNOPSIS
  Runs DrakesTestLab test suites unattended and returns their result as the exit code.

.DESCRIPTION
  1. Starts Valheim through Steam with the mod profile (the same doorstop arguments Gale and r2modman use)
     plus DrakesTestLab's command-line switches: -testlab-run <suites> -testlab-quit [-testlab-delay <s>].
     No config file is changed.
  2. DrakesTestLab boots into the test world, runs the suites, writes results and quits with 0 (all passed) or 1.
  3. Prints the summary and exits:
       0 = all passed, 1 = a test failed, 2 = could not start (game already running, profile missing),
       3 = timed out or no results were written.

  The game must be able to start on this PC (Steam running, a desktop session, a GPU).

.EXAMPLE
  .\Run-TestLabSuites.ps1                                   # every suite, Gale profile drakeTest
  .\Run-TestLabSuites.ps1 -Suites "DrakesRenameIt,LockSmith"
  .\Run-TestLabSuites.ps1 -ProfilePath "$env:APPDATA\r2modmanPlus-local\Valheim\profiles\drakeTest"
  .\Run-TestLabSuites.ps1 -Stay                             # leave the game open afterwards
  .\Run-TestLabSuites.ps1 -Suites DrakesRenameIt-Manual -Attended -TimeoutSec 3600   # you answer Pass / Fail
#>
[CmdletBinding()]
param(
    [string]$Suites = 'all',
    [string]$Profile = 'drakeTest',
    [string]$ProfilePath = '',
    [string]$SteamExe = 'C:\Program Files (x86)\Steam\steam.exe',
    [int]$ValheimAppId = 892970,
    [int]$TimeoutSec = 900,
    [double]$StepDelay = -1,
    [ValidateSet('', 'Off', 'Failures', 'Always')]
    [string]$Screenshots = '',
    [switch]$Stay,
    [switch]$Attended,
    [switch]$NoKillOnTimeout
)

$ErrorActionPreference = 'Stop'

if ($ProfilePath -eq '') {
    $ProfilePath = Join-Path $env:APPDATA "com.kesomannen.gale\valheim\profiles\$Profile"
}

$bepInEx = Join-Path $ProfilePath 'BepInEx'
$preloader = Join-Path $bepInEx 'core\BepInEx.Preloader.dll'
$resultsDir = Join-Path $bepInEx 'config\DrakesTestLab\results'
$latest = Join-Path $resultsDir 'latest.json'

if (-not (Test-Path -LiteralPath $preloader)) {
    Write-Host "Profile not found (no BepInEx preloader): $preloader"
    exit 2
}

if (-not (Test-Path -LiteralPath $SteamExe)) {
    Write-Host "Steam not found: $SteamExe"
    exit 2
}

# never touch a game the user is playing
if (Get-Process -Name 'valheim' -ErrorAction SilentlyContinue) {
    Write-Host 'Valheim is already running. Close it first (this script only stops a game it started itself).'
    exit 2
}

$launchArgs = @('-applaunch', "$ValheimAppId",
    '--doorstop-enabled', 'true', '--doorstop-target-assembly', "`"$preloader`"",
    '-testlab-run', "`"$Suites`"")
# -Attended: you answer the checkpoints (manual suites); the game stays open afterwards
if ($Attended) { $Stay = $true; $launchArgs += '-testlab-attended' }
$launchArgs += if ($Stay) { '-testlab-stay' } else { '-testlab-quit' }
if ($Screenshots -ne '') {
    $launchArgs += @('-testlab-screenshots', $Screenshots)
}
if ($StepDelay -ge 0) {
    $launchArgs += @('-testlab-delay', [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}', $StepDelay))
}

# ---- settings some mods only read at game start ("launchConfig" in a suite.json): written into the mod's cfg before
# launching and put back afterwards (the mod itself never edits configs)
$suitesDir = Join-Path $bepInEx 'config\DrakesTestLab\suites'
$restoreCfg = @{}

function Set-IniValue([string]$Path, [string]$Section, [string]$Key, [string]$Value) {
    $lines = [System.Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $Path) { $lines.AddRange([string[]]((Get-Content -LiteralPath $Path -Raw) -split "`r?`n")) }
    $header = "[$Section]"
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i].Trim() -eq $header) { $start = $i; break } }
    if ($start -lt 0) {
        $lines.Add(''); $lines.Add($header); $lines.Add("$Key = $Value")
    } else {
        $done = $false
        for ($i = $start + 1; $i -lt $lines.Count; $i++) {
            $t = $lines[$i].Trim()
            if ($t.StartsWith('[')) { break }
            if ($t -match "^$([regex]::Escape($Key))\s*=") { $lines[$i] = "$Key = $Value"; $done = $true; break }
        }
        if (-not $done) { $lines.Insert($start + 1, "$Key = $Value") }
    }
    [IO.File]::WriteAllText($Path, ($lines -join "`r`n"))
}

function Restore-LaunchConfig {
    foreach ($path in $restoreCfg.Keys) {
        if ($null -eq $restoreCfg[$path]) { Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue }
        else { [IO.File]::WriteAllText($path, $restoreCfg[$path]) }
    }
    $restoreCfg.Clear()
}

function Finish([int]$Code) {
    Restore-LaunchConfig
    exit $Code
}

if (Test-Path -LiteralPath $suitesDir) {
    $wanted = if ($Suites -eq 'all') { $null } else { $Suites.Split(',') | ForEach-Object { $_.Trim() } }
    foreach ($dir in Get-ChildItem -LiteralPath $suitesDir -Directory) {
        if ($dir.Name.StartsWith('_') -or ($wanted -and $wanted -notcontains $dir.Name)) { continue }
        $file = Join-Path $dir.FullName 'suite.json'
        if (-not (Test-Path -LiteralPath $file)) { continue }
        try { $suite = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json } catch { continue }
        foreach ($c in @($suite.launchConfig)) {
            if (-not $c -or -not $c.guid) { continue }
            $cfg = Join-Path $bepInEx "config\$($c.guid).cfg"
            if (-not $restoreCfg.ContainsKey($cfg)) { $restoreCfg[$cfg] = if (Test-Path -LiteralPath $cfg) { [IO.File]::ReadAllText($cfg) } else { $null } }
            Set-IniValue $cfg $c.section $c.key ([string]$c.value)
            Write-Host "Launch setting for $($dir.Name): [$($c.section)] $($c.key) = $($c.value)"
        }
    }
}

$started = Get-Date
Write-Host "DrakesTestLab: running suites '$Suites' with profile $ProfilePath"
Start-Process -FilePath $SteamExe -ArgumentList $launchArgs | Out-Null

# wait for the game to start
$game = $null
$deadline = (Get-Date).AddSeconds(120)
while (-not $game -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $game = Get-Process -Name 'valheim' -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -ge $started.AddSeconds(-5) } | Select-Object -First 1
}

if (-not $game) {
    Write-Host 'Valheim did not start within 2 minutes.'
    Finish 3
}

Write-Host "Valheim started (pid $($game.Id)); waiting for the tests..."
$deadline = $started.AddSeconds($TimeoutSec)
# results are written after every test; the run is finished when latest.json says "complete" (older builds: any new file)
$haveResults = {
    if (-not (Test-Path -LiteralPath $latest) -or (Get-Item -LiteralPath $latest).LastWriteTime -lt $started) { return $false }
    try { $j = Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json } catch { return $false }
    return ($null -eq $j.complete) -or [bool]$j.complete
}
# latest.json is rewritten after every test ("complete": false) and once more when the run ends ("complete": true)
$runDone = {
    if (-not (& $haveResults)) { return $false }
    try { return [bool](Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json).complete } catch { return $false }
}
while (-not $game.HasExited -and (Get-Date) -lt $deadline) {
    # with -Stay the game keeps running, so stop waiting once the run has finished
    if ($Stay -and (& $runDone)) { break }
    Start-Sleep -Seconds 3
    $game.Refresh()
}

if (-not $game.HasExited -and -not (& $runDone)) {
    Write-Host "Timed out after $TimeoutSec s."
    if (-not $NoKillOnTimeout) {
        Write-Host 'Stopping the game this script started.'
        Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
    }
}

# ---- results
if (& $haveResults) {
    $r = Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json
    Write-Host ''
    Write-Host ("Results: {0} passed, {1} failed, {2} skipped" -f $r.passed, $r.failed, $r.skipped)
    foreach ($t in $r.tests) {
        $mark = switch ($t.status) { 'Passed' { 'PASS' } 'Failed' { 'FAIL' } 'Skipped' { 'SKIP' } default { $t.status } }
        $line = "  [$mark] $($t.suite) / $($t.test)"
        if ($t.reason) { $line += " - $($t.reason)" }
        Write-Host $line
    }

    Write-Host ''
    Write-Host "Details: $latest"
    if ([int]$r.failed -gt 0) { Finish 1 }
    if ($r.complete -eq $false) {
        Write-Host 'The run did not finish (game closed or timed out): the results above are what it got to.'
        Finish 3
    }
    Finish 0
}

Write-Host 'No new results were written (the game may have crashed or never reached the test world).'
Write-Host "Check $(Join-Path $bepInEx 'LogOutput.log')"
Finish 3
