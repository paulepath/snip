<#
.SYNOPSIS
    Installs Snip for this user: by default the latest release from GitHub, or with -Dev a build of this checkout.

.DESCRIPTION
    By default:
    1. If a dev copy is in %LOCALAPPDATA%\Snip\app (an earlier -Dev install), offers to remove it so the two don't fight
       over Print Screen: asks it to quit (Snip.exe --quit; if a picture in it has marks not yet copied or saved, it
       stops here), removes its HKCU Run "Snip" entry if that points at it, and deletes the folder. -Force removes it
       without asking.
    2. Asks a running Snip to quit, with the same check for marks.
    3. Downloads SnipApp-win-Setup.exe from the latest release of github.com/paulepath/snip to a temp folder and runs
       it. It installs to %LOCALAPPDATA%\SnipApp and starts Snip; its install hook starts Snip at sign-in (HKCU Run
       "Snip") and turns off Windows' own "Use the Print screen key to open screen capture" setting, keeping the old
       value in %LOCALAPPDATA%\Snip\install.json. Snip updates itself from then on. Settings > Apps uninstalls it and
       puts both back.

    With -Dev (this checkout, as before releases):
    1. Asks a running Snip to quit, with the same check for marks.
    2. Publishes src/Snip to %LOCALAPPDATA%\Snip\app.
    3. Starts Snip at sign-in (HKCU Run "Snip") and gives it Print Screen (PrintScreenKeyForSnippingEnabled = 0 under
       HKCU\Control Panel\Keyboard). The old value is kept in %LOCALAPPDATA%\Snip\install.json the first time.
    4. Starts Snip.

    Snip is started detached (Win32_Process Create), so it outlives the shell that ran this, an agent's included.

.EXAMPLE
    pwsh tools/install.ps1                        # the latest release
    pwsh tools/install.ps1 -Force                 # the same, removing a dev copy without asking
    pwsh tools/install.ps1 -Dev                   # build this checkout into %LOCALAPPDATA%\Snip\app
    pwsh tools/install.ps1 -Dev -NoStart
    pwsh tools/install.ps1 -RestorePrintScreen    # give Print Screen back to Windows
#>
#Requires -Version 7
[CmdletBinding()]
param(
    [switch]$Dev,
    [switch]$NoStart,
    [switch]$Force,
    [switch]$RestorePrintScreen
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$root = Join-Path $env:LOCALAPPDATA 'Snip'
$appDir = Join-Path $root 'app'
$exe = Join-Path $appDir 'Snip.exe'
$state = Join-Path $root 'install.json'
$releaseDir = Join-Path $env:LOCALAPPDATA 'SnipApp'            # Velopack's, after packId SnipApp
$releaseExe = Join-Path $releaseDir 'current\Snip.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$keyboard = 'HKCU:\Control Panel\Keyboard'
$printValue = 'PrintScreenKeyForSnippingEnabled'
$latest = 'https://api.github.com/repos/paulepath/snip/releases/latest'
$setupName = 'SnipApp-win-Setup.exe'

# Asks every running Snip to quit; stops if one holds marks. $Only limits it to copies in that folder.
function Stop-Snip([string]$Only) {
    $procs = Get-Process Snip -ErrorAction SilentlyContinue | Where-Object { -not $Only -or ($_.Path -and $_.Path.StartsWith($Only, 'OrdinalIgnoreCase')) }
    if (-not $procs) { return }
    Write-Host 'Asking Snip to quit...'
    $running = ($procs | Select-Object -First 1).Path
    $p = Start-Process -FilePath $running -ArgumentList '--quit' -Wait -PassThru
    if ($p.ExitCode -eq 2) { throw 'A picture in Snip has marks you haven''t copied or saved: close or save it, then run this again.' }
    $procs | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    $procs | Where-Object { -not $_.HasExited } | Stop-Process -Force
}

# Starts a program outside this shell's process tree, so it lives on after the shell (or an agent's turn) ends.
function Start-Detached([string]$CommandLine) {
    $r = Invoke-CimMethod Win32_Process -MethodName Create -Arguments @{ CommandLine = $CommandLine }
    if ($r.ReturnValue -ne 0) { throw "Couldn't start $CommandLine (Win32_Process Create returned $($r.ReturnValue))" }
    $r.ProcessId
}

function Get-RunValue { (Get-ItemProperty $runKey -Name 'Snip' -ErrorAction SilentlyContinue).Snip }

if ($RestorePrintScreen) {
    $old = if (Test-Path $state) { (Get-Content -Raw $state | ConvertFrom-Json).printScreenKeyForSnippingEnabled } else { $null }
    if ($null -eq $old) { Remove-ItemProperty $keyboard -Name $printValue -ErrorAction SilentlyContinue }
    else { Set-ItemProperty $keyboard -Name $printValue -Value ([int]$old) -Type DWord }
    $run = Get-RunValue
    if ($run -and -not $run.Contains($releaseDir, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-ItemProperty $runKey -Name 'Snip'
        Write-Host 'Print Screen is Windows'' again, and the dev copy no longer starts at sign-in.'
    }
    else { Write-Host 'Print Screen is Windows'' again.' }
    if (Test-Path $releaseExe) {
        Write-Host 'The installed Snip still starts at sign-in (its tray menu has Start with Windows). To remove it, uninstall Snip in Settings > Apps.'
    }
    Write-Host 'Sign out and in for Windows to take the key back.'
    return
}

if ($Dev) {
    # 1. Stop a running copy, unless it holds marks.
    Stop-Snip

    # 2. Build.
    Write-Host "Publishing to $appDir..."
    dotnet publish (Join-Path $repo 'src\Snip\Snip.csproj') -c Release -o $appDir --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    # 3. Sign-in and Print Screen.
    Set-ItemProperty $runKey -Name 'Snip' -Value "`"$exe`""
    Write-Host 'Start at sign-in: HKCU Run "Snip"'
    $current = (Get-ItemProperty $keyboard -Name $printValue -ErrorAction SilentlyContinue).$printValue
    if (-not (Test-Path $state)) {
        New-Item -ItemType Directory -Force $root | Out-Null
        @{ printScreenKeyForSnippingEnabled = $current } | ConvertTo-Json | Set-Content $state
    }
    if ($current -ne 0) {
        Set-ItemProperty $keyboard -Name $printValue -Value 0 -Type DWord
        Write-Host 'Print Screen: Windows'' own screen capture setting turned off, so Snip can have the key (old value kept in install.json).'
    }
    if (Test-Path $releaseExe) {
        Write-Host "Note: the release install in $releaseDir is still there; uninstall it in Settings > Apps so the two don't fight over Print Screen."
    }

    # 4. Start.
    if (-not $NoStart) {
        Start-Detached "`"$exe`"" | Out-Null
        Write-Host 'Started. Snip is in the tray; press Print Screen. Log: %LOCALAPPDATA%\Snip\snip.log'
    }
    return
}

# 1. Remove a dev copy, so it and the release don't fight over Print Screen.
if (Test-Path $exe) {
    if (-not $Force -and -not $PSCmdlet.ShouldContinue("Remove the dev copy in $appDir first? It would fight the release over Print Screen.", 'Dev copy found')) {
        throw "Left the dev copy in $appDir. Remove it (or run with -Force), then run this again."
    }
    Stop-Snip -Only $appDir
    if ((Get-RunValue) -like "*$appDir*") { Remove-ItemProperty $runKey -Name 'Snip' }
    Remove-Item -Recurse -Force $appDir
    Write-Host "Removed the dev copy in $appDir (settings, log and install.json stay)."
}

# 2. Stop a running release, unless it holds marks.
Stop-Snip

# 3. Download and run Setup.exe.
$release = Invoke-RestMethod $latest -Headers @{ 'User-Agent' = 'snip-install'; Accept = 'application/vnd.github+json' }
$asset = $release.assets | Where-Object name -EQ $setupName | Select-Object -First 1
if (-not $asset) { throw "Release $($release.tag_name) has no $setupName." }
$tmp = Join-Path ([IO.Path]::GetTempPath()) "snip-setup-$($release.tag_name)"
New-Item -ItemType Directory -Force $tmp | Out-Null
$setup = Join-Path $tmp $setupName
Write-Host "Downloading $($release.tag_name)..."
Invoke-WebRequest $asset.browser_download_url -OutFile $setup -Headers @{ 'User-Agent' = 'snip-install' }

# Setup starts Snip when it's done, so it runs detached too; wait for it to finish.
Write-Host "Running $setupName..."
$setupId = Start-Detached "`"$setup`""
while (Get-Process -Id $setupId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }
Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
if (-not (Test-Path $releaseExe)) { throw "Setup finished but $releaseExe isn't there. Run $setupName from the release page by hand to see why." }

$printOff = (Get-ItemProperty $keyboard -Name $printValue -ErrorAction SilentlyContinue).$printValue -eq 0
Write-Host "Installed $($release.tag_name) to $releaseDir. Start at sign-in: $(if (Get-RunValue) { 'on' } else { 'off' }). Print Screen: $(if ($printOff) { 'Snip''s' } else { 'still Windows''' })."
if ($NoStart) {
    # Setup always starts Snip; give it a moment, then ask it to quit.
    $deadline = (Get-Date).AddSeconds(15)
    while (-not (Get-Process Snip -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    Stop-Snip
    Write-Host 'Not left running (-NoStart).'
}
else { Write-Host 'Snip is in the tray; press Print Screen. It updates itself. Log: %LOCALAPPDATA%\Snip\snip.log' }
