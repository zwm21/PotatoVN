using System.IO;
using GalgameManager.Helpers;

namespace GalgameManager.Test.Helpers;

/// <summary>
/// DesktopShortcutHelper 测试。用临时目录创建真实 .lnk 并校验，测完即清理。<br/>
/// 这些用例同时是「显式 ComImport 互操作可用」的回归保护——它取代了原先
/// dynamic 调 WScript.Shell 的实现（后者在 PublishTrimmed 下会让便携版闪退）。
/// </summary>
[TestFixture]
public class DesktopShortcutHelperTest
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pvn_lnk_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    [Test]
    public void Create_ShouldWriteLnkFile_WithResolvableTarget()
    {
        string lnk = Path.Combine(_tempDir, "PotatoVN.lnk");
        // 目标指向本测试程序集即可，IShellLinkW 不校验目标是否存在
        string target = typeof(DesktopShortcutHelperTest).Assembly.Location;

        DesktopShortcutHelper.Create(lnk, target, workingDirectory: _tempDir, description: "PotatoVN");

        Assert.That(File.Exists(lnk), Is.True, "快捷方式文件应当被创建");
        Assert.That(new FileInfo(lnk).Length, Is.GreaterThan(0), ".lnk 不应是空文件");
        Assert.That(DesktopShortcutHelper.TryGetTargetPath(lnk), Is.EqualTo(target),
            "读回的目标路径应与写入的一致");
    }

    [Test]
    public void Create_ShouldOverwriteExistingFile()
    {
        string lnk = Path.Combine(_tempDir, "PotatoVN.lnk");
        string first = typeof(DesktopShortcutHelperTest).Assembly.Location;
        string second = Path.Combine(_tempDir, "other.exe");

        DesktopShortcutHelper.Create(lnk, first);
        DesktopShortcutHelper.Create(lnk, second);

        Assert.That(File.Exists(lnk), Is.True);
        Assert.That(DesktopShortcutHelper.TryGetTargetPath(lnk), Is.EqualTo(second),
            "第二次创建应覆盖第一次的内容");
    }

    [Test]
    public void Create_ShouldRejectBlankArguments()
    {
        Assert.Throws<ArgumentException>(() =>
            DesktopShortcutHelper.Create(string.Empty, typeof(DesktopShortcutHelperTest).Assembly.Location));
        Assert.Throws<ArgumentException>(() =>
            DesktopShortcutHelper.Create(Path.Combine(_tempDir, "a.lnk"), "   "));
    }

    [Test]
    public void TryGetTargetPath_ShouldReturnNull_WhenFileMissing()
    {
        Assert.That(DesktopShortcutHelper.TryGetTargetPath(Path.Combine(_tempDir, "nope.lnk")), Is.Null);
    }

    [Test]
    public void TryGetTargetPath_ShouldReturnNull_WhenFileIsNotAShortcut()
    {
        string junk = Path.Combine(_tempDir, "junk.lnk");
        File.WriteAllText(junk, "this is not a shell link");
        Assert.That(DesktopShortcutHelper.TryGetTargetPath(junk), Is.Null);
    }
}
