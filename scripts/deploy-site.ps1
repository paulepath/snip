<#
.SYNOPSIS
  Publishes site/ to https://snip.e-path.co.uk, served by nginx on the Pi 5.
.DESCRIPTION
  Copies site-host/ and site/ (as html/) to ~/docker/snip-site. Refills the
  directory bind mounts in place, then reloads nginx or starts the container.
  Routing and DNS must be configured separately; see docs/website.md.
#>
param([string]$PiHost = 'pgj99@192.168.0.120', [string]$Dir = '~/docker/snip-site')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# The destination becomes shell syntax on the Pi. Only accept a simple home-relative path.
if ($Dir -notmatch '^~/[A-Za-z0-9][A-Za-z0-9/_-]*$') {
    throw 'Dir must be a simple path below ~/ (for example ~/docker/snip-site).'
}
foreach ($asset in @('editor.webp', 'steps.webp', 'capture.webp', 'og.png', 'favicon.png', 'apple-touch-icon.png', 'mark-dark.svg', 'snip-logo-dark.svg')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root "site/img/$asset"))) {
        throw "Missing site/img/$asset. Capture the screenshots and run python tools/make_site_images.py before deploying."
    }
}
$stage = Join-Path ([IO.Path]::GetTempPath()) "snip-site-$PID"
$tar = "$stage.tar"
if (Test-Path -LiteralPath $stage) { throw "Staging path already exists: $stage" }
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    Copy-Item (Join-Path $root 'site-host/*') $stage -Recurse
    Copy-Item (Join-Path $root 'site') (Join-Path $stage 'html') -Recurse
    tar -cf $tar -C $stage .
    if ($LASTEXITCODE -ne 0) { throw "tar failed ($LASTEXITCODE)" }
    $remoteTar = "/tmp/snip-site-$PID.tar"
    # Keep html/ and conf/ themselves: nginx mounts those directories.
    $remote = "set -e; mkdir -p $Dir/html $Dir/conf; cd $Dir; " +
              "mkdir -p .incoming; tar -xf $remoteTar -C .incoming; rm -f $remoteTar; " +
              "cp .incoming/compose.yaml .; rm -rf conf/* html/*; " +
              "cp -r .incoming/conf/. conf/; cp -r .incoming/html/. html/; rm -rf .incoming; " +
              "if docker ps --format '{{.Names}}' | grep -qx snip-site; then " +
              "docker exec snip-site nginx -t; docker exec snip-site nginx -s reload; else docker compose up -d; fi"
    scp -q $tar "${PiHost}:$remoteTar"
    if ($LASTEXITCODE -ne 0) { throw "Copying to $PiHost failed ($LASTEXITCODE)" }
    ssh $PiHost $remote
    if ($LASTEXITCODE -ne 0) { throw "Deploying to $PiHost failed ($LASTEXITCODE)" }
    Write-Host "Published site/ to ${PiHost}:$Dir"
} finally {
    # Verify the exact staging target before recursive cleanup.
    $expectedStage = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) "snip-site-$PID"))
    if ([IO.Path]::GetFullPath($stage) -eq $expectedStage) {
        Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $tar -Force -ErrorAction SilentlyContinue
    }
}
