using GalgameManager.Enums;
using PotatoVN.Client.Model;

namespace GalgameManager.Models.MapperProfile;

/// <summary>
/// PVN 同步用的角色对象映射（原先由 AutoMapper 的 <see cref="CharacterMapper"/> Profile 驱动）。
/// <para>
/// 只有两条同步链路会用到：上行 <see cref="ToUpdateDto"/>（PvnSyncTask）与
/// 下行 <see cref="ToCharacter"/>（PvnSyncTaskPullGame）。字段逐一显式写出，
/// 不再依赖同名约定的隐式匹配——AutoMapper 那套约定在这里踩过两个坑：
/// 目标类的多参构造函数会按参数名抢走 ForMember 的 MapFrom；名字差一格的字段
/// （<c>BirthMon</c> / <c>BirthMonth</c>）则静默不映射。
/// </para>
/// </summary>
public static class CharacterMapping
{
    /// <summary>
    /// 本地角色 → 同步上行 DTO。<br/>
    /// <c>ImageLoc</c> / <c>PreviewImageLoc</c> 不在此处填充：由调用方上传文件后回填
    /// （见 <c>PvnSyncTask</c> 里上传完成后的赋值）。
    /// </summary>
    public static CharacterUpdateDto ToUpdateDto(GalgameCharacter c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new CharacterUpdateDto(
            id: ParseId(c.Ids[(int)RssType.PotatoVn]),
            name: c.Name,
            relation: c.Relation,
            summary: c.Summary,
            gender: (PotatoVN.Client.Model.Gender?)c.Gender,
            birthYear: c.BirthYear ?? 0,
            birthMonth: 0,   // 既有缺口：本地属性叫 BirthMon，DTO 叫 BirthMonth，从未同步
            birthDay: c.BirthDay ?? 0,
            // DTO 的 ctor 参数声明为非可空 string，但 PVN 的服务端模型本来允许这些字段为 null
            // （本地对应字段就是 string?），这里按原样透传。
            bloodType: c.BloodType!,
            height: c.Height!,
            weight: c.Weight!,
            threeSize: c.BWH!,
            imageLoc: null!,
            previewImageLoc: null!);
    }

    /// <summary>
    /// 同步下行 DTO → 本地角色。<br/>
    /// <c>ImagePath</c> / <c>PreviewImagePath</c> 保持默认头像：由调用方下载图片后回填
    /// （见 <c>PvnSyncTaskPullGame</c> 里下载完成后的赋值）。<c>Ids</c> 不动。
    /// </summary>
    public static GalgameCharacter ToCharacter(CharacterDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        GalgameCharacter c = new()
        {
            Name = dto.Name,
            Relation = dto.Relation,
            Summary = dto.Summary,
            Gender = (Gender?)dto.Gender ?? GalgameManager.Models.Gender.Unknown,
            BirthYear = dto.BirthYear,
            BirthDay = dto.BirthDay,
            BloodType = dto.BloodType,
            Height = dto.Height,
            Weight = dto.Weight,
            BWH = dto.ThreeSize,
            ImageUrl = dto.ImageUrl,
            PreviewImageUrl = dto.PreviewImageUrl,
        };
        return c;
    }

    /// <summary>
    /// PVN 侧的角色 id 是 <see cref="int"/>，本地存在 <c>Ids[(int)RssType.PotatoVn]</c> 里是字符串。
    /// 取不到或不是数字时给 0——与 AutoMapper 的 string→int 转换在 null 时的行为一致。
    /// </summary>
    private static int ParseId(string? id) => int.TryParse(id, out int value) ? value : 0;
}
