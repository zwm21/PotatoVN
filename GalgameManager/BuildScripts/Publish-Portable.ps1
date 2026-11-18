<#
.SYNOPSIS
    构建并发布自包含便携包，目录名带动态版本号。

.DESCRIPTION
    发布到 publish/PotatoVN-<动态版本>-<平台>-portable。
    动态版本号 = Package.appxmanifest 版本前三段不变、第 4 段叠加"自 manifest 改动以来的提交数"
    （见 BuildScripts/Get-BuildVersionInfo.ps1 的 Get-DynamicVersion），
    例如 manifest 1.10.3.0 的第 7 次提交得到 1.10.3.7，目录名为 PotatoVN-1.10.3.7-x64-portable。
    与 .github/workflows/build.yml 的产物命名规则一致。

.PARAMETER Platform
    目标平台：x64 / x86 / arm64，默认 x64。

.EXAMPLE
    pwsh -File GalgameManager/BuildScripts/Publish-Portable.ps1
.EXAMPLE
    pwsh -File GalgameManager/BuildScripts/Publish-Portable.ps1 -Platform arm64
#>
[CmdletBinding()]
param(
    [ValidateSet("x64", "x86", "arm64")]
    [string]$Platform = "x64"
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repoRoot = (git rev-parse --show-toplevel).Trim()
if (-not $repoRoot) { throw "当前目录不是 git 仓库" }
$project = Join-Path $repoRoot "GalgameManager\GalgameManager.csproj"
$publishRoot = Join-Path $repoRoot "publish"
$staging = Join-Path $publishRoot "portable"

# 动态版本号只有在构建后才拿得到，因此先发布到固定中间目录，再改名
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
$publishDir = $staging.Replace('\', '/') + '/'
dotnet publish $project -c Release -p:Platform=$Platform -p:PublishProfile=win10-$Platform `
    -p:PotatoVnPortable=true -p:PublishDir=$publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败，退出码 $LASTEXITCODE" }

$infoFile = Get-ChildItem -Path (Join-Path $repoRoot "GalgameManager\obj") -Recurse -Filter BuildVersionInfo.txt |
    # obj 下同时存在 Debug / Release 与各平台的产物，只取本次 Release 构建生成的（最新写入的）那个
    Where-Object { $_.FullName -match '\\Release\\' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $infoFile) { throw "未找到构建期生成的 BuildVersionInfo.txt" }
$fields = (Get-Content $infoFile.FullName).Trim() -split '\|'
$version = $fields[0]
if ([string]::IsNullOrWhiteSpace($version)) { throw "无法从 $($infoFile.FullName) 解析动态版本号" }

$target = Join-Path $publishRoot "PotatoVN-$version-$Platform-portable"
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Move-Item -Path $staging -Destination $target

# 自包含校验：运行时不在包里，目标机就跑不起来
foreach ($name in @("Microsoft.WindowsAppRuntime.dll", "GalgameManager.exe")) {
    if (-not (Test-Path (Join-Path $target $name))) { throw "输出目录中未找到 $name，自包含打包未生效" }
}

$files = (Get-ChildItem $target -Recurse -File).Count
Write-Host "发布完成: $target"
Write-Host "动态版本: $version  ($files 个文件)"
Write-Host "运行: $target\GalgameManager.exe"
