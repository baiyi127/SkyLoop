param(
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository = 'baiyi127/SkyLoop',
    [string]$DalamudPath = "$env:APPDATA\XIVLauncherCN\addon\Hooks\dev",
    [string]$Changelog = '修复库啵好运票已满的确认框导致提交中止。新增“库啵好运票已满时继续提交（放弃新好运章）”开关，默认开启；仅对本次交付产生且文字完全匹配的提示自动确认，继续核对扣物品及振兴票增加。其他奖励溢出或未知确认仍停止。'
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
