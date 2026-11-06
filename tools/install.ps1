<#
.SYNOPSIS
    Builds Snip and installs it for this user: %LOCALAPPDATA%\Snip\app, started at sign-in, with Print Screen.

.DESCRIPTION
    1. Asks a running Snip to quit (Snip.exe --quit). If a picture in it has marks not yet copied or saved, it stops
       here: close or save that picture and run this again.
    2. Publishes src/Snip to %LOCALAPPDATA%\Snip\app.
    3. Starts Snip at sign-in (HKCU Run value "Snip") and gives it Print Screen: Windows' own "Use the Print screen key
       to open screen capture" setting (HKCU\Control Panel\Keyboard, PrintScreenKeyForSnippingEnabled) is turned off.
       Its old value is kept in %LOCALAPPDATA%\Snip\install.json the first time, and -RestorePrintScreen puts it back.
    4. Starts Snip.

.EXAMPLE
    pwsh tools/install.ps1
    pwsh tools/install.ps1 -NoStart
    pwsh tools/install.ps1 -RestorePrintScreen    # give Print Screen back to Windows (and stop starting Snip at sign-in)
#>
#Requires -Version 7
[CmdletBinding()]
param(
    [switch]$NoStart,
    [switch]$RestorePrintScreen
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$root = Join-Path $env:LOCALAPPDATA 'Snip'
$appDir = Join-Path $root 'app'
$exe = Join-Path $appDir 'Snip.exe'
$state = Join-Path $root 'install.json'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$keyboard = 'HKCU:\Control Panel\Keyboard'
$printValue = 'PrintScreenKeyForSnippingEnabled'

if ($RestorePrintScreen) {
    $old = if (Test-Path $state) { (Get-Content -Raw $state | ConvertFrom-Json).printScreenKeyForSnippingEnabled } else { $null }
    if ($null -eq $old) { Remove-ItemProperty $keyboard -Name $printValue -ErrorAction SilentlyContinue }
    else { Set-ItemProperty $keyboard -Name $printValue -Value ([int]$old) -Type DWord }
    Remove-ItemProperty $runKey -Name 'Snip' -ErrorAction SilentlyContinue
    Write-Host 'Print Screen is Windows'' again, and Snip no longer starts at sign-in. Sign out and in for Windows to take it back.'
    return
}

# 1. Stop a running copy, unless it holds marks.
if (Get-Process Snip -ErrorAction SilentlyContinue) {
    Write-Host 'Asking Snip to quit...'
    $running = (Get-Process Snip | Select-Object -First 1).Path
    $p = Start-Process -FilePath $running -ArgumentList '--quit' -Wait -PassThru
    if ($p.ExitCode -eq 2) { throw 'A picture in Snip has marks you haven''t copied or saved: close or save it, then run this again.' }
    Get-Process Snip -ErrorAction SilentlyContinue | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    Get-Process Snip -ErrorAction SilentlyContinue | Stop-Process -Force
}

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

# 4. Start.
if (-not $NoStart) {
    Start-Process -FilePath $exe -WorkingDirectory $appDir
    Write-Host 'Started. Snip is in the tray; press Print Screen. Log: %LOCALAPPDATA%\Snip\snip.log'
}
