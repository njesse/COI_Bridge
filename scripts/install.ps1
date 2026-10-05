[CmdletBinding()]
param([string]$ModsDirectory = (Join-Path $env:APPDATA 'Captain of Industry\Mods'),
      [string]$ClientDirectory = (Join-Path $env:APPDATA 'Captain of Industry\StateReporter\client'))
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$id = 'CoIBridge'
$source = Join-Path $repoRoot ('artifacts\' + $id)
foreach ($file in @(($id + '.dll'), 'manifest.json', 'readme.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $file))) { throw "Missing $id/$file. Run scripts/build.ps1 first." }
}
$manifest = Get-Content -Raw -LiteralPath (Join-Path $source 'manifest.json') | ConvertFrom-Json
if ($manifest.id -ne $id) { throw "Unexpected mod id in $source" }
$client = Join-Path $repoRoot 'artifacts\CoIBridgeClient\CoIBridgeClient.exe'
if (-not (Test-Path -LiteralPath $client)) { throw 'Missing client. Run scripts/build.ps1 first.' }
$destination = Join-Path $ModsDirectory $id
New-Item -ItemType Directory -Force -Path $destination | Out-Null
foreach ($file in @(($id + '.dll'), 'manifest.json', 'readme.txt')) {
    Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $destination $file) -Force
}
Write-Output "Installed: $destination"
New-Item -ItemType Directory -Force -Path $ClientDirectory | Out-Null
Copy-Item -LiteralPath $client -Destination (Join-Path $ClientDirectory 'CoIBridgeClient.exe') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\local-bridge.md') -Destination $ClientDirectory -Force
Write-Output "Client installed: $ClientDirectory"
Write-Output 'Restart the game yourself and enable CoI Bridge.'
