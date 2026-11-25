param([string]$DalamudPath = "$env:APPDATA\XIVLauncherCN\addon\Hooks\dev")
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath (Join-Path $DalamudPath 'Dalamud.dll'))) {
    throw "Dalamud.dll not found under $DalamudPath"
}
& dotnet build (Join-Path $PSScriptRoot 'SkyLoop/SkyLoop.csproj') -c Release "-p:DALAMUD_HOME=$DalamudPath"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$skyOutput = Join-Path $PSScriptRoot 'SkyLoop/bin/Release'
$skyDist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $skyDist -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $skyOutput 'SkyLoop.dll') -Destination $skyDist -Force
Copy-Item -LiteralPath (Join-Path $skyOutput 'SkyLoop.json') -Destination $skyDist -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE.md') -Destination $skyDist -Force
Compress-Archive -LiteralPath (Join-Path $skyDist 'SkyLoop.dll'),(Join-Path $skyDist 'SkyLoop.json'),(Join-Path $skyDist 'LICENSE.md') -DestinationPath (Join-Path $skyDist 'SkyLoop.zip') -Force
Write-Output "Plugin package: $skyDist"
