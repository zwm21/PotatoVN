using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models.Sources;
using LiteDB;
using Microsoft.UI.Xaml.Controls;

namespace GalgameManager.Services;

public partial class GalgameSourceCollectionService
{
    /// <summary>
    /// 本地库路径启动校验：<br/>
    /// 1. 路径存在 → 标记可用，并顺带修复单个条目的被截断路径；<br/>
    /// 2. 路径不存在且开启了启动检查 → 尝试唯一匹配真实路径后整树重定位；<br/>
    /// 3. 仍修复不了 → 标记为不可用，但保留库和游戏数据，绝不删除。
    /// </summary>
    private async Task RepairOrMarkUnavailableSourcesAsync()
    {
        if (AppStoragePaths.IsUpgradeUiTest) return;

        List<GalgameSourceBase> unavailable = [];
        foreach (GalgameSourceBase source in _galgameSources
                     .Where(s => s.SourceType == GalgameSourceType.LocalFolder)
                     .ToList())
        {
            try
            {
                bool exists = Directory.Exists(source.Path);
                if (!exists && source.CheckOnStart)
                {
                    string? repaired = await StoragePathHelper.TryResolveExactPathAsync(source.Path);
                    if (repaired is not null && RepairSourceTree(source, repaired))
                    {
                        source.IsAvailable = true;
                        await RepairEntryPathsAsync(source);
                        continue;
                    }

                    source.IsAvailable = false;
                    unavailable.Add(source);
                    continue;
                }

                source.IsAvailable = exists;
                if (exists)
                    await RepairEntryPathsAsync(source);
            }
            catch (Exception e)
            {
                source.IsAvailable = false;
                unavailable.Add(source);
                infoService.DeveloperEvent(e: e);
            }
        }

        if (unavailable.Count > 0)
        {
            infoService.Event(EventType.GalgameEvent, InfoBarSeverity.Warning,
                "GalgameSourceCollectionService_Unavailable_Title".GetLocalized(),
                msg: "GalgameSourceCollectionService_Unavailable_Msg".GetLocalized(
                    $"\n{string.Join('\n', unavailable.Select(s => s.Path))}"));
        }
    }

    /// <summary>
    /// 修复 source.Path 后，把该库及其子库、条目路径、LocalConfig 一起重定位到新根目录。<br/>
    /// 出现重复目标库等歧义时返回 false，由调用方标记为不可用。
    /// </summary>
    private bool RepairSourceTree(GalgameSourceBase source, string newRoot)
    {
        string oldRoot = source.Path;
        if (string.IsNullOrEmpty(oldRoot) || string.IsNullOrEmpty(newRoot)
            || string.Equals(oldRoot, newRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        List<GalgameSourceBase> affected = _galgameSources
            .Where(s => s.SourceType == source.SourceType
                        && StoragePathHelper.RelocatePath(s.Path, oldRoot, newRoot) is not null)
            .ToList();
        if (affected.Count == 0) return false;

        Dictionary<GalgameSourceBase, string> newPaths = [];
        foreach (GalgameSourceBase item in affected)
        {
            string? newPath = StoragePathHelper.RelocatePath(item.Path, oldRoot, newRoot);
            if (newPath is null) return false;
            newPaths[item] = newPath;
        }

        // 重定位后不能与未受影响的库撞路径
        foreach (GalgameSourceBase other in _galgameSources.Where(s => !affected.Contains(s)))
        {
            if (string.IsNullOrEmpty(other.Path)) continue;
            if (newPaths.Values.Any(p => Utils.ArePathsEqual(p, other.Path)))
                return false;
        }

        foreach (GalgameSourceBase item in affected)
        {
            string oldItemPath = item.Path;
            string newItemPath = newPaths[item];

            foreach (GalgameAndPath entry in item.Galgames.ToList())
            {
                string oldEntryPath = entry.Path;
                string? newEntryPath = StoragePathHelper.RelocatePath(oldEntryPath, oldItemPath, newItemPath);
                if (newEntryPath is null) continue;

                entry.Path = newEntryPath;
                if (entry.LocalConfig is { } config)
                    entry.LocalConfig = config.Relocated(oldEntryPath, newEntryPath);
            }

            item.Path = newItemPath;
            item.SetNameFromPath();
            item.IsAvailable = true;
            PersistSourceDocument(item);
        }

        return true;
    }

    /// <summary>
    /// 修复可用库中单个游戏条目被截断的路径。
    /// </summary>
    private async Task RepairEntryPathsAsync(GalgameSourceBase source)
    {
        bool changed = false;
        foreach (GalgameAndPath entry in source.Galgames.ToList())
        {
            if (Directory.Exists(entry.Path) || File.Exists(entry.Path)) continue;

            string? repaired;
            try
            {
                repaired = await StoragePathHelper.TryResolveExactPathAsync(entry.Path);
            }
            catch (Exception e)
            {
                infoService.DeveloperEvent(e: e);
                continue;
            }

            if (repaired is null) continue;

            string oldEntryPath = entry.Path;
            entry.Path = repaired;
            if (entry.LocalConfig is { } config)
                entry.LocalConfig = config.Relocated(oldEntryPath, repaired);
            changed = true;
        }

        if (changed)
        {
            PersistSourceDocument(source);
            await GameService.SaveGalgamesAsync();
        }
    }

    /// <summary>
    /// 把修复后的 source 及条目路径直接写回 LiteDB 原始 BSON 文档。<br/>
    /// 说明：LiteDB 对从 DB 加载的部分 source 对象重新序列化时可能使用旧快照，
    /// 路径修复场景直接更新 BsonDocument，确保 Path/Name/LocalConfig 落盘。
    /// </summary>
    private void PersistSourceDocument(GalgameSourceBase item)
    {
        ILiteCollection<BsonDocument> raw = localSettingsService.Database.GetCollection<BsonDocument>("source");
        BsonDocument? doc = raw.FindById(item.Id);
        if (doc is null) return;

        doc["Path"] = item.Path;
        doc["Name"] = item.Name;

        if (doc.ContainsKey("GalgamesDto") && doc["GalgamesDto"].IsArray)
        {
            foreach (BsonValue value in doc["GalgamesDto"].AsArray)
            {
                if (!value.IsDocument) continue;

                BsonDocument dto = value.AsDocument;
                if (!dto.ContainsKey("EntryId")) continue;

                Guid entryId = dto["EntryId"].AsGuid;
                GalgameAndPath? entry = item.Galgames.FirstOrDefault(e => e.EntryId == entryId);
                if (entry is null) continue;

                bool pathChanged = !dto.ContainsKey("Path")
                                   || !string.Equals(dto["Path"].AsString, entry.Path, StringComparison.Ordinal);
                dto["Path"] = entry.Path;
                if (pathChanged && entry.LocalConfig is { } config)
                    dto["LocalConfig"] = BsonMapper.Global.ToDocument(typeof(LocalInstallationConfig), config);
            }
        }

        raw.Upsert(doc);
    }
}
