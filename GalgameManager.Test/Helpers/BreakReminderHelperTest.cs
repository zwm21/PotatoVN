using GalgameManager.Helpers;

namespace GalgameManager.Test.Helpers;

/// <summary>
/// <see cref="BreakReminderHelper"/> 测试。<br/>
/// 全部用例都是纯托管断言：本地化模板与单位词一律以参数注入，不碰 <c>App</c>、
/// 不碰 <c>ResourceLoader</c>，所以能在没有 WinAppSDK bootstrap 的测试进程里直接跑。<br/>
/// 判定用的是「相对周期」——距上次提醒满一个间隔就提醒。重点盯四类边界：
/// 关闭态、首周期、多周期，以及中途调大/调小间隔后既不漏提醒也不连环补弹。
/// </summary>
[TestFixture]
public class BreakReminderHelperTest
{
    private const string MinuteZh = "分钟";
    private const string MsgTemplate = "已连续游玩 {0}，记得起身休息";

    #region ShouldRemind

    [Test]
    public void ShouldRemind_IntervalZero_ReturnsFalse()
    {
        // 0 表示关闭，任何游玩时长都不提醒
        Assert.That(BreakReminderHelper.ShouldRemind(1, 0, 0), Is.False);
        Assert.That(BreakReminderHelper.ShouldRemind(60, 0, 0), Is.False);
        Assert.That(BreakReminderHelper.ShouldRemind(9999, 0, 120), Is.False);
    }

    [Test]
    public void ShouldRemind_NegativeInterval_ReturnsFalse()
    {
        Assert.That(BreakReminderHelper.ShouldRemind(60, -30, 0), Is.False);
        Assert.That(BreakReminderHelper.ShouldRemind(60, int.MinValue, 0), Is.False);
    }

    [Test]
    public void ShouldRemind_ZeroOrNegativeCurrentMinutes_ReturnsFalse()
    {
        Assert.That(BreakReminderHelper.ShouldRemind(0, 30, 0), Is.False);
        Assert.That(BreakReminderHelper.ShouldRemind(-1, 30, 0), Is.False);
        Assert.That(BreakReminderHelper.ShouldRemind(int.MinValue, 30, 0), Is.False);
    }

    [Test]
    public void ShouldRemind_FirstCycle_RemindsExactlyAtInterval()
    {
        Assert.That(BreakReminderHelper.ShouldRemind(59, 60, 0), Is.False);
        Assert.That(BreakReminderHelper.ShouldRemind(60, 60, 0), Is.True);
    }

    [Test]
    public void ShouldRemind_NotYetDue_ReturnsFalse()
    {
        Assert.That(BreakReminderHelper.ShouldRemind(30, 60, 0), Is.False);
        Assert.That(BreakReminderHelper.ShouldRemind(1, 1440, 0), Is.False);
    }

    [Test]
    public void ShouldRemind_MultipleCycles_RemindsOnEveryInterval()
    {
        // 每次提醒后 baseline 前移到当前分钟，于是每满一个间隔就再提醒一次
        Assert.That(BreakReminderHelper.ShouldRemind(30, 30, 0), Is.True);
        Assert.That(BreakReminderHelper.ShouldRemind(60, 30, 30), Is.True);
        Assert.That(BreakReminderHelper.ShouldRemind(90, 30, 60), Is.True);
        Assert.That(BreakReminderHelper.ShouldRemind(120, 30, 90), Is.True);
    }

    [Test]
    public void ShouldRemind_SameMinute_DoesNotRepeat()
    {
        // 同一分钟内重复求值不能再次触发，否则 UI 线程块被重入时会连环弹窗
        Assert.That(BreakReminderHelper.ShouldRemind(30, 30, 30), Is.False);
        Assert.That(BreakReminderHelper.ShouldRemind(60, 30, 60), Is.False);
    }

    [Test]
    public void ShouldRemind_NegativeBaseline_TreatedAsZero()
    {
        Assert.That(BreakReminderHelper.ShouldRemind(30, 30, -5), Is.True);
        Assert.That(BreakReminderHelper.ShouldRemind(29, 30, -5), Is.False);
    }

    [Test]
    public void ShouldRemind_IntervalShrunkMidPlay_RemindsOnceThenResumesCycle()
    {
        // 玩到 75 分钟时把间隔从 60 调成 30：下一拍温和提醒一次，并从 75 重新起算
        Assert.That(BreakReminderHelper.ShouldRemind(75, 30, 0), Is.True);
        // 已提醒过，baseline = 75：90 - 75 = 15 不足一个间隔，不补弹
        Assert.That(BreakReminderHelper.ShouldRemind(90, 30, 75), Is.False);
        Assert.That(BreakReminderHelper.ShouldRemind(104, 30, 75), Is.False);
        // 满一个间隔后恢复周期
        Assert.That(BreakReminderHelper.ShouldRemind(105, 30, 75), Is.True);
    }

    [Test]
    public void ShouldRemind_IntervalGrewMidPlay_DelaysReminder()
    {
        // 玩到 75 分钟时把间隔从 30 调成 120：按新间隔延后，不提前
        Assert.That(BreakReminderHelper.ShouldRemind(75, 120, 0), Is.False);
        Assert.That(BreakReminderHelper.ShouldRemind(119, 120, 0), Is.False);
        Assert.That(BreakReminderHelper.ShouldRemind(120, 120, 0), Is.True);
    }

    [Test]
    public void ShouldRemind_DisabledMidPlay_StopsImmediately()
    {
        // 中途把间隔设为 0，下一拍立即停用
        Assert.That(BreakReminderHelper.ShouldRemind(90, 0, 60), Is.False);
    }

    #endregion

    #region FormatMessage

    [Test]
    public void FormatMessage_SubstitutesDurationWithInjectedUnitWord()
    {
        Assert.That(
            BreakReminderHelper.FormatMessage(90, false, MsgTemplate, MinuteZh),
            Is.EqualTo("已连续游玩 90 分钟，记得起身休息"));
    }

    [Test]
    public void FormatMessage_SubstitutesUsingTimeAsHourStyle()
    {
        Assert.That(
            BreakReminderHelper.FormatMessage(90, true, MsgTemplate, MinuteZh),
            Is.EqualTo("已连续游玩 1h30m，记得起身休息"));
        Assert.That(
            BreakReminderHelper.FormatMessage(60, true, MsgTemplate, MinuteZh),
            Is.EqualTo("已连续游玩 60m，记得起身休息"));
    }

    [Test]
    public void FormatMessage_ZeroMinutes_StillRendersSlot()
    {
        Assert.That(
            BreakReminderHelper.FormatMessage(0, false, MsgTemplate, MinuteZh),
            Is.EqualTo("已连续游玩 0 分钟，记得起身休息"));
    }

    [Test]
    public void FormatMessage_TemplateWithoutPlaceholder_ReturnedAsIs()
    {
        Assert.That(BreakReminderHelper.FormatMessage(30, true, "记得起身休息", MinuteZh),
            Is.EqualTo("记得起身休息"));
    }

    [Test]
    public void FormatMessage_EmptyTemplate_ReturnsEmpty()
    {
        Assert.That(BreakReminderHelper.FormatMessage(30, true, string.Empty, MinuteZh),
            Is.EqualTo(string.Empty));
    }

    [Test]
    public void FormatMessage_OverMaxLength_TruncatedWithinWin32Limit()
    {
        // 模板本身就超长：截断必须兜住，绝不能让正文越过 Win32 szInfo 的安全上限
        // 注意写成 new string(...) 而不是 new(...)：后者是 target-typed new，
        // 在 "+ {0}" 这种没有目标类型的表达式里推不出类型，会报 CS8310。
        string template = new string('X', 400) + "{0}";

        string result = BreakReminderHelper.FormatMessage(30, true, template, MinuteZh);

        Assert.That(result.Length, Is.EqualTo(PlayTimeSummaryHelper.MessageMaxLength));
        Assert.That(result, Does.EndWith("..."));
    }

    [Test]
    public void FormatMessage_PlaceholderCountMismatch_DoesNotThrow()
    {
        // string.Format 在占位符与实参不匹配时会抛 FormatException；本地化文案是三方可改的，
        // 这里钉住「不抛异常、无法识别的占位符原样保留」的行为。
        Assert.That(BreakReminderHelper.FormatMessage(30, true, "{0} {9}", MinuteZh),
            Is.EqualTo("30m {9}"));
    }

    #endregion
}
