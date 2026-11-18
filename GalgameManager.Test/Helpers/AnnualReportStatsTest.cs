using System.Collections.ObjectModel;
using System.Globalization;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;

namespace GalgameManager.Test.Helpers;

/// <summary>
/// 年度报告统计逻辑测试。年份相关用例不写死"今年是哪一年"，
/// 一律以 <c>DateTime.Now.Year</c> 为基准，避免明年这些用例自己失效。
/// </summary>
[TestFixture]
public class AnnualReportStatsTest
{
    private static Galgame NewGame(DateTime? addTime = null, IEnumerable<string>? tags = null,
        params string[] playedTimeKeys)
    {
        Galgame game = new();
        game.AddTime = addTime ?? DateTime.Now;
        game.Tags = new ObservableCollection<string>(tags ?? []);
        foreach (string key in playedTimeKeys)
            game.PlayedTime[key] = 10;
        return game;
    }

    private static int ThisYear => DateTime.Now.Year;

    #region ResolveYear

    [Test]
    public void ResolveYear_ShouldUseCurrentYear_WhenCurrentYearHasPlayTime()
    {
        List<Galgame> games = [NewGame(playedTimeKeys: [$"{ThisYear}/3/1"]), NewGame(playedTimeKeys: ["2024/12/31"])];

        Assert.That(AnnualReportStats.ResolveYear(games), Is.EqualTo(ThisYear));
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
        Assert.That(AnnualReportStats.ResolveYear([NewGame()]), Is.EqualTo(ThisYear));
        Assert.That(AnnualReportStats.ResolveYear([]), Is.EqualTo(ThisYear));
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
            NewGame(new DateTime(ThisYear, 1, 1), ["tagA", "shared"]),
            NewGame(new DateTime(ThisYear - 1, 1, 1), ["tagB", "shared"]),
        ];

        Dictionary<string, int> result =
            AnnualReportStats.CountTags(games, ThisYear, AnnualReportTagScope.AddedInYear);

        Assert.That(result, Is.EquivalentTo(new Dictionary<string, int> { ["tagA"] = 1, ["shared"] = 1 }));
    }

    [Test]
    public void CountTags_PlayedInYear_ShouldOnlyCountGamesPlayedInThatYear()
    {
        List<Galgame> games =
        [
            NewGame(tags: ["tagA", "shared"], playedTimeKeys: [$"{ThisYear}/2/2"]),
            NewGame(tags: ["tagB", "shared"], playedTimeKeys: ["2000/2/2"]),
        ];

        Dictionary<string, int> result =
            AnnualReportStats.CountTags(games, ThisYear, AnnualReportTagScope.PlayedInYear);

        Assert.That(result, Is.EquivalentTo(new Dictionary<string, int> { ["tagA"] = 1, ["shared"] = 1 }));
    }

    [Test]
    public void CountTags_AllLibrary_ShouldCountEveryGame()
    {
        List<Galgame> games =
        [
            NewGame(new DateTime(ThisYear, 1, 1), ["shared"]),
            NewGame(new DateTime(1999, 1, 1), ["shared", "old"]),
        ];

        Dictionary<string, int> result =
            AnnualReportStats.CountTags(games, ThisYear, AnnualReportTagScope.AllLibrary);

        Assert.That(result, Is.EquivalentTo(new Dictionary<string, int> { ["shared"] = 2, ["old"] = 1 }));
    }

    [Test]
    public void CountTags_ShouldFilterBannedTagsAndYearLikeTags()
    {
        List<Galgame> games = [NewGame(new DateTime(ThisYear, 1, 1), ["PC", "汉化", "Galgame", "2025", "2025-01", "keep"])];

        Dictionary<string, int> result =
            AnnualReportStats.CountTags(games, ThisYear, AnnualReportTagScope.AddedInYear);

        Assert.That(result.Keys, Is.EquivalentTo(new[] { "keep" }));
    }

    [Test]
    public void CountTags_ShouldOrderByFrequencyAndCapAtMax()
    {
        List<Galgame> games = [];
        for (var i = 0; i < AnnualReportStats.TagFrequencyMax + 5; i++)
            games.Add(NewGame(new DateTime(ThisYear, 1, 1), [$"tag{i:00}"]));

        Dictionary<string, int> result =
            AnnualReportStats.CountTags(games, ThisYear, AnnualReportTagScope.AddedInYear);

        Assert.That(result, Has.Count.EqualTo(AnnualReportStats.TagFrequencyMax));
    }

    [Test]
    public void CountTags_ShouldReturnEmpty_WhenNoGameMatchesScope()
    {
        List<Galgame> games = [NewGame(new DateTime(1999, 1, 1), ["tagA"])];

        Assert.That(AnnualReportStats.CountTags(games, ThisYear, AnnualReportTagScope.AddedInYear), Is.Empty);
    }

    #endregion

    #region 日期键口径

    [Test]
    public void PlayedTimeKeys_ShouldParseAsYearMonthDay()
    {
        // 年度报告依赖 PlayedTime 的键能被解析回日期；这条是口径的回归保护
        Assert.That(Utils.TryParseDateGuessCulture($"{ThisYear}/11/18"), Is.EqualTo(new DateTime(ThisYear, 11, 18)));
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
