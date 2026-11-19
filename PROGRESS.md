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
