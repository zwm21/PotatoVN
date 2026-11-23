using System.Collections.Generic;
using GalgameManager.Enums;
using GalgameManager.Helpers.Phrase;
using GalgameManager.Models;
using GalgameManager.Services;
using Windows.System;

namespace GalgameManager.Test.Services;

/// <summary>
/// <see cref="LocalSettingsService.TryGetDefaultValue{T}"/> 的参数化回归测试。<br/>
/// 该方法是一个 80+ case 的 switch，每个 case 都写成 <c>(T?)(object?)value</c> 的硬转型：
/// <b>一旦调用方的 T 与 value 的运行时类型不一致，就会抛 InvalidCastException</b>，
/// 而这类错误只在「有人新增/修改设置项却忘了对齐类型」时才暴露，且发生在运行时读设置的路上，
/// 很难在开发期发现。这里把每个 key 与它正确的 T 显式钉住。<br/>
/// 之所以能直接测这个私有逻辑：真实的 LocalSettingsService 构造函数会触碰
/// App.OnAppClosing，而 App 的静态构造在无 WinAppSDK bootstrap 的测试进程中会失败，
/// 无法在测试里实例化，故该方法以 internal static 暴露（见 GalgameManager.csproj 的
/// InternalsVisibleTo）。
/// </summary>
[TestFixture]
public class LocalSettingsServiceTest
{
    /// <summary>
    /// 每个用例：设置 key + 期望默认值 + 以「正确 T」调用 TryGetDefaultValue 的委托。<br/>
    /// 委托的存在是为了在编译期就把 T 钉死——若有人改了 switch 里 value 的类型，
    /// 对应的委托会先在编译期报错，而不是等到运行时才抛 InvalidCastException。
    /// </summary>
    private static IEnumerable<TestCaseData> DefaultCases()
    {
        // ---- 枚举 ----
        yield return Case(KeyValues.RssType, RssType.Mixed, LocalSettingsService.TryGetDefaultValue<RssType>);
        yield return Case(KeyValues.PrimarySortKey, SortKeys.LastPlay, LocalSettingsService.TryGetDefaultValue<SortKeys>);
        yield return Case(KeyValues.SecondarySortKey, SortKeys.Name, LocalSettingsService.TryGetDefaultValue<SortKeys>);
        yield return Case(KeyValues.LibrarySortKey, SortKeys.LastPlay, LocalSettingsService.TryGetDefaultValue<SortKeys>);
        yield return Case(KeyValues.LibraryFolderSortKey, SortKeys.Name, LocalSettingsService.TryGetDefaultValue<SortKeys>);
        yield return Case(KeyValues.PlayingWindowMode, WindowMode.Minimize,
            LocalSettingsService.TryGetDefaultValue<WindowMode>);
        yield return Case(KeyValues.DefaultGameName, DisplayName.Name,
            LocalSettingsService.TryGetDefaultValue<DisplayName>);
        yield return Case(KeyValues.GalgamePagePrimaryTitleType, DisplayName.ChineseName,
            LocalSettingsService.TryGetDefaultValue<DisplayName>);
        yield return Case(KeyValues.GalgamePageSecondaryTitleType, DisplayName.OriginalName,
            LocalSettingsService.TryGetDefaultValue<DisplayName>);

        // ---- 数组 / 集合 ----
        yield return Case(KeyValues.SortKeys, new[] { SortKeys.LastPlay, SortKeys.Developer },
            LocalSettingsService.TryGetDefaultValue<SortKeys[]>);
        yield return Case(KeyValues.SortKeysAscending, new[] { false, false },
            LocalSettingsService.TryGetDefaultValue<bool[]>);
        yield return Case(KeyValues.MagpieHotkeys,
            new List<int>([(int)VirtualKey.LeftWindows, (int)VirtualKey.Shift, (int)VirtualKey.A]),
            LocalSettingsService.TryGetDefaultValue<List<int>>);
        yield return Case(KeyValues.CustomTextFileExtensions,
            new List<string> { ".doc", ".docx", ".pdf", ".txt", ".md" },
            LocalSettingsService.TryGetDefaultValue<List<string>>);

        // ---- 整数 ----
        yield return Case(KeyValues.SearchChildFolderDepth, 1,
            LocalSettingsService.TryGetDefaultValue<int>);
        yield return Case(KeyValues.MinPlayTimeRecordThreshold, 5,
            LocalSettingsService.TryGetDefaultValue<int>);
        yield return Case(KeyValues.MixedPhraserTimeout, 12,
            LocalSettingsService.TryGetDefaultValue<int>);

        // ---- 字符串 ----
        yield return Case(KeyValues.RegexPattern, ".+",
            LocalSettingsService.TryGetDefaultValue<string>);
        yield return Case(KeyValues.GameFolderMustContain, ".exe",
            LocalSettingsService.TryGetDefaultValue<string>);
        yield return Case(KeyValues.GameFolderShouldContain,
            ".xp3\n.arc\n.dat\n.ini\n.dll\n.txt\n.pac\n.noa\n.sh\n.bin\n.pck",
            LocalSettingsService.TryGetDefaultValue<string>);

        // ---- 布尔：默认 true ----
        foreach (string key in new[]
                 {
                     KeyValues.PrimarySortDescending,
                     KeyValues.LibraryGameSortDescending,
                     KeyValues.SearchChildFolder,
                     KeyValues.FixHorizontalPicture,
                     KeyValues.AutoCategory,
                     KeyValues.DownloadCharacters,
                     KeyValues.UsePvnBanner,
                     KeyValues.DownloadStaffs,
                     KeyValues.OverrideLocalNameWithChinese,
                     KeyValues.MemoryImprove,
                     KeyValues.NotifyWhenGetGalgameInFolder,
                     KeyValues.NotifyWhenUnpackGame,
                     KeyValues.NotifyPlayTimeSummary,
                     KeyValues.EventPvnSyncNotify,
                     KeyValues.DisplayVirtualGame,
                     KeyValues.SpecialDisplayVirtualGame,
                     KeyValues.HomeFilterShowPlayStatusAndSourcePanel,
                     KeyValues.HomeFilterShowDeveloperPanel,
                     KeyValues.HomeFilterShowTagPanel,
                     KeyValues.LibraryNavBar,
                     KeyValues.LibraryStatistics,
                     KeyValues.SyncGameCharacters,
                     KeyValues.SyncStaff,
                     KeyValues.SyncHeaderImage,
                     KeyValues.GalgamePageNewLayout_ShowPainter,
                     KeyValues.GalgamePageNewLayout_ShowSeiyu,
                     KeyValues.GalgamePageNewLayout_ShowWriter,
                     KeyValues.GalgamePageNewLayout_ShowMusician,
                     KeyValues.GalgamePageNewLayout_ShowHeaderImage,
                     KeyValues.GalgamePageNewLayout_CoverImage,
                     KeyValues.GalgamePageNewLayout_ShowCoverWhenNoBackground,
                     KeyValues.GalgameSourcePageShowSubSourceGames,
                     KeyValues.ShowGameNameInControl,
                     KeyValues.GalgamePageNewLayout_ShowExpectedPlayTime,
                     KeyValues.GalgamePageNewLayout_ShowRating,
                     KeyValues.GalgamePageNewLayout_ShowTags,
                     KeyValues.GalgamePageNewLayout_ShowCharacters,
                     KeyValues.VndbTranslateTags,
                     KeyValues.VndbCensorTags,
                     KeyValues.VndbRemoveSpoilerTags,
                 })
        {
            yield return Case(key, true, LocalSettingsService.TryGetDefaultValue<bool>);
        }

        // ---- 布尔：默认 false（唯一一个，单独列出以免被上面批量 true 的循环掩盖）----
        yield return Case(KeyValues.SaveBackupMetadata, false, LocalSettingsService.TryGetDefaultValue<bool>);
    }

    private static TestCaseData Case<T>(string key, T expected, Func<string, T?> call) =>
        new(key, expected, (Func<string, object?>)(k => call(k)));

    [Test, TestCaseSource(nameof(DefaultCases))]
    public void TryGetDefaultValue_KnownKey_ReturnsExpectedValueOfExpectedType(
        string key, object expected, Func<string, object?> call)
    {
        // 不抛 InvalidCastException 本身就是本次测试的核心价值：
        // 那正是「T 与 value 类型不一致」时的失败模式。
        object? actual = call(key);

        Assert.That(actual, Is.Not.Null, $"key {key} 的默认值不应为 null");
        Assert.That(actual, Is.EqualTo(expected), $"key {key} 的默认值不符");
    }

    [Test]
    public void TryGetDefaultValue_UnknownKey_ReturnsDefault()
    {
        Assert.That(LocalSettingsService.TryGetDefaultValue<bool>("DefinitelyNotARealKey_12345"), Is.False);
        Assert.That(LocalSettingsService.TryGetDefaultValue<string>("DefinitelyNotARealKey_12345"), Is.Null);
        Assert.That(LocalSettingsService.TryGetDefaultValue<SortKeys>("DefinitelyNotARealKey_12345"),
            Is.EqualTo(default(SortKeys)));
    }

    /// <summary>
    /// RemoteFolder 的默认值依赖 OneDrive 环境变量，属于唯一一个「跑不同机器结果不同」的 case。
    /// 这里显式清掉相关环境变量，钉住「无 OneDrive 时默认值为 null」这一半行为；
    /// 另一半（有 OneDrive 时的路径拼接）依赖真实文件系统，留给手工验证。
    /// </summary>
    [Test]
    public void TryGetDefaultValue_RemoteFolder_WithoutOneDrive_ReturnsNull()
    {
        string? originalConsumer = Environment.GetEnvironmentVariable("OneDriveConsumer");
        string? original = Environment.GetEnvironmentVariable("OneDrive");
        try
        {
            Environment.SetEnvironmentVariable("OneDriveConsumer", null);
            Environment.SetEnvironmentVariable("OneDrive", null);

            Assert.That(LocalSettingsService.TryGetDefaultValue<string>(KeyValues.RemoteFolder), Is.Null);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OneDriveConsumer", originalConsumer);
            Environment.SetEnvironmentVariable("OneDrive", original);
        }
    }

    /// <summary>
    /// MixedPhraserOrder 的默认值需要从 DI 取 ILocalSettingsService 读语言设置，
    /// 测试进程里没有宿主，因此这里只钉住「它确实走 DI」这一契约：
    /// 调用应因取不到服务而抛异常，而不是静默返回一个错误类型的默认值。<br/>
    /// 用 Assert.Catch 而非 Assert.Throws&lt;Exception&gt;：无 WinAppSDK bootstrap 时
    /// App.GetService 实际抛的是 COMException(REGDB_E_CLASSNOTREG)，
    /// Assert.Throws 要求精确类型匹配，这里关心的是「会抛」而不是「抛哪种」。
    /// </summary>
    [Test]
    public void TryGetDefaultValue_MixedPhraserOrder_RequiresDiContainer()
    {
        Assert.Catch(() =>
            LocalSettingsService.TryGetDefaultValue<MixedPhraserOrder>(KeyValues.MixedPhraserOrder));
    }
}
