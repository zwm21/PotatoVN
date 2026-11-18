<#
.SYNOPSIS
    采集 git 元数据并写入构建期版本资源。

.DESCRIPTION
    资源行格式：base|commits|commit|[delta]
        base    —— Package.appxmanifest 的 Identity Version
        commits —— git rev-list --count HEAD
        commit  —— git rev-parse --short=7 HEAD
        delta   —— 工作区未提交改动（含未跟踪，排除 ignore 与 Build/）的 8 位 SHA256，干净时为空

    全部输入只来自 git 与 base 版本，不含时间戳、随机量，同一份源码的版本可复现。
    无 git（或不是 git 目录）时写 base|0|| ，由运行期据此回退到 +src.<指纹>。
    写出的内容与磁盘上已有文件一致时不重写，避免无谓触发重新编译。

.NOTES
    运行期解析逻辑见 GalgameManager/Helpers/AppVersionInfo.cs。
    兼容 Windows PowerShell 5.1 与 PowerShell 7。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BaseVersion,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$StartDir = (Get-Location).Path
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

$commitCount = 0
$commitHash = ''
$delta = ''

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
}

$resourceLine = '{0}|{1}|{2}|{3}' -f $BaseVersion, $commitCount, $commitHash, $delta

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
