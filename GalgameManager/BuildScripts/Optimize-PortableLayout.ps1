#Requires -Version 7
<#
.SYNOPSIS
    收拢自包含便携包根目录：把与运行时加载机制无关的产物移入子目录，并清理冗余语言目录。

.DESCRIPTION
    .NET 8 自包含应用的加载机制决定了哪些文件必须留在应用根目录（= 可执行文件所在目录）：

    * 托管程序集：hostpolicy 组装 TPA（可信平台程序集）列表时，"probe type=app" 只取 deps.json
      资产路径的"文件名"部分，固定拼成 <应用目录>\<文件名>.dll（COREHOST_TRACE 实测），
      因此改写 deps.json 里的路径既无效、也无法把 DLL 放进子目录。
    * 原生 DLL：由 OS 加载器按"可执行文件目录 → System32 → PATH"查找，子目录不在搜索范围内。
    * .pri：MRT 只在应用根目录发现资源映射，移走后 ms-appx:///MainWindow.xaml 直接加载失败
      （XamlParseException）。
    * workloads*.json：onnxruntime / DirectML 执行提供包的清单，跟随 onnxruntime.dll 所在目录解析。
    * .exe / GalgameManager.dll / *.deps.json / *.runtimeconfig.json：宿主与 apphost 的同目录约定。

    可以安全搬走的只有两类：
    * *.winmd：WinRT 元数据仅在 XAML 编译期使用；运行时把全部 .winmd 删掉应用仍能启动并渲染
      （实测），也不存在 Type.GetType / ApiInformation 之类的运行时 winmd 查找。
    * *.pdb：仅调试用，运行期不加载。

    语言目录：每个目录只含 WinUI 内置控件的 *.mui；应用自身三语言文案在 resources.pri 里。
    仅保留应用支持的三种语言，其余目录删除（MRT 会按主语言回退）。

.PARAMETER Target
    发布目录（含 GalgameManager.exe 的那个目录）。

.PARAMETER LibDir
    收拢用的子目录名（相对于 Target），默认 lib。

.PARAMETER MovePatterns
    要从根目录搬走的文件名通配符，默认 *.winmd 与 *.pdb。

.PARAMETER KeepLanguages
    要保留的语言目录名，其余"只含 *.mui"的目录会被删除。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Target,
    [string]$LibDir = 'lib',
    [string[]]$MovePatterns = @('*.winmd', '*.pdb'),
    [string[]]$KeepLanguages = @('zh-CN', 'en-us', 'ja-JP')
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Target)) { throw "目标目录不存在: $Target" }
$Target = (Resolve-Path -LiteralPath $Target).Path
foreach ($required in @('GalgameManager.exe', 'GalgameManager.dll', 'GalgameManager.deps.json', 'GalgameManager.runtimeconfig.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $Target $required))) {
        throw "$Target 不是 PotatoVN 自包含发布目录（缺少 $required）"
    }
}

function Get-RootEntries {
    return @(Get-ChildItem -LiteralPath $Target -Force)
}

$before = Get-RootEntries
$beforeFiles = @($before | Where-Object { -not $_.PSIsContainer })
$beforeDirs = @($before | Where-Object { $_.PSIsContainer })

# ---------- 1. 搬走与加载机制无关的产物 ----------
$libPath = Join-Path $Target $LibDir
$moved = 0
foreach ($pattern in $MovePatterns) {
    foreach ($file in @(Get-ChildItem -LiteralPath $Target -Filter $pattern -File -Force)) {
        if (-not (Test-Path -LiteralPath $libPath)) { New-Item -ItemType Directory -Path $libPath | Out-Null }
        Move-Item -LiteralPath $file.FullName -Destination (Join-Path $libPath $file.Name)
        $moved++
    }
}

# ---------- 2. 清理冗余语言目录 ----------
$removedLangs = 0
foreach ($dir in @(Get-ChildItem -LiteralPath $Target -Directory -Force)) {
    if ($KeepLanguages -contains $dir.Name) { continue }
    # 只删"只含 *.mui"的语言目录：Assets / Microsoft.UI.Xaml / Uno.UI.Toolkit 等含其他文件，天然被排除
    if (@(Get-ChildItem -LiteralPath $dir.FullName -Directory -Force).Count -gt 0) { continue }
    $files = @(Get-ChildItem -LiteralPath $dir.FullName -File -Force)
    if ($files.Count -eq 0) { continue }
    if (@($files | Where-Object { $_.Extension -ne '.mui' }).Count -gt 0) { continue }
    Remove-Item -LiteralPath $dir.FullName -Recurse -Force
    $removedLangs++
}

# ---------- 3. 校验宿主/加载器必需的同目录文件没有被误搬 ----------
foreach ($required in @('GalgameManager.exe', 'GalgameManager.dll', 'GalgameManager.deps.json', 'GalgameManager.runtimeconfig.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $Target $required))) { throw "整理后根目录缺少 $required" }
}

$after = Get-RootEntries
$afterFiles = @($after | Where-Object { -not $_.PSIsContainer })
$afterDirs = @($after | Where-Object { $_.PSIsContainer })
Write-Host ("根目录整理: {0} -> {1} 个条目（文件 {2} -> {3}，目录 {4} -> {5}）" -f `
        $before.Count, $after.Count, $beforeFiles.Count, $afterFiles.Count, $beforeDirs.Count, $afterDirs.Count)
Write-Host ("  搬入 $LibDir/: $moved 个文件；删除语言目录: $removedLangs 个（保留 $($KeepLanguages -join ', ')）")
