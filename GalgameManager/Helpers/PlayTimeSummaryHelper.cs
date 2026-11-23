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

    /// <summary>游戏名在气泡标题/正文里的最大长度，超出则截断并补省略号。</summary>
    public const int GameNameMaxLength = 40;

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
    /// 游戏名兜底 + 截断：空名回落到 <paramref name="fallbackName"/>（调用方一般传游戏 UUID），
    /// 两者都空则返回空串；超长按 <see cref="GameNameMaxLength"/> 截断并补省略号。
    /// </summary>
    public static string TruncateGameName(string? gameName, string? fallbackName = null,
        int maxLength = GameNameMaxLength)
    {
        string name = string.IsNullOrWhiteSpace(gameName)
            ? (string.IsNullOrWhiteSpace(fallbackName) ? string.Empty : fallbackName!)
            : gameName;
        return Truncate(name, maxLength);
    }

    /// <summary>
    /// 拼装气泡标题（一般是「{游戏名}」或固定的「游玩小结」），并保证不超过
    /// <see cref="TitleMaxLength"/>。
    /// </summary>
    public static string FormatTitle(string? gameName, string titleTemplate, string? fallbackName = null)
    {
        string name = TruncateGameName(gameName, fallbackName);
        return Truncate(Substitute(titleTemplate, name), TitleMaxLength);
    }

    /// <summary>
    /// 拼装气泡正文，占位符为 <c>{0}</c>=游戏名、<c>{1}</c>=本次时长、<c>{2}</c>=累计时长，
    /// 并保证不超过 <see cref="MessageMaxLength"/>。<br/>
    /// 用 <see cref="string.Replace(string, string)"/> 而不是 <c>string.Format</c>：
    /// 后者的占位符个数与实参不匹配时会抛 <c>FormatException</c>，而本地化文案的占位符
    /// 是三方可改的，不能让它把结算流程带崩。
    /// </summary>
    public static string FormatMessage(string? gameName, int currentMinutes, int totalMinutes,
        bool timeAsHour, string messageTemplate, string? minuteWord = null, string? fallbackName = null)
    {
        string name = TruncateGameName(gameName, fallbackName);
        string body = Substitute(messageTemplate, name,
            FormatDuration(currentMinutes, timeAsHour, minuteWord),
            FormatDuration(totalMinutes, timeAsHour, minuteWord));
        return Truncate(body, MessageMaxLength);
    }

    /// <summary>
    /// 截断到 <paramref name="maxLength"/> 以内，超长时补省略号且总长度仍不超过上限。
    /// 策略与 <see cref="RecentGamesHelper.FormatMenuTitle"/> 保持一致。
    /// </summary>
    public static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        int limit = Math.Max(maxLength, 1);
        if (value.Length <= limit) return value;
        return limit <= Ellipsis.Length
            ? value.Substring(0, limit)
            : value.Substring(0, limit - Ellipsis.Length) + Ellipsis;
    }

    /// <summary>按位置替换 <c>{0}</c>/<c>{1}</c>/<c>{2}</c>，缺失的占位符原样留下。</summary>
    private static string Substitute(string template, string arg0, string? arg1 = null, string? arg2 = null)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        string result = template.Replace("{0}", arg0);
        if (arg1 is not null) result = result.Replace("{1}", arg1);
        if (arg2 is not null) result = result.Replace("{2}", arg2);
        return result;
    }
}
