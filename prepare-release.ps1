param(
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository = 'baiyi127/SkyLoop',
    [string]$DalamudPath = "$env:APPDATA\XIVLauncherCN\addon\Hooks\dev",
    [string]$Changelog = '修复重建提交物品身份误判：读取实际NPC交付请求，等待窗口数据就绪，确实不符时才停止。增加当前提交阈值、操作步骤和阶段日志，便于定位制作后停止。仅适用于国服API15测试版，完整游戏循环尚未验证通过。'
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -DalamudPath $DalamudPath
$skyProject = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'SkyLoop/SkyLoop.csproj') -Raw)
$skyVersion = [string]$skyProject.Project.PropertyGroup.Version
if ($skyVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Project version is not a release version.' }
$skyDist = Join-Path $PSScriptRoot 'dist'
$skyManifest = Get-Content -LiteralPath (Join-Path $skyDist 'SkyLoop.json') -Raw | ConvertFrom-Json -AsHashtable
$skyRepoUrl = "https://github.com/$Repository"
if ($skyManifest.RepoUrl -ne $skyRepoUrl) { throw 'RepoUrl in the project does not match the target repository.' }
$skyArchiveName = "SkyLoop-$skyVersion.zip"
$skyDownloadUrl = "$skyRepoUrl/releases/download/v$skyVersion/$skyArchiveName"
$skyManifest['IsHide'] = $false
$skyManifest['IsTestingExclusive'] = $false
$skyManifest['DownloadLinkInstall'] = $skyDownloadUrl
$skyManifest['DownloadLinkUpdate'] = $skyDownloadUrl
$skyManifest['DownloadLinkTesting'] = $skyDownloadUrl
$skyManifest['LastUpdate'] = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$skyManifest['Changelog'] = $Changelog
$skyArray = [object[]]@($skyManifest)
$skyJson = ConvertTo-Json -InputObject $skyArray -Depth 12
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'repo.json'), $skyJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $skyDist 'SkyLoop.zip') -Destination (Join-Path $skyDist $skyArchiveName) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'repo.json') -Destination $skyDist -Force
Write-Output "Repository URL: https://raw.githubusercontent.com/$Repository/main/repo.json"
Write-Output "Release asset: $(Join-Path $skyDist $skyArchiveName)"
