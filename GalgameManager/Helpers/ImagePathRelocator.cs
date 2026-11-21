using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalgameManager.Models;
using LiteDB;
using Serilog;

namespace GalgameManager.Helpers;

/// <summary>
/// 图片路径重定位：便携包移动/重命名/跨版本升级、或从其他机器恢复备份后，
/// LiteDB 里持久化的旧绝对路径会全部失联。此类在启动时把这些路径重定向到
/// 当前的托管 Images 目录。
/// <para>
/// 只处理「托管 Images 目录」下的文件：判定依据是路径中含 <c>Images</c> 目录段，
/// 且把该段之后的相对子路径拼到当前 Images 目录后目标文件真实存在。
/// 用户指向外部目录的合法图片不会被改写。
/// </para>
/// </summary>
public static class ImagePathRelocator
{
    /// <summary>托管图片目录名，与 <see cref="FileHelper.FolderType.Images"/> 一致。</summary>
    private const string ImagesFolderName = "Images";

    /// <summary>
    /// 判断路径是否指向托管 Images 目录下的文件（便携版 UserData 或历史 MSIX 的
    /// LocalState）。仅作纯字符串识别，不访问文件系统，也不判断是否在当前目录下。
    /// </summary>
    public static bool IsManagedImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        // ms-appx:/// 之类的 scheme 与相对路径都不按托管路径处理
        if (!Path.IsPathRooted(path)) return false;
        if (path.Contains("://", StringComparison.Ordinal)) return false;
        return ExtractRelativeUnderImages(path) is not null;
    }

    /// <summary>
    /// 取出路径中位于**最后一个** <c>Images</c> 目录段之后的相对子路径；
    /// 找不到 <c>Images</c> 目录段时返回 null。
    /// <para>
    /// 从右往左定位：形如 <c>D:\Images\backup\UserData\Images\a.jpg</c> 的多段路径
    /// 必须取最后一段，否则会把前半截错误地当作相对子路径。
    /// </para>
    /// </summary>
    public static string? ExtractRelativeUnderImages(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        char[] separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

        // 从右往左找第一个独立的 Images 目录段
        for (int i = path.Length - 1; i >= 0; i--)
        {
            if (!separators.Contains(path[i])) continue;
            int end = i;                                  // 目录段结束位置（分隔符处）
            int start = path.LastIndexOfAny(separators, end - 1) + 1;  // 目录段起点
            if (end - start != ImagesFolderName.Length) continue;
            if (string.Compare(path, start, ImagesFolderName, 0, ImagesFolderName.Length,
                    StringComparison.OrdinalIgnoreCase) != 0) continue;

            string relative = path[(end + 1)..];
            return string.IsNullOrEmpty(relative) ? null : relative;
        }
        return null;
    }

    /// <summary>
    /// 判断路径是否已经位于当前 Images 目录之下。用 <see cref="Path.GetFullPath"/> 规范化后
    /// 补齐尾部分隔符做前缀比较，避免 <c>Images2</c> / <c>ImagesBackup</c> 被误判。
    /// </summary>
    public static bool IsUnderCurrentImages(string? path, string currentImagesDir)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(currentImagesDir);
            if (!root.EndsWith(Path.DirectorySeparatorChar))
                root += Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                   full.Length > root.Length;
        }
        catch (Exception)
        {
            // 非法路径不当成已在目录下
            return false;
        }
    }

    /// <summary>
    /// 对单个图片路径应用重定位规则；无需改动时原样返回。
    /// </summary>
    public static string? RelocatePath(string? path, string currentImagesDir)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        // 相对路径（.potatovn 的 meta.json）与 scheme 路径都不处理
        if (!Path.IsPathRooted(path) || path.Contains("://", StringComparison.Ordinal)) return path;
        // 已在当前 Images 目录下：无需改动
        if (IsUnderCurrentImages(path, currentImagesDir)) return path;
        // 不是托管 Images 路径：可能是用户手动指定的外部图片，不动
        if (!IsManagedImagePath(path)) return path;

        string? relative = ExtractRelativeUnderImages(path);
        if (relative is null) return path;

        string target;
        try
        {
            target = Path.GetFullPath(Path.Combine(currentImagesDir, relative));
        }
        catch (Exception)
        {
            return path;
        }

        // 保守策略：当前 Images 下没有对应文件就保持原值，避免把指向别处的合法路径改坏
        return File.Exists(target) ? target : path;
    }

    /// <summary>
    /// 遍历数据库中的所有托管图片字段并重定位；仅对实际发生变更的文档写回（幂等）。
    /// <para>
    /// 覆盖的集合与字段：
    /// <list type="bullet">
    /// <item><c>galgame</c>：<c>ImagePath</c>/<c>HeaderImagePath</c>（嵌套 <c>LockableProperty</c>，
    /// 只改 <c>Value</c>，<c>IsLock</c> 原样保留）以及内嵌 <c>Characters</c> 数组里的
    /// <c>ImagePath</c>/<c>PreviewImagePath</c>（纯字符串）</item>
    /// <item><c>staff</c>：<c>ImagePath</c></item>
    /// <item><c>category</c>：<c>ImagePath</c></item>
    /// </list>
    /// 刻意不碰 <c>source</c> 集合：那里面是用户自己的游戏库路径，与托管图片无关。
    /// </para>
    /// <para>
    /// 单个集合的失败（页损坏、权限、字段类型意外）只跳过该集合，绝不上抛——
    /// 调用方位于启动关键路径上，异常冒泡会触发 <c>Database.Rebuild()</c> 与
    /// 安全模式重启。图片路径重定位是增强，不该有能力让软件起不来。
    /// </para>
    /// </summary>
    public static void Relocate(LiteDatabase db, string currentImagesDir)
    {
        RelocateCollection(db, "galgame", coll => RelocateGalgames(coll, currentImagesDir));
        RelocateCollection(db, "staff", coll => RelocatePlainStringField(coll, "ImagePath", currentImagesDir));
        RelocateCollection(db, "category", coll => RelocatePlainStringField(coll, "ImagePath", currentImagesDir));
    }

    private static void RelocateCollection(LiteDatabase db, string name, Action<ILiteCollection<BsonDocument>> action)
    {
        if (!db.GetCollectionNames().Contains(name)) return;
        try
        {
            action(db.GetCollection<BsonDocument>(name));
        }
        catch (Exception e)
        {
            // 跳过该集合：见 Relocate 的说明。
            // 经 Serilog 静态入口记录（与 InfoService 同一个 Log.Logger）；
            // 若 Serilog 尚未初始化（InfoService 未构造），Serilog 会静默丢弃而不抛。
            Serilog.Log.Warning("ImagePathRelocator: 跳过集合 {Collection} 的图片路径重定位：{Error}", name, e.Message);
        }
    }

    private static void RelocateGalgames(ILiteCollection<BsonDocument> coll, string currentImagesDir)
    {
        List<BsonDocument> changed = [];
        foreach (BsonDocument doc in coll.FindAll())
        {
            bool dirty = false;

            dirty |= RelocateNestedValue(doc, "ImagePath", currentImagesDir);
            dirty |= RelocateNestedValue(doc, "HeaderImagePath", currentImagesDir);

            if (doc.TryGetValue("Characters", out BsonValue? characters) &&
                characters is { IsArray: true })
            {
                foreach (BsonValue item in characters.AsArray)
                {
                    if (!item.IsDocument) continue;
                    BsonDocument character = item.AsDocument;
                    dirty |= RelocatePlainString(character, "ImagePath", currentImagesDir);
                    dirty |= RelocatePlainString(character, "PreviewImagePath", currentImagesDir);
                }
            }

            // 只在真正发生变更时写回：无变更零写入，保证幂等
            if (dirty) changed.Add(doc);
        }

        if (changed.Count > 0) coll.Update(changed);
    }

    /// <summary>
    /// 重定位形如 <c>{ "Value": "path", "IsLock": false }</c> 的嵌套字段：
    /// 只替换 <c>Value</c>，其余键（尤其是 <c>IsLock</c>）原样保留，绝不压扁成纯字符串。
    /// </summary>
    private static bool RelocateNestedValue(BsonDocument doc, string fieldName, string currentImagesDir)
    {
        if (!doc.TryGetValue(fieldName, out BsonValue? raw) || raw is null) return false;
        if (!raw.IsDocument) return false;
        BsonDocument nested = raw.AsDocument;
        if (!nested.TryGetValue("Value", out BsonValue? value) || value is null) return false;
        if (!value.IsString) return false;

        string? relocated = RelocatePath(value.AsString, currentImagesDir);
        if (relocated is null || relocated == value.AsString) return false;

        nested["Value"] = relocated;
        doc[fieldName] = nested;
        return true;
    }

    private static void RelocatePlainStringField(ILiteCollection<BsonDocument> coll, string fieldName,
        string currentImagesDir)
    {
        List<BsonDocument> changed = [];
        foreach (BsonDocument doc in coll.FindAll())
        {
            if (RelocatePlainString(doc, fieldName, currentImagesDir)) changed.Add(doc);
        }
        if (changed.Count > 0) coll.Update(changed);
    }

    private static bool RelocatePlainString(BsonDocument doc, string fieldName, string currentImagesDir)
    {
        if (!doc.TryGetValue(fieldName, out BsonValue? raw) || raw is null) return false;
        if (!raw.IsString) return false;

        string? relocated = RelocatePath(raw.AsString, currentImagesDir);
        if (relocated is null || relocated == raw.AsString) return false;

        doc[fieldName] = relocated;
        return true;
    }
}
