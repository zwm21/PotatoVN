namespace GalgameManager.Enums;

/// <summary>
/// 年度报告「词云」分页的标签统计口径。<see cref="GalgameManager.Helpers.Extensions.EnumExtension.GetLocalized"/>
/// 会按 <c>AnnualReportTagScope_&lt;值名&gt;</c> 取本地化字符串
/// </summary>
public enum AnnualReportTagScope
{
    /// 今年入库的游戏
    AddedInYear,
    /// 今年游玩过的游戏
    PlayedInYear,
    /// 整个游戏库
    AllLibrary,
}
