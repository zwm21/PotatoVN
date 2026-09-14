using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace GalgameManager.Helpers;

/// <summary>
/// 处理 <see cref="StorageFolder.Path"/> / <see cref="StorageFile.Path"/> 会截断路径最后一段末尾
/// Unicode 空白字符（例如 U+3000）的问题。<br/>
/// 解析只在原路径不存在时进行，并且只有唯一匹配才返回结果，避免把不相关的目录/文件误判为修复目标。
/// </summary>
public static class StoragePathHelper
{
    /// <summary>
    /// 获取 <see cref="StorageFolder"/> 对应的精确路径。<br/>
    /// 正常路径直接返回；被截断时通过父目录枚举 + FolderRelativeId 或规范化 Path 比对还原。
    /// </summary>
    /// <returns>精确路径；无法唯一确认时返回 null。</returns>
    public static async Task<string?> GetExactPathAsync(StorageFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        string trimmedPath = folder.Path;
        if (Directory.Exists(trimmedPath)) return trimmedPath;

        string? parent = Path.GetDirectoryName(trimmedPath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return null;

        string trimmedName = Path.GetFileName(trimmedPath);
        if (string.IsNullOrEmpty(trimmedName)) return null;

        List<string> matches = [];
        foreach (string candidate in EnumerateTrailingSuffixCandidates(parent, trimmedName, includeFiles: false))
        {
            try
            {
                StorageFolder candidateFolder = await StorageFolder.GetFolderFromPathAsync(candidate);
                bool sameId = !string.IsNullOrEmpty(folder.FolderRelativeId)
                              && string.Equals(candidateFolder.FolderRelativeId, folder.FolderRelativeId,
                                  StringComparison.Ordinal);
                bool samePath = string.Equals(candidateFolder.Path, trimmedPath,
                    StringComparison.OrdinalIgnoreCase);
                if (sameId || samePath) matches.Add(candidate);
            }
            catch (Exception)
            {
                // 权限、驱动器不可用等异常时跳过该候选
            }
        }

        return UniqueOrNull(matches);
    }

    /// <summary>
    /// 把持久化的、可能被截断的路径解析为真实存在的目录/文件路径。<br/>
    /// 用于启动时修复数据库中的旧路径。
    /// </summary>
    /// <returns>精确路径；无法唯一确认时返回 null。</returns>
    public static async Task<string?> TryResolveExactPathAsync(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;
        if (Directory.Exists(storedPath) || File.Exists(storedPath)) return storedPath;

        string? parent = Path.GetDirectoryName(storedPath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return null;

        string trimmedName = Path.GetFileName(storedPath);
        if (string.IsNullOrEmpty(trimmedName)) return null;

        List<string> matches = [];
        foreach (string candidate in EnumerateTrailingSuffixCandidates(parent, trimmedName, includeFiles: true))
        {
            try
            {
                if (Directory.Exists(candidate))
                {
                    StorageFolder candidateFolder = await StorageFolder.GetFolderFromPathAsync(candidate);
                    if (string.Equals(candidateFolder.Path, storedPath, StringComparison.OrdinalIgnoreCase))
                        matches.Add(candidate);
                }
                else if (File.Exists(candidate))
                {
                    StorageFile candidateFile = await StorageFile.GetFileFromPathAsync(candidate);
                    if (string.Equals(candidateFile.Path, storedPath, StringComparison.OrdinalIgnoreCase))
                        matches.Add(candidate);
                }
            }
            catch (Exception)
            {
                // 权限、驱动器不可用等异常时跳过该候选
            }
        }

        return UniqueOrNull(matches);
    }

    /// <summary>
    /// 将旧根目录下的路径重定位到新根目录。<br/>
    /// 路径不在旧根目录下、或参数非法时返回 null。
    /// </summary>
    public static string? RelocatePath(string? path, string? oldRoot, string? newRoot)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(oldRoot) || string.IsNullOrEmpty(newRoot))
            return null;

        try
        {
            string relative = Path.GetRelativePath(oldRoot, path);
            if (relative == ".")
                return Path.GetFullPath(newRoot);

            string parentPrefix = ".." + Path.DirectorySeparatorChar;
            if (relative == ".." || relative.StartsWith(parentPrefix, StringComparison.Ordinal))
                return null;

            return Path.GetFullPath(Path.Combine(newRoot, relative));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 枚举父目录中“以被截断名称为前缀、且有一段额外后缀”的候选路径。<br/>
    /// 纯文件系统逻辑，不调用 WinRT，便于单元测试。
    /// </summary>
    public static IEnumerable<string> EnumerateTrailingSuffixCandidates(string parent, string trimmedName,
        bool includeFiles = false)
    {
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(trimmedName)) yield break;

        string[] entries;
        try
        {
            entries = includeFiles
                ? Directory.GetFileSystemEntries(parent)
                : Directory.GetDirectories(parent);
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (string candidate in entries)
        {
            string candidateName = Path.GetFileName(candidate);
            if (candidateName.Length <= trimmedName.Length) continue;
            if (!candidateName.StartsWith(trimmedName, StringComparison.Ordinal)) continue;
            yield return candidate;
        }
    }

    private static string? UniqueOrNull(List<string> matches) => matches.Count == 1 ? matches[0] : null;
}
