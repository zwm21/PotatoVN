using GalgameManager.Helpers;
using GalgameManager.Models;

namespace GalgameManager.Test.Helpers;

/// <summary>
/// 游玩热力图统计逻辑测试。所有"今天"都通过参数注入固定日期，测试 100% 确定性。
/// </summary>
[TestFixture]
public class PlayHeatmapStatsTest
{
    private const int TestYear = 2026;

    /// <summary>固定的"今天"：2026-11-22 是周日，滚动窗口就是 [2025-11-23, 2026-11-22]</summary>
    private static readonly DateTime Today = new(2026, 11, 22);

    private static Galgame NewGame(string name, params (string Key, int Minutes)[] playedTime)
    {
        Galgame game = new();
        game.Name = name;
        foreach ((string key, int minutes) in playedTime)
            game.PlayedTime[key] = minutes;
        return game;
    }

    private static (string Key, int Minutes) Play(string key, int minutes) => (key, minutes);

    #region GetIntensityLevel

    [Test]
    [TestCase(0, 0)]
    [TestCase(-5, 0)]
    [TestCase(1, 1)]
    [TestCase(29, 1)]
    [TestCase(30, 2)]
    [TestCase(89, 2)]
    [TestCase(90, 3)]
    [TestCase(179, 3)]
    [TestCase(180, 4)]
    [TestCase(1440, 4)]
    public void GetIntensityLevel_ShouldMatchFixedThresholds(int minutes, int expected)
    {
        Assert.That(PlayHeatmapStats.GetIntensityLevel(minutes), Is.EqualTo(expected));
    }

    #endregion

    #region GetAvailableYears

    [Test]
    public void GetAvailableYears_ShouldUnionRecordYearsWithTodayYear_Descending()
    {
        List<Galgame> games =
        [
            NewGame("a", Play("2024/3/1", 60)),
            NewGame("b", Play("2026/1/1", 30), Play("2023/12/31", 10)),
        ];

        Assert.That(PlayHeatmapStats.GetAvailableYears(games, Today), Is.EqualTo(new[] { 2026, 2024, 2023 }));
    }

    [Test]
    public void GetAvailableYears_ShouldIgnoreInvalidKeysAndNonPositiveMinutes()
    {
        List<Galgame> games =
        [
            NewGame("a", Play("not-a-date", 60), Play("2025/5/5", 0), Play("2025/5/6", -3)),
        ];

        Assert.That(PlayHeatmapStats.GetAvailableYears(games, Today), Is.EqualTo(new[] { 2026 }));
    }

    [Test]
    public void GetAvailableYears_ShouldReturnOnlyTodayYear_WhenLibraryIsEmpty()
    {
        Assert.That(PlayHeatmapStats.GetAvailableYears([], Today), Is.EqualTo(new[] { 2026 }));
    }

    #endregion

    #region 范围与网格对齐

    [Test]
    public void Build_RollingYear_ShouldSpanTodayMinus364ToToday()
    {
        PlayHeatmapResult result = PlayHeatmapStats.Build([], Today, null);

        Assert.That(result.RangeStart, Is.EqualTo(new DateTime(2025, 11, 23)));
        Assert.That(result.RangeEnd, Is.EqualTo(new DateTime(2026, 11, 22)));
        Assert.That((result.RangeEnd - result.RangeStart).Days, Is.EqualTo(364));
    }

    [Test]
    public void Build_NaturalYear_ShouldSpanJanFirstToDecThirtyFirst()
    {
        PlayHeatmapResult result = PlayHeatmapStats.Build([], Today, 2026);

        Assert.That(result.RangeStart, Is.EqualTo(new DateTime(2026, 1, 1)));
        Assert.That(result.RangeEnd, Is.EqualTo(new DateTime(2026, 12, 31)));
    }

    [Test]
    public void Build_RollingYear_ShouldAlignGridStartToSundayAndGridEndToSaturday()
    {
        // 2025-11-23 本身就是周日，GridStart 无需前移；2026-11-22 是周日，GridEnd 后移到周六 2026-11-28
        PlayHeatmapResult result = PlayHeatmapStats.Build([], Today, null);

        Assert.That(result.GridStart, Is.EqualTo(new DateTime(2025, 11, 23)));
        Assert.That(result.GridStart.DayOfWeek, Is.EqualTo(DayOfWeek.Sunday));
        Assert.That(result.GridEnd, Is.EqualTo(new DateTime(2026, 11, 28)));
        Assert.That(result.GridEnd.DayOfWeek, Is.EqualTo(DayOfWeek.Saturday));
    }

    [Test]
    public void Build_CellCount_ShouldBeSevenTimesColumnCount_ForBothLeapAndCommonYears()
    {
        // 2024 是闰年：366 天；2025 是平年：365 天。两者网格都必须恰好 7 * 列数
        PlayHeatmapResult leap = PlayHeatmapStats.Build([], Today, 2024);
        PlayHeatmapResult common = PlayHeatmapStats.Build([], Today, 2025);

        Assert.That(leap.Cells.Count, Is.EqualTo(7 * leap.ColumnCount));
        Assert.That(common.Cells.Count, Is.EqualTo(7 * common.ColumnCount));
        // 2024-01-01 是周一，向前对齐到 2023-12-31（周日）；2024-12-31 是周二，向后对齐到 2025-01-04（周六）
        Assert.That(leap.GridStart, Is.EqualTo(new DateTime(2023, 12, 31)));
        Assert.That(leap.GridEnd, Is.EqualTo(new DateTime(2025, 1, 4)));
        Assert.That(leap.ColumnCount, Is.EqualTo(53));
        // 2025-01-01 是周三，向前对齐到 2024-12-29（周日）；2025-12-31 也是周三，向后对齐到 2026-01-03（周六）
        Assert.That(common.GridStart, Is.EqualTo(new DateTime(2024, 12, 29)));
        Assert.That(common.GridEnd, Is.EqualTo(new DateTime(2026, 1, 3)));
        Assert.That(common.ColumnCount, Is.EqualTo(53));
    }

    [Test]
    public void Build_OutOfRangePaddingCells_ShouldBeMarkedAndZeroed()
    {
        // 2026-01-01 是周四，网格从 2025-12-28（周日）开始，前 4 格是范围外补位
        PlayHeatmapResult result = PlayHeatmapStats.Build([], Today, 2026);

        for (int row = 0; row < 4; row++)
        {
            PlayHeatmapCell cell = result.Cells[row * result.ColumnCount];
            Assert.That(cell.IsOutOfRange, Is.True, $"row {row} 应为补位格");
            Assert.That(cell.Date, Is.EqualTo(new DateTime(2025, 12, 28).AddDays(row)));
            Assert.That(cell.TotalMinutes, Is.EqualTo(0));
            Assert.That(cell.Level, Is.EqualTo(0));
            Assert.That(cell.Games, Is.Empty);
        }
        // 第 5 行（周四）是范围内第一天
        PlayHeatmapCell firstInRange = result.Cells[4 * result.ColumnCount];
        Assert.That(firstInRange.IsOutOfRange, Is.False);
        Assert.That(firstInRange.Date, Is.EqualTo(new DateTime(2026, 1, 1)));
    }

    [Test]
    public void Build_Cells_ShouldBeRowMajorSundayFirst()
    {
        PlayHeatmapResult result = PlayHeatmapStats.Build([], Today, 2026);

        // 行主序：下标 = row * ColumnCount + col，row 0 = 周日
        // Cells[0] = (周日, 第 0 列) = 网格起始日
        Assert.That(result.Cells[0].Date, Is.EqualTo(result.GridStart));
        Assert.That(result.Cells[0].Date.DayOfWeek, Is.EqualTo(DayOfWeek.Sunday));
        // Cells[1] = (周日, 第 1 列)，比 Cells[0] 晚整整一周，同是周日
        Assert.That(result.Cells[1].Date, Is.EqualTo(result.GridStart.AddDays(7)));
        Assert.That(result.Cells[1].Date.DayOfWeek, Is.EqualTo(DayOfWeek.Sunday));
        // Cells[ColumnCount] = (周一, 第 0 列)，是网格起始日的次日
        Assert.That(result.Cells[result.ColumnCount].Date, Is.EqualTo(result.GridStart.AddDays(1)));
        Assert.That(result.Cells[result.ColumnCount].Date.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
    }

    #endregion

    #region 月份标签

    [Test]
    public void Build_MonthLabels_ShouldStartAtColumnZero_AndAdvanceOnMonthChange()
    {
        PlayHeatmapResult result = PlayHeatmapStats.Build([], Today, 2026);

        Assert.That(result.MonthLabels, Is.Not.Empty);
        Assert.That(result.MonthLabels[0].ColumnIndex, Is.EqualTo(0));
        // 第 0 列首日 2025-12-28 是范围外补位格，不能拿它产出标签；
        // 该列第一个范围内日期是 2026-01-01，所以首标签是 1 月
        Assert.That(result.MonthLabels[0].MonthIndex, Is.EqualTo(1));
        // 列索引严格递增且不重复
        for (int i = 1; i < result.MonthLabels.Count; i++)
            Assert.That(result.MonthLabels[i].ColumnIndex, Is.GreaterThan(result.MonthLabels[i - 1].ColumnIndex));
        Assert.That(result.MonthLabels.Select(m => m.ColumnIndex).Distinct().Count(),
            Is.EqualTo(result.MonthLabels.Count));
    }

    [Test]
    public void Build_MonthLabels_ForNaturalYear_ShouldOnlyCoverOneToTwelve()
    {
        PlayHeatmapResult result = PlayHeatmapStats.Build([], Today, 2026);

        // 自然年网格只应出现 1~12 月，且恰好各一次，不能被范围外的 2025-12 / 2027-01 污染
        Assert.That(result.MonthLabels.Select(m => m.MonthIndex).OrderBy(m => m),
            Is.EqualTo(Enumerable.Range(1, 12)));
    }

    [Test]
    public void Build_MonthLabels_ForRollingYear_ShouldAllowSameMonthAtBothEnds()
    {
        // 滚动窗口 [2025-11-23, 2026-11-22] 横跨两年，首尾同月（11 月）是允许的，且不加 Year 字段
        PlayHeatmapResult result = PlayHeatmapStats.Build([], Today, null);

        Assert.That(result.MonthLabels[0].MonthIndex, Is.EqualTo(11));
        Assert.That(result.MonthLabels[^1].MonthIndex, Is.EqualTo(11));
        Assert.That(result.MonthLabels.Count(m => m.MonthIndex == 11), Is.EqualTo(2));
    }

    [Test]
    public void Build_MonthLabels_ShouldEnforceMinimumColumnGap()
    {
        PlayHeatmapResult result = PlayHeatmapStats.Build([], Today, 2026);

        for (int i = 1; i < result.MonthLabels.Count; i++)
        {
            int gap = result.MonthLabels[i].ColumnIndex - result.MonthLabels[i - 1].ColumnIndex;
            Assert.That(gap, Is.GreaterThanOrEqualTo(PlayHeatmapStats.MinMonthLabelColumnGap),
                $"标签 {result.MonthLabels[i - 1].MonthIndex} 月与 {result.MonthLabels[i].MonthIndex} 月列距仅 {gap}");
        }
    }

    [Test]
    public void Build_MonthLabels_ShouldMatchMonthOfColumnFirstInRangeDay()
    {
        PlayHeatmapResult result = PlayHeatmapStats.Build([], Today, 2026);

        foreach (PlayHeatmapMonthLabel label in result.MonthLabels)
        {
            DateTime columnStart = result.GridStart.AddDays(label.ColumnIndex * PlayHeatmapStats.RowCount);
            // 与实现同口径：取该列第一个落在范围内的日期
            DateTime firstInRange = columnStart < result.RangeStart ? result.RangeStart : columnStart;
            Assert.That(firstInRange, Is.LessThanOrEqualTo(result.RangeEnd));
            Assert.That(firstInRange.Month, Is.EqualTo(label.MonthIndex));
        }
    }

    #endregion

    #region 聚合与脏数据

    [Test]
    public void Build_ShouldAggregateMultipleGamesOnSameDay_AndSortByMinutesDescThenNameAsc()
    {
        List<Galgame> games =
        [
            NewGame("zeta", Play("2026/7/1", 30)),
            NewGame("alpha", Play("2026/7/1", 90)),
            NewGame("beta", Play("2026/7/1", 30)),
        ];
        PlayHeatmapResult result = PlayHeatmapStats.Build(games, Today, 2026);

        PlayHeatmapCell cell = FindCell(result, new DateTime(2026, 7, 1));
        Assert.That(cell.TotalMinutes, Is.EqualTo(150));
        Assert.That(cell.Level, Is.EqualTo(3));
        Assert.That(cell.Games.Select(g => g.GameName),
            Is.EqualTo(new[] { "alpha", "beta", "zeta" })); // 90 / 30 / 30，同分钟按名称升序
        Assert.That(cell.Games[0].Minutes, Is.EqualTo(90));
        Assert.That(cell.Games[1].Minutes, Is.EqualTo(30));
        Assert.That(cell.Games[2].Minutes, Is.EqualTo(30));
    }

    [Test]
    public void Build_ShouldMergeDirtyKeysOfSameGameOnSameDate()
    {
        List<Galgame> games =
        [
            // 同一款游戏同一天的多种历史格式，分钟数应累加
            NewGame("dirty", Play("2026/7/1", 20), Play("2026-07-01", 15), Play("2026/07/01", 5)),
        ];
        PlayHeatmapResult result = PlayHeatmapStats.Build(games, Today, 2026);

        PlayHeatmapCell cell = FindCell(result, new DateTime(2026, 7, 1));
        Assert.That(cell.TotalMinutes, Is.EqualTo(40));
        Assert.That(cell.Games, Has.Count.EqualTo(1));
        Assert.That(cell.Games[0].Minutes, Is.EqualTo(40));
        Assert.That(cell.Games[0].Galgame, Is.SameAs(games[0]));
    }

    [Test]
    public void Build_ShouldIgnoreUnparsableKeysAndNonPositiveMinutes()
    {
        List<Galgame> games =
        [
            NewGame("dirty",
                Play("not-a-date", 60),
                Play("2026/7/1", 0),
                Play("2026/7/2", -10),
                Play("2026/7/3", 45)),
        ];
        PlayHeatmapResult result = PlayHeatmapStats.Build(games, Today, 2026);

        Assert.That(result.TotalMinutes, Is.EqualTo(45));
        Assert.That(result.ActiveDays, Is.EqualTo(1));
        Assert.That(FindCell(result, new DateTime(2026, 7, 3)).TotalMinutes, Is.EqualTo(45));
        Assert.That(FindCell(result, new DateTime(2026, 7, 1)).TotalMinutes, Is.EqualTo(0));
        Assert.That(FindCell(result, new DateTime(2026, 7, 2)).TotalMinutes, Is.EqualTo(0));
    }

    [Test]
    public void Build_ShouldIgnoreRecordsOutsideSelectedYear()
    {
        List<Galgame> games =
        [
            NewGame("a", Play("2025/12/31", 120), Play("2026/1/1", 30), Play("2026/12/31", 30)),
            NewGame("b", Play("2027/1/1", 999)),
        ];
        PlayHeatmapResult result = PlayHeatmapStats.Build(games, Today, 2026);

        Assert.That(result.TotalMinutes, Is.EqualTo(60));
        Assert.That(result.ActiveDays, Is.EqualTo(2));
        // 范围外的格子必须是补位或 0，绝不能被 2025/2027 的数据污染
        Assert.That(FindCell(result, new DateTime(2025, 12, 31)).TotalMinutes, Is.EqualTo(0));
        Assert.That(FindCell(result, new DateTime(2027, 1, 1)).TotalMinutes, Is.EqualTo(0));
    }

    #endregion

    #region 汇总指标

    [Test]
    public void Build_ShouldComputeTotalsAndActiveDays()
    {
        List<Galgame> games =
        [
            NewGame("a", Play("2026/5/1", 60), Play("2026/5/2", 30)),
            NewGame("b", Play("2026/5/2", 90)),
        ];
        PlayHeatmapResult result = PlayHeatmapStats.Build(games, Today, 2026);

        Assert.That(result.TotalMinutes, Is.EqualTo(180));
        Assert.That(result.ActiveDays, Is.EqualTo(2));
        Assert.That(FindCell(result, new DateTime(2026, 5, 2)).TotalMinutes, Is.EqualTo(120));
    }

    [Test]
    public void Build_LongestStreak_ShouldSpanAcrossMonthAndYearBoundary()
    {
        // 2026-12-30 ~ 2027-01-02 连续 4 天，但 2027 超出 2026 年范围，范围内最长连续是 3 天
        List<Galgame> games =
        [
            NewGame("a",
                Play("2026/11/28", 30), Play("2026/11/29", 30), Play("2026/11/30", 30),
                Play("2026/12/30", 30), Play("2026/12/31", 30)),
        ];
        PlayHeatmapResult result = PlayHeatmapStats.Build(games, Today, 2026);

        Assert.That(result.LongestStreak, Is.EqualTo(3));
    }

    [Test]
    public void Build_LongestStreak_ShouldNotCrossRangeStart()
    {
        // 2026-01-01 与 01-02 连续，2025-12-31 有记录但在范围外，不能把连续段接出去
        List<Galgame> games =
        [
            NewGame("a", Play("2025/12/31", 60), Play("2026/1/1", 30), Play("2026/1/2", 30)),
        ];
        PlayHeatmapResult result = PlayHeatmapStats.Build(games, Today, 2026);

        Assert.That(result.LongestStreak, Is.EqualTo(2));
    }

    [Test]
    public void Build_LongestStreak_ShouldBeZero_WhenNoRecords()
    {
        Assert.That(PlayHeatmapStats.Build([], Today, 2026).LongestStreak, Is.EqualTo(0));
    }

    [Test]
    public void Build_CurrentStreak_ShouldCountBackFromToday_WhenTodayHasPlay()
    {
        List<Galgame> games =
        [
            NewGame("a", Play("2026/11/20", 30), Play("2026/11/21", 30), Play("2026/11/22", 30)),
        ];
        PlayHeatmapResult result = PlayHeatmapStats.Build(games, Today, null);

        Assert.That(result.CurrentStreak, Is.EqualTo(3));
    }

    [Test]
    public void Build_CurrentStreak_ShouldCountBackFromYesterday_WhenTodayIsEmpty()
    {
        List<Galgame> games =
        [
            NewGame("a", Play("2026/11/20", 30), Play("2026/11/21", 30)),
        ];
        PlayHeatmapResult result = PlayHeatmapStats.Build(games, Today, null);

        Assert.That(result.CurrentStreak, Is.EqualTo(2));
    }

    [Test]
    public void Build_CurrentStreak_ShouldBeZero_WhenNeitherTodayNorYesterdayHasPlay()
    {
        List<Galgame> games =
        [
            NewGame("a", Play("2026/11/19", 30), Play("2026/11/18", 30)),
        ];
        PlayHeatmapResult result = PlayHeatmapStats.Build(games, Today, null);

        Assert.That(result.CurrentStreak, Is.EqualTo(0));
    }

    [Test]
    public void Build_CurrentStreak_ShouldBeZero_WhenTodayIsOutsideSelectedYear()
    {
        // 查 2025 年时 today 不在范围内，当前连续天数应为 0
        List<Galgame> games =
        [
            NewGame("a", Play("2025/11/22", 30), Play("2025/11/21", 30)),
        ];
        PlayHeatmapResult result = PlayHeatmapStats.Build(games, Today, 2025);

        Assert.That(result.CurrentStreak, Is.EqualTo(0));
    }

    #endregion

    #region 默认选中日期与空态退化

    [Test]
    public void Build_DefaultSelectedDate_ShouldBeLatestDayWithRecords()
    {
        List<Galgame> games =
        [
            NewGame("a", Play("2026/3/10", 30), Play("2026/9/20", 30), Play("2026/7/1", 30)),
        ];
        PlayHeatmapResult result = PlayHeatmapStats.Build(games, Today, 2026);

        Assert.That(result.DefaultSelectedDate, Is.EqualTo(new DateTime(2026, 9, 20)));
    }

    [Test]
    public void Build_ShouldDegradeSafely_WhenLibraryIsEmpty()
    {
        PlayHeatmapResult result = PlayHeatmapStats.Build([], Today, 2026);

        Assert.That(result.TotalMinutes, Is.EqualTo(0));
        Assert.That(result.ActiveDays, Is.EqualTo(0));
        Assert.That(result.LongestStreak, Is.EqualTo(0));
        Assert.That(result.CurrentStreak, Is.EqualTo(0));
        // 全范围无记录时回退到范围结束日，不抛异常
        Assert.That(result.DefaultSelectedDate, Is.EqualTo(result.RangeEnd));
        Assert.That(result.Cells, Is.Not.Empty);
        Assert.That(result.Cells.All(c => c.TotalMinutes == 0 && c.Level == 0 && c.Games.Count == 0), Is.True);
    }

    [Test]
    public void Build_ShouldDegradeSafely_WhenAllMinutesAreZero()
    {
        List<Galgame> games =
        [
            NewGame("a", Play("2026/7/1", 0), Play("2026/7/2", 0)),
        ];
        PlayHeatmapResult result = PlayHeatmapStats.Build(games, Today, 2026);

        Assert.That(result.TotalMinutes, Is.EqualTo(0));
        Assert.That(result.ActiveDays, Is.EqualTo(0));
        Assert.That(result.DefaultSelectedDate, Is.EqualTo(result.RangeEnd));
    }

    [Test]
    public void Build_DefaultSelectedDate_ShouldFallBackToRangeEnd_WhenRollingWindowHasNoRecords()
    {
        PlayHeatmapResult result = PlayHeatmapStats.Build([], Today, null);

        Assert.That(result.DefaultSelectedDate, Is.EqualTo(Today.Date));
    }

    #endregion

    private static PlayHeatmapCell FindCell(PlayHeatmapResult result, DateTime date)
    {
        PlayHeatmapCell? cell = result.Cells.FirstOrDefault(c => c.Date == date.Date);
        Assert.That(cell, Is.Not.Null, $"找不到 {date:yyyy-MM-dd} 对应的格子");
        return cell!;
    }
}
