#Requires -Version 7
<#
.SYNOPSIS
    收拢自包含便携包根目录：把与运行时加载机制无关的产物移入子目录，并清理冗余语言目录。

    必须留在应用根目录的（前两条是机制限制，后两条是踩过的坑）：
    * 托管程序集：hostpolicy 组装 TPA（可信平台程序集）列表时，"probe type=app" 只取 deps.json
      资产路径的"文件名"部分，固定拼成 <应用目录>\<文件名>.dll（COREHOST_TRACE 实测），
      因此改写 deps.json 里的路径既无效、也无法把 DLL 放进子目录。
    * 原生 DLL：由 OS 加载器按"可执行文件目录 → System32 → PATH"查找，子目录不在搜索范围内。
    * *.pdb：运行时的栈跟踪符号解析只找程序集**同目录**的 portable PDB。搬进子目录后
      Exception.ToString() / CrashDiagnostics 的崩溃日志会丢掉文件名和行号（已实测复现），
      所以 PDB 必须留在根目录——这是诊断能力，不是可选装饰。
    * *.pri：MRT 只在应用根目录发现资源映射，移走后 ms-appx:///MainWindow.xaml 直接加载失败
      （XamlParseException）。
    * workloads*.json / .exe / GalgameManager.dll / *.deps.json / *.runtimeconfig.json：
      执行提供包清单与宿主、apphost 的同目录约定。

    可以处理的只有两类：
    * *.winmd：WinRT 元数据仅在 XAML 编译期使用；运行时把全部 .winmd 删掉应用仍能启动并渲染
      （实测），也不存在 Type.GetType / ApiInformation 之类的运行时 winmd 查找。
      默认搬进 lib/，-DeleteWinmd 可直接删。
    * AI/ML 栈（onnxruntime / DirectML / Microsoft.Windows.AI.* / Microsoft.Windows.Workloads.*）：
      Windows App SDK 的可选 workload，应用源码零引用、运行期一个都不加载，直接删。

    语言目录：每个目录只含 WinUI 内置控件的 *.mui；应用自身三语言文案在 resources.pri 里。
    仅保留应用支持的三种语言，其余目录删除（MRT 会按主语言回退）。

    本脚本只删除**发布产物**里可再生的文件（重新 dotnet publish 就会回来），从不碰用户数据；
    删除用 Remove-Item 而不是回收站——它是 CI 构建步骤，不能依赖交互式 Shell 的回收站。

.PARAMETER Target
    发布目录（含 GalgameManager.exe 的那个目录）。

.PARAMETER LibDir
    收拢用的子目录名（相对于 Target），默认 lib。

.PARAMETER MovePatterns
    要从根目录搬走的文件名通配符，默认 *.winmd 与 *.pdb。

.PARAMETER KeepLanguages
    要保留的语言目录名，其余"只含 *.mui"的目录会被删除。

.PARAMETER RemoveAiMlStack
    删除 Windows App SDK 传递带入的 AI/ML 栈（onnxruntime / DirectML / Microsoft.Windows.AI.* /
    Microsoft.Windows.Workloads.* / workloads*.json）。应用源码零引用、运行期一个都不加载，
    属可选 workload 组件。注意 **不删** Microsoft.Graphics.Imaging.*：那是通用图像投影，
    DownloadHelper 在用它。

.PARAMETER MovePatterns
    额外要从根目录搬走的文件名通配符，**默认为空**。
    不要把 *.pdb 放进来：.NET 运行时的栈跟踪符号解析只找程序集**同目录**的 portable PDB，
    搬走会让 Exception.ToString() / CrashDiagnostics 的崩溃日志丢掉文件名和行号（已实测复现）。
    也不要把 *.pri 放进来：MRT 只在应用根目录发现资源映射，搬走直接 ms-appx 加载失败。

.PARAMETER DeleteWinmd
    把 *.winmd 直接删掉（而不是搬进 lib/）。WinRT 元数据只在 XAML 编译期使用，运行期不需要；
    默认 false。已用导入真实备份的全页面实测确认无影响后才在调用方开启。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Target,
    [string]$LibDir = 'lib',
    [string[]]$MovePatterns = @(),
    [string[]]$KeepLanguages = @('zh-CN', 'en-us', 'ja-JP'),
    [bool]$RemoveAiMlStack = $true,
    [bool]$DeleteWinmd = $false
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

# 子目录必须严格位于发布目录内，否则 -LibDir '..' 之类就能把文件搬出去、甚至删到外面
$targetFull = [System.IO.Path]::GetFullPath($Target)
$libPath = [System.IO.Path]::GetFullPath((Join-Path $Target $LibDir))
if (-not $libPath.StartsWith($targetFull + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "LibDir 解析后越出发布目录: '$LibDir' -> '$libPath'"
}

# 搬移前就拒绝会把受保护文件搬走的通配符：这些文件离开根目录就直接破坏运行期行为
# （.pri -> MRT 找不到资源映射；.pdb -> 栈跟踪丢行号；宿主文件 -> 起不来）
$protectedPatterns = @('*.pri', '*.pdb', 'GalgameManager.exe', 'GalgameManager.dll',
                       'GalgameManager.deps.json', 'GalgameManager.runtimeconfig.json',
                       'resources.pri', 'appsettings.json')
foreach ($pattern in $MovePatterns) {
    foreach ($p in $protectedPatterns) {
        if ($pattern -like $p -or $p -like $pattern) {
            throw "MovePatterns 不允许搬移受保护文件: '$pattern'（命中保护规则 '$p'）"
        }
    }
}

function Get-RootEntries {
    return @(Get-ChildItem -LiteralPath $Target -Force)
}

$before = Get-RootEntries
$beforeFiles = @($before | Where-Object { -not $_.PSIsContainer })
$beforeDirs = @($before | Where-Object { $_.PSIsContainer })
# 记录整理前根目录的 .pri：MRT 只在应用根目录发现资源映射，一个都不能少
$priBefore = @(Get-ChildItem -LiteralPath $Target -Filter '*.pri' -File -Force | ForEach-Object { $_.Name })

# ---------- 1. winmd：搬进 lib/，或按参数直接删 ----------
$movedWinmd = 0
$deletedWinmd = 0
foreach ($file in @(Get-ChildItem -LiteralPath $Target -Filter '*.winmd' -File -Force)) {
    if ($DeleteWinmd) {
        Remove-Item -LiteralPath $file.FullName -Force
        $deletedWinmd++
    }
    else {
        if (-not (Test-Path -LiteralPath $libPath)) { New-Item -ItemType Directory -Path $libPath | Out-Null }
        Move-Item -LiteralPath $file.FullName -Destination (Join-Path $libPath $file.Name)
        $movedWinmd++
    }
}

# ---------- 2. 额外搬移（默认无；PDB 与 .pri 都不该搬，见 MovePatterns 注释） ----------
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

# ---------- 3. 删除 AI/ML 栈 ----------
# 来源：Microsoft.Windows.AI.MachineLearning 2.1.1 与 Microsoft.WindowsAppSDK.AI 2.1.10
# （后者由 Microsoft.WindowsAppSDK 2.1.3 直接依赖）。应用源码零引用，实测运行期 18 个 PE 一个都不加载。
# 刻意用显式清单而不是通配符：Microsoft.WindowsAppSDK.AI 里混着通用的
# Microsoft.Graphics.Imaging.Projection.dll（DownloadHelper 用 Windows.Graphics.Imaging），不能误删。
$aiMlFiles = @(
    'onnxruntime.dll'
    'DirectML.dll'
    'NPUDetect.dll'
    'Microsoft.ML.OnnxRuntime.dll'
    'Microsoft.Windows.AI.MachineLearning.dll'
    'Microsoft.Windows.AI.MachineLearning.Projection.dll'
    'Microsoft.Windows.AI.Imaging.dll'
    'Microsoft.Windows.AI.Imaging.Projection.dll'
    'Microsoft.Windows.AI.Text.dll'
    'Microsoft.Windows.AI.Text.Projection.dll'
    'Microsoft.Windows.AI.ContentSafety.dll'
    'Microsoft.Windows.AI.ContentSafety.Projection.dll'
    'Microsoft.Windows.AI.AICapabilities.dll'
    'Microsoft.Windows.AI.Projection.dll'
    'Microsoft.Windows.AI.Foundation.Projection.dll'
    'Microsoft.Windows.Workloads.dll'
    'Microsoft.Windows.Workloads.Resources.dll'
    'Microsoft.Windows.Workloads.Resources_ec.dll'
    'Microsoft.Windows.Workloads.pri'
    'workloads.json'
    'workloads.365.json'
    'workloads.j32.json'
    'workloads.lnl.json'
    'workloads.qnn.json'
    'workloads.stx.json'
)
$removedAiMl = 0
$removedAiMlBytes = 0L
if ($RemoveAiMlStack) {
    foreach ($name in $aiMlFiles) {
        # 只在发布目录内删：越界的情况已在入口被 $libPath 断言挡掉
        $path = Join-Path $Target $name
        if (Test-Path -LiteralPath $path) {
            $removedAiMlBytes += (Get-Item -LiteralPath $path).Length
            Remove-Item -LiteralPath $path -Force
            $removedAiMl++
        }
    }
}

# ---------- 5. 校验：宿主/加载器必需的同目录文件、以及资源映射，一个都不能少 ----------
foreach ($required in @('GalgameManager.exe', 'GalgameManager.dll', 'GalgameManager.deps.json',
                       'GalgameManager.runtimeconfig.json', 'resources.pri', 'appsettings.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $Target $required))) { throw "整理后根目录缺少 $required" }
}
$priAfter = @(Get-ChildItem -LiteralPath $Target -Filter '*.pri' -File -Force | ForEach-Object { $_.Name })
# AI/ML 清单里的 .pri（Microsoft.Windows.Workloads.pri）是故意删的，不参与校验
$priRequired = @($priBefore | Where-Object { $_ -notin $aiMlFiles })
$priMissing = @($priRequired | Where-Object { $_ -notin $priAfter })
if ($priMissing.Count -gt 0) { throw "整理后根目录缺少资源映射: $($priMissing -join ', ')" }

$after = Get-RootEntries
$afterFiles = @($after | Where-Object { -not $_.PSIsContainer })
$afterDirs = @($after | Where-Object { $_.PSIsContainer })
Write-Host ("根目录整理: {0} -> {1} 个条目（文件 {2} -> {3}，目录 {4} -> {5}）" -f `
        $before.Count, $after.Count, $beforeFiles.Count, $afterFiles.Count, $beforeDirs.Count, $afterDirs.Count)
Write-Host ("  winmd: 搬入 $LibDir/ $movedWinmd 个，删除 $deletedWinmd 个；额外搬移 $moved 个；删除语言目录 $removedLangs 个（保留 $($KeepLanguages -join ', ')）")
if ($RemoveAiMlStack) {
    Write-Host ("  删除 AI/ML 栈: $removedAiMl 个文件 / {0:N1} MB" -f ($removedAiMlBytes/1MB))
}
