namespace GalgameManager.Helpers;

/// <summary>
/// 「本次游玩小结」托盘气泡的判定与文案拼装。<br/>
/// 刻意做成纯静态函数、零 WinRT / <c>App</c> / <c>ResourceLoader</c> 依赖：所有本地化模板与单位词
/// 都由调用方注入，这样单测能在没有 WinAppSDK bootstrap 的测试进程里纯托管运行
/// （参照 <see cref="UiThreadInvokeHelper"/> 对同一问题的处理）。<br/>
/// 使用者：<c>RecordPlayTimeTask</c> 的结算块。
/// </summary>
public static class PlayTimeSummaryHelper
{
    /// <summary>
    /// 低于这个本次游玩时长（分钟）就不弹气泡。<br/>
    /// 时长统计的精度是 1 分钟且向下取整，所以 <c>0</c> 等价于「不足 1 分钟」——
    /// 启动器秒退、误点、闪退全部落在这一档，必须静默，否则用户会被刷屏。
    /// </summary>
    public const int MinNotifyMinutes = 1;

    /// <summary>
    /// Win32 <c>NOTIFYICONDATAA.szInfoTitle</c> 的安全上限。Shell 实际按 TCHAR 计，
    /// 上限约 64；这里主动收敛到 60，给多字节语言留余量。
    /// </summary>
    public const int TitleMaxLength = 60;

    /// <summary>
    /// Win32 <c>NOTIFYICONDATAA.szInfo</c> 的安全上限（256 个 TCHAR 含结尾 NUL）。
    /// 收敛到 240 给多字节语言留余量。
    /// </summary>
    public const int MessageMaxLength = 240;

    /// <summary>截断后补充的省略号。</summary>
    public const string Ellipsis = "...";

    /// <summary>
    /// 是否应当弹气泡。仅由本次游玩时长决定，与窗口状态无关。
    /// </summary>
    public static bool ShouldNotify(int currentMinutes) => currentMinutes >= MinNotifyMinutes;

    /// <summary>
    /// 把分钟数格式化成主界面 <c>TimeAsHour</c> 设置下的展示风格。<br/>
    /// <c>true</c> 走 <c>__h__m</c>：与 <c>TimeToDisplayTimeConverter</c> 一致，
    /// 用 <c>&gt; 60</c> 而不是 <c>&gt;= 60</c>，所以整 60 分钟仍显示为 <c>60m</c>；
    /// 这一路的 <c>h</c>/<c>m</c> 是主界面一直在用的中性符号，不做本地化。<br/>
    /// <c>false</c> 走 <c>__{单位词}</c>，单位词由调用方注入本地化后的 <c>Minute</c> 资源值，
    /// 这里绝不硬编码中文。
    /// </summary>
    public static string FormatDuration(int minutes, bool timeAsHour, string? minuteWord = null)
    {
        int safe = Math.Max(minutes, 0);
        if (timeAsHour)
            return safe > 60 ? $"{safe / 60}h{safe % 60}m" : $"{safe}m";
        string unit = string.IsNullOrEmpty(minuteWord) ? "m" : minuteWord;
        return $"{safe} {unit}";
    }

    /// <summary>
    /// 拼装气泡标题：标题位直接放游戏名，让用户一眼认出刚退出的是哪一款。<br/>
    /// 游戏名为空/空白时才回落到 <paramref name="fallbackTitle"/>（调用方传本地化的
    /// 「游玩小结」），并按 <see cref="TitleMaxLength"/> 截断。
    /// </summary>
    public static string FormatTitle(string? gameName, string fallbackTitle)
    {
        string title = string.IsNullOrWhiteSpace(gameName) ? fallbackTitle : gameName;
        return Truncate(title, TitleMaxLength);
    }

    /// <summary>
    /// 拼装气泡正文，占位符为 <c>{0}</c>=本次时长、<c>{1}</c>=累计时长，
    /// 并按 <see cref="MessageMaxLength"/> 截断。<br/>
    /// 正文刻意不放游戏名——标题已经是游戏名，重复一遍只会把气泡撑成两行，
    /// 而气泡高度是 Shell 固定的，换行就会把时长挤出可见区域。<br/>
    /// 用 <see cref="string.Replace(string, string)"/> 而不是 <c>string.Format</c>：
    /// 后者的占位符个数与实参不匹配时会抛 <c>FormatException</c>，而本地化文案的占位符
    /// 是三方可改的，不能让它把结算流程带崩。
    /// </summary>
    public static string FormatMessage(int currentMinutes, int totalMinutes, bool timeAsHour,
        string messageTemplate, string minuteWord)
    {
        string body = Substitute(messageTemplate,
            FormatDuration(currentMinutes, timeAsHour, minuteWord),
            FormatDuration(totalMinutes, timeAsHour, minuteWord));
        return Truncate(body, MessageMaxLength);
    }

    /// <summary>
    /// 截断到 <paramref name="maxLength"/> 以内，超长时补省略号且总长度仍不超过上限。
    /// 策略与 <see cref="RecentGamesHelper.FormatMenuTitle"/> 保持一致。
    /// </summary>
    public static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        int limit = Math.Max(maxLength, 1);
        if (value.Length <= limit) return value;
        return limit <= Ellipsis.Length
            ? value.Substring(0, limit)
            : value.Substring(0, limit - Ellipsis.Length) + Ellipsis;
    }

    /// <summary>按位置替换 <c>{0}</c>/<c>{1}</c>，缺失的占位符原样留下。</summary>
    private static string Substitute(string template, string arg0, string arg1)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        return template.Replace("{0}", arg0).Replace("{1}", arg1);
    }
}
