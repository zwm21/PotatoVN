using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Models.Sources;
using GalgameManager.Services;
using LiteDB;
using NUnit.Framework;

namespace GalgameManager.Test.Helpers;

/// <summary>
/// 图片路径重定位：便携包移动/重命名/跨版本升级、或从其他机器恢复备份后，
/// LiteDB 里焊死的旧绝对路径会全部失联。这些测试锁定重定位的判定与改写规则。
/// </summary>
[TestFixture]
public class ImagePathRelocatorTest
{
    private string _dir = null!;
    private string _imagesDir = null!;

    [SetUp]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PotatoVN.Test", "ImagePathRelocator",
            Guid.NewGuid().ToString("N"));
        _imagesDir = Path.Combine(_dir, "Images");
        Directory.CreateDirectory(_imagesDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private void TouchImage(string relativePath)
    {
        string full = Path.Combine(_imagesDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, [1, 2, 3]);
    }

    #region 纯字符串判定规则

    [Test]
    public void IsManagedImagePath_RecognizesPortableAndMsixLayouts()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ImagePathRelocator.IsManagedImagePath(
                @"E:\anywhere\win-x64\UserData\Images\a.jpg"), Is.True);
            Assert.That(ImagePathRelocator.IsManagedImagePath(
                @"C:\Users\x\AppData\Local\Packages\PotatoVN_xxx\LocalState\Images\a.jpg"), Is.True);
            Assert.That(ImagePathRelocator.IsManagedImagePath(
                @"E:\github\PotatoVN\publish\PotatoVN-1.10.4.5-x64-portable\UserData\Images\a.jpg"), Is.True);
            // 非托管：ms-appx、默认值、空
            Assert.That(ImagePathRelocator.IsManagedImagePath("ms-appx:///Assets/WindowIcon.ico"), Is.False);
            Assert.That(ImagePathRelocator.IsManagedImagePath(Galgame.DefaultImagePath), Is.False);
            Assert.That(ImagePathRelocator.IsManagedImagePath(Galgame.DefaultCharacterImagePath), Is.False);
            Assert.That(ImagePathRelocator.IsManagedImagePath(null), Is.False);
            Assert.That(ImagePathRelocator.IsManagedImagePath(string.Empty), Is.False);
            // 外部目录即使含 Images 段也算"疑似托管"：由 target 是否存在来兜底防误伤，
            // 见 RelocatePath_ExternalImagesLikePath_TargetMissing_KeepsOriginal
            Assert.That(ImagePathRelocator.IsManagedImagePath(@"D:\MyPics\Images\a.jpg"), Is.True);
        });
    }

    [Test]
    public void IsManagedImagePath_RejectsSimilarDirectoryNames()
    {
        Assert.Multiple(() =>
        {
            // Images2 / MyImages / ImagesX 都不是 Images 目录段
            Assert.That(ImagePathRelocator.IsManagedImagePath(@"D:\Images2\a.jpg"), Is.False);
            Assert.That(ImagePathRelocator.IsManagedImagePath(@"D:\MyImages\a.jpg"), Is.False);
            Assert.That(ImagePathRelocator.IsManagedImagePath(@"D:\ImagesX\a.jpg"), Is.False);
            Assert.That(ImagePathRelocator.IsManagedImagePath(@"D:\x\Images_backup\a.jpg"), Is.False);
        });
    }

    [Test]
    public void ExtractRelativeUnderImages_TakesRightmostImagesSegment()
    {
        // 多段 Images：必须从右往左取，不能盲取第一个匹配
        Assert.That(ImagePathRelocator.ExtractRelativeUnderImages(
            @"D:\Images\backup\UserData\Images\sub\a.jpg"), Is.EqualTo(@"sub\a.jpg"));
        Assert.That(ImagePathRelocator.ExtractRelativeUnderImages(
            @"D:\a\Images\b\Images\c.jpg"), Is.EqualTo(@"c.jpg"));
        Assert.That(ImagePathRelocator.ExtractRelativeUnderImages(
            @"/home/u/Images/x.png"), Is.EqualTo("x.png"));
        Assert.That(ImagePathRelocator.ExtractRelativeUnderImages(@"D:\noImages\a.jpg"), Is.Null);
    }

    [Test]
    public void IsUnderCurrentImages_UsesFullPathAndOrdinalIgnoreCase()
    {
        string current = Path.Combine(_dir, "Images");
        Assert.Multiple(() =>
        {
            // 同目录（大小写不同）算已在下
            Assert.That(ImagePathRelocator.IsUnderCurrentImages(
                Path.Combine(_dir, "images", "a.jpg"), current), Is.True);
            Assert.That(ImagePathRelocator.IsUnderCurrentImages(
                Path.Combine(_dir, "IMAGES", "a.jpg"), current), Is.True);
            // Images2 不得被前缀误判
            Assert.That(ImagePathRelocator.IsUnderCurrentImages(
                Path.Combine(_dir, "Images2", "a.jpg"), current), Is.False);
            Assert.That(ImagePathRelocator.IsUnderCurrentImages(
                Path.Combine(_dir, "ImagesBackup", "a.jpg"), current), Is.False);
            // 目录本身不算"在目录下"
            Assert.That(ImagePathRelocator.IsUnderCurrentImages(current, current), Is.False);
            // 未规范化的相对段也应被判定
            Assert.That(ImagePathRelocator.IsUnderCurrentImages(
                Path.Combine(_dir, "Images", "..", "Images", "a.jpg"), current), Is.True);
        });
    }

    #endregion

    #region RelocatePath 单值规则

    private string? Relocate(string? path) =>
        ImagePathRelocator.RelocatePath(path, _imagesDir);

    [Test]
    public void RelocatePath_RewritesPortableOldPath_ToCurrentImages()
    {
        TouchImage("a.jpg");
        Assert.That(Relocate(@"E:\github\testing\win-x64\UserData\Images\a.jpg"),
            Is.EqualTo(Path.Combine(_imagesDir, "a.jpg")));
    }

    [Test]
    public void RelocatePath_RewritesMsixLocalStatePath_ToCurrentImages()
    {
        TouchImage("a.jpg");
        Assert.That(Relocate(
            @"C:\Users\x\AppData\Local\Packages\PotatoVN_abc\LocalState\Images\a.jpg"),
            Is.EqualTo(Path.Combine(_imagesDir, "a.jpg")));
    }

    [Test]
    public void RelocatePath_RewritesArbitraryPortableRoot()
    {
        TouchImage("a.jpg");
        Assert.That(Relocate(@"Z:\other\place\UserData\Images\a.jpg"),
            Is.EqualTo(Path.Combine(_imagesDir, "a.jpg")));
    }

    [Test]
    public void RelocatePath_TakesRightmostImagesSegment()
    {
        TouchImage(@"sub\a.jpg");
        Assert.That(Relocate(@"D:\Images\backup\UserData\Images\sub\a.jpg"),
            Is.EqualTo(Path.Combine(_imagesDir, "sub", "a.jpg")));
    }

    [Test]
    public void RelocatePath_OldPathStillExistsOnDisk_StillRewrites()
    {
        // 旧绝对路径在磁盘上依然存在也必须重写：否则跨版本升级后会删错图/失联
        string legacyDir = Path.Combine(_dir, "legacy", "Images");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllBytes(Path.Combine(legacyDir, "a.jpg"), [9]);
        TouchImage("a.jpg");

        Assert.That(Relocate(Path.Combine(legacyDir, "a.jpg")),
            Is.EqualTo(Path.Combine(_imagesDir, "a.jpg")));
    }

    [Test]
    public void RelocatePath_AlreadyUnderCurrentImages_Skips()
    {
        TouchImage("a.jpg");
        string current = Path.Combine(_imagesDir, "a.jpg");
        Assert.That(Relocate(current), Is.EqualTo(current));
    }

    [Test]
    public void RelocatePath_TargetMissing_KeepsOriginal()
    {
        // 当前 Images 下没有同名文件 → 保持原值不动（保守策略，防误伤外部图）
        string external = @"D:\MyPics\Images\only-here.jpg";
        Assert.That(Relocate(external), Is.EqualTo(external));
        Assert.That(Relocate(@"E:\old\win-x64\UserData\Images\gone.jpg"),
            Is.EqualTo(@"E:\old\win-x64\UserData\Images\gone.jpg"));
    }

    [Test]
    public void RelocatePath_ExternalAbsolutePathWithoutImagesSegment_Untouched()
    {
        TouchImage("a.jpg");
        const string external = @"D:\MyPics\a.jpg";
        Assert.That(Relocate(external), Is.EqualTo(external));
    }

    [TestCase("ms-appx:///Assets/WindowIcon.ico")]
    [TestCase("ms-appx:///Assets/default_character.jpg")]
    public void RelocatePath_MsAppxAndDefaults_Untouched(string path)
    {
        Assert.That(Relocate(path), Is.EqualTo(path));
    }

    [Test]
    public void RelocatePath_NullOrEmpty_ReturnsAsIs()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Relocate(null), Is.Null);
            Assert.That(Relocate(string.Empty), Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void RelocatePath_RelativePath_Untouched()
    {
        // .potatovn\meta.json 那条链存的是相对路径，与本地 Images 无关
        const string relative = @".\a.jpg";
        Assert.That(Relocate(relative), Is.EqualTo(relative));
    }

    #endregion

    #region 集成测试：真实实体往返，禁止手拼 BsonDocument

    private LiteDatabase OpenDb(string name)
    {
        BsonMapper.Global.EnumAsInteger = true;
        return new LiteDatabase(Path.Combine(_dir, name));
    }

    private static Galgame MakeGalgame(string legacyRoot)
    {
        Galgame game = new("测试游戏")
        {
            Uuid = Guid.NewGuid(),
        };
        game.ImagePath = new LockableProperty<string>(Path.Combine(legacyRoot, "cover.jpg"));
        game.HeaderImagePath = new LockableProperty<string?>(Path.Combine(legacyRoot, "header.png"));
        game.Characters.Add(new GalgameCharacter
        {
            Name = "角色甲",
            ImagePath = Path.Combine(legacyRoot, "char1.jpg"),
            PreviewImagePath = Path.Combine(legacyRoot, "char1_preview.jpg"),
        });
        return game;
    }

    [Test]
    public void Relocate_RealGalgameRoundTrip_RewritesAllFieldsAndPreservesIsLock()
    {
        string legacyRoot = Path.Combine(_dir, "legacy", "Images");
        Directory.CreateDirectory(legacyRoot);

        // 当前 Images 下放入目标文件
        TouchImage("cover.jpg");
        TouchImage("header.png");
        TouchImage("char1.jpg");
        TouchImage("char1_preview.jpg");

        Galgame game = MakeGalgame(legacyRoot);
        game.ImagePath.IsLock = true; // 锁定的字段也必须保住 IsLock

        using (LiteDatabase db = OpenDb("gal.db"))
        {
            db.GetCollection<Galgame>("galgame").Insert(game);

            ImagePathRelocator.Relocate(db, _imagesDir);

            Galgame? read = db.GetCollection<Galgame>("galgame").FindById(game.Uuid);
            Assert.That(read, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(read!.ImagePath.Value, Is.EqualTo(Path.Combine(_imagesDir, "cover.jpg")));
                Assert.That(read.ImagePath.IsLock, Is.True, "IsLock 必须保留，不能被压扁成纯字符串");
                Assert.That(read.HeaderImagePath.Value, Is.EqualTo(Path.Combine(_imagesDir, "header.png")));
                Assert.That(read.Characters[0].ImagePath, Is.EqualTo(Path.Combine(_imagesDir, "char1.jpg")));
                Assert.That(read.Characters[0].PreviewImagePath,
                    Is.EqualTo(Path.Combine(_imagesDir, "char1_preview.jpg")));
            });
        }
    }

    [Test]
    public void Relocate_RealStaffAndCategory_RewritesPlainStringFields()
    {
        string legacyRoot = Path.Combine(_dir, "legacy", "Images");
        Directory.CreateDirectory(legacyRoot);
        TouchImage("staff.jpg");
        TouchImage("cat.jpg");

        Staff staff = new() { JapaneseName = " Staff 甲" };
        staff.ImagePath = Path.Combine(legacyRoot, "staff.jpg");
        Category category = new() { Name = "分类甲" };
        category.ImagePath = Path.Combine(legacyRoot, "cat.jpg");

        using (LiteDatabase db = OpenDb("sc.db"))
        {
            db.GetCollection<Staff>("staff").Insert(staff);
            db.GetCollection<Category>("category").Insert(category);

            ImagePathRelocator.Relocate(db, _imagesDir);

            Staff? readStaff = db.GetCollection<Staff>("staff").FindAll().FirstOrDefault();
            Category? readCat = db.GetCollection<Category>("category").FindById(category.Id);
            Assert.Multiple(() =>
            {
                Assert.That(readStaff!.ImagePath, Is.EqualTo(Path.Combine(_imagesDir, "staff.jpg")));
                Assert.That(readCat!.ImagePath, Is.EqualTo(Path.Combine(_imagesDir, "cat.jpg")));
            });
        }
    }

    [Test]
    public void Relocate_DoesNotTouchSourcePaths()
    {
        TouchImage("a.jpg");
        GalgameFolderSource source = new(Path.Combine(_dir, "library"))
        {
            Path = Path.Combine(_dir, "legacy", "Images"),
        };
        source.DontScanPath.Add(Path.Combine(_dir, "legacy", "Images", "skip"));

        using (LiteDatabase db = OpenDb("src.db"))
        {
            db.GetCollection<GalgameSourceBase>("source").Insert(source);

            ImagePathRelocator.Relocate(db, _imagesDir);

            GalgameSourceBase? read = db.GetCollection<GalgameSourceBase>("source").FindById(source.Id);
            Assert.Multiple(() =>
            {
                Assert.That(read!.Path, Is.EqualTo(source.Path), "游戏库路径绝不能被重定位");
                Assert.That(read.DontScanPath, Is.EqualTo(source.DontScanPath));
            });
        }
    }

    [Test]
    public void Relocate_Idempotent_SecondRunWritesNothing()
    {
        string legacyRoot = Path.Combine(_dir, "legacy", "Images");
        Directory.CreateDirectory(legacyRoot);
        TouchImage("cover.jpg");

        Galgame game = MakeGalgame(legacyRoot);
        game.ImagePath = new LockableProperty<string>(Path.Combine(legacyRoot, "cover.jpg"));

        using (LiteDatabase db = OpenDb("idem.db"))
        {
            ILiteCollection<Galgame> coll = db.GetCollection<Galgame>("galgame");
            coll.Insert(game);

            ImagePathRelocator.Relocate(db, _imagesDir);
            Galgame afterFirst = coll.FindById(game.Uuid)!;
            string expected = Path.Combine(_imagesDir, "cover.jpg");
            Assert.That(afterFirst.ImagePath.Value, Is.EqualTo(expected));

            // 第二次运行：值已正确，不得产生任何变更（含 IsLock 等周边字段一并校验）
            ImagePathRelocator.Relocate(db, _imagesDir);
            Galgame afterSecond = coll.FindById(game.Uuid)!;
            Assert.Multiple(() =>
            {
                Assert.That(afterSecond.ImagePath.Value, Is.EqualTo(expected));
                Assert.That(afterSecond.ImagePath.IsLock, Is.EqualTo(afterFirst.ImagePath.IsLock));
                Assert.That(afterSecond.HeaderImagePath.Value, Is.EqualTo(afterFirst.HeaderImagePath.Value));
                Assert.That(afterSecond.Characters.Count, Is.EqualTo(afterFirst.Characters.Count));
            });
        }
    }

    [Test]
    public void Relocate_NoChangesAtAll_ZeroWrites()
    {
        TouchImage("a.jpg");
        Galgame game = new("干净的");
        game.ImagePath = new LockableProperty<string>(Path.Combine(_imagesDir, "a.jpg"));

        using (LiteDatabase db = OpenDb("noop.db"))
        {
            db.GetCollection<Galgame>("galgame").Insert(game);

            ImagePathRelocator.Relocate(db, _imagesDir);

            Galgame? read = db.GetCollection<Galgame>("galgame").FindById(game.Uuid);
            Assert.Multiple(() =>
            {
                Assert.That(read!.ImagePath.Value, Is.EqualTo(Path.Combine(_imagesDir, "a.jpg")));
                Assert.That(read.Name.Value, Is.EqualTo("干净的"), "无关字段不得被改动");
            });
        }
    }

    [Test]
    public void Relocate_MissingImageFile_KeepsOriginalValue()
    {
        // 备份里路径指向旧机器，且当前 Images 没有对应文件 → 保持原值
        Galgame game = new("丢图");
        game.ImagePath = new LockableProperty<string>(
            @"E:\old\win-x64\UserData\Images\missing.jpg");

        using (LiteDatabase db = OpenDb("missing.db"))
        {
            db.GetCollection<Galgame>("galgame").Insert(game);
            ImagePathRelocator.Relocate(db, _imagesDir);

            Galgame? read = db.GetCollection<Galgame>("galgame").FindById(game.Uuid);
            Assert.That(read!.ImagePath.Value, Is.EqualTo(@"E:\old\win-x64\UserData\Images\missing.jpg"));
        }
    }

    [Test]
    public void Relocate_EmptyCollections_DoesNotThrow()
    {
        using (LiteDatabase db = OpenDb("empty.db"))
        {
            Assert.DoesNotThrow(() => ImagePathRelocator.Relocate(db, _imagesDir));
        }
    }

    [Test]
    public void Relocate_MalformedFieldTypes_DoesNotThrowAndSkipsThem()
    {
        // 坏文档：ImagePath 是数字/数组/null，Characters 里混入非文档项。
        // 单个坏字段绝不能让整个应用启动失败（那会触发安全模式重启）。
        using (LiteDatabase db = OpenDb("malformed.db"))
        {
            ILiteCollection<BsonDocument> coll = db.GetCollection<BsonDocument>("galgame");
            coll.Insert(new BsonDocument { ["_id"] = Guid.NewGuid(), ["ImagePath"] = 12345 });
            coll.Insert(new BsonDocument { ["_id"] = Guid.NewGuid(), ["ImagePath"] = new BsonArray() });
            coll.Insert(new BsonDocument { ["_id"] = Guid.NewGuid(), ["ImagePath"] = BsonValue.Null });
            coll.Insert(new BsonDocument
            {
                ["_id"] = Guid.NewGuid(),
                ["Characters"] = new BsonArray { "not-a-document", 42, BsonValue.Null },
            });

            Assert.DoesNotThrow(() => ImagePathRelocator.Relocate(db, _imagesDir));
        }
    }

    [Test]
    public void Relocate_BadDocument_DoesNotPreventGoodDocumentsFromBeingRelocated()
    {
        // 坏文档与好文档混在一起：好文档必须照样被重定位
        TouchImage("good.jpg");
        string legacyRoot = Path.Combine(_dir, "legacy", "Images");
        Directory.CreateDirectory(legacyRoot);

        Galgame good = new("好文档");
        good.ImagePath = new LockableProperty<string>(Path.Combine(legacyRoot, "good.jpg"));

        using (LiteDatabase db = OpenDb("mixed.db"))
        {
            db.GetCollection<Galgame>("galgame").Insert(good);
            ILiteCollection<BsonDocument> raw = db.GetCollection<BsonDocument>("galgame");
            raw.Insert(new BsonDocument { ["_id"] = Guid.NewGuid(), ["ImagePath"] = 999 });

            Assert.DoesNotThrow(() => ImagePathRelocator.Relocate(db, _imagesDir));

            Galgame? read = db.GetCollection<Galgame>("galgame").FindById(good.Uuid);
            Assert.That(read!.ImagePath.Value, Is.EqualTo(Path.Combine(_imagesDir, "good.jpg")),
                "坏文档不得妨碍好文档被重定位");
        }
    }

    [Test]
    public void Relocate_MissingCollections_DoesNotThrow()
    {
        // 数据库里只有 source 集合，没有任何图片集合
        using (LiteDatabase db = OpenDb("onlysource.db"))
        {
            db.GetCollection<BsonDocument>("source").Insert(new BsonDocument { ["_id"] = Guid.NewGuid() });
            Assert.DoesNotThrow(() => ImagePathRelocator.Relocate(db, _imagesDir));
        }
    }

    #endregion
}
