namespace GalgameManager.Helpers;

/// <summary>
/// 首页手动排序顺序（CustomSortOrder）的辅助方法。
/// </summary>
public static class CustomSortOrderHelper
{
    /// <summary>
    /// 将指定游戏的 Uuid 放到顺序列表最前面；若列表中已存在则先去重。
    /// </summary>
    /// <param name="customSortOrder">现有顺序，null 表示尚未建立手动顺序。</param>
    /// <param name="uuid">要置顶的游戏 Uuid。</param>
    /// <returns>新的顺序列表，不修改原列表。</returns>
    public static List<string> PutFirst(IEnumerable<string>? customSortOrder, Guid uuid)
    {
        string id = uuid.ToString();
        List<string> result = customSortOrder?.ToList() ?? [];
        result.RemoveAll(item => string.Equals(item, id, StringComparison.OrdinalIgnoreCase));
        result.Insert(0, id);
        return result;
    }
}
