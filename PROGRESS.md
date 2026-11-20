# PROGRESS

## 本轮任务：精简 PotatoVN 自包含便携包根目录
分支 `dev`，本轮任务基线 HEAD `4b51c4df`。

### 一、结论：分层可迁移性（x64 实测，COREHOST_TRACE + 实跑验证）

| 层 | 根目录条目数 | 能否搬进子目录 | 依据 |
| --- | --- | --- | --- |
| 托管 DLL（deps.json runtime 条目） | 308 | **不能** | `COREHOST_TRACE=1` 显示 hostpolicy 组装 TPA 时 `probe type=app` 只取资产路径的**文件名**，固定拼成 `<应用目录>\<文件名>.dll`；改写 deps.json 路径（`lib/X.dll`）后 TPA 仍指向根目录，启动即 `Failed to load System.Private.CoreLib.dll (0x80070002)` / `FileNotFoundException: System.Runtime` |
| 原生 DLL | 62 | **不能** | OS 加载器按「可执行文件目录 → System32 → PATH」查找，子目录不在搜索范围；全仓 `[DllImport]` 只调 Windows 系统库，无 `NativeLibrary` 自定义解析 |
| WinRT `.winmd` | 51 | **能** → `lib/` | 只在 XAML 编译期使用：把全部 51 个 `.winmd` 删掉，应用仍能启动并渲染主界面（实测）；再往根目录放一个 4KB 垃圾 `.winmd` 也不报错，说明运行期根本不扫描应用目录的 winmd；仓库内也无 `Type.GetType` / `ApiInformation` 之类运行期 winmd 查找 |
| XAML 与框架资源 | 2 个目录（`Microsoft.UI.Xaml/`、`Uno.UI.Toolkit/`） | 不能 | WinUI / Uno 按相对路径读 `Assets/map.html`、`NoiseAsset_256x256_PNG.png`、`Themes/Generic.xaml(.xbf)` |
| `.pri` | 6 | **不能** | MRT 只在应用根目录发现资源映射。搬走后 `MainWindow..ctor` 抛 `XamlParseException: Cannot locate resource from 'ms-appx:///MainWindow.xaml'`（实测崩溃报告） |
| `.json` | 9 | 部分 | `GalgameManager.deps.json` / `.runtimeconfig.json`（apphost 同目录约定）、`appsettings.json`（内容根 = `AppContext.BaseDirectory`）必须留根目录；`workloads*.json` 6 个是 onnxruntime / DirectML 执行提供包清单，跟随 `onnxruntime.dll` 解析，一并留在根目录 |
| `.pdb` | 3 | **能** → `lib/` | 仅调试用，运行期不加载 |
| `.exe` | 3 | 不能 | `GalgameManager.exe`（入口）、`RestartAgent.exe`（WinAppSDK 重启代理）、`createdump.exe`（运行时崩溃转储） |
| 语言目录 | 83 | 可清理 | 每个只含 2 个 WinUI 内置控件 `.mui`，应用自身三语言文案在 `resources.pri`；保留 `zh-CN` / `en-us` / `ja-JP`，其余删除后 MRT 按主语言回退 |

已试过并**否决**的两条"搬托管 DLL"路线（都有实测证据，详见 `Optimize-PortableLayout.ps1` 注释）：

1. 改写 `deps.json` 的 runtime 资产路径 → 无效（TPA 探测只用文件名）。
2. `runtimeconfig.json` 的 `additionalProbingPaths` → 能注册 `probe type=lookup`（`<probeDir>/<包名>/<版本>/<relpath>`，即 NuGet 磁盘布局），按此布局堆放 307 个 DLL 后应用确实能跑；但该相对路径是**按进程当前工作目录**解析的——用 `Start-Process -WorkingDirectory C:\Windows` 启动立刻起不来。便携包不能依赖 CWD，故不采用。（顺带：`PROBING_DIRECTORIES` 对默认 ALC 无效，把 DLL 搬去 `lib/` 同时从 deps.json 删条目同样起不来。）

### 二、改动文件

- 新增 `GalgameManager/BuildScripts/Optimize-PortableLayout.ps1`：搬 `*.winmd` / `*.pdb` 到 `lib/`，删除只含 `*.mui` 的多余语言目录，并校验宿主必需的同目录文件没被误搬。幂等，非 PotatoVN 发布目录直接报错退出。无架构假设，x64 / x86 / arm64 通用。
- `GalgameManager/BuildScripts/Publish-Portable.ps1`：自包含校验之后调用上面的脚本。
- `.github/workflows/build.yml`：新增 `Slim down publish root` 步骤，调用同一脚本 + 复核 4 个必须留在根目录的文件（未硬编码架构名，`env.PLATFORM` 已有）。
- `.kilocode/rules/project-info-galgamemanager.md` 第 9 节：补记上述加载机制结论（可复用知识）。

### 三、根目录条目数前后对比（x64 发布产物）

| | 条目总数 | 文件 | 目录 |
| --- | --- | --- | --- |
| 整理前 | 531 | 442 | 89 |
| 整理后 | **395** | 388 | 7 |

搬入 `lib/` 54 个文件（51 `.winmd` + 3 `.pdb`），删除 83 个语言目录（释放 166 个 `.mui`），保留目录：`Assets`、`Microsoft.UI.Xaml`、`Uno.UI.Toolkit`、`zh-CN`、`en-us`、`ja-JP`、`lib`。

### 四、验证记录

- 构建：`dotnet publish GalgameManager/GalgameManager.csproj -c Release -p:Platform=x64 -p:PublishProfile=win10-x64 -p:PotatoVnPortable=true -p:PublishDir=...` 成功（动态版本 1.10.3.12，commit 4b51c4d）。注意 `.sln`/`GalgameManager.csproj` 需带 `-p:Platform=x64`。
- 测试：`dotnet test GalgameManager.Test/GalgameManager.Test.csproj` → **通过 306 / 失败 7 / 跳过 0 / 总计 313**，与基线完全一致（7 项既有环境失败：缺 `winapp` CLI 的 `UnpackagedApp_ShouldDisplayMigratedData` 等，按要求未修）。
- 运行：干净临时副本（全新发布产物，不含 `UserData`/`Settings`/`Temp`；数据经 `POTATOVN_LOCALDATA_PATH` / `POTATOVN_TEMP_PATH` 重定向到副本外的临时目录）启动验证：
  - 主界面渲染正常，UIA 树与整理前一致（标题栏、导航项「主页/游戏/游戏库/分类/年度报告/状态/账户/插件/设置」、命令栏「过滤/添加游戏/排序/更多选项」）。
  - 三语言本地化：zh-CN 界面文案（"游戏""过滤""添加游戏"）正常，即 `resources.pri` + `zh-CN/Microsoft.ui.xaml.dll.mui` 加载正常；`en-us`、`ja-JP` 的 `.mui` 原样保留。
  - 导航到「年度报告」并逐个子页（总览/习惯/月度/鉴赏/词云/最爱）全部正常渲染，无异常；图表控件位于 TeachingTip 内且需要游玩数据才会实例化，而本轮改动不移动任何托管/原生程序集，LiveChartsCore 链路与整理前完全一致。
  - `UserData/pvn_data.db`、`Settings/pvn_settings.db`、`UserData/Plugins`、`UserData/data.dataStatus.json` 均正常创建；日志 `UserData/Logs/log<date>.txt` 正常写入版本行，无 ERR 记录。
- 脚本调用路径：以 CI 相同方式（仓库根 + 相对目标路径）调用成功；二次调用幂等；对非发布目录报错退出 1。

### 五、风险与未做事项

- ~~**不要**在本机直接跑 `Publish-Portable.ps1`~~：当时动态版本 1.10.3.12 与 `publish/PotatoVN-1.10.3.12-x64-portable` 同名且内含真实数据，脚本会 `Remove-Item -Recurse` 掉它，所以那轮只用等价的 `dotnet publish` 命令手工发布到临时目录（`publish/_lab/`、`publish/_verify/`，均已清理）。提交后动态版本升到 1.10.3.13，该风险已解除（见下方追加记录）。
- 改动已提交到本地 `dev`（未 push）。
- 实验副本与临时脚本都已删除（按仓库规则移入回收站，未永久删除）；回收站里因此多出约 4.7 GB 的可再生产物，用户可按需清空。

---

## 追加：完整备份导入新版便携包验证（1.10.3.13）

提交 `2542e47d` 后用 `Publish-Portable.ps1` 打出 `publish/PotatoVN-1.10.3.13-x64-portable`
（根目录 395 条目 / 462 文件 / 332 MB）。动态版本升到 1.10.3.13，因此不会再覆盖带真实数据的 1.10.3.12 目录。

把 `publish/PotatoVN_26-11-18_15-47-00.pvnBackup.zip`（168 MB，3617 条目 = UserData 3615 + Settings 2）
导入该包：`.pvnBackup.zip` 走 `FullBackupService` 恢复流程（在 `%TEMP%\PotatoVN_FullBackup.json`
写 `Kind=restore` 标记，`App.OnLaunched` 先于任何数据库打开完成恢复），不需要改代码。

结果（实跑 + UIA 验证）：
- 恢复完整：`UserData` 3615 文件 / 170.6 MB、`Settings` 2 文件；`Temp/ImportBackup` 为空（原目录本就是空的）。
- 主界面/游戏库/年度报告全部正常：游戏列表加载出全部作品，游戏库按来源分组（虚拟库 + 多个本地文件夹），
  zh-CN 界面文案与导航项（主页/游戏/游戏库/分类/年度报告/状态/账户/插件/设置）正常。
- 年度报告有真实数据：过去一年 27.43 小时、6 部作品、最活跃月份 9 月；点开「游玩时长分析」（CartesianChart）
  与「游戏完成度分析」（PieChart）TeachingTip 均正常渲染，进程稳定——LiveChartsCore + SkiaSharp 链路在整理后的
  布局下用真实数据验证通过（此前空数据态图表不会实例化，这一轮补上了）。
- `UserData/Images` 3470 文件 / 167.5 MB（封面图全部就位），`pvn_data.db` 2.28 MB、`ex-vndb.db` 0.67 MB、
  `Settings/pvn_settings.db`；日志只有版本行，无 ERR。
- 旧的 1.10.3.12 包（含真实 UserData/Settings）未被触碰，时间戳仍是 11/18 22:48。

导入后该包体积变为 503 MB / 4079 文件，应用仍在运行（pid 见当次汇报）；分发改包前需删掉包内
`UserData/`、`Settings/`、`Temp/`。

---

## 追加：第一档轻量化——移除两个死依赖（已调研 + 验证，未提交）

`GalgameManager.csproj` 移除 `<PackageReference Include="Lib.Harmony" />` 与
`<PackageReference Include="System.Linq.Async" />`。

### 可移除性证据（全仓检索 + deps.json 依赖图）
- `Lib.Harmony` → `0Harmony.dll`（4.95 MB）：除 csproj 外全仓零引用；不在 `PluginLoadContext`
  的 15 个共享程序集清单里（插件需要会自带）；deps.json 中无任何下游依赖、自身也无传递依赖。
- `System.Linq.Async` → `System.Linq.Async.dll`（2.71 MB）：全仓无 `using System.Linq.Async`，
  独有方法（SelectAwait/WhereAwait/ForEachAsync/ToAsyncEnumerable/MergeAwait/AggregateAwait/
  ToLookupAsync/ToHashSetAsync/ToEnumerable）一个都没用；唯一两个同名方法 `ToListAsync()` /
  `FirstOrDefaultAsync()`（`PhraseHelper.cs:90,167`）接收者是 `AsyncTableQuery<T>`
  （PotatoVnDBMapper 的 `Db` 是 `SQLiteAsyncConnection`，已用 strings 验证），属 sqlite-net-pcl 的 API。
  同样无下游依赖、无传递依赖。
- `Polly` 删不得：`GalFingerPrint.Client` 与 `PotatoVN.Client` 的传递依赖（本次未动）。

### 验证结果
- 构建：`dotnet build GalgameManager.sln -c Release` → 0 错误（首次因上次中断的 XAML 编译留下
  `output.json` 锁 + 缺 `resources.pri`，`dotnet clean` 后通过；与本次改动无关）。**编译通过本身就是
  最硬的证明**——若真有代码用到这两个包，会直接 CS1061/CS0012。
- 测试：306 通过 / 7 失败 / 313 总计，与基线逐项一致。
- 产物：发布输出中两个 DLL 与 deps.json 引用均消失；根目录条目 395 → 393，包体 332.1 → 324.5 MB（-7.7 MB）。
- 运行：干净副本启动正常（主窗口 1532×958、9 导航项、4 命令栏、zh-CN 文案、日志无 ERR）；
  再导入真实备份全数据验证：3615 文件恢复、游戏列表正常、RSS 247 MB、日志只有版本行。

---

## 追加：第二轮轻量化——删除 AI/ML 栈与 .winmd（已调研 + 全页面实测，未提交）

`Optimize-PortableLayout.ps1` 增加两个开关，`Publish-Portable.ps1` 与 `build.yml` 都传
`-DeleteWinmd:$true`（AI/ML 删除默认开启）。

### 重新审视后确定的删除范围

| 范围 | 文件数 | 体积 | 判定依据 |
| --- | --- | --- | --- |
| AI/ML 栈 | 25 | **43.5 MB** | 源码零引用；运行期 18 个 PE 一个都不加载（对比进程已加载模块得出）；来源是 `Microsoft.Windows.AI.MachineLearning 2.1.1` + `Microsoft.WindowsAppSDK.AI 2.1.10` 的可选 workload |
| `lib/*.winmd` | 51 | **2.7 MB** | WinRT 元数据仅 XAML 编译期使用；运行期全删也能跑 |
| 合计 | 76 | **46.2 MB** | 包体 324.5 → 278.3 MB，根目录 393 → 368 条目 |

**关键发现（差点误删）**：`Microsoft.WindowsAppSDK.AI` 包里混着通用的
`Microsoft.Graphics.Imaging.Projection.dll`，`DownloadHelper` 在用 `Windows.Graphics.Imaging`，
所以删除用**显式文件清单**而不是 `Microsoft.Windows.AI.*` 通配符，并已在合成目录测试中确认
`Microsoft.Graphics.Imaging.dll` / `.Projection.dll` / `.winmd` 三个都保留。

### 明确不删的（有影响）
- **诊断件 7.6 MB**（`mscordaccore*`/`mscordbi`/`mscorrc`/`DiaSymReader`/`createdump.exe`）：
  不影响正常功能，但会削弱崩溃转储与调试能力——仓库有专门的 CrashDiagnostics 体系，不碰。
- **`Assets/Data/vn_mapper.db` 11.1 MB**：运行期要被 `PhraseHelper` 读，不是"删掉"而是要改成
  按需下载，属代码改动。
- **未加载的 `System.*` 23 MB（122 个）**：只能靠 `PublishTrimmed` 裁，已排除。
- **R2R 77 MB**：实测关掉后 `GalgameManager.dll` 8.89→3.89 MB、整包 324.5→~247 MB，但换启动速度，需单独决策。

### 验证（两阶段各自全页面实测）
- 阶段1（只删 AI/ML）与阶段2（再删 winmd）都用**导入真实备份**的副本跑：
  9 个导航页 + 年度报告 6 个子页 + 过滤/排序弹出层全部渲染，两阶段的 UIA 元素数**逐个页面完全一致**，
  进程稳定、stderr 0 字节、日志只有版本行。
- 以 CI 相同方式（仓库根 + 相对路径 + `-DeleteWinmd:$true`）调用成功，4 个必须保留的文件复核通过。

---

## 2026-11-19：最新三个提交的独立审查

范围：`4b51c4df...13a6bb0`，即 `2542e47`、`97413d2`、`13a6bb0`。本轮只审查，不修改生产代码或发布产物；按仓库要求补充知识文件和本记录。

### 已验证的目标偏差（Spec）

- 默认参数存在诊断回归：`Optimize-PortableLayout.ps1` 将 PDB 搬入 `lib/`，而 `CrashDiagnostics` 原样记录 `Exception.ToString()`，没有子目录符号解析器。在用户机器无法访问构建机 PDB 路径时，异常栈丢失源码路径和行号。
  - 隔离复现使用现有 `GalgameManager.Core.dll` 和对应 PDB，仅将测试副本的 PE CodeView 路径改成不存在的位置，避免本机 `obj/` 掩盖问题。调用 `DateTimeExtensions.ToDateTime(long.MaxValue)`：PDB 同目录时栈含 `DateTimeExtension.cs:line 19`，仅在 `lib/` 时同一栈没有源码路径和行号。
  - 建议保留同目录 PDB，或实现并验证符号解析后再移动。此影响不等同于业务功能崩溃，但不能声称诊断能力完全不变。
- 自定义 `-LibDir '..'` 未被拒绝。安全的隔离目录实验中，PDB 实际被移出 `Target`；AI/ML 删除分支还会处理该越界目录中的同名文件。实验关闭 AI/ML 删除，没有永久删除文件。建议限制输出目录并校验解析后的绝对路径。
- 自定义 `-MovePatterns '*.pri'` 会移走 `resources.pri`，脚本仍成功通过末尾校验。隔离实验确认根目录资源不存在、`lib/resources.pri` 存在；没有启动该损坏布局。建议限制可搬文件类型并在移动前后校验必需的运行时资源。

后两项仅在自定义参数下触发，当前本地/CI 默认调用不触发。

### 规范审查（Standards）

- `LibDir` 缺少解析后路径边界检查，与 Windows 文件移动安全要求不一致；该项同时是上面的参数契约问题。
- 清理分支直接使用 `Remove-Item` 永久删除，与上级 `E:/github/AGENTS.md` 的回收站要求不一致。发布产物清理如需例外，应明确约定；此项本身不是业务运行回归。

### 验证结果与边界

- 对最新 x64 便携包扫描 353 个根目录 PE 的普通/延迟原生导入，未发现保留文件引用已删除的 AI/ML DLL；对根目录 DLL 的托管程序集引用扫描，也未发现对已删 AI/ML、Harmony、System.Linq.Async 的引用。`Microsoft.Graphics.Imaging` 相关文件仍保留。
- `dotnet test GalgameManager.Test/GalgameManager.Test.csproj --no-restore -c Release` 完成编译。沙箱内初跑为 305 通过 / 8 失败；其中单实例 E2E 在批准的沙箱外复跑通过。
- 沙箱外完整复跑（`--no-build --no-restore -c Release`）：**306 通过 / 7 失败 / 0 跳过 / 313 总计**，数量与前述基线记录一致。剩余失败为 5 个尾部空白路径相关测试和 2 个缺少 `winapp` CLI 的升级 UI 测试；默认排除 Phraser。
- 构建另有既存 NuGet 漏洞/漏洞源访问警告，未修复与审查范围无关的问题。
- 测试日志位于 `GalgameManager.Test/TestResults/latest-three-logs/latest-three/`；隔离复现二进制位于同一 `TestResults` 下的 `latest-three-symbols/`、`latest-three-layout/`，均为忽略的测试产物。
- 本轮没有重跑每个 UI 页面、arm64/x86 实机、外部插件或全部系统语言，因此业务路径暂未发现新回归不代表所有环境下零影响。

---

## 追加：审查回归修复——PDB 回根目录 + 两道参数守卫（未提交）

外部审查在 2542e47 / 97413d2 / 13a6bb0 里发现 3 个问题，逐条核实后全部成立并修复：

1. **PDB 搬进 lib/ 导致崩溃日志丢行号（真回归）**。已用独立小程序复现：PDB 与 DLL 同目录时
   栈为 `at P.Boom() in ...Program.cs:line 3`，搬进子目录后变成 `at P.Boom()`——.NET 的栈跟踪
   符号解析只找程序集同目录的 portable PDB。影响 `Exception.ToString()` 与 `CrashDiagnostics`
   写进日志的崩溃报告。修复：`MovePatterns` 默认改为空，PDB 留在根目录（+3 个条目，体积不变）。
2. **`-LibDir` 可越出发布目录**：传 `..` 会把文件搬到父目录，AI/ML 清理分支还可能删到外面同名文件。
   当前调用方都用默认值所以未触发。修复：解析后断言子目录必须严格位于发布目录内，否则 throw。
3. **坏参数仍报成功**：`-MovePatterns '*.pri'` 会把 resources.pri 搬走且退出码仍为 0（应用已坏）。
   修复：受保护模式（`*.pri`/`*.pdb`/宿主文件）在**搬移前**拒绝；事后再校验根目录 `.pri` 集合
   与 6 个必需文件一个不少（AI/ML 清单里的 Workloads.pri 属故意删除，不参与该校验）。

审查另外提的"删除应进回收站"未采纳：该规则管交互式文件操作，CI 构建步骤不能依赖回收站；
脚本只删发布产物里可再生的文件，从不碰用户数据（已在脚本注释中写明）。

### 验证
- 三条修复各自用合成目录验证：5 种坏参数全部在动文件前被拒且目录保持原样；好参数正常通过。
- 完整发布 + 导入真实备份实跑：9 导航页 + 年报 6 子页 + 过滤/排序弹出层正常。
- **修复前/后对照实验**：1.10.3.15（PDB 在 lib/，修复前）与修复后包跑同一套操作，
  年报子页的点击/选中态/内容变化/文本数量逐项完全一致（33→27→24→30→49→24），证明行为无变化。

---

## 追加：删除 AppCenter 遥测与四个零引用 NuGet 包（未提交）

### 改动内容

**AppCenter 遥测（`Microsoft.AppCenter.Analytics` 5.0.6 + `Microsoft.AppCenter.Crashes` 5.0.2）**
服务已于 2025 年退役，且需上报用户行为数据。删除范围：

- 移入回收站：`Services/AppCenterService.cs`、`Contracts/Services/IAppCenterService.cs`
- `App.xaml.cs`：删 DI 注册
- `Services/ActivationService.cs`：删字段、构造参数、赋值、`StartAsync()` 调用（4 处）
- `Services/InfoService.cs`：删字段、构造参数、赋值、`UploadEvent()` 调用（4 处）
- `ViewModels/SettingsViewModel.cs`：删 `UploadToAppCenter` 属性、构造读取、`OnUploadToAppCenterChanged`（3 处）
- `Enums/KeyValues.cs`：删 `UploadData` 常量
- `Views/SettingsPage.xaml`：删「上传匿名事件日志」设置面板
- 三个语言 `Resources.resw`：经 `resw_tool.py delete` 删 `SettingsPage_Other_AppCenter.Title` / `.Description`
- `GalgameManager.csproj`：删两个 `PackageReference`

未改 `docs/UpdateContent/zh-CN.md` 的历史更新记录（那是历史事实，不应篡改；其中
`PotatoVN.TurnOnAppCenter` 协议链接在代码中本就不存在对应处理器，是既存死链）。

**四个零引用 NuGet 包**

| 包 | 引用审计结论 |
|---|---|
| `CommunityToolkit.WinUI.Controls.Sizers` 8.2.241112-preview1 | 全仓无 `Sizers` 使用（唯一命中是 `KnowledgePicker.WordCloud.Sizers`，属另一个包） |
| `CommunityToolkit.WinUI.Behaviors` 8.1.240916 | 仅 `Views/GalgameSourcePage.xaml:13` 声明 xmlns，无 `behaviors:` 元素使用；连带删掉该 xmlns |
| `System.Memory` 4.6.3 | 代码无 `MemoryMarshal`/`Unsafe.*` 调用，.NET 8 运行时内置 |
| `System.Runtime.CompilerServices.Unsafe` 6.1.2 | 同上 |

注意：`Grep` 工具在本次调查中对 `GalgameManager/Views/*.xaml` 漏报（返回空），
实际用 `grep -rn --include="*.xaml"` 才拿到全部命中。**结论以 bash grep 为准。**

### 验证
- `dotnet build GalgameManager.sln -c Debug`：**0 错误**，10 个警告全部为既存（NU1900 网络、
  AutoMapper 16.1.1 两条高危漏洞、CS1998）。
- `dotnet test GalgameManager.Test`：**306 通过 / 7 失败 / 0 跳过 / 313 总计**，与基线完全一致；
  7 项失败均为既存（5 个尾部空白路径 + 2 个缺 `winapp` CLI 的升级 UI 测试）。
- `Publish-Portable.ps1`：278.3 MB → **276.3 MB**（-2 MB）。
- 目标程序集对比：改动前包含 `Microsoft.AppCenter.dll`、`Microsoft.AppCenter.Analytics.dll`、
  `Microsoft.AppCenter.Crashes.dll`、`CommunityToolkit.WinUI.Controls.Sizers.dll`、
  `CommunityToolkit.WinUI.Behaviors.dll`；改动后**全部消失**。
- 运行时（沙箱 `%TEMP%\PotatoVN_Verify_AppCenterRemoval`，`POTATOVN_PORTABLE=0` +
  `POTATOVN_LOCALDATA_PATH` + `POTATOVN_TEMP_PATH` 重定向，已用完清理）：进程正常启动、
  主窗口 1532×958、9 个导航项、中文资源加载正常、设置页正常打开、关于区显示
  `PotatoVN - v1.10.3.17`。

### 已知遗留
- 三个 `Resources.resw` 各留下 2 个空行（`resw_tool.py` 的删除方式所致）。遵守「不手工编辑
  .resw」的仓库规则未处理，XML 中无害。
- 未验证「其他设置」区块展开后的逐项列表（用 UI 自动化点击展开时占用电脑过久被用户中止）；
  该点已由编译通过 + 设置页正常打开 + resw/XAML/ViewModel 三处同步删除覆盖。
- `HalfFullWidth`（37 KB，1 处调用）与图表栈改直引（3.1 MB）本轮未做，等用户决定。
