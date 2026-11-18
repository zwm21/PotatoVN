using System.Text.RegularExpressions;
using GalgameManager.Enums;
using GalgameManager.Models;

namespace GalgameManager.Helpers;

/// <summary>
/// 年度报告的纯统计逻辑：不依赖 DI，可在测试中直接调用
/// </summary>
public static class AnnualReportStats
{
    public const int TagFrequencyMax = 30;
    public static readonly string[] BannedTags = ["PC", "汉化", "GAL", "galgame", "Galgame", "ADV", "R18", "AVG", "游戏", "生肉", "硬盘已存"];
    private static readonly Regex YearPattern = new(@"(19|20)\d{2}(-\d{1,2})?");

    /// <summary>
    /// 决定本次年度报告统计哪一年：优先当前年份；当前年份一条游玩记录都没有时，
    /// 回退到最近一个有游玩记录的年份（避免年初打开报告是一片空白）；完全没有记录时仍用当前年份
    /// </summary>
    public static int ResolveYear(IEnumerable<Galgame> games)
    {
        int currentYear = DateTime.Now.Year;
        HashSet<int> years = [];
        foreach (Galgame game in games)
            foreach (string key in game.PlayedTime.Keys)
            {
                DateTime date = Utils.TryParseDateGuessCulture(key);
                if (date != DateTime.MinValue) years.Add(date.Year);
            }
        return years.Contains(currentYear) ? currentYear : (years.Count > 0 ? years.Max() : currentYear);
    }

    /// <summary>
    /// 按口径统计标签词频，已剔除无意义标签、含年份的标签，并取前 <see cref="TagFrequencyMax"/> 个
    /// </summary>
    public static Dictionary<string, int> CountTags(IEnumerable<Galgame> games, int year, AnnualReportTagScope scope)
    {
        IEnumerable<Galgame> selected = scope switch
        {
            AnnualReportTagScope.AddedInYear => games.Where(game => game.AddTime.Year == year),
            AnnualReportTagScope.PlayedInYear => games.Where(game => HasPlayedTimeInYear(game, year)),
            _ => games,
        };

        Dictionary<string, int> tags = new();
        foreach (Galgame game in selected)
            foreach (string tag in game.Tags.Value ?? [])
            {
                if (string.IsNullOrEmpty(tag)) continue;
                tags[tag] = tags.GetValueOrDefault(tag) + 1;
            }

        // 可以优化成nlogn的，但应该不会造成太大的性能问题（应该不会有几十万个tag吧？）
        return tags
            .Where(pair => !BannedTags.Contains(pair.Key) && !YearPattern.IsMatch(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .Take(TagFrequencyMax)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static bool HasPlayedTimeInYear(Galgame game, int year)
    {
        foreach (string key in game.PlayedTime.Keys)
        {
            DateTime date = Utils.TryParseDateGuessCulture(key);
            if (date != DateTime.MinValue && date.Year == year) return true;
        }
        return false;
    }
}
