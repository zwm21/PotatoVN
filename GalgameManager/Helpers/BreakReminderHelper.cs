namespace GalgameManager.Helpers;

/// <summary>
/// 连续游玩「定时休息提醒」的触发判定与文案拼装。<br/>
/// 与 <see cref="PlayTimeSummaryHelper"/> 一样刻意做成纯静态函数、零 WinRT / <c>App</c> /
/// <c>ResourceLoader</c> 依赖：本地化模板与单位词全部由调用方注入，单测能在没有 WinAppSDK
/// bootstrap 的测试进程里纯托管运行。<br/>
/// 使用者：<c>RecordPlayTimeTask</c> 的每分钟计时循环。
/// </summary>
public static class BreakReminderHelper
{
    /// <summary>
    /// 相对周期判定：距上次提醒已满一个间隔就提醒。<br/>
    /// 极简且无边界漏洞——只用一次减法与一次比较，不引入任何取整或向上取整到「绝对周期节点」
    /// 的运算（那种写法在间隔被调小后会把 baseline 之后的所有整点一次性补齐，造成连环弹窗）。<br/>
    /// 调用方在返回 <c>true</c> 后应把 <paramref name="lastRemindedAt"/> 更新为
    /// <paramref name="currentMinutes"/>，从该时刻重新起算下一个周期。
    /// </summary>
    /// <param name="currentMinutes">本次连续游玩时长（分钟）</param>
    /// <param name="intervalMinutes">提醒间隔（分钟），<c>&lt;= 0</c> 表示关闭</param>
    /// <param name="lastRemindedAt">上次提醒时的游玩时长；负数按 0 处理</param>
    public static bool ShouldRemind(int currentMinutes, int intervalMinutes, int lastRemindedAt)
    {
        if (intervalMinutes <= 0 || currentMinutes <= 0) return false;
        int baseline = Math.Max(lastRemindedAt, 0);
        return currentMinutes - baseline >= intervalMinutes;
    }

    /// <summary>
    /// 拼装休息提醒正文，占位符 <c>{0}</c> = 连续游玩时长，并按
    /// <see cref="PlayTimeSummaryHelper.MessageMaxLength"/> 截断。<br/>
    /// 用 <see cref="string.Replace(string, string)"/> 而不是 <c>string.Format</c>：
    /// 后者的占位符个数与实参不匹配时会抛 <c>FormatException</c>，而本地化文案的占位符
    /// 是三方可改的，不能让它把计时循环带崩。策略与
    /// <see cref="PlayTimeSummaryHelper"/> 完全一致。
    /// </summary>
    public static string FormatMessage(int currentMinutes, bool timeAsHour, string messageTemplate,
        string minuteWord)
    {
        string duration = PlayTimeSummaryHelper.FormatDuration(currentMinutes, timeAsHour, minuteWord);
        string body = Substitute(messageTemplate, duration);
        return PlayTimeSummaryHelper.Truncate(body, PlayTimeSummaryHelper.MessageMaxLength);
    }

    /// <summary>替换 <c>{0}</c>；模板为空返回空串，其余内容原样保留。</summary>
    private static string Substitute(string template, string arg0)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        return template.Replace("{0}", arg0);
    }
}
