<#
.SYNOPSIS
  Run a mod's TestLab tests as a "hot job": the game stays warm between hot jobs in the Valheim queue, each job gets a
  fresh world, the mod under test is hot-swapped in (BepInEx ScriptEngine), and a failing job gets ONE free retry.

.DESCRIPTION
  OPT-IN, local Debug testing only. Settings live in a local file (default
  C:\Modding\Vahleim\Tools\ScriptEngine\hotswap.json, or $env:DRAKE_HOTSWAP_CONFIG):
    enabled, scriptEngineDll, scriptEngineSha256, hotMods[], holdMinutes, idleGraceMinutes, coldEveryJobs, maxWarmMinutes
  No file, enabled=false, no ScriptEngine, or a mod not listed in hotMods -> the job runs as a normal cold
  Run-TestLabSuites run (normal Debug build + deploy, own queue turn). Nothing here is needed for TestLab itself.

  ScriptEngine is installed only into the separate Gale profile drakeHotSwap (a mirror of drakeTest without the mod
  under test). Never into drakeTest, a package or a repo; no mod references it.

  run      Joins the queue as a HOT entry. On its turn it builds -Project with -p:HotSwap=<Mod> (staged only), then:
             - a warm game that already hosts <Mod> in scripts/ -> swap the new build in, fresh world, run -Suites;
             - otherwise -> close any warm game, prepare drakeHotSwap, launch cold in TestLab hot mode, run -Suites.
           Failed and nobody waiting in the queue -> HELD for one retry (exit 4): fix, then "rerun".
           Afterwards the game is left WARM for the next hot job, unless a normal (cold) entry is next, it has run
           coldEveryJobs jobs, or it is older than maxWarmMinutes. A warm game closes by itself after idleGraceMinutes
           without a hot job, or as soon as a normal entry is first in line (strict first come, first served).
  rerun    The one free retry of a held job: rebuild, swap, rerun the failed tests (or -Suites). Exit 0/1.
  release  Close a held or warm game now.
  status   Show the held / warm game.

  Limits: the mod loads from scripts/, so files next to its DLL (Assets/...) are not there and Info.Location is
  empty. Jotunn pieces/items, RPCs and ServerSync registered at world start keep old code unless the mod cleans up on
  unload. A fresh world does not clear mods' static state; the cold restart every N jobs does. Release checks stay cold.

.EXAMPLE
  .\Run-HotSwap.ps1 run -Mod DrakesReskinIt -Project ..\..\DrakesReskinIt\DrakesReskinIt.csproj -Suites "DrakesReskinIt/R-01"
  .\Run-HotSwap.ps1 rerun -Project ..\..\DrakesReskinIt\DrakesReskinIt.csproj
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('run', 'rerun', 'release', 'status', 'holdwatch', 'warmwatch')]
    [string]$Action = 'status',
    [string]$Mod = '',
    [string]$Project = '',
    [string]$Suites = '',
    [int]$TimeoutSec = 900,
    [string]$SourceProfile = 'drakeTest',
    [string]$HotProfile = 'drakeHotSwap',
    [string]$Config = '',
    [string]$Token = '',
    [switch]$Priority,
    [string]$QueueName = ''
)

$ErrorActionPreference = 'Stop'

$queueDir = 'C:\Modding\Vahleim\.valheim-queue'
$queueFile = Join-Path $queueDir 'queue.json'
$warmFile = Join-Path $queueDir 'warm.json'
$valheimQueue = Join-Path $PSScriptRoot '..\..\scripts\Valheim-Queue.ps1'
$runner = Join-Path $PSScriptRoot 'Run-TestLabSuites.ps1'
$cecil = Join-Path $PSScriptRoot '..\..\DrakesReskinIt\packages\JotunnLib.2.26.1\build\Mono.Cecil.dll'

$gale = Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles'
$src = Join-Path $gale $SourceProfile
$hot = Join-Path $gale $HotProfile
$bep = Join-Path $hot 'BepInEx'
$stage = Join-Path $bep 'hotswap-staging'
$scripts = Join-Path $bep 'scripts'
$holdFile = Join-Path $hot 'hotswap-hold.json'
$requests = Join-Path $bep 'config\DrakesTestLab\requests'
$latest = Join-Path $bep 'config\DrakesTestLab\results\latest.json'
$log = Join-Path $bep 'LogOutput.log'

# ------------------------------------------------------------------ settings (local, opt-in)

if ($Config -eq '') { $Config = if ($env:DRAKE_HOTSWAP_CONFIG) { $env:DRAKE_HOTSWAP_CONFIG } else { 'C:\Modding\Vahleim\Tools\ScriptEngine\hotswap.json' } }
$cfg = $null
if (Test-Path -LiteralPath $Config) { try { $cfg = Get-Content -LiteralPath $Config -Raw | ConvertFrom-Json } catch { Write-Host "Hot swap config unreadable ($Config): $($_.Exception.Message)" } }
function Get-Setting([string]$name, $default) { if ($cfg -and $null -ne $cfg.$name) { return $cfg.$name } return $default }
$hotEnabled = [bool](Get-Setting 'enabled' $false)
$scriptEngineDll = [string](Get-Setting 'scriptEngineDll' '')
$scriptEngineSha = [string](Get-Setting 'scriptEngineSha256' '')
$hotMods = @(Get-Setting 'hotMods' @())
$holdMinutes = [int](Get-Setting 'holdMinutes' 15)
$idleGraceMinutes = [int](Get-Setting 'idleGraceMinutes' 3)
$coldEveryJobs = [int](Get-Setting 'coldEveryJobs' 10)
$maxWarmMinutes = [int](Get-Setting 'maxWarmMinutes' 60)
if ($hotEnabled -and ($scriptEngineDll -eq '' -or -not (Test-Path -LiteralPath $scriptEngineDll))) { Write-Host "Hot swap: ScriptEngine not found ($scriptEngineDll)."; $hotEnabled = $false }

# ------------------------------------------------------------------ helpers

function Read-Json([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    try { return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json } catch { return $null }
}

function Write-Json([string]$path, $obj) { ConvertTo-Json -InputObject $obj -Depth 4 | Set-Content -LiteralPath $path -Encoding UTF8 }

# Only a Valheim launched on THIS profile counts (its command line carries the drakeHotSwap doorstop path).
# Never select or stop a game by start time or name alone: other sessions' games run on other profiles.
function Test-OurGame([int]$id) {
    if ($id -le 0) { return $false }
    $p = Get-CimInstance Win32_Process -Filter "ProcessId=$id" -ErrorAction SilentlyContinue
    return [bool]($p -and $p.Name -eq 'valheim.exe' -and $p.CommandLine -and $p.CommandLine.IndexOf($hot, [StringComparison]::OrdinalIgnoreCase) -ge 0)
}

function Find-OurGame {
    foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name='valheim.exe'" -ErrorAction SilentlyContinue)) {
        if ($p.CommandLine -and $p.CommandLine.IndexOf($hot, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return [int]$p.ProcessId }
    }
    return 0
}

function Get-QueueEntries {
    $raw = $null
    try { $raw = Get-Content -LiteralPath $queueFile -Raw -ErrorAction Stop } catch { return @() }
    if (-not $raw -or -not $raw.Trim()) { return @() }
    try {
        return @(($raw | ConvertFrom-Json) | ForEach-Object { $_ } | Where-Object { $_ -and (Get-Process -Id ([int]$_.ownerPid) -ErrorAction SilentlyContinue) })
    }
    catch { return @() }
}

function Get-Waiting { return @(Get-QueueEntries | Where-Object { $_.state -eq 'waiting' }) }

function Send-Request([string]$name, [string]$text) {
    New-Item -ItemType Directory -Force -Path $requests | Out-Null
    $tmp = Join-Path $requests "$name.tmp"
    [IO.File]::WriteAllText($tmp, $text)
    Move-Item -LiteralPath $tmp -Destination (Join-Path $requests "$name.txt") -Force
}

# wait until TestLab took the request file and reports ready again
function Wait-Ready([string]$name, [int]$gameId, [int]$seconds) {
    $file = Join-Path $requests "$name.txt"
    $state = Join-Path $requests 'state.txt'
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline -and (Test-OurGame $gameId) -and (Test-Path -LiteralPath $file)) { Start-Sleep -Milliseconds 500 }
    if (Test-Path -LiteralPath $file) { return $false }
    Start-Sleep -Seconds 2
    while ((Get-Date) -lt $deadline -and (Test-OurGame $gameId)) {
        try { if ((Get-Content -LiteralPath $state -Raw).Trim() -eq 'ready') { return $true } } catch { }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

# Close the game this tooling started: ask TestLab first, then stop it (only ever our profile's game).
function Close-Game([int]$id) {
    if (-not (Test-OurGame $id)) { return }
    Send-Request 'quit' 'quit'
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline -and (Test-OurGame $id)) { Start-Sleep -Seconds 1 }
    if (Test-OurGame $id) {
        Write-Host "The hot-swap game (pid $id, profile $HotProfile) did not close on request; stopping it."
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
    }
}

function Build-HotSwap {
    if ($Project -eq '') { return }
    $proj = Resolve-Path -LiteralPath $Project
    Write-Host "Building $Mod for hot swap (Debug, staged only; drakeTest untouched)..."
    & dotnet msbuild $proj -p:Configuration=Debug "-p:HotSwap=$Mod" "-p:HotSwapProfile=$HotProfile" -v:m -nologo | Where-Object { $_ -match ' error |Staged ' } | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
}

function Count-LogLines([string]$text) {
    if (-not (Test-Path -LiteralPath $log)) { return 0 }
    return @(Select-String -LiteralPath $log -Pattern $text -SimpleMatch -ErrorAction SilentlyContinue).Count
}

# copy the staged build into scripts/ and wait for ScriptEngine to reload it
function Swap-In([int]$gameId) {
    $dll = Join-Path $stage "$Mod.dll"
    $pdb = Join-Path $stage "$Mod.pdb"
    if (-not (Test-Path -LiteralPath $dll) -or -not (Test-Path -LiteralPath $pdb)) { throw "nothing staged for $Mod in $stage (pass -Project, or build with -p:HotSwap=$Mod)" }
    $reloadsBefore = Count-LogLines 'Reloaded all plugins!'
    $failsBefore = Count-LogLines 'Failed to load plugin'
    New-Item -ItemType Directory -Force -Path $scripts | Out-Null
    Copy-Item -LiteralPath $pdb -Destination (Join-Path $scripts "$Mod.pdb") -Force
    Copy-Item -LiteralPath $dll -Destination (Join-Path $scripts "$Mod.dll") -Force
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline -and (Test-OurGame $gameId) -and (Count-LogLines 'Reloaded all plugins!') -le $reloadsBefore) { Start-Sleep -Seconds 1 }
    if ((Count-LogLines 'Reloaded all plugins!') -le $reloadsBefore -or (Count-LogLines 'Failed to load plugin') -gt $failsBefore) {
        throw "ScriptEngine did not reload $Mod cleanly (see $log)"
    }
    Write-Host "Hot-swapped $Mod into the running game."
}

function Mirror([string]$from, [string]$to, [string[]]$excludeDirs = @()) {
    $rc = @($from, $to, '/MIR', '/NJH', '/NJS', '/NP', '/NFL', '/NDL', '/R:1', '/W:1')
    if ($excludeDirs.Count -gt 0) { $rc += '/XD'; $rc += $excludeDirs }
    & robocopy @rc | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy $from -> $to failed ($LASTEXITCODE)" }
    $global:LASTEXITCODE = 0
}

function Prepare-Profile {
    if (-not (Test-Path -LiteralPath (Join-Path $src 'BepInEx\core\BepInEx.Preloader.dll'))) { throw "source profile not found: $src" }
    if ($scriptEngineSha -and (Get-FileHash -LiteralPath $scriptEngineDll -Algorithm SHA256).Hash -ne $scriptEngineSha) { throw "ScriptEngine hash mismatch: $scriptEngineDll" }

    New-Item -ItemType Directory -Force -Path $bep, (Join-Path $bep 'config'), $scripts | Out-Null
    foreach ($f in Get-ChildItem -LiteralPath $src -File | Where-Object { $_.Name -notmatch '\.log' }) { Copy-Item -LiteralPath $f.FullName -Destination $hot -Force }
    if (Test-Path (Join-Path $src 'doorstop_libs')) { Mirror (Join-Path $src 'doorstop_libs') (Join-Path $hot 'doorstop_libs') }
    Mirror (Join-Path $src 'BepInEx\core') (Join-Path $bep 'core')
    Mirror (Join-Path $src 'BepInEx\patchers') (Join-Path $bep 'patchers')

    # plugins: everything drakeTest has except the mod under test (it loads from scripts/), plus ScriptEngine
    $srcPlugins = Join-Path $src 'BepInEx\plugins'
    $modDirs = @(Get-ChildItem -LiteralPath $srcPlugins -Directory | Where-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -Filter "$Mod.dll" -ErrorAction SilentlyContinue | Select-Object -First 1 } | ForEach-Object { $_.Name })
    Mirror $srcPlugins (Join-Path $bep 'plugins') (@('BepInEx-ScriptEngine') + $modDirs)
    foreach ($d in $modDirs) { Remove-Item -LiteralPath (Join-Path $bep "plugins\$d") -Recurse -Force -ErrorAction SilentlyContinue }

    # hot mode lives in DrakesTestLab itself: use this repo's Debug build, not whatever another session last
    # deployed to drakeTest (that copy may predate it)
    $labDll = Join-Path $PSScriptRoot '..\bin\Debug\DrakesTestLab.dll'
    if (-not (Test-Path -LiteralPath $labDll) -or -not (Select-String -LiteralPath $labDll -Pattern 'RunRequests' -SimpleMatch -Quiet)) {
        throw "DrakesTestLab Debug build with hot mode not found ($labDll). Build DrakesTestLab (Debug) first."
    }
    $labTarget = Get-ChildItem -LiteralPath (Join-Path $bep 'plugins') -Recurse -Filter 'DrakesTestLab.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $labTarget) { throw "DrakesTestLab is not installed in $SourceProfile" }
    Copy-Item -LiteralPath $labDll -Destination $labTarget.FullName -Force

    $sePlugin = Join-Path $bep 'plugins\BepInEx-ScriptEngine'
    New-Item -ItemType Directory -Force -Path $sePlugin | Out-Null
    Copy-Item -LiteralPath $scriptEngineDll -Destination $sePlugin -Force

    # config: the cfg files and TestLab suites/cases from drakeTest; ScriptEngine set up for watcher loading
    foreach ($f in Get-ChildItem -LiteralPath (Join-Path $src 'BepInEx\config') -File -Filter '*.cfg') { Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $bep 'config') -Force }
    foreach ($d in 'suites', 'cases') {
        $from = Join-Path $src "BepInEx\config\DrakesTestLab\$d"
        if (Test-Path -LiteralPath $from) { Mirror $from (Join-Path $bep "config\DrakesTestLab\$d") }
    }
    # LoadOnStart would run before Jotunn/Libs load and its exception kills the watcher: load by watcher instead
    @(
        '[General]', 'LoadOnStart = false', 'ReloadKey = F6', 'QuietMode = false', 'IncludeSubdirectories = false', '',
        '[AutoReload]', 'EnableFileSystemWatcher = true', 'AutoReloadDelay = 2', 'DumpAssemblies = false'
    ) | Set-Content -LiteralPath (Join-Path $bep 'config\com.bepis.bepinex.scriptengine.cfg') -Encoding ASCII

    # ScriptEngine's resolver only searches scripts/, Managed/ and BepInEx/core: give it reference copies of the
    # plugin assemblies the mod uses (metadata only; the game keeps using the copies loaded from plugins/)
    Add-Type -Path $cecil
    $m = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $stage "$Mod.dll"))
    try {
        foreach ($ref in $m.AssemblyReferences) {
            if (Test-Path -LiteralPath (Join-Path $bep "core\$($ref.Name).dll")) { continue }
            $hit = Get-ChildItem -LiteralPath (Join-Path $bep 'plugins') -Recurse -Filter "$($ref.Name).dll" -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($hit) { Copy-Item -LiteralPath $hit.FullName -Destination (Join-Path $bep 'core') -Force }
        }
    }
    finally { $m.Dispose() }

    Get-ChildItem -LiteralPath $scripts -File | Remove-Item -Force
    if (Test-Path -LiteralPath $requests) { Get-ChildItem -LiteralPath $requests -File | Remove-Item -Force }
}

function Wait-Results([datetime]$since, [int]$gameId) {
    $deadline = $since.AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline -and (Test-OurGame $gameId)) {
        Start-Sleep -Seconds 2
        if ((Test-Path -LiteralPath $latest) -and (Get-Item -LiteralPath $latest).LastWriteTime -ge $since) {
            try { if ((Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json).complete) { return } } catch { }
        }
    }
}

function Show-Results([datetime]$since) {
    if (-not (Test-Path -LiteralPath $latest) -or (Get-Item -LiteralPath $latest).LastWriteTime -lt $since) { Write-Host 'No new results were written.'; return 3 }
    $r = Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json
    Write-Host ("Results: {0} passed, {1} failed, {2} skipped" -f $r.passed, $r.failed, $r.skipped)
    foreach ($t in $r.tests) {
        $mark = switch ($t.status) { 'Passed' { 'PASS' } 'Failed' { 'FAIL' } 'Skipped' { 'SKIP' } default { $t.status } }
        $line = "  [$mark] $($t.suite) / $($t.test)"
        if ($t.reason) { $line += " - $($t.reason)" }
        Write-Host $line
    }
    Write-Host "Details: $latest"
    if ([int]$r.failed -gt 0) { return 1 }
    if ($r.complete -eq $false) { return 3 }
    return 0
}

# Run -Suites in a warm game that already hosts $Mod: swap the new build in, fresh world, run.
function Run-InWarmGame([int]$gameId) {
    Swap-In $gameId
    Send-Request 'reset' 'reset'
    if (-not (Wait-Ready 'reset' $gameId 300)) { throw 'the warm game did not come back from the reset' }
    Write-Host 'Fresh world ready.'
    $since = Get-Date
    Send-Request 'run' $Suites
    Wait-Results $since $gameId
    return (Show-Results $since)
}

# After a job: leave the game warm for the next hot job, or close it.
function Finish-Job([int]$gameId, [int]$jobs, [string]$launched) {
    if (-not (Test-OurGame $gameId)) { Remove-Item -LiteralPath $warmFile -Force -ErrorAction SilentlyContinue; return }
    $age = ((Get-Date) - [datetime]$launched).TotalMinutes
    $next = Get-Waiting | Select-Object -First 1
    $why = $null
    if ($jobs -ge $coldEveryJobs) { $why = "it has run $jobs jobs (cold restart every $coldEveryJobs)" }
    elseif ($age -ge $maxWarmMinutes) { $why = "it is $([int]$age) min old (max $maxWarmMinutes)" }
    elseif ($next -and -not $next.hot) { $why = "a normal entry is next ($($next.name))" }
    if ($why) {
        Write-Host "Closing the hot-swap game: $why."
        Close-Game $gameId
        Remove-Item -LiteralPath $warmFile -Force -ErrorAction SilentlyContinue
        return
    }
    $token = [guid]::NewGuid().ToString('N')
    Write-Json $warmFile ([pscustomobject]@{
        pid = $gameId; profile = $HotProfile; scriptsMods = @($Mod); jobs = $jobs; launched = $launched
        idleSince = (Get-Date).ToString('s'); token = $token
    })
    Start-Process -FilePath 'powershell' -WindowStyle Hidden -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", 'warmwatch', '-HotProfile', $HotProfile, '-Token', $token) | Out-Null
    Write-Host "Game left WARM for the next hot job (closes after $idleGraceMinutes idle min, or when a normal entry is next)."
}

# Failed with nobody waiting: hold the game for one retry.
function Hold-Job([int]$gameId, [int]$jobs, [string]$launched) {
    Write-Json $holdFile ([pscustomobject]@{
        pid = $gameId; mod = $Mod; suites = $Suites; jobs = $jobs; launched = $launched
        deadline = (Get-Date).AddMinutes($holdMinutes).ToString('s'); retryUsed = $false
    })
    Start-Process -FilePath 'powershell' -WindowStyle Hidden -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", 'holdwatch', '-HotProfile', $HotProfile) | Out-Null
    Write-Host ''
    Write-Host "HELD: tests failed and the queue is empty, so the game stays open for ONE hot-swap retry (closes by itself in $holdMinutes min)."
    Write-Host "  Fix the code, then:  .\Run-HotSwap.ps1 rerun -Project <csproj>   (or: .\Run-HotSwap.ps1 release)"
}

# ------------------------------------------------------------------ actions

switch ($Action) {
    'status' {
        Write-Host ("Hot swap: {0} (config {1}; hot mods: {2})" -f $(if ($hotEnabled) { 'on' } else { 'off' }), $Config, ($hotMods -join ', '))
        $h = Read-Json $holdFile
        if ($h) { Write-Host ("Held: {0} ({1}), pid {2} {3}, retry used: {4}, closes by {5}" -f $h.mod, $h.suites, $h.pid, $(if (Test-OurGame ([int]$h.pid)) { 'running' } else { 'gone' }), $h.retryUsed, $h.deadline) }
        $w = Read-Json $warmFile
        if ($w) { Write-Host ("Warm: pid {0} {1}, hosts {2}, {3} job(s), idle since {4}" -f $w.pid, $(if (Test-OurGame ([int]$w.pid)) { 'running' } else { 'gone' }), ($w.scriptsMods -join ', '), $w.jobs, $w.idleSince) }
        if (-not $h -and -not $w) { Write-Host 'No held or warm game.' }
        exit 0
    }

    'holdwatch' {
        while ($true) {
            Start-Sleep -Seconds 10
            $h = Read-Json $holdFile
            if (-not $h) { exit 0 }
            if (-not (Test-OurGame ([int]$h.pid))) { Remove-Item -LiteralPath $holdFile -Force -ErrorAction SilentlyContinue; exit 0 }
            if ((Get-Date) -gt [datetime]$h.deadline) {
                Close-Game ([int]$h.pid)
                Remove-Item -LiteralPath $holdFile -Force -ErrorAction SilentlyContinue
                exit 0
            }
        }
    }

    'warmwatch' {
        while ($true) {
            Start-Sleep -Seconds 5
            $w = Read-Json $warmFile
            if (-not $w -or $w.token -ne $Token) { exit 0 }  # taken over by a hot job (or replaced)
            if (-not (Test-OurGame ([int]$w.pid))) { Remove-Item -LiteralPath $warmFile -Force -ErrorAction SilentlyContinue; exit 0 }
            $first = Get-QueueEntries | Select-Object -First 1
            $idle = ((Get-Date) - [datetime]$w.idleSince).TotalMinutes
            $why = $null
            if ($first -and -not $first.hot) { $why = "a normal entry is first in line ($($first.name))" }
            elseif (-not $first -and $idle -ge $idleGraceMinutes) { $why = "no hot job for $idleGraceMinutes min" }
            if ($why) {
                $again = Read-Json $warmFile
                if (-not $again -or $again.token -ne $Token) { exit 0 }
                Remove-Item -LiteralPath $warmFile -Force -ErrorAction SilentlyContinue
                Close-Game ([int]$w.pid)
                exit 0
            }
        }
    }

    'release' {
        foreach ($f in $holdFile, $warmFile) {
            $x = Read-Json $f
            if ($x) { Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue; Close-Game ([int]$x.pid); Write-Host "Closed the game from $([IO.Path]::GetFileName($f))." }
        }
        exit 0
    }

    'rerun' {
        $h = Read-Json $holdFile
        if (-not $h -or -not (Test-OurGame ([int]$h.pid))) { Write-Host 'No held hot-swap game (it closed or was never held). Queue a normal run.'; Remove-Item -LiteralPath $holdFile -Force -ErrorAction SilentlyContinue; exit 2 }
        if ($h.retryUsed) { Write-Host 'The free retry was already used. Release the game and queue a normal run.'; exit 2 }
        $Mod = $h.mod
        if ($Suites -eq '') { $Suites = 'failed' }
        $h.retryUsed = $true
        $h.deadline = (Get-Date).AddSeconds($TimeoutSec + 300).ToString('s')
        Write-Json $holdFile $h
        $gameId = [int]$h.pid
        try {
            Build-HotSwap
            $code = Run-InWarmGame $gameId
        }
        catch {
            Write-Host "Hot retry failed: $($_.Exception.Message)"
            $code = 3
        }
        Remove-Item -LiteralPath $holdFile -Force -ErrorAction SilentlyContinue
        if ($code -eq 0) { Finish-Job $gameId ([int]$h.jobs + 1) ([string]$h.launched) }
        else { Close-Game $gameId; Write-Host 'Retry used; the game is closed. A further pass queues normally (at the back).' }
        exit $code
    }

    'run' {
        if ($Mod -eq '' -or $Suites -eq '') { throw '-Mod and -Suites are required for run' }

        # ---- hot swap off for this job: a normal cold run (normal Debug build + deploy, own queue turn)
        if (-not $hotEnabled -or $hotMods -notcontains $Mod) {
            $why = if (-not $hotEnabled) { "hot swap is off ($Config)" } else { "$Mod is not in hotMods" }
            Write-Host "Cold run: $why."
            $cmd = ''
            if ($Project) { $cmd += "dotnet msbuild '$((Resolve-Path -LiteralPath $Project).Path)' -p:Configuration=Debug -v:m -nologo; if (`$LASTEXITCODE -ne 0) { exit 2 }; " }
            $cmd += "& '$runner' -Suites '$($Suites -replace "'", "''")' -TimeoutSec $TimeoutSec"
            if ($env:VALHEIM_QUEUE_HELD) { Invoke-Expression $cmd; exit $LASTEXITCODE }
            $who = if ($QueueName) { $QueueName } else { "TestLab run $Mod $Suites" }
            & $valheimQueue run -Name $who -Priority:$Priority -Command $cmd
            exit $LASTEXITCODE
        }

        # ---- hot job: queue as a hot entry (Valheim-Queue sets VALHEIM_QUEUE_HELD on our turn)
        if (-not $env:VALHEIM_QUEUE_HELD) {
            $passOn = foreach ($p in $PSBoundParameters.GetEnumerator()) {
                if ($p.Key -in 'Priority', 'QueueName', 'Action') { continue }
                if ($p.Value -is [System.Management.Automation.SwitchParameter]) { if ($p.Value.IsPresent) { "-$($p.Key)" } }
                else { "-$($p.Key) '$(([string]$p.Value) -replace "'", "''")'" }
            }
            $who = if ($QueueName) { $QueueName } else { "Hot job $Mod $Suites" }
            & $valheimQueue run -Hot -Name $who -Priority:$Priority -Command "& '$PSCommandPath' run $($passOn -join ' ')"
            exit $LASTEXITCODE
        }

        $held = Read-Json $holdFile
        if ($held -and (Test-OurGame ([int]$held.pid))) { Write-Host "A held hot-swap game is waiting for its retry ($($held.mod)); not taking it over."; exit 2 }

        Build-HotSwap

        # take over a warm game that already hosts this mod; otherwise close it and start cold
        $w = Read-Json $warmFile
        Remove-Item -LiteralPath $warmFile -Force -ErrorAction SilentlyContinue   # ours now: its watchdog stands down
        $gameId = 0; $jobs = 0; $launched = (Get-Date).ToString('s')
        if ($w -and (Test-OurGame ([int]$w.pid))) {
            $age = ((Get-Date) - [datetime]$w.launched).TotalMinutes
            if (@($w.scriptsMods) -contains $Mod -and [int]$w.jobs -lt $coldEveryJobs -and $age -lt $maxWarmMinutes) {
                $gameId = [int]$w.pid; $jobs = [int]$w.jobs; $launched = [string]$w.launched
                Write-Host "Taking over the warm game (pid $gameId, $jobs job(s) so far)."
            }
            else {
                Write-Host 'The warm game cannot take this job (other mod, or due for a cold restart): closing it.'
                Close-Game ([int]$w.pid)
            }
        }

        if ($gameId -ne 0) {
            try { $code = Run-InWarmGame $gameId }
            catch { Write-Host "Warm run failed: $($_.Exception.Message); falling back to a cold start."; Close-Game $gameId; $gameId = 0 }
        }

        if ($gameId -eq 0) {
            Write-Host "Preparing profile $HotProfile from $SourceProfile (without $Mod, plus ScriptEngine)..."
            Prepare-Profile
            $t0 = Get-Date
            # keep the previous game's log for diagnosis (a warm game that failed its reset, for example)
            if (Test-Path -LiteralPath $log) { Move-Item -LiteralPath $log -Destination (Join-Path $bep 'LogOutput.prev.log') -Force -ErrorAction SilentlyContinue }
            # first load: once the chainloader is done, ScriptEngine's watcher picks the mod up from scripts/
            $job = Start-Job -ArgumentList $log, $stage, $scripts, $Mod -ScriptBlock {
                param($log, $stage, $scripts, $mod)
                $deadline = (Get-Date).AddSeconds(300)
                while ((Get-Date) -lt $deadline) {
                    Start-Sleep -Milliseconds 500
                    if ((Test-Path $log) -and (Select-String -LiteralPath $log -Pattern 'Chainloader startup complete' -SimpleMatch -Quiet)) {
                        Copy-Item (Join-Path $stage "$mod.pdb") (Join-Path $scripts "$mod.pdb") -Force
                        Copy-Item (Join-Path $stage "$mod.dll") (Join-Path $scripts "$mod.dll") -Force
                        return
                    }
                }
            }
            & $runner -ProfilePath $hot -Suites $Suites -Hot -TimeoutSec $TimeoutSec -NoKillOnTimeout
            $code = $LASTEXITCODE
            Receive-Job $job -Wait -AutoRemoveJob | Out-Null
            if ($code -eq 2) { exit 2 }   # the runner refused to launch: no game of ours exists
            $gameId = Find-OurGame
            if ($gameId -eq 0) { exit $code }
        }

        $jobs++
        if ($code -eq 1 -and @(Get-Waiting).Count -eq 0) {
            Hold-Job $gameId $jobs $launched
            exit 4
        }
        if ($code -eq 1) { Write-Host ("Tests failed and {0} waiting in the queue: no hot retry. Rejoin at the back after the fix." -f @(Get-Waiting).Count) }
        if ($code -eq 0 -or $code -eq 1) { Finish-Job $gameId $jobs $launched }
        else { Close-Game $gameId }
        exit $code
    }
}
