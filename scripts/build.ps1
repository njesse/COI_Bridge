[CmdletBinding()]
param(
    [string]$GameRoot = $env:COI_ROOT,
    [switch]$RunTests
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $GameRoot) { $GameRoot = Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Captain of Industry' }
$managed = Join-Path $GameRoot 'Captain of Industry_Data\Managed'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found. Install .NET Framework 4.8 on Windows.' }
$sharedSource = Join-Path $repoRoot 'src\SharedReadModel'

$bridgeSource = Join-Path $repoRoot 'src\CoIBridge'
# Use the exact Unity/game reference assemblies, never redistribute them.
$references = @('mscorlib', 'netstandard', 'System', 'System.Core', 'System.Runtime.Serialization', 'System.Xml', 'System.IO.Compression', 'Mafi', 'Mafi.Core')
$referenceArgs = foreach ($name in $references) {
    $file = Join-Path $managed ($name + '.dll')
    if (-not (Test-Path -LiteralPath $file)) { throw "Game assembly missing: $file. Pass -GameRoot or set COI_ROOT." }
    '/reference:' + $file
}
# SharedReadModel is owned locally by this repository; no sibling checkout needed.
$modId = 'CoIBridge'
$modSource = Join-Path $repoRoot ('src\' + $modId)
$output = Join-Path $repoRoot ('artifacts\' + $modId)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sources = @(Get-ChildItem -LiteralPath $modSource, $sharedSource -Filter '*.cs' | ForEach-Object FullName)
$defines = @()
$modReferences = @($referenceArgs)
$defines = @('/define:COI_BRIDGE')
foreach ($name in @('Mafi.Unity', 'Mafi.UnityCore', 'UnityEngine.CoreModule')) {
    $file = Join-Path $managed ($name + '.dll')
    if (-not (Test-Path -LiteralPath $file)) { throw "Camera assembly missing: $file" }
    $modReferences += '/reference:' + $file
}
& $compiler /noconfig /nologo /codepage:65001 /target:library /optimize+ /warn:4 /warnaserror+ /nostdlib+ @defines ('/out:' + (Join-Path $output ($modId + '.dll'))) @modReferences @sources
if ($LASTEXITCODE -ne 0) { throw "$modId compilation failed." }
Copy-Item -LiteralPath (Join-Path $modSource 'manifest.json'), (Join-Path $modSource 'readme.txt') -Destination $output -Force
Write-Output "Built: $output"
$clientOutput = Join-Path $repoRoot 'artifacts\CoIBridgeClient'
New-Item -ItemType Directory -Force -Path $clientOutput | Out-Null
$bridgeShared = @((Join-Path $bridgeSource 'BridgeJson.cs'), (Join-Path $bridgeSource 'BridgeBroker.cs'), (Join-Path $bridgeSource 'BridgePipe.cs'))
& $compiler /nologo /codepage:65001 /target:exe /warn:4 /warnaserror+ ('/out:' + (Join-Path $clientOutput 'CoIBridgeClient.exe')) /reference:System.Runtime.Serialization.dll /reference:System.Xml.dll @bridgeShared (Join-Path $repoRoot 'src\CoIBridgeClient\Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Bridge client compilation failed.' }
if ($RunTests) {
    $testOutput = Join-Path $repoRoot 'artifacts\tests'
    New-Item -ItemType Directory -Force -Path $testOutput | Out-Null
    $bridgeTestExe = Join-Path $testOutput 'BridgeTests.exe'
    & $compiler /nologo /codepage:65001 /target:exe /warn:4 /warnaserror+ ('/out:' + $bridgeTestExe) /reference:System.Runtime.Serialization.dll /reference:System.Xml.dll @bridgeShared (Join-Path $repoRoot 'tests\BridgeTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Bridge test compilation failed.' }
    & $bridgeTestExe
    if ($LASTEXITCODE -ne 0) { throw 'Bridge tests failed.' }
}
