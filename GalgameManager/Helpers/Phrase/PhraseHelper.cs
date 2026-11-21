using System.Reflection;
using GalgameManager.Enums;
using GalgameManager.Models;
using NugetPackage;
using PotatoDBMapper.Models;
using SQLite;

namespace GalgameManager.Helpers.Phrase;

public static class PhraseHelper
{
    private const string DbFile = @"Assets\Data\vn_mapper.db";

    /// <summary>
    /// 只采用完全匹配（两边游戏名归一化后一模一样）的映射，低于此相似度的一律不用，
    /// 宁可查不到也不要给出错误的映射
    /// </summary>
    public const double MinSimilarity = 1d;

    private static VnDbMapper? _vnDbMapper;
    private static Task? _unloadDbTask;
    private static bool _isUsing;

    private static void Init()
    {
        if (_vnDbMapper is not null) return;
        Assembly assembly = Assembly.GetExecutingAssembly();
        var file = Path.Combine(Path.GetDirectoryName(assembly.Location)!, DbFile);
        if (!File.Exists(file)) return;
        _vnDbMapper = new VnDbMapper();
        _vnDbMapper.Init(file);
        if (_unloadDbTask is not null)
            _unloadDbTask = Task.Run(async () =>
            {
                do
                {
                    await Task.Delay(1000 * 60 * 5); // 5 minutes
                    if (_isUsing) continue;
                    _vnDbMapper.Dispose();
                    _vnDbMapper = null;
                    _unloadDbTask = null;
                }while (_vnDbMapper is not null);
            });
    }

    public static async Task<int?> TryGetVndbIdAsync(string name) =>
        await TryGetMapAsync(name) is { } mapModel ? mapModel.VndbId : null;

    public static async Task<int?> TryGetBgmIdAsync(string name) =>
        await TryGetMapAsync(name) is { BgmId: > 0 } mapModel && mapModel.BgmSimilarity >= MinSimilarity
            ? mapModel.BgmId
            : null;

    public static async Task<int?> TryGetSteamIdAsync(string name) =>
        await TryGetMapAsync(name) is { SteamId: > 0 } mapModel && mapModel.SteamSimilarity >= MinSimilarity
            ? mapModel.SteamId
            : null;

    public static async Task<MapModel?> TryGetMapAsync(Galgame game)
    {
        _isUsing = true;
        Init();
        MapModel? result = null;
        if (!string.IsNullOrEmpty(game.Ids[(int)RssType.Vndb]))
            result ??= await _vnDbMapper!.TryGetMapAsync(VndbPhraser.GetId(game.Ids[(int)RssType.Vndb]!));
        if (!string.IsNullOrEmpty(game.Ids[(int)RssType.Bangumi]))
            result ??= (await _vnDbMapper!.TryGetMapsWithBgmId(Convert.ToInt32(game.Ids[(int)RssType.Bangumi])))
                .FirstOrDefault(map => map.BgmSimilarity >= MinSimilarity);
        if (!string.IsNullOrEmpty(game.Name.Value))
            result ??= await TryGetMapAsync(game.Name.Value);
        _isUsing = false;
        return result;
    }

    /// <summary>
    /// 获取别名列表，若无则返回空列表
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public static async Task<List<string>> TryGetAliasesAsync(string name)
    {
        List<string> result = [];
        _isUsing = true;
        Init();
        List<(MapModel model, double similarity)> tmp = await _vnDbMapper!.TryGetMapsWithName(name, MinSimilarity);
        if (tmp.Count > 0)
        {
            var vndbId = tmp[0].model.VndbId;
            List<TitleModel> t = await _vnDbMapper.Db.Table<TitleModel>().Where(t => t.VndbId == vndbId).ToListAsync();
            result.AddRange(t.Where(tit => !string.IsNullOrEmpty(tit.Title)).Select(title => title.Title!));
        }
        _isUsing = false;
        return result;
    }

    private static async Task<MapModel?> TryGetMapAsync(string name)
    {
        _isUsing = true;
        Init();
        List<(MapModel model, double similarity)> result =
            await _vnDbMapper!.TryGetMapsWithName(name, MinSimilarity);
        _isUsing = false;
        // 结果由库按相似度从高到低排好序，取第一个即可
        return result.Count > 0 ? result[0].model : null;
    }
}
