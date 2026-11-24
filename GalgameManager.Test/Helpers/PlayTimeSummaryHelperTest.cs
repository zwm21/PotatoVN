using GalgameManager.Helpers;

namespace GalgameManager.Test.Helpers;

/// <summary>
/// <see cref="PlayTimeSummaryHelper"/> 测试。<br/>
/// 全部用例都是纯托管断言：本地化模板与单位词一律以参数注入，不碰 <c>App</c>、
/// 不碰 <c>ResourceLoader</c>，所以能在没有 WinAppSDK bootstrap 的测试进程里直接跑。<br/>
/// 重点盯三处边界：静音阈值（<c>ShouldNotify</c>）、时长格式化的 <c>TimeAsHour</c> 两套路径
/// （含整 60 分钟这个 <c>&gt;</c> 与 <c>&gt;=</c> 的分界）、以及 Win32 气泡的字符上限。
/// </summary>
[TestFixture]
public class PlayTimeSummaryHelperTest
{
    private const string MinuteZh = "分钟";
    private const string MinuteEn = "mins";
    private const string FallbackTitle = "游玩小结";
    private const string MsgTemplate = "本次游玩 {0} · 累计 {1}";

    private static string LongName(int length) => new('长', length);

    #region ShouldNotify

    [Test]
    public void ShouldNotify_NegativeMinutes_ReturnsFalse()
    {
        Assert.That(PlayTimeSummaryHelper.ShouldNotify(-1), Is.False);
        Assert.That(PlayTimeSummaryHelper.ShouldNotify(int.MinValue), Is.False);
    }

    [Test]
    public void ShouldNotify_ZeroMinutes_ReturnsFalse()
    {
        // 0 分钟 == 不足 1 分钟（启动器秒退 / 误点 / 闪退），必须静默
        Assert.That(PlayTimeSummaryHelper.ShouldNotify(0), Is.False);
    }

    [Test]
    public void ShouldNotify_OneMinute_ReturnsTrue()
    {
        Assert.That(PlayTimeSummaryHelper.ShouldNotify(1), Is.True);
    }

    [Test]
    public void ShouldNotify_LargeMinutes_ReturnsTrue()
    {
        Assert.That(PlayTimeSummaryHelper.ShouldNotify(1440), Is.True);
        Assert.That(PlayTimeSummaryHelper.ShouldNotify(int.MaxValue), Is.True);
    }

    #endregion

    #region FormatDuration

    [Test]
    public void FormatDuration_TimeAsHourFalse_UsesInjectedUnitWord()
    {
        Assert.That(PlayTimeSummaryHelper.FormatDuration(30, false, MinuteZh), Is.EqualTo("30 分钟"));
        Assert.That(PlayTimeSummaryHelper.FormatDuration(30, false, MinuteEn), Is.EqualTo("30 mins"));
        Assert.That(PlayTimeSummaryHelper.FormatDuration(0, false, MinuteZh), Is.EqualTo("0 分钟"));
        Assert.That(PlayTimeSummaryHelper.FormatDuration(90, false, MinuteZh), Is.EqualTo("90 分钟"));
    }

    [Test]
    public void FormatDuration_TimeAsHourTrue_BelowSixty_UsesBareMinutes()
    {
        Assert.That(PlayTimeSummaryHelper.FormatDuration(0, true), Is.EqualTo("0m"));
        Assert.That(PlayTimeSummaryHelper.FormatDuration(30, true), Is.EqualTo("30m"));
        Assert.That(PlayTimeSummaryHelper.FormatDuration(59, true), Is.EqualTo("59m"));
    }

    [Test]
    public void FormatDuration_TimeAsHourTrue_ExactlySixty_StaysBareMinutes()
    {
        // 与主界面 TimeToDisplayTimeConverter 一致：用的是 > 60 而不是 >= 60，
        // 整 60 分钟仍是 "60m"。这一行就是钉住那个不等号方向的。
        Assert.That(PlayTimeSummaryHelper.FormatDuration(60, true), Is.EqualTo("60m"));
    }

    [Test]
    public void FormatDuration_TimeAsHourTrue_AboveSixty_UsesHoursAndMinutes()
    {
        Assert.That(PlayTimeSummaryHelper.FormatDuration(61, true), Is.EqualTo("1h1m"));
        Assert.That(PlayTimeSummaryHelper.FormatDuration(90, true), Is.EqualTo("1h30m"));
        Assert.That(PlayTimeSummaryHelper.FormatDuration(125, true), Is.EqualTo("2h5m"));
    }

    [Test]
    public void FormatDuration_TimeAsHourTrue_WholeDays_StillHoursAndMinutes()
    {
        Assert.That(PlayTimeSummaryHelper.FormatDuration(1440, true), Is.EqualTo("24h0m"));
        Assert.That(PlayTimeSummaryHelper.FormatDuration(1441, true), Is.EqualTo("24h1m"));
    }

    [Test]
    public void FormatDuration_NegativeMinutes_ClampsToZero()
    {
        Assert.That(PlayTimeSummaryHelper.FormatDuration(-5, false, MinuteEn), Is.EqualTo("0 mins"));
        Assert.That(PlayTimeSummaryHelper.FormatDuration(-5, true), Is.EqualTo("0m"));
    }

    [Test]
    public void FormatDuration_EmptyUnitWord_FallsBackToNeutralSymbol()
    {
        Assert.That(PlayTimeSummaryHelper.FormatDuration(30, false, null), Is.EqualTo("30 m"));
        Assert.That(PlayTimeSummaryHelper.FormatDuration(30, false, string.Empty), Is.EqualTo("30 m"));
    }

    #endregion

    #region FormatTitle

    [Test]
    public void FormatTitle_NormalGameName_TitleIsGameName()
    {
        Assert.That(PlayTimeSummaryHelper.FormatTitle("千恋万花", FallbackTitle), Is.EqualTo("千恋万花"));
    }

    [Test]
    public void FormatTitle_NullOrWhitespaceGameName_FallsBackToLocalizedTitle()
    {
        Assert.That(PlayTimeSummaryHelper.FormatTitle(null, FallbackTitle), Is.EqualTo(FallbackTitle));
        Assert.That(PlayTimeSummaryHelper.FormatTitle(string.Empty, FallbackTitle), Is.EqualTo(FallbackTitle));
        Assert.That(PlayTimeSummaryHelper.FormatTitle("   ", FallbackTitle), Is.EqualTo(FallbackTitle));
    }

    [Test]
    public void FormatTitle_LongGameName_TruncatedToTitleMaxLength()
    {
        string result = PlayTimeSummaryHelper.FormatTitle(LongName(200), FallbackTitle);

        Assert.That(result.Length, Is.EqualTo(PlayTimeSummaryHelper.TitleMaxLength));
        Assert.That(result, Is.EqualTo(new string('长', 57) + "..."));
    }

    [Test]
    public void FormatTitle_ExactlyTitleMaxLength_KeepsAsIs()
    {
        Assert.That(PlayTimeSummaryHelper.FormatTitle(LongName(60), FallbackTitle), Is.EqualTo(LongName(60)));
    }

    [Test]
    public void FormatTitle_Limits_AreWithinWin32Budget()
    {
        Assert.That(PlayTimeSummaryHelper.TitleMaxLength, Is.LessThanOrEqualTo(60));
        Assert.That(PlayTimeSummaryHelper.MessageMaxLength, Is.LessThanOrEqualTo(240));
    }

    #endregion

    #region FormatMessage

    [Test]
    public void FormatMessage_SubstitutesBothPlaceholders()
    {
        string result = PlayTimeSummaryHelper.FormatMessage(30, 90, false, MsgTemplate, MinuteZh);

        Assert.That(result, Is.EqualTo("本次游玩 30 分钟 · 累计 90 分钟"));
    }

    [Test]
    public void FormatMessage_SubstitutesUsingTimeAsHourStyle()
    {
        string result = PlayTimeSummaryHelper.FormatMessage(30, 90, true, MsgTemplate, MinuteZh);

        Assert.That(result, Is.EqualTo("本次游玩 30m · 累计 1h30m"));
    }

    [Test]
    public void FormatMessage_ZeroTotalMinutes_StillRendersBothSlots()
    {
        string result = PlayTimeSummaryHelper.FormatMessage(1, 0, false, MsgTemplate, MinuteZh);

        Assert.That(result, Is.EqualTo("本次游玩 1 分钟 · 累计 0 分钟"));
    }

    [Test]
    public void FormatMessage_TemplateWithoutPlaceholders_ReturnedAsIs()
    {
        Assert.That(PlayTimeSummaryHelper.FormatMessage(30, 90, true, "本次游玩小结", MinuteZh),
            Is.EqualTo("本次游玩小结"));
    }

    [Test]
    public void FormatMessage_EmptyTemplate_ReturnsEmpty()
    {
        Assert.That(PlayTimeSummaryHelper.FormatMessage(30, 90, true, string.Empty, MinuteZh),
            Is.EqualTo(string.Empty));
    }

    [Test]
    public void FormatMessage_OverMaxLength_TruncatedWithinWin32Limit()
    {
        // 模板本身就超长：截断必须兜住，绝不能让正文越过 Win32 szInfo 的安全上限
        // 注意写成 new string(...) 而不是 new(...)：后者是 target-typed new，
        // 在 "+ {0}" 这种没有目标类型的表达式里推不出类型，会报 CS8310。
        string template = new string('X', 400) + "{0}";

        string result = PlayTimeSummaryHelper.FormatMessage(30, 90, true, template, MinuteZh);

        Assert.That(result.Length, Is.EqualTo(PlayTimeSummaryHelper.MessageMaxLength));
        Assert.That(result, Does.EndWith("..."));
    }

    [Test]
    public void FormatMessage_PlaceholderCountMismatch_DoesNotThrow()
    {
        // string.Format 在占位符与实参不匹配时会抛 FormatException；本地化文案是三方可改的，
        // 这里钉住「不抛异常、无法识别的占位符原样保留」的行为。
        Assert.That(PlayTimeSummaryHelper.FormatMessage(30, 90, true, "{0} {1} {9}", MinuteZh),
            Is.EqualTo("30m 1h30m {9}"));
    }

    #endregion
}
