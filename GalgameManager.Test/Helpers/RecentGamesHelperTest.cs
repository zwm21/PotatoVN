using System.Collections.Generic;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Models.Sources;

namespace GalgameManager.Test.Helpers;

/// <summary>
/// <see cref="RecentGamesHelper"/> 测试。覆盖「最近玩过 + 可本地一键启动」的全部筛选规则、
/// 排序、Top N 截断与标题截断。<br/>
/// 文件系统探测一律以委托注入，测试不碰真实磁盘。
/// </summary>
[TestFixture]
public class RecentGamesHelperTest
{
    private const string FakePath = @"D:\games\somegame";
    private const string FakeExe = @"D:\games\somegame\game.exe";
    private const string OtherPath = @"D:\games\othergame";
    private const string OtherExe = @"D:\games\othergame\game.exe";

    /// 全部目录与文件都视为存在（默认桩）</summary>
    private static readonly Func<string, bool> EverythingExists = _ => true;

    private static GalgameAndPath CreateInstallation(Galgame? game = null, string? path = FakePath,
        string? exePath = FakeExe, GalgameSourceBase? source = null)
    {
        // 注意：GalgameAndPath 的构造函数在 IsLocalInstallation 为 true 时就会把 LocalConfig
        // 初始化成非 null，所以这里必须直接赋值而不是 ??=（否则 ExePath 永远设不上）。
        Galgame target = game ?? new Galgame("占位");
        GalgameAndPath entry = new(target, path ?? string.Empty, source ?? new GalgameFolderSource());
        entry.LocalConfig ??= new LocalInstallationConfig();
        entry.LocalConfig.ExePath = exePath;
        return entry;
    }

    private static Galgame CreateGame(string name, DateTime lastPlay, params GalgameAndPath[] entries)
    {
        Galgame game = new(name) { LastPlayTime = lastPlay };
        foreach (GalgameAndPath entry in entries) game.AttachSourceEntry(entry);
        return game;
    }

    [Test]
    public void SelectRecentGames_EmptyInput_ReturnsEmpty()
    {
        Assert.That(RecentGamesHelper.SelectRecentGames([], 5, EverythingExists, EverythingExists), Is.Empty);
    }

    [Test]
    public void SelectRecentGames_OrdersByLastPlayTimeDescending_AndTakesTopN()
    {
        Galgame oldest = CreateGame("最旧", new DateTime(2026, 1, 1), CreateInstallation());
        Galgame middle = CreateGame("中间", new DateTime(2026, 6, 1), CreateInstallation());
        Galgame newest = CreateGame("最新", new DateTime(2026, 11, 1), CreateInstallation());
        Galgame alsoNew = CreateGame("次新", new DateTime(2026, 10, 1), CreateInstallation());

        List<Galgame> result = RecentGamesHelper.SelectRecentGames([oldest, newest, middle, alsoNew],
            3, EverythingExists, EverythingExists);

        Assert.That(result, Is.EqualTo(new[] { newest, alsoNew, middle }));
    }

    [Test]
    public void SelectRecentGames_FiltersOutNeverPlayedGames()
    {
        // LastPlayTime 保持默认值（DateTime.MinValue）的游戏不该出现在「最近游玩」里
        Galgame neverPlayed = CreateGame("从未游玩", DateTime.MinValue, CreateInstallation());
        Galgame played = CreateGame("玩过", new DateTime(2026, 5, 1), CreateInstallation());

        List<Galgame> result = RecentGamesHelper.SelectRecentGames([neverPlayed, played],
            5, EverythingExists, EverythingExists);

        Assert.That(result, Is.EqualTo(new[] { played }));
    }

    [Test]
    public void SelectRecentGames_FiltersOutGamesWithoutLocalInstallation()
    {
        Galgame virtualOnly = CreateGame("纯虚拟", new DateTime(2026, 5, 1));
        Galgame played = CreateGame("有本地", new DateTime(2026, 5, 1), CreateInstallation());

        List<Galgame> result = RecentGamesHelper.SelectRecentGames([virtualOnly, played],
            5, EverythingExists, EverythingExists);

        Assert.That(result, Is.EqualTo(new[] { played }));
    }

    [Test]
    public void SelectRecentGames_FiltersOutSteamSource()
    {
        Galgame steam = CreateGame("Steam游戏", new DateTime(2026, 5, 1),
            CreateInstallation(source: new SteamSource()));
        Galgame local = CreateGame("本地游戏", new DateTime(2026, 5, 1), CreateInstallation());

        List<Galgame> result = RecentGamesHelper.SelectRecentGames([steam, local],
            5, EverythingExists, EverythingExists);

        Assert.That(result, Is.EqualTo(new[] { local }));
    }

    [Test]
    public void SelectRecentGames_FiltersOutMissingDirectory()
    {
        Galgame missingDir = CreateGame("目录没了", new DateTime(2026, 5, 1), CreateInstallation());
        Galgame ok = CreateGame("正常", new DateTime(2026, 5, 1),
            CreateInstallation(path: OtherPath, exePath: OtherExe));

        List<Galgame> result = RecentGamesHelper.SelectRecentGames([missingDir, ok],
            5, p => p != FakePath, EverythingExists);

        Assert.That(result, Is.EqualTo(new[] { ok }));
    }

    [Test]
    public void SelectRecentGames_FiltersOutMissingExe()
    {
        Galgame missingExe = CreateGame("exe没了", new DateTime(2026, 5, 1), CreateInstallation());
        Galgame ok = CreateGame("正常", new DateTime(2026, 5, 1),
            CreateInstallation(path: OtherPath, exePath: OtherExe));

        List<Galgame> result = RecentGamesHelper.SelectRecentGames([missingExe, ok],
            5, EverythingExists, f => f != FakeExe);

        Assert.That(result, Is.EqualTo(new[] { ok }));
    }

    [Test]
    public void SelectRecentGames_FiltersOutGameWithoutExeConfigured()
    {
        Galgame noExe = CreateGame("没配exe", new DateTime(2026, 5, 1), CreateInstallation(exePath: null));
        Galgame ok = CreateGame("正常", new DateTime(2026, 5, 1), CreateInstallation());

        List<Galgame> result = RecentGamesHelper.SelectRecentGames([noExe, ok],
            5, EverythingExists, EverythingExists);

        Assert.That(result, Is.EqualTo(new[] { ok }));
    }

    [Test]
    public void SelectRecentGames_FiltersOutEmptyExePath()
    {
        Galgame emptyExe = CreateGame("exe路径为空", new DateTime(2026, 5, 1), CreateInstallation(exePath: ""));
        Galgame ok = CreateGame("正常", new DateTime(2026, 5, 1), CreateInstallation());

        List<Galgame> result = RecentGamesHelper.SelectRecentGames([emptyExe, ok],
            5, EverythingExists, EverythingExists);

        Assert.That(result, Is.EqualTo(new[] { ok }));
    }

    [Test]
    public void SelectRecentGames_AllFilteredOut_ReturnsEmpty()
    {
        Galgame neverPlayed = CreateGame("没玩过", DateTime.MinValue, CreateInstallation());
        Galgame noExe = CreateGame("没exe", new DateTime(2026, 5, 1), CreateInstallation(exePath: null));

        Assert.That(RecentGamesHelper.SelectRecentGames([neverPlayed, noExe], 5, EverythingExists, EverythingExists),
            Is.Empty);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(5)]
    [TestCase(100)]
    public void SelectRecentGames_MaxCountBoundaries(int maxCount)
    {
        List<Galgame> games = [];
        for (var i = 0; i < 7; i++)
            games.Add(CreateGame($"游戏{i}", new DateTime(2026, 1, 1).AddDays(i), CreateInstallation()));

        List<Galgame> result = RecentGamesHelper.SelectRecentGames(games, maxCount,
            EverythingExists, EverythingExists);

        Assert.That(result.Count, Is.EqualTo(Math.Min(Math.Max(maxCount, 0), games.Count)));
    }

    [Test]
    public void SelectRecentGames_DefaultMaxCount_IsFive()
    {
        List<Galgame> games = [];
        for (var i = 0; i < 8; i++)
            games.Add(CreateGame($"游戏{i}", new DateTime(2026, 1, 1).AddDays(i), CreateInstallation()));

        List<Galgame> result = RecentGamesHelper.SelectRecentGames(games,
            directoryExists: EverythingExists, fileExists: EverythingExists);

        Assert.That(result.Count, Is.EqualTo(RecentGamesHelper.DefaultMaxCount));
    }

    [Test]
    public void IsRecentlyPlayed_MinValue_ReturnsFalse()
    {
        Assert.That(RecentGamesHelper.IsRecentlyPlayed(CreateGame("x", DateTime.MinValue)), Is.False);
    }

    [Test]
    public void IsRecentlyPlayed_RealValue_ReturnsTrue()
    {
        Assert.That(RecentGamesHelper.IsRecentlyPlayed(CreateGame("x", new DateTime(2026, 1, 1))), Is.True);
    }

    [TestCase("短名", "短名")]
    [TestCase("", "")]
    public void FormatMenuTitle_ShortName_Unchanged(string input, string expected)
    {
        Assert.That(RecentGamesHelper.FormatMenuTitle(input), Is.EqualTo(expected));
    }

    [Test]
    public void FormatMenuTitle_ExactlyAtLimit_Unchanged()
    {
        string name = new('x', RecentGamesHelper.DefaultTitleMaxLength);
        Assert.That(RecentGamesHelper.FormatMenuTitle(name), Is.EqualTo(name));
    }

    [Test]
    public void FormatMenuTitle_OverLimit_TruncatesWithEllipsis()
    {
        string name = new('x', RecentGamesHelper.DefaultTitleMaxLength + 10);

        string result = RecentGamesHelper.FormatMenuTitle(name);

        Assert.That(result.Length, Is.EqualTo(RecentGamesHelper.DefaultTitleMaxLength));
        Assert.That(result, Does.EndWith("..."));
        Assert.That(result, Is.EqualTo(new string('x', RecentGamesHelper.DefaultTitleMaxLength - 3) + "..."));
    }

    [Test]
    public void FormatMenuTitle_CustomLimit_Respected()
    {
        string name = new('x', 50);

        string result = RecentGamesHelper.FormatMenuTitle(name, 10);

        Assert.That(result, Is.EqualTo("xxxxxxx..."));
    }

    [Test]
    public void FormatMenuTitle_VerySmallLimit_TruncatesWithoutEllipsis()
    {
        // limit 小于等于 3 时补省略号会把内容全吃掉，改为纯截断
        string name = new('x', 20);

        Assert.That(RecentGamesHelper.FormatMenuTitle(name, 3), Is.EqualTo("xxx"));
        Assert.That(RecentGamesHelper.FormatMenuTitle(name, 1), Is.EqualTo("x"));
    }

    [Test]
    public void SelectRecentGames_UsesPreferredInstallation_NotFirstEntry()
    {
        // 首选实例才是启动目标；首个条目即使可启动也不代表游戏「可一键启动」
        Galgame game = new("多安装") { LastPlayTime = new DateTime(2026, 5, 1) };
        GalgameAndPath first = CreateInstallation(game, path: @"D:\broken", exePath: @"D:\broken\a.exe");
        GalgameAndPath second = CreateInstallation(game, path: FakePath, exePath: FakeExe);
        game.AttachSourceEntry(first);
        game.AttachSourceEntry(second);
        game.SetPreferredInstallation(second);

        List<Galgame> result = RecentGamesHelper.SelectRecentGames([game], 5,
            p => p == FakePath, f => f == FakeExe);

        Assert.That(result, Is.EqualTo(new[] { game }));
    }
}
