param(
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository = 'baiyi127/SkyLoop',
    [string]$DalamudPath = "$env:APPDATA\XIVLauncherCN\addon\Hooks\dev",
    [string]$Changelog = '新增可搜索的制作配方下拉选择，提交与兑换改用内置固定交互位置。修复制作品检测、第二轮开工、退出制作姿态、普通购买确认及低阈值兑换误判。增加流程和路径诊断。国服API15测试版。'
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
