using GalgameManager.Enums;
using GalgameManager.Models;
using GalgameManager.Models.MapperProfile;
using PotatoVN.Client.Model;
using Gender = GalgameManager.Models.Gender;

namespace GalgameManager.Test.Models;

/// <summary>
/// PVN 同步两条对象映射的契约测试。
/// <para>
/// 这两条映射原先由 AutoMapper 驱动。换成手写映射后，这些测试锁死每条字段的语义，
/// 特别是 profile 里用 ForMember 声明过的两条自定义映射（<c>BWH ↔ ThreeSize</c>、
/// <c>Ids[(int)RssType.PotatoVn] → Id</c>）与空值边界。
/// </para>
/// <para>
/// 背景：AutoMapper 时代这两条自定义映射在上行方向其实是失效的——
/// <c>CharacterUpdateDto</c> 有一个 14 参构造函数，AutoMapper 按参数名从源对象取 ctor 参数，
/// 把 ForMember 的 MapFrom 盖掉了，导致 <c>ThreeSize</c> 恒为 null、<c>Id</c> 恒为 0
/// （已用「目标类无多参 ctor」的判别实验复现，见提交信息）。
/// 手写映射按 profile 声明的意图实现，因此这两条从「静默丢数据」变成「真的生效」——
/// 这是本次替换唯一的行为变化，是有意为之。
/// </para>
/// </summary>
[TestFixture]
public class CharacterMapperTest
{
    private static GalgameCharacter MakeCharacter() => new()
    {
        Name = "角色甲",
        Relation = "主人公",
        Summary = "简介",
        Gender = Gender.Female,
        BirthYear = 1999,
        BirthMon = 12,
        BirthDay = 24,
        BloodType = "A",
        Height = "160",
        Weight = "48",
        BWH = "80/58/84",
        ImagePath = @"C:\img\a.jpg",
        PreviewImagePath = @"C:\img\a_preview.jpg",
        ImageUrl = "https://example.com/a.jpg",
        PreviewImageUrl = "https://example.com/a_preview.jpg",
    };

    #region GalgameCharacter -> CharacterUpdateDto（同步上行）

    [Test]
    public void GalgameCharacter_To_CharacterUpdateDto_MapsConventionFields()
    {
        GalgameCharacter c = MakeCharacter();

        CharacterUpdateDto dto = CharacterMapping.ToUpdateDto(c);

        Assert.Multiple(() =>
        {
            Assert.That(dto.Name, Is.EqualTo("角色甲"));
            Assert.That(dto.Relation, Is.EqualTo("主人公"));
            Assert.That(dto.Summary, Is.EqualTo("简介"));
            Assert.That(dto.BirthYear, Is.EqualTo(1999));
            Assert.That(dto.BirthDay, Is.EqualTo(24));
            Assert.That(dto.BloodType, Is.EqualTo("A"));
            Assert.That(dto.Height, Is.EqualTo("160"));
            Assert.That(dto.Weight, Is.EqualTo("48"));
        });
    }

    [Test]
    public void GalgameCharacter_To_CharacterUpdateDto_MapsCustomFields()
    {
        GalgameCharacter c = MakeCharacter();
        c.Ids[(int)RssType.PotatoVn] = "4242";

        CharacterUpdateDto dto = CharacterMapping.ToUpdateDto(c);

        Assert.Multiple(() =>
        {
            // ForMember: dto.Id <- character.Ids[(int)RssType.PotatoVn]
            Assert.That(dto.Id, Is.EqualTo(4242));
            // ForMember: dto.ThreeSize <- character.BWH
            Assert.That(dto.ThreeSize, Is.EqualTo("80/58/84"));
        });
    }

    [Test]
    public void GalgameCharacter_To_CharacterUpdateDto_MapsGenderByValue()
    {
        GalgameCharacter male = MakeCharacter();
        male.Gender = Gender.Male;
        GalgameCharacter unknown = MakeCharacter();
        unknown.Gender = Gender.Unknown;

        Assert.Multiple(() =>
        {
            Assert.That(CharacterMapping.ToUpdateDto(male).Gender, Is.EqualTo(PotatoVN.Client.Model.Gender.NUMBER_1));
            Assert.That(CharacterMapping.ToUpdateDto(unknown).Gender, Is.EqualTo(PotatoVN.Client.Model.Gender.NUMBER_0));
        });
    }

    [Test]
    public void GalgameCharacter_To_CharacterUpdateDto_NullableFieldsBecomeZero()
    {
        // DTO 的 BirthYear/BirthMonth/BirthDay 是非可空 int，源对象的 int? 为 null 时映射为 0
        GalgameCharacter c = MakeCharacter();
        c.BirthYear = null;
        c.BirthDay = null;

        CharacterUpdateDto dto = CharacterMapping.ToUpdateDto(c);

        Assert.Multiple(() =>
        {
            Assert.That(dto.BirthYear, Is.EqualTo(0));
            Assert.That(dto.BirthDay, Is.EqualTo(0));
        });
    }

    [Test]
    public void GalgameCharacter_To_CharacterUpdateDto_IdUnparseableOrMissing_BecomesZero()
    {
        GalgameCharacter missing = MakeCharacter();               // Ids[PotatoVn] 为 null
        GalgameCharacter garbage = MakeCharacter();
        garbage.Ids[(int)RssType.PotatoVn] = "not-a-number";

        Assert.Multiple(() =>
        {
            Assert.That(CharacterMapping.ToUpdateDto(missing).Id, Is.EqualTo(0));
            Assert.That(CharacterMapping.ToUpdateDto(garbage).Id, Is.EqualTo(0));
        });
    }

    [Test]
    public void GalgameCharacter_To_CharacterUpdateDto_EmptyAndNullBoundaries()
    {
        GalgameCharacter c = new()
        {
            Name = "空角色",
            Relation = string.Empty,
            Summary = string.Empty,
            Gender = Gender.Unknown,
            BloodType = null,
            Height = null,
            Weight = null,
            BWH = null,
        };

        CharacterUpdateDto dto = CharacterMapping.ToUpdateDto(c);

        Assert.Multiple(() =>
        {
            Assert.That(dto.Name, Is.EqualTo("空角色"));
            Assert.That(dto.Relation, Is.EqualTo(string.Empty));
            Assert.That(dto.Summary, Is.EqualTo(string.Empty));
            Assert.That(dto.BirthYear, Is.EqualTo(0));
            Assert.That(dto.BirthDay, Is.EqualTo(0));
            Assert.That(dto.BloodType, Is.Null);
            Assert.That(dto.Height, Is.Null);
            Assert.That(dto.Weight, Is.Null);
            Assert.That(dto.ThreeSize, Is.Null);
            Assert.That(dto.Id, Is.EqualTo(0));
            Assert.That(dto.Gender, Is.EqualTo(PotatoVN.Client.Model.Gender.NUMBER_0));
        });
    }

    [Test]
    public void GalgameCharacter_To_CharacterUpdateDto_ImageLocIsNotMappedFromLocalPath()
    {
        // ImageLoc / PreviewImageLoc 由调用方上传文件后回填（PvnSyncTask.cs:395-398），
        // 映射阶段必须是 null，不能被本地 ImagePath / PreviewImagePath 污染。
        GalgameCharacter c = MakeCharacter();

        CharacterUpdateDto dto = CharacterMapping.ToUpdateDto(c);

        Assert.Multiple(() =>
        {
            Assert.That(dto.ImageLoc, Is.Null);
            Assert.That(dto.PreviewImageLoc, Is.Null);
        });
    }

    /// <summary>
    /// 已知缺口（AutoMapper 时代就有，本次不扩大范围）：源属性叫 <c>BirthMon</c>、
    /// DTO 属性叫 <c>BirthMonth</c>，名字不同所以约定映射配不上，profile 里也没有为它写 ForMember。
    /// 这条测试把现状钉住，避免以后误以为已经同步。
    /// </summary>
    [Test]
    public void GalgameCharacter_To_CharacterUpdateDto_BirthMonthIsKnownGap()
    {
        GalgameCharacter c = MakeCharacter();
        c.BirthMon = 7;

        CharacterUpdateDto dto = CharacterMapping.ToUpdateDto(c);

        Assert.That(dto.BirthMonth, Is.EqualTo(0),
            "BirthMon -> BirthMonth 是既有的名字不匹配缺口；修它属于独立的行为变更");
    }

    #endregion

    #region CharacterDto -> GalgameCharacter（同步下行）

    private static CharacterDto MakeDto() => new(777, "角色乙", "配角", "简介乙",
        PotatoVN.Client.Model.Gender.NUMBER_1, 2001, 3, 15, "B", "155", "45", "78/56/82",
        "https://example.com/b.jpg", "https://example.com/b_preview.jpg");

    [Test]
    public void CharacterDto_To_GalgameCharacter_MapsConventionFields()
    {
        GalgameCharacter c = CharacterMapping.ToCharacter(MakeDto());

        Assert.Multiple(() =>
        {
            Assert.That(c.Name, Is.EqualTo("角色乙"));
            Assert.That(c.Relation, Is.EqualTo("配角"));
            Assert.That(c.Summary, Is.EqualTo("简介乙"));
            Assert.That(c.BirthYear, Is.EqualTo(2001));
            Assert.That(c.BirthDay, Is.EqualTo(15));
            Assert.That(c.BloodType, Is.EqualTo("B"));
            Assert.That(c.Height, Is.EqualTo("155"));
            Assert.That(c.Weight, Is.EqualTo("45"));
            Assert.That(c.ImageUrl, Is.EqualTo("https://example.com/b.jpg"));
            Assert.That(c.PreviewImageUrl, Is.EqualTo("https://example.com/b_preview.jpg"));
        });
    }

    [Test]
    public void CharacterDto_To_GalgameCharacter_MapsCustomFields()
    {
        CharacterDto dto = MakeDto();

        GalgameCharacter c = CharacterMapping.ToCharacter(dto);

        // ForMember: character.BWH <- dto.ThreeSize
        Assert.That(c.BWH, Is.EqualTo("78/56/82"));
    }

    [Test]
    public void CharacterDto_To_GalgameCharacter_MapsGenderByValue()
    {
        CharacterDto male = MakeDto();
        SetGender(male, PotatoVN.Client.Model.Gender.NUMBER_2);

        GalgameCharacter c = CharacterMapping.ToCharacter(male);

        Assert.That(c.Gender, Is.EqualTo(Gender.Female));
    }

    [Test]
    public void CharacterDto_To_GalgameCharacter_NullableFieldsBecomeNull()
    {
        CharacterDto dto = new(777, "角色乙", "配角", "简介乙", null, 0, 0, 0, null!, null!, null!, null!, null!, null!);

        GalgameCharacter c = CharacterMapping.ToCharacter(dto);

        Assert.Multiple(() =>
        {
            Assert.That(c.BirthYear, Is.EqualTo(0));
            Assert.That(c.BirthDay, Is.EqualTo(0));
            Assert.That(c.BWH, Is.Null);
            Assert.That(c.BloodType, Is.Null);
            Assert.That(c.Height, Is.Null);
            Assert.That(c.Weight, Is.Null);
            Assert.That(c.Gender, Is.EqualTo(Gender.Unknown));
        });
    }

    [Test]
    public void CharacterDto_To_GalgameCharacter_ImagePathsFallBackToDefault()
    {
        // 本地图片路径由调用方下载完成后回填（PvnSyncTask_PullGame.cs:126-127），
        // 映射阶段必须是默认头像，不能被 DTO 的 URL 污染。
        GalgameCharacter c = CharacterMapping.ToCharacter(MakeDto());

        Assert.Multiple(() =>
        {
            Assert.That(c.ImagePath, Is.EqualTo(Galgame.DefaultCharacterImagePath));
            Assert.That(c.PreviewImagePath, Is.EqualTo(Galgame.DefaultCharacterImagePath));
        });
    }

    [Test]
    public void CharacterDto_To_GalgameCharacter_IdsAreNotTouched()
    {
        GalgameCharacter c = CharacterMapping.ToCharacter(MakeDto());

        Assert.That(c.Ids, Is.Not.Null);
        Assert.That(c.Ids.Length, Is.EqualTo(Galgame.PhraserNumber));
        Assert.That(c.Ids, Has.All.Null);
    }

    [Test]
    public void CharacterDto_To_GalgameCharacter_EmptyStringsStayEmpty()
    {
        CharacterDto dto = new(777, string.Empty, string.Empty, string.Empty, null, 0, 0, 0, null!, null!, null!, null!, null!, null!);

        GalgameCharacter c = CharacterMapping.ToCharacter(dto);

        Assert.Multiple(() =>
        {
            Assert.That(c.Name, Is.EqualTo(string.Empty));
            Assert.That(c.Relation, Is.EqualTo(string.Empty));
            Assert.That(c.Summary, Is.EqualTo(string.Empty));
        });
    }

    /// <summary>
    /// 已知缺口（AutoMapper 时代就有，本次不扩大范围）：DTO 属性叫 <c>BirthMonth</c>、
    /// 模型属性叫 <c>BirthMon</c>，名字不同所以约定映射配不上。与上行方向的缺口是同一条。
    /// </summary>
    [Test]
    public void CharacterDto_To_GalgameCharacter_BirthMonIsKnownGap()
    {
        CharacterDto dto = new(777, "角色乙", "配角", "简介乙", null, 0, 9, 0, null!, null!, null!, null!, null!, null!);

        GalgameCharacter c = CharacterMapping.ToCharacter(dto);

        Assert.That(c.BirthMon, Is.Null);
    }

    #endregion

    #region 往返

    [Test]
    public void Character_RoundTrip_PreservesBusinessFields()
    {
        GalgameCharacter original = MakeCharacter();
        original.Ids[(int)RssType.PotatoVn] = "9999";

        CharacterUpdateDto up = CharacterMapping.ToUpdateDto(original);

        // 下行用 CharacterDto（另一个类型）等价还原后再比业务字段
        CharacterDto down = new(up.Id, up.Name!, up.Relation!, up.Summary!, up.Gender,
            up.BirthYear, up.BirthMonth, up.BirthDay, up.BloodType, up.Height, up.Weight,
            up.ThreeSize!, null!, null!);
        GalgameCharacter back = CharacterMapping.ToCharacter(down);

        Assert.Multiple(() =>
        {
            Assert.That(back.Name, Is.EqualTo(original.Name));
            Assert.That(back.Relation, Is.EqualTo(original.Relation));
            Assert.That(back.Summary, Is.EqualTo(original.Summary));
            Assert.That(back.BWH, Is.EqualTo(original.BWH));
            Assert.That(back.BirthYear, Is.EqualTo(original.BirthYear));
            Assert.That(back.BirthDay, Is.EqualTo(original.BirthDay));
            Assert.That(back.BloodType, Is.EqualTo(original.BloodType));
            Assert.That(back.Height, Is.EqualTo(original.Height));
            Assert.That(back.Weight, Is.EqualTo(original.Weight));
            Assert.That(back.Gender, Is.EqualTo(original.Gender));
        });
    }

    #endregion

    private static void SetGender(CharacterDto dto, PotatoVN.Client.Model.Gender value) => dto.Gender = value;
}
