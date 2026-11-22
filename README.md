<p align="center">
  <img src="GalgameManager/Assets/Pictures/Potato.png" width="80px" alt="PotatoVN" />
</p>

<div align="center">

# PotatoVN (Portable Fork)

[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-blue)](https://github.com/zwm21/PotatoVN/releases)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![Build](https://github.com/zwm21/PotatoVN/actions/workflows/build.yml/badge.svg)](https://github.com/zwm21/PotatoVN/actions/workflows/build.yml)

一个面向 Windows 的 VisualNovel 管理工具。  
本仓库为 [GoldenPotato137/PotatoVN](https://github.com/GoldenPotato137/PotatoVN) 的个人精简便携版 Fork，用于自用留档与分享。

[下载便携版](https://github.com/zwm21/PotatoVN/releases) · [上游项目](https://github.com/GoldenPotato137/PotatoVN)

</div>

## 关于本 Fork

- **原始项目**：PotatoVN 由 [GoldenPotato137](https://github.com/GoldenPotato137/PotatoVN) 及上游贡献者开发；如需官方版本、微软商店安装或完整社区支持，请前往上游仓库。
- **本仓库定位**：专注 **Windows x64 零外部依赖自包含便携版**，移除了 MSIX / 微软商店打包流程与自建服务端 `GalgameManager.Server`，围绕便携包体积压缩与跨目录迁移体验做了深度定制。

## 核心功能

- 自动扫描本地目录并管理游戏库
- 从 [Bangumi](https://bgm.tv/)、[VNDB](https://vndb.org/) 等站点搜刮游戏信息并同步游玩状态
- 记录与统计游玩时间、生成年度报告
- 配合外部网盘工具同步游戏存档
- 支持自动解压游戏压缩包并入库、完整备份与恢复用户数据

## 相对上游的主要改动

### 1. 便携包深度轻量化（~500 MiB → ~123 MiB，7z 约 33 MiB）

| 优化项 | 说明 |
|---|---|
| 关闭 ReadyToRun | 便携发布设 `PublishReadyToRun=false`，避免托管程序集预编译膨胀 |
| 定向 BCL + WinRT 投影裁剪 | 仅在便携 Release 构建下对 BCL 与 `Microsoft.Windows.SDK.NET.dll` 启用 `partial` 裁剪；业务程序集、WinAppSDK 托管投影及核心第三方库保持 `TrimMode=copy`，同时保留 `BuiltInComInteropSupport` |
| `vn_mapper.db` 结构瘦身 | 通过 `GalgameManager/Assets/Data/Slim-VnMapperDb.ps1` 将 `title` 表重建为 `WITHOUT ROWID` 并 `VACUUM INTO`，离线零解压体积减半 |
| 静态数据压缩 | `producers.json`、`vndb-tags-latest.json`、`vndb-tags-translation.json` 改为 `.json.gz` 随包分发，运行期流式解压 |
| 清理冗余运行时与依赖 | 便携布局脚本默认移除 `.winmd`、多余语言目录、AI/ML 栈、Widgets 运行时与外部调试器支撑文件（保留应用 `.pdb` 以维持崩溃堆栈行号）；移除 `CSharpMarkup.WinUI`、`AutoMapper` 及未用依赖 |

### 2. 便携体验与工程改进

- **启动期托管图片路径自动重定位**：移动便携包目录、跨版本升级或换机恢复完整备份后，启动时自动将 `UserData\Images` 下的旧绝对路径重定向到当前位置，避免全库图片失联。
- **确定性构建版本号**：构建期自动结合仓库信息生成便携包版本号与发布目录名，便于区分各次构建。
- **数据备份聚焦**：移除旧版导入导出与自动导出功能，保留完整备份与恢复。

### 3. 移除项

- MSIX / 微软商店打包与侧载签名流水线
- 服务端项目 `GalgameManager.Server` 及其测试项目
- 从未随包发布的 `ExVndb` 死路径
- Crowdin 翻译流水线（改为使用 `GalgameManager/Strings/resw_tool.py` 本地维护）

## 使用方式

> **Windows 10 用户提示**：若图标显示异常，请安装 [Segoe Fluent 图标字体](https://aka.ms/SegoeFluentIcons)。

1. 前往 [Releases](https://github.com/zwm21/PotatoVN/releases) 下载最新的 `PotatoVN-<版本>-x64-portable.7z`。
2. 解压到任意目录，直接运行 `GalgameManager.exe`（无需预先安装 .NET 运行时或 Windows App Runtime）。
3. 所有用户数据与配置均保存在程序同级的 `UserData/` 与 `Settings/` 目录下，可整目录拷贝迁移。

## 本地构建与测试

环境仅需 **.NET 8 SDK**（Windows App SDK 与构建工具通过 NuGet 获取，参考 [WinUI 开发环境文档](https://learn.microsoft.com/zh-cn/windows/apps/windows-app-sdk/set-up-your-development-environment)）。

```powershell
# 还原依赖
dotnet restore GalgameManager.sln

# 构建 Windows x64 自包含便携包（输出到 publish/PotatoVN-<版本>-x64-portable）
pwsh -File GalgameManager/BuildScripts/Publish-Portable.ps1

# 运行离线测试（默认排除需联网的 Phraser 分类）
dotnet test GalgameManager.Test/GalgameManager.Test.csproj

```

推送至 `dev` 或手动触发 `.github/workflows/build.yml` 时，CI 会自动执行相同的便携构建、打包 7z 并发布至 GitHub Releases。

## 本地化维护

界面文案位于 `GalgameManager/Strings/<语言>/Resources.resw`，请勿直接手改 XML，请在 `GalgameManager/Strings/` 目录下通过 `resw_tool.py` 操作：

```bash
python resw_tool.py search "Settings_*"
python resw_tool.py update "NewKey.Text" en-US="English" ja-JP="日本語" zh-CN="中文"
python resw_tool.py delete "ObsoleteKey.Text"
python resw_tool.py validate

```

## 开源协议

本项目沿用上游的 [Apache License 2.0](LICENSE) 开源协议，版权与主要开发工作归属上游项目 [GoldenPotato137/PotatoVN](https://github.com/GoldenPotato137/PotatoVN) 及各位贡献者。
