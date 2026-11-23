using GalgameManager.Enums;
using GalgameManager.Models;
using GalgameManager.Models.Sources;

namespace GalgameManager.Helpers;

/// <summary>
/// 「最近游玩」列表的筛选、排序与截断。纯函数、零 IO 依赖（文件系统探测以委托注入），便于单测。<br/>
/// 使用者：托盘右键菜单的「最近游玩」二级子菜单（<see cref="TrayMenuService"/>）。
/// </summary>
public static class RecentGamesHelper
{
    /// <summary>托盘菜单默认展示的最近游玩条目数</summary>
    public const int DefaultMaxCount = 5;

    /// <summary>菜单标题默认最大长度，超出则截断并补省略号</summary>
    public const int DefaultTitleMaxLength = 30;

    /// <summary>
    /// 从全部游戏中筛出「最近玩过且可本地一键启动」的条目，按 <see cref="Galgame.LastPlayTime"/> 降序取前
    /// <paramref name="maxCount"/> 条。<br/>
    /// 「可一键启动」的判定刻意比 <c>GameLaunchService</c> 更严格：要求 exe 与目录都已存在且非 Steam 源，
    /// 这样托盘启动可以直接走到 <c>Process.Start()</c>，不会触发任何需要用户交互的 ContentDialog——
    /// 主窗口在托盘隐藏态时那些对话框用户根本看不见。
    /// </summary>
    /// <param name="galgames">全部游戏</param>
    /// <param name="maxCount">最多返回多少条</param>
    /// <param name="directoryExists">目录存在性探测，默认 <see cref="Directory.Exists(string)"/></param>
    /// <param name="fileExists">文件存在性探测，默认 <see cref="File.Exists(string)"/></param>
    public static List<Galgame> SelectRecentGames(IEnumerable<Galgame> galgames, int maxCount = DefaultMaxCount,
        Func<string, bool>? directoryExists = null, Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(galgames);
        Func<string, bool> dirExists = directoryExists ?? Directory.Exists;
        Func<string, bool> fExists = fileExists ?? File.Exists;

        List<Galgame> candidates = [];
        foreach (Galgame game in galgames)
        {
            if (!IsRecentlyPlayed(game)) continue;
            if (!IsOneClickLaunchable(game, dirExists, fExists)) continue;
            candidates.Add(game);
        }

        candidates.Sort((a, b) => b.LastPlayTime.CompareTo(a.LastPlayTime));

        int take = Math.Max(maxCount, 0);
        if (take >= candidates.Count) return candidates;

        List<Galgame> result = [];
        for (var i = 0; i < take; i++) result.Add(candidates[i]);
        return result;
    }

    /// <summary>
    /// 是否玩过：<see cref="Galgame.LastPlayTime"/> 大于 <see cref="DateTime.MinValue"/>。<br/>
    /// 从未游玩过的游戏不该出现在「最近游玩」里——它的 LastPlayTime 是默认值。
    /// </summary>
    public static bool IsRecentlyPlayed(Galgame game) => game is not null && game.LastPlayTime > DateTime.MinValue;

    /// <summary>
    /// 是否满足「本地 exe 一键启动」：有本地安装实例、目录存在、exe 已配置且存在、且不是 Steam 源。<br/>
    /// Steam 源被排除是因为它的启动链路要查 AppID、可能要用户手动选进程（见
    /// <c>GameLaunchService.LaunchCoreAsync</c>），在托盘隐藏态下那些对话框不可见。
    /// </summary>
    public static bool IsOneClickLaunchable(Galgame game, Func<string, bool>? directoryExists = null,
        Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (!game.IsLocalGame) return false;

        GalgameAndPath? installation = game.PreferredLocalInstallation;
        if (installation is null) return false;
        // 排除 Steam 源：其启动链路依赖 AppID 查询与可能的用户交互
        if (installation.Source?.SourceType == GalgameSourceType.Steam) return false;

        if (string.IsNullOrEmpty(installation.Path)) return false;
        Func<string, bool> dirExists = directoryExists ?? Directory.Exists;
        if (!dirExists(installation.Path)) return false;

        LocalInstallationConfig? config = installation.LocalConfig;
        if (config is null || string.IsNullOrEmpty(config.ExePath)) return false;
        Func<string, bool> fExists = fileExists ?? File.Exists;
        return fExists(config.ExePath);
    }

    /// <summary>
    /// 生成托盘菜单项标题：超长时截断并补 <c>...</c>。<br/>
    /// <see cref="MenuFlyoutItem"/> 没有内建文本截断，过长的游戏名会把二级子菜单撑到屏幕外。
    /// </summary>
    /// <param name="name">游戏名</param>
    /// <param name="maxLength">最大字符数</param>
    public static string FormatMenuTitle(string name, int maxLength = DefaultTitleMaxLength)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        int limit = Math.Max(maxLength, 1);
        if (name.Length <= limit) return name;
        // 截断后补省略号，总长度不超过 limit
        return limit <= 3
            ? name.Substring(0, limit)
            : name.Substring(0, limit - 3) + "...";
    }
}
