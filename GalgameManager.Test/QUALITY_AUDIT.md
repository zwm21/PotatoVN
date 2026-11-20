# 测试质量审计

> 测试清理阶段已提交为 `79ba99cd`，该提交不含生产代码改动。以下保留该阶段的原始结论；随后获准修复源码的问题见文末“后续源码修复”。

## 范围与原则

- 扫描原有全部 51 个 C# 文件，包括测试基座、共享断言和 E2E 辅助代码。
- 不提交任何生产代码改动；`ProjectTest.cs` 整个文件保持原样。
- `Is.Not.Null` 作为后续精确断言的前置保护保留，不做机械删除。
- 期望值来自既有契约、独立构造的不同源元数据或既有数据集期望，不从本次运行结果生成快照。
- 断言收紧后，以临时生产逻辑变异验证敏感性；每个变异运行后立即按原始内容还原，并校验文件 SHA-256。
- 暴露真实源码问题的临时探针运行后删除，只在此记录问题，不把错误输出写成通过条件。

## 扫描与处置

下表覆盖原有全部文件；同类的精确行为测试不为制造改动而重写。

| 文件/类 | 处置 |
| --- | --- |
| `Contracts/Phrase/GalInfoPhraserTests` | 容差 `Assert.That` 替代 Fail/Pass；补单侧空标题、完全不匹配、全角/空白归一化边界。 |
| `EnvironmentProbeTest` | 缺失资源键必须原样返回，不再仅判非空。 |
| `ProjectTest` | 保留已删功能的反向校验及其他既有校验，文件不动。 |
| `SidebarServiceTest` | 保留设置入口可见性和插件按钮输出断言；Mock 只作为依赖。 |
| `ServiceTestBase`, `TestEnvironmentSetup`, `Usings` | 基座/全局清理不再吞异常，恢复环境变量；全局 using 不动。 |
| `Helpers/AdvancedCollectionViewFilterTest` | 保留既有第三方过滤刷新契约的特征测试；不新增框架自证测试。 |
| `Helpers/AnnualReportStatsTest` | 统计数据固定年份；频次各不相同、输入逆序，精确检查排序、频数和 30 项截断；时钟绑定的 ResolveYear 用调用前后年份处理跨年。 |
| `Helpers/BuildVersionScriptTest` | 临时 Git 仓库归属 fixture 并清理只读对象；只捕获启动程序不可用的 Win32Exception；去掉无 await 的 async。 |
| `Helpers/AppVersionInfoTest`, `Helpers/CustomSortOrderHelperTest` | 保留版本解析/格式/容错、排序去重及不修改输入的精确断言。 |
| `Helpers/DownloadCountToStringConverterTest`, `Helpers/GameToOpacityConverterTest`, `Helpers/UtilTest` | 保留参数化的输出/边界断言，无零校验测试。 |
| `Helpers/DownloadHelperTest`, `Helpers/RepoFlowApiModelsTest` | 保留图片尺寸/像素/透明度和外部数据契约断言；不是仅验证框架能调用。 |
| `Helpers/FolderOperationsTest`, `Helpers/StoragePathHelperTest` | 保留路径/文件/符号链接输出与既有失败断言；清理异常可见。 |
| `Helpers/ProcessExtensionsTest` | 当前进程必须返回真实完整路径；已退出进程必须返回 null，而非仅不抛异常。 |
| `Helpers/KeyMappingMergeHelperTest`, `Helpers/KeyMappingOutputStateTest`, `Helpers/KeyMappingRuntimeSnapshotTest` | 保留映射合并、输出顺序、引用计数和快照隔离的精确断言。 |
| `Helpers/Phraser/MixedPhraserInfoTypeFilterTest` | 构造函数注入内存接口假实现，移出 Phraser；11 字段逐项关闭/单独启用、组合、全关、默认优先级、缺值回落、标签提取均校验明确输出。 |
| `Helpers/Phraser/MixedPhraserTest` | 同样离线并移出 Phraser；校验自定义字段优先级、禁用/-1 源、ID 分派、查询不变、全 null、单源失败及无可用字段来源。 |
| `Helpers/Phraser/MixedPhraserTimeoutTest` | 不再用 50ms/400ms/5s 的执行速度竞赛；用完成/未完成的 TaskCompletionSource 控制来源，finally 释放。仍实际测试生产的秒级软超时。 |
| `Helpers/Phraser/HikarinagiPhraserTest` | 已通过注入 HttpClient/本地处理器离线，字段/请求/排序断言明确，保持默认执行。 |
| `Helpers/Phraser/PhraseHelperTest` | 仅访问随包的本地映射数据库，无联网调用，移出 Phraser；保留原有期望。 |
| `Helpers/Phraser/VndbPhraserTest` | 数字和 v 前缀的 2 个离线用例搬到独立 `VndbPhraserIdTest`；release-ID 及联网用例保留 Phraser，不改变其期望。 |
| `Helpers/Phraser/BangumiPhraserTest` | 联网/令牌依赖保留，等待生产 HTTP 接缝专项。 |
| `Helpers/Phraser/CngalPhraserTest`, `Helpers/Phraser/ParserTestUtil` | 等价替换 if+Fail/尾部 Pass，保留原有期望，不联网收紧断言。 |
| `Helpers/Phraser/YmgalPhraserTest` | 删除不接收结果、仅 Assert.Pass 的重复烟测；同一作品的 22374 用例及其真实 ID 校验保留。 |
| `Models/GalgameTest`, `Models/GalgameInstallationTest` | 保留应用事件绑定、多安装、路径/元数据往返、兼容性和 SDK 契约断言，不新增 getter/setter 测试。 |
| `Models/GetHeaderFromRssTaskTest`, `Models/StorePluginTest` | 保留链接规则、一次性处理策略、安装状态和下载数计算断言。 |
| `Services/BgTaskServiceTest` | 保留并发/去重/恢复/任务生命周期断言；搜索匹配使用确定的不同 UUID，消除随机碰撞前提。 |
| `Services/CategoryServiceTest` | 保留分组/分类、共享引用、持久化、自动分类和变更事件的实质校验。 |
| `Services/FullBackupServiceTest` | 保留归档、回滚、路径逃逸和数据保留断言；过期请求时间固定为 2000 年。 |
| `Services/GalgameCollectionServiceTest` | 全量保存前修改已添加的两个游戏，精确校验整个数据库集合的 UUID、名称与开发商；其余行为断言保留。 |
| `Services/GalgameSourceCollectionServiceTest` | 保留来源修复、移除、路径/条目/配置持久化断言，包括尾空白路径既有失败。 |
| `Services/ZipSourceServiceTest` | 移动校验区分无本地安装、源包不存在和目标冲突的具体错误，而非“任意非空错误都通过”；其余文件/压缩内容校验保留。 |
| `Services/GameLaunchServiceTest`, `Services/SourceMoveTest` | 保留入口选择、参数/启动策略、移动结果和失败时不破坏现有数据的校验。 |
| `Services/PluginApiContractTest`, `Services/PvnServiceTest`, `Services/StaffServiceTest` | 保留 SDK 兼容契约、同步回声抑制和事件驱动的实际协作行为；不是直接调用 Mock 后 Verify 自己。 |
| `Lifecycle/UnpackagedAppSingleInstanceTest` | 保留真实进程单实例、前台恢复及退出校验；清理中的退出竞争捕获不掩盖主断言。 |
| `Upgrade/UnpackagedAppUpgradeUiTest`, `Upgrade/UpgradeTestData` | 保留真实迁移/UI 数据断言及仓库内历史样本；finally 中的进程回收是 best-effort，不吞主路径失败。 |

## 待专项解耦的联网测试

暂不改生产代码，以下测试仍在 `Category("Phraser")`，未通过联网重采输出来收紧期望：

- `BangumiPhraserTest`：游戏搜索/按 ID、staff、开发商图片；还依赖 BGM_TOKEN。
- `VndbPhraserTest`：在线名称搜索、release-ID 解析/封面、staff、角色和中英标签；`GetVndbId_ShouldNormalize_ToVnId("r2166", ...)` 也调用 API，不能与数字/v 前缀分支混为一谈。
- `YmgalPhraserTest`：名称搜索及远端数据。
- `CngalPhraserTest`：名称搜索及远端角色数据。

这些测试需要可注入的 HTTP/API 客户端与固定响应数据；应在生产接缝专项中处理，不放宽默认过滤器或吞异常假装离线。

`AnnualReportStats.ResolveYear` 的“当前年”本身绑定 DateTime.Now；完全冻结时钟需要生产时钟接缝，本次只固定其他统计输入并防跨年竞态。

## 测试清理阶段发现的问题（当时未改生产代码）

临时的 3 个纯内存探针全部失败，证据在 `TestResults/quality-audit/quality-bug-probes.trx`；探针已删除：

1. **混合元数据保留错误的少数来源** — `MixedPhraser.GetGalgameInfo` 的名称一致性分支：Bangumi/VNDB 同名、Hikarinagi 完全异名时，`isSameGame.Any(true)` 分支不移除异名来源，Hikarinagi 的 ID/简介仍进入结果。预期只采用一致游戏的元数据；应核查该分支及不可达的删除分支。
2. **低相似度标签仍变成开发商** — `MixedPhraser.GetDeveloperFromTags` 只在大于 0.75 时提前退出，却无条件返回候选。100 个 x 的非开发商标签被推成 XXX，预期保持默认开发商；不能将 XXX 写进断言。
3. **子搜刮器请求名称与调用者共享对象** — `MixedPhraser.GetGalgameInfo` 复制 IDs，但 `Name = galgame.Name` 共用 LockableProperty。子搜刮器修改 request.Name.Value 会修改调用者名称，预期请求隔离。
4. **ScanAll 为不可扫描的虚拟库排队** — 既有 `ScanAll_UnavailableSource_DoesNotQueueScanTask` 在初始两次基线即失败，独立复跑也失败。初始化夹具保留一个 VirtualSource（IsAvailable=true、IsSourceScanable=false）；`GalgameSourceCollectionService.ScanAll` 只过滤 IsAvailable，仍为该虚拟库创建扫描任务。该源码及测试均保持原样，不能把“不会扫描”改成容忍错误排队。

默认基线在沙箱内外各跑一次，均已有以下 7 个失败；保留原期望，不跳过、不改为 null/截断路径：

- `StoragePathHelperTest.TryResolveExactPathAsync_TrailingWhitespaceFile_ResolvesToRealPath`
- `StoragePathHelperTest.TryResolveExactPathAsync_TrailingWhitespaceFolder_ResolvesToRealPath`
- `GalgameSourceCollectionServiceTest.InitAsync_TruncatedEntryPath_RepairsEntryAndLocalConfig`
- `GalgameSourceCollectionServiceTest.InitAsync_TruncatedLocalFolderPath_RepairsSource`
- `GalgameSourceCollectionServiceTest.ScanAll_UnavailableSource_DoesNotQueueScanTask`
- `UnpackagedAppUpgradeUiTest.UnpackagedApp_ShouldDisplayMigratedData` 的 1.7.2、1.8.0 两个用例：本机缺少 WinApp CLI（不是测试断言 Bug）。

## 验证证据

- 基线：`dotnet test GalgameManager.Test/GalgameManager.Test.csproj`，309 例，302 通过、7 既有失败。
- 定向初验：70 例通过；随后补充边界/离线用例，并执行变异和最终默认全量对照。
- 变异脚本与逐项 TRX/失败断言：`TestResults/quality-audit/run-mutations.ps1`、`mutation-evidence.json`、`mutant-*.trx`。覆盖字段开关、源优先级/缺值、默认值、图片候选、标签提取、IDs/输入隔离、失败/超时、相似度/归一化、本地化回退、进程路径、统计排序/截断和持久化/错误分支。
- 原始测试快照从 HEAD 归档到忽略目录 `TestResults/quality-audit/pristine`，仅修改其中项目引用为当前工作区绝对路径，重新构建与本次测试对照；实际测试源码仍为原始版本。最初一次改后全量曾出现单实例 E2E 的额外退出超时，扫描失败并非新增（完整 TRX 差集为单实例测试）；不修改这两个既有断言来掩盖失败。
- 最终默认全量（无测试过滤器，保留原 runsettings）：`quality-final-default-confirmed.trx`，369 例、362 通过、7 既有失败、0 跳过。与重新构建的原始测试快照（309 例、302 通过、7 失败）逐用例比较失败集合，差集为 0；不是全库已绿。
- 新默认离线覆盖单独运行：`quality-offline-default.trx`，57 例全部通过、0 跳过（字段筛选 28、混合编排 7、本地映射 18、VNDB ID 归一化 4）。另增加 3 个标题相似度边界，使默认总数比原始版本增加 60。
- 45 个独立、有针对性的生产逻辑变异全部被测试捕获，逐项恢复后校验原始文件 SHA-256；生产目录及 `ProjectTest.cs` 的 Git 差异均为 0。`dotnet format whitespace` 用于重写/新增的混合测试和假实现；`git diff --check` 通过。
- 单实例 E2E 曾在第一次改后全量中出现“第二实例 20s 内未退出”的偶发失败；原始快照重建及最终同环境复跑均通过。未增大时限、未增加 Ignore/重试、未放宽断言。该测试仍依赖真实 Windows 进程/激活时序，完全去除启动等待的竞态需要应用就绪信号接缝，留待专项。

## 后续源码修复（2026-11-20）

- `MixedPhraser` 按来源键记录名称一致性，移除不与任何其他来源匹配的元数据及 ID，修复原不可达分支和错误的枚举/列表索引。没有一致来源时仍保留第一来源，不更改原匹配阈值或字段优先级。
- 开发商标签候选限定为相似度大于 0.75 才返回；低相似度标签继续寻找后续标签，全部无匹配则保持默认开发商。
- 游戏信息与封面查询共用隔离请求构造逻辑：名称属性独立，保留原值及锁定状态，不共享调用者对象或事件订阅；IDs 也保持独立副本。封面入口的同类问题由 `bugfix-covers-red.trx` 的两个锁定/未锁定用例先复现。
- `ScanAll`、单库扫描及启动扫描共用可用/可扫描校验。启动入口也曾对启用 `ScanOnStart` 的虚拟库错误排队，已用独立回归先复现（预期 1 个任务、实际 2 个），然后改为复用 `Scan`。
- 新增 16 个离线回归用例，覆盖异名来源位于三个不同位置、无一致来源回退、游戏信息/封面的锁定/未锁定请求隔离、调用者通知隔离、低相似度及后续有效开发商标签、单库/全库/启动扫描条件。`ProjectTest.cs` 仍保持原样，联网适配器及其分类未改。
- 最小复现命令运行结果见 `TestResults/bugfix-audit/bugfix-red.trx`：四项原始问题全部失败；启动扫描另见 `bugfix-startup-red.trx`。原始期望没有改成错误输出。
- 最终定向回归 `bugfix-focused-confirmed.trx`：55/55 通过；新增 16 个用例在无过滤器的默认全量中也全部执行通过。
- 完整重跑的 10 个针对性生产逻辑变异全部被捕获，逐项立即还原并核对 SHA-256；`TestResults/bugfix-audit/run-mutations.ps1`、`mutation-evidence.json` 和 `mutant-*.trx` 保留逐项证据。每次检查新 TRX 的时间戳，不能把构建失败配上旧 TRX 误认成杀死变异。
- 第一轮变异的同名代码保护曾中止自动还原；当即按上下文定点还原并确认哈希一致。该同名代码正是封面入口的另一处浅拷贝；提取共享请求构造后，重新从固定源码完整执行了上述 10 项验证，全部成功还原。
- 最终默认 `dotnet test GalgameManager.Test/GalgameManager.Test.csproj`（只增加日志选项，不改 runsettings、无测试过滤器）：`bugfix-default.trx`，385 例、379 通过、6 既有失败、0 跳过。与修复前 369 例/7 失败逐项比较，新增失败为 0，已消除 `ScanAll_UnavailableSource_DoesNotQueueScanTask`；单实例 E2E 本次通过。
- 剩余失败仍为 4 项尾部空白路径/安装路径修复和缺少 WinApp CLI 的 2 项升级 UI 测试，不属于本次列出的四项修复范围。未放宽、忽略或重试这些用例；`ProjectTest.cs` 无差异，`git diff --check` 通过。
- 默认还原报告现有依赖 `AutoMapper 16.1.1` 的高严重性 `NU1903` 警告（`GHSA-6vhm-j9h7-f9gh`、`GHSA-gpwp-5797-mjff`）；本次没有修改依赖版本，留待安全依赖专项。
