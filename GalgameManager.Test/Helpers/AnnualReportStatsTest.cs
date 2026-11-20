using System.Collections.ObjectModel;
using System.Globalization;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;

namespace GalgameManager.Test.Helpers;

/// <summary>
/// 年度报告统计逻辑测试。统计数据使用固定年份，只有 ResolveYear 的当前年份契约读取系统年份。
/// </summary>
[TestFixture]
public class AnnualReportStatsTest
{
    private static Galgame NewGame(DateTime? addTime = null, IEnumerable<string>? tags = null,
        params string[] playedTimeKeys)
    {
        Galgame game = new();
        game.AddTime = addTime ?? new DateTime(ReportYear, 6, 15);
        game.Tags = new ObservableCollection<string>(tags ?? []);
        foreach (string key in playedTimeKeys)
            game.PlayedTime[key] = 10;
        return game;
    }

    private const int ReportYear = 2020;

    #region ResolveYear

    [Test]
    public void ResolveYear_ShouldUseCurrentYear_WhenCurrentYearHasPlayTime()
    {
        int currentYear = DateTime.Now.Year;
        List<Galgame> games = [NewGame(playedTimeKeys: [$"{currentYear}/3/1", $"{currentYear + 1}/1/1"])];

        Assert.That(AnnualReportStats.ResolveYear(games), Is.InRange(currentYear, DateTime.Now.Year));
    }

    [Test]
    public void ResolveYear_ShouldFallBackToLatestYearWithData_WhenCurrentYearHasNone()
    {
        List<Galgame> games =
        [
            NewGame(playedTimeKeys: ["2024/1/1"]),
            NewGame(playedTimeKeys: ["2023/6/6"]),
            NewGame(playedTimeKeys: ["2024/12/31"]),
        ];

        Assert.That(AnnualReportStats.ResolveYear(games), Is.EqualTo(2024));
    }

    [Test]
    public void ResolveYear_ShouldUseCurrentYear_WhenThereIsNoPlayTimeAtAll()
    {
        int currentYear = DateTime.Now.Year;
        Assert.That(AnnualReportStats.ResolveYear([NewGame()]), Is.InRange(currentYear, DateTime.Now.Year));
        Assert.That(AnnualReportStats.ResolveYear([]), Is.InRange(currentYear, DateTime.Now.Year));
    }

    [Test]
    public void ResolveYear_ShouldIgnoreUnparsableKeys()
    {
        List<Galgame> games = [NewGame(playedTimeKeys: ["not-a-date", "2025/5/5"])];

        Assert.That(AnnualReportStats.ResolveYear(games), Is.EqualTo(2025));
    }

    #endregion

    #region CountTags

    [Test]
    public void CountTags_AddedInYear_ShouldOnlyCountGamesAddedInThatYear()
    {
        List<Galgame> games =
        [
            NewGame(new DateTime(ReportYear, 1, 1), ["tagA", "shared"]),
            NewGame(new DateTime(ReportYear - 1, 1, 1), ["tagB", "shared"]),
        ];

        Dictionary<string, int> result =
            AnnualReportStats.CountTags(games, ReportYear, AnnualReportTagScope.AddedInYear);

        Assert.That(result, Is.EquivalentTo(new Dictionary<string, int> { ["tagA"] = 1, ["shared"] = 1 }));
    }

    [Test]
    public void CountTags_PlayedInYear_ShouldOnlyCountGamesPlayedInThatYear()
    {
        List<Galgame> games =
        [
            NewGame(tags: ["tagA", "shared"], playedTimeKeys: [$"{ReportYear}/2/2"]),
            NewGame(tags: ["tagB", "shared"], playedTimeKeys: ["2000/2/2"]),
        ];

        Dictionary<string, int> result =
            AnnualReportStats.CountTags(games, ReportYear, AnnualReportTagScope.PlayedInYear);

        Assert.That(result, Is.EquivalentTo(new Dictionary<string, int> { ["tagA"] = 1, ["shared"] = 1 }));
    }

    [Test]
    public void CountTags_AllLibrary_ShouldCountEveryGame()
    {
        List<Galgame> games =
        [
            NewGame(new DateTime(ReportYear, 1, 1), ["shared"]),
            NewGame(new DateTime(1999, 1, 1), ["shared", "old"]),
        ];

        Dictionary<string, int> result =
            AnnualReportStats.CountTags(games, ReportYear, AnnualReportTagScope.AllLibrary);

        Assert.That(result, Is.EquivalentTo(new Dictionary<string, int> { ["shared"] = 2, ["old"] = 1 }));
    }

    [Test]
    public void CountTags_ShouldFilterBannedTagsAndYearLikeTags()
    {
        List<Galgame> games = [NewGame(new DateTime(ReportYear, 1, 1), ["PC", "汉化", "Galgame", "2025", "2025-01", "keep"])];

        Dictionary<string, int> result =
            AnnualReportStats.CountTags(games, ReportYear, AnnualReportTagScope.AddedInYear);

        Assert.That(result.Keys, Is.EquivalentTo(new[] { "keep" }));
    }

    [Test]
    public void CountTags_ShouldOrderByFrequencyAndCapAtMax()
    {
        List<Galgame> games = [];
        for (var tagIndex = 34; tagIndex >= 0; tagIndex--)
            for (var occurrence = 0; occurrence < 35 - tagIndex; occurrence++)
                games.Add(NewGame(tags: [$"tag{tagIndex:00}"]));

        Dictionary<string, int> result =
            AnnualReportStats.CountTags(games, ReportYear, AnnualReportTagScope.AddedInYear);

        Assert.That(result.ToArray(), Is.EqualTo(Enumerable.Range(0, 30)
            .Select(index => new KeyValuePair<string, int>($"tag{index:00}", 35 - index))));
    }

    [Test]
    public void CountTags_ShouldReturnEmpty_WhenNoGameMatchesScope()
    {
        List<Galgame> games = [NewGame(new DateTime(1999, 1, 1), ["tagA"])];

        Assert.That(AnnualReportStats.CountTags(games, ReportYear, AnnualReportTagScope.AddedInYear), Is.Empty);
    }

    #endregion

    #region 日期键口径

    [Test]
    public void PlayedTimeKeys_ShouldParseAsYearMonthDay()
    {
        // 年度报告依赖 PlayedTime 的键能被解析回日期；这条是口径的回归保护
        Assert.That(Utils.TryParseDateGuessCulture($"{ReportYear}/11/18"), Is.EqualTo(new DateTime(ReportYear, 11, 18)));
        Assert.That(Utils.TryParseDateGuessCulture("not-a-date"), Is.EqualTo(DateTime.MinValue));
    }

    [Test]
    public void TryParseDateGuessCulture_ShouldNotBeCultureDependent()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.That(Utils.TryParseDateGuessCulture("2026/9/14"), Is.EqualTo(new DateTime(2026, 9, 14)));
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");
            Assert.That(Utils.TryParseDateGuessCulture("2026/9/14"), Is.EqualTo(new DateTime(2026, 9, 14)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    #endregion
}
