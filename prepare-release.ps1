param(
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository = 'baiyi127/SkyLoop',
    [string]$DalamudPath = "$env:APPDATA\XIVLauncherCN\addon\Hooks\dev"
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
$skyManifest['Changelog'] = '首次公开测试版：宏队列、重建收藏品提交和天穹票兑换。仅适用于国服 API 15；完整游戏循环尚未验证通过。'
$skyArray = [object[]]@($skyManifest)
$skyJson = ConvertTo-Json -InputObject $skyArray -Depth 12
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'repo.json'), $skyJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $skyDist 'SkyLoop.zip') -Destination (Join-Path $skyDist $skyArchiveName) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'repo.json') -Destination $skyDist -Force
Write-Output "Repository URL: https://raw.githubusercontent.com/$Repository/main/repo.json"
Write-Output "Release asset: $(Join-Path $skyDist $skyArchiveName)"
