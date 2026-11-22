using GalgameManager.Models;

namespace GalgameManager.Helpers;

/// <summary>
/// 游玩热力图（GitHub 贡献图风格）的纯统计逻辑：不依赖 DI、不依赖 UI 类型，可在测试中直接调用。<br/>
/// 所有"今天"的概念都通过参数 <paramref name="today"/> 注入，保证测试 100% 确定性。
/// </summary>
public static class PlayHeatmapStats
{
    /// <summary>
    /// 热力图固定 7 行，行索引 0 = 周日 .. 6 = 周六（与 <see cref="DayOfWeek"/> 取值一致）。
    /// </summary>
    public const int RowCount = 7;

    /// <summary>
    /// 相邻月份标签的最小列距。小于该间距时丢弃较新的那个标签，避免顶部文字相互重叠。
    /// </summary>
    public const int MinMonthLabelColumnGap = 2;

    /// <summary>
    /// 把当日总分钟数映射到 0~4 的热力等级。
    /// </summary>
    public static int GetIntensityLevel(int totalMinutes) => totalMinutes switch
    {
        <= 0 => 0,
        < 30 => 1,
        < 90 => 2,
        < 180 => 3,
        _ => 4
    };

    /// <summary>
    /// 列出所有可供选择的统计年份：所有存在有效游玩记录（分钟 &gt; 0）的年份，
    /// 与 <paramref name="today"/> 所在年份的并集，降序排列。
    /// </summary>
    public static IReadOnlyList<int> GetAvailableYears(IEnumerable<Galgame> games, DateTime today)
    {
        HashSet<int> years = [today.Date.Year];
        foreach (Galgame game in games)
            foreach (KeyValuePair<string, int> pair in game.PlayedTime)
            {
                if (pair.Value <= 0) continue;
                DateTime date = Utils.TryParseDateGuessCulture(pair.Key);
                if (date == DateTime.MinValue) continue;
                years.Add(date.Year);
            }
        return years.OrderByDescending(y => y).ToList();
    }

    /// <summary>
    /// 聚合出热力图所需的全部数据。
    /// </summary>
    /// <param name="games">全部游戏</param>
    /// <param name="today">"今天"，用于滚动窗口与当前连续天数；由调用方注入以保证可测试</param>
    /// <param name="year">
    /// null = 滚动一年，范围为 [today.Date.AddDays(-364), today.Date]；
    /// 非 null = 该自然年，范围为 [year-01-01, year-12-31]
    /// </param>
    public static PlayHeatmapResult Build(IEnumerable<Galgame> games, DateTime today, int? year = null)
    {
        DateTime todayDate = today.Date;
        DateTime rangeStart;
        DateTime rangeEnd;
        if (year is null)
        {
            rangeEnd = todayDate;
            rangeStart = todayDate.AddDays(-364);
        }
        else
        {
            rangeStart = new DateTime(year.Value, 1, 1);
            rangeEnd = new DateTime(year.Value, 12, 31);
        }

        // 聚合：日期 -> (游戏 -> 分钟)。
        // 解析失败（DateTime.MinValue）与分钟数 <= 0 的脏数据直接忽略；
        // 同一款游戏同一天若有多个脏 key 指向同一日期，分钟数累加。
        Dictionary<DateTime, Dictionary<Galgame, int>> daily = new();
        foreach (Galgame game in games)
            foreach (KeyValuePair<string, int> pair in game.PlayedTime)
            {
                if (pair.Value <= 0) continue;
                DateTime date = Utils.TryParseDateGuessCulture(pair.Key).Date;
                if (date == DateTime.MinValue) continue;
                if (date < rangeStart || date > rangeEnd) continue;
                if (!daily.TryGetValue(date, out Dictionary<Galgame, int>? gamesOfDay))
                {
                    gamesOfDay = new Dictionary<Galgame, int>();
                    daily[date] = gamesOfDay;
                }
                gamesOfDay[game] = gamesOfDay.GetValueOrDefault(game) + pair.Value;
            }

        // 网格对齐：起始日向前对齐到所在周的周日，结束日向后对齐到所在周的周六，
        // 因此总天数必然是 7 的整数倍。
        DateTime gridStart = rangeStart.AddDays(-(int)rangeStart.DayOfWeek);
        DateTime gridEnd = rangeEnd.AddDays(RowCount - 1 - (int)rangeEnd.DayOfWeek);
        int columnCount = ((gridEnd - gridStart).Days + 1) / RowCount;

        List<PlayHeatmapCell> cells = new(columnCount * RowCount);
        for (int row = 0; row < RowCount; row++)
        {
            for (int col = 0; col < columnCount; col++)
            {
                DateTime date = gridStart.AddDays(col * RowCount + row);
                bool inRange = date >= rangeStart && date <= rangeEnd;
                // daily 里只可能有范围内的日期，且每个日期的合计必然 > 0
                Dictionary<Galgame, int>? gamesOfDay =
                    inRange && daily.TryGetValue(date, out Dictionary<Galgame, int>? found) ? found : null;
                int totalMinutes = gamesOfDay?.Values.Sum() ?? 0;

                List<DailyGamePlayItem> items = [];
                if (gamesOfDay is not null)
                    items.AddRange(gamesOfDay
                        .Select(kv => new DailyGamePlayItem
                        {
                            Galgame = kv.Key,
                            GameName = kv.Key.Name.Value ?? string.Empty,
                            Minutes = kv.Value
                        })
                        // 分钟数降序，同分钟按游戏名升序；用 Ordinal 保证结果不受当前区域性影响
                        .OrderByDescending(i => i.Minutes)
                        .ThenBy(i => i.GameName, StringComparer.Ordinal));

                cells.Add(new PlayHeatmapCell
                {
                    Date = date,
                    TotalMinutes = totalMinutes,
                    Level = GetIntensityLevel(totalMinutes),
                    IsOutOfRange = !inRange,
                    Games = items
                });
            }
        }

        return new PlayHeatmapResult
        {
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
            GridStart = gridStart,
            GridEnd = gridEnd,
            ColumnCount = columnCount,
            Cells = cells,
            MonthLabels = BuildMonthLabels(gridStart, columnCount),
            TotalMinutes = daily.Values.Sum(g => g.Values.Sum()),
            ActiveDays = daily.Count,
            LongestStreak = ComputeLongestStreak(daily),
            CurrentStreak = ComputeCurrentStreak(daily, todayDate),
            DefaultSelectedDate = daily.Count > 0 ? daily.Keys.Max() : rangeEnd
        };
    }

    /// <summary>
    /// 计算顶部月份标签：某一列的首日所在月份与前一列不同时（以及第 0 列）产生一个候选，
    /// 再按 <see cref="MinMonthLabelColumnGap"/> 丢弃过近的候选。
    /// </summary>
    private static List<PlayHeatmapMonthLabel> BuildMonthLabels(DateTime gridStart, int columnCount)
    {
        List<PlayHeatmapMonthLabel> labels = [];
        // 初始化为 -MinMonthLabelColumnGap，保证第 0 列一定可以通过间距检查
        int lastAcceptedColumn = -MinMonthLabelColumnGap;
        for (int col = 0; col < columnCount; col++)
        {
            DateTime firstDayOfColumn = gridStart.AddDays(col * RowCount);
            if (col > 0 && firstDayOfColumn.Month == gridStart.AddDays((col - 1) * RowCount).Month) continue;
            if (col - lastAcceptedColumn < MinMonthLabelColumnGap) continue;
            labels.Add(new PlayHeatmapMonthLabel { MonthIndex = firstDayOfColumn.Month, ColumnIndex = col });
            lastAcceptedColumn = col;
        }
        return labels;
    }

    /// <summary>
    /// 范围内最长连续游玩天数。连续段不能跨越范围边界。
    /// </summary>
    private static int ComputeLongestStreak(Dictionary<DateTime, Dictionary<Galgame, int>> daily)
    {
        if (daily.Count == 0) return 0;
        List<DateTime> sorted = daily.Keys.OrderBy(d => d).ToList();
        int longest = 1;
        int current = 1;
        for (int i = 1; i < sorted.Count; i++)
        {
            if ((sorted[i] - sorted[i - 1]).Days == 1)
            {
                current++;
            }
            else
            {
                longest = Math.Max(longest, current);
                current = 1;
            }
        }
        return Math.Max(longest, current);
    }

    /// <summary>
    /// 以 <paramref name="todayDate"/> 为基准的当前连续游玩天数：
    /// 今天有记录则从今天往前数；今天没有但昨天有，则从昨天往前数；两天都没有则为 0。
    /// </summary>
    private static int ComputeCurrentStreak(Dictionary<DateTime, Dictionary<Galgame, int>> daily, DateTime todayDate)
    {
        if (!daily.ContainsKey(todayDate) && !daily.ContainsKey(todayDate.AddDays(-1))) return 0;
        DateTime cursor = daily.ContainsKey(todayDate) ? todayDate : todayDate.AddDays(-1);
        int streak = 0;
        while (daily.ContainsKey(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }
        return streak;
    }
}

/// <summary>
/// 热力图中一天的游玩数据
/// </summary>
public class PlayHeatmapCell
{
    /// <summary>该格对应的日期</summary>
    public required DateTime Date { get; init; }

    /// <summary>当日总分钟数；补位格（<see cref="IsOutOfRange"/>）恒为 0</summary>
    public int TotalMinutes { get; init; }

    /// <summary>热力等级 0~4，由 <see cref="PlayHeatmapStats.GetIntensityLevel"/> 得出</summary>
    public int Level { get; init; }

    /// <summary>是否为范围外的补位格（网格对齐产生）</summary>
    public bool IsOutOfRange { get; init; }

    /// <summary>
    /// 当日各游戏明细，按 <see cref="DailyGamePlayItem.Minutes"/> 降序、
    /// 同分钟按 <see cref="DailyGamePlayItem.GameName"/> 升序稳定排序
    /// </summary>
    public IReadOnlyList<DailyGamePlayItem> Games { get; init; } = [];
}

/// <summary>
/// 热力图单日明细中的一款游戏
/// </summary>
public class DailyGamePlayItem
{
    /// <summary>游戏对象引用</summary>
    public required Galgame Galgame { get; init; }

    /// <summary>游戏名称，取自 <see cref="Galgame.Name"/></summary>
    public required string GameName { get; init; }

    /// <summary>当日该游戏的分钟数</summary>
    public int Minutes { get; init; }
}

/// <summary>
/// 热力图顶部的月份标签
/// </summary>
public class PlayHeatmapMonthLabel
{
    /// <summary>月份，1~12</summary>
    public int MonthIndex { get; init; }

    /// <summary>该标签所在的列索引（0 起）</summary>
    public int ColumnIndex { get; init; }
}

/// <summary>
/// 一次热力图聚合的结果。<br/>
/// <see cref="Cells"/> 按行主序排列：下标 = row * <see cref="ColumnCount"/> + col，
/// 其中 row 0 = 周日 .. 6 = 周六。
/// </summary>
public class PlayHeatmapResult
{
    /// <summary>统计范围起始日（含）</summary>
    public required DateTime RangeStart { get; init; }

    /// <summary>统计范围结束日（含）</summary>
    public required DateTime RangeEnd { get; init; }

    /// <summary>网格起始日，已向前对齐到 <see cref="RangeStart"/> 所在周的周日</summary>
    public required DateTime GridStart { get; init; }

    /// <summary>网格结束日，已向后对齐到 <see cref="RangeEnd"/> 所在周的周六</summary>
    public required DateTime GridEnd { get; init; }

    /// <summary>列数（周数）</summary>
    public int ColumnCount { get; init; }

    /// <summary>
    /// 全部格子，行主序：下标 = row * <see cref="ColumnCount"/> + col。
    /// 长度为 <see cref="PlayHeatmapStats.RowCount"/> * <see cref="ColumnCount"/>。
    /// </summary>
    public IReadOnlyList<PlayHeatmapCell> Cells { get; init; } = [];

    /// <summary>顶部月份标签，按列索引升序</summary>
    public IReadOnlyList<PlayHeatmapMonthLabel> MonthLabels { get; init; } = [];

    /// <summary>范围内总分钟数</summary>
    public int TotalMinutes { get; init; }

    /// <summary>范围内有游玩记录的天数</summary>
    public int ActiveDays { get; init; }

    /// <summary>范围内最长连续游玩天数</summary>
    public int LongestStreak { get; init; }

    /// <summary>以今天（或昨天）为基准的当前连续游玩天数</summary>
    public int CurrentStreak { get; init; }

    /// <summary>默认选中日期：范围内最近一个有记录的日期；全范围无记录时为 <see cref="RangeEnd"/></summary>
    public required DateTime DefaultSelectedDate { get; init; }
}
