<#
.SYNOPSIS
    采集 git 元数据并写入构建期版本资源。

.DESCRIPTION
    资源行格式：base|commits|commit|[delta]
        base    —— 动态版本号：manifest 版本前三段不变、第 4 段叠加构建号（见 Get-DynamicVersion）
        commits —— git rev-list --count HEAD
        commit  —— git rev-parse --short=7 HEAD
        delta   —— 工作区未提交改动（含未跟踪，排除 ignore 与 Build/）的 8 位 SHA256，干净时为空

    构建号 = 自 Package.appxmanifest 最后一次改动以来的提交数：改动基础版本的那次提交构建号为 0，
    之后每提交一次第 4 段 +1（manifest 1.10.3.0 => 1.10.3.0 → 1.10.3.1 → …），
    分发目录名 PotatoVN-1.10.3.<n>-x64-portable 即取自此值。需要完整 git 历史，浅克隆下取不到时保持 0。

    全部输入只来自 git 与 manifest 版本，不含时间戳、随机量，同一份源码的版本可复现。
    无 git（或不是 git 目录）时 base 退化为 manifest 版本本身、写 base|0|| ，
    由运行期据此回退到 +src.<指纹>。
    写出的内容与磁盘上已有文件一致时不重写，避免无谓触发重新编译。

.NOTES
    运行期解析逻辑见 GalgameManager/Helpers/AppVersionInfo.cs。
    兼容 Windows PowerShell 5.1 与 PowerShell 7。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BaseVersion,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$StartDir = (Get-Location).Path,
    [string]$ManifestPath = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# MSIX/便携包构建产物目录，与 GalgameManager.csproj 的 AppxPackageDir 对应
$ExcludedPathPrefix = '^[Bb][Uu][Ii][Ll][Dd][\\/]'

function Get-RepoRoot {
    try {
        $root = & git -C $StartDir rev-parse --show-toplevel 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) { return $null }
        return $root.Trim()
    }
    catch {
        return $null
    }
}

function Get-GitValue {
    param([string[]]$GitArgs)
    try {
        $value = & git @GitArgs 2>$null
        if ($LASTEXITCODE -ne 0) { return $null }
        return (($value | ForEach-Object { $_.ToString().TrimEnd([char]13) }) -join "`n").Trim()
    }
    catch {
        return $null
    }
}

<#
    构建号：自 Package.appxmanifest 最后一次改动以来的提交数。
    基础版本被改动时归零（该次提交自身即为 x.y.z.0），之后每提交一次 +1，
    因此分发目录名 PotatoVN-1.10.3.<n>-x64-portable 会随提交递增。
    需要完整 git 历史；浅克隆下取不到就保持 0。
#>
function Get-BuildNumber {
    param([string]$Root, [string]$Manifest)
    if ([string]::IsNullOrWhiteSpace($Manifest) -or -not (Test-Path $Manifest)) { return 0 }
    try {
        $full = (Resolve-Path $Manifest).Path.Replace('\', '/')
        # git 输出正斜杠、Resolve-Path 输出反斜杠，比较前统一分隔符
        $root = $Root.TrimEnd('/').Replace('\', '/')
        if (-not $full.StartsWith($root + '/', [StringComparison]::OrdinalIgnoreCase)) { return 0 }
        $relative = $full.Substring($root.Length + 1)
        $baseCommit = Get-GitValue -GitArgs @('-C', $Root, 'log', '-1', '--format=%H', '--', $relative)
        if ([string]::IsNullOrWhiteSpace($baseCommit)) { return 0 }
        $count = Get-GitValue -GitArgs @('-C', $Root, 'rev-list', '--count', "$baseCommit..HEAD")
        if ($count -and $count -match '^\d+$') { return [int]$count }
    }
    catch {
        return 0
    }
    return 0
}

<#
    动态版本号：manifest 版本的前三段不变，第 4 段叠加构建号。
    例：manifest 1.10.3.0 + 构建号 1 => 1.10.3.1；manifest 1.10.3.0 + 构建号 0 => 1.10.3.0。
    因此新基础版本写成 x.y.z.0 时，动态版本就是 x.y.z.0、x.y.z.1、x.y.z.2 … 逐个提交递增。
#>
function Get-DynamicVersion {
    param([string]$ManifestVersion, [int]$BuildNumber)
    $parts = $ManifestVersion -split '\.'
    if ($parts.Count -lt 3) { return $ManifestVersion }
    $revision = 0
    if ($parts.Count -ge 4 -and $parts[3] -match '^\d+$') { $revision = [int]$parts[3] }
    return ('{0}.{1}.{2}.{3}' -f $parts[0], $parts[1], $parts[2], ($revision + $BuildNumber))
}

$commitCount = 0
$commitHash = ''
$delta = ''
$buildNumber = 0

$repoRoot = Get-RepoRoot
if ($repoRoot) {
    $countText = Get-GitValue -GitArgs @('-C', $repoRoot, 'rev-list', '--count', 'HEAD')
    if ($countText -and $countText -match '^\d+$') { $commitCount = [int]$countText }

    $hashText = Get-GitValue -GitArgs @('-C', $repoRoot, 'rev-parse', '--short=7', 'HEAD')
    if ($hashText) { $commitHash = $hashText }

    try {
        # --untracked-files=all 把未跟踪文件也列出来；git 默认不列出被 ignore 的文件
        $status = & git -C $repoRoot status --porcelain=v1 --untracked-files=all 2>$null
        if ($LASTEXITCODE -eq 0 -and $status) {
            $lines = @($status | ForEach-Object {
                $line = $_.ToString().TrimEnd([char]13)
                if ($line.Length -eq 0) { return }
                # porcelain 行格式为 "XY <path>"，前 3 列是状态码
                $path = if ($line.Length -gt 3) { $line.Substring(3) } else { $line }
                if ($path -match $ExcludedPathPrefix) { return }
                $line
            })
            if ($lines.Count -gt 0) {
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($lines -join "`n")
                $sha = [System.Security.Cryptography.SHA256]::Create()
                try {
                    $digest = $sha.ComputeHash($bytes)
                }
                finally {
                    $sha.Dispose()
                }
                $delta = ([System.BitConverter]::ToString($digest)).Replace('-', '').ToLowerInvariant().Substring(0, 8)
            }
        }
    }
    catch {
        # 采集不到改动信息时按干净处理，不影响构建
    }

    $buildNumber = Get-BuildNumber -Root $repoRoot -Manifest $ManifestPath
}

# 资源行的 base 用动态版本号（manifest 前三段 + 构建号），日志、UI 与分发目录名都以它为准
$resourceLine = '{0}|{1}|{2}|{3}' -f (Get-DynamicVersion -ManifestVersion $BaseVersion -BuildNumber $buildNumber), $commitCount, $commitHash, $delta

$outputDir = Split-Path -Parent $OutputPath
if ($outputDir -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$newText = $resourceLine + "`r`n"
$oldText = $null
if (Test-Path $OutputPath) { $oldText = [System.IO.File]::ReadAllText($OutputPath) }
if ($oldText -ne $newText) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($OutputPath, $newText, $utf8NoBom)
}

Write-Host "build version: $resourceLine"
