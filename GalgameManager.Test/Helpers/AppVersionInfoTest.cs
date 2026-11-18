using GalgameManager.Helpers;

namespace GalgameManager.Test.Helpers;

/// <summary>
/// 动态版本测试。资源行格式为 <c>base|commits|commit|[delta]</c>，
/// 其中 delta 为工作区未提交改动的 8 位 SHA256，干净时为空。
/// </summary>
[TestFixture]
public class AppVersionInfoTest
{
    private const string BaseVersion = "1.10.2.1";
    private const string CommitHash = "3f91c2";

    [Test]
    public void Parse_CleanGit_HasNoDelta()
    {
        AppVersionInfo? info = AppVersionInfo.TryParse($"{BaseVersion}|1234|{CommitHash}|");

        Assert.That(info, Is.Not.Null);
        Assert.That(info!.BaseVersion, Is.EqualTo(BaseVersion));
        Assert.That(info.CommitCount, Is.EqualTo(1234));
        Assert.That(info.CommitHash, Is.EqualTo(CommitHash));
        Assert.That(info.Delta, Is.Null.Or.Empty);
        Assert.That(info.HasGit, Is.True);
        Assert.That(info.ToLogLine(), Is.EqualTo($"{BaseVersion}+r1234.g{CommitHash}"));
        Assert.That(info.ToDisplayVersion(), Is.EqualTo("v1.10.1234"));
    }

    [Test]
    public void Parse_DirtyGitStateA_AppendsDelta()
    {
        AppVersionInfo? info = AppVersionInfo.TryParse($"{BaseVersion}|1234|{CommitHash}|4a1b2c3d");

        Assert.That(info!.Delta, Is.EqualTo("4a1b2c3d"));
        Assert.That(info.ToLogLine(), Is.EqualTo($"{BaseVersion}+r1234.g{CommitHash}+4a1b2c3d"));
        Assert.That(info.ToDisplayVersion(), Is.EqualTo("v1.10.1234"));
    }

    [Test]
    public void Parse_DirtyGitStateB_AppendsDifferentDelta()
    {
        AppVersionInfo? stateA = AppVersionInfo.TryParse($"{BaseVersion}|1234|{CommitHash}|4a1b2c3d");
        AppVersionInfo? stateB = AppVersionInfo.TryParse($"{BaseVersion}|1234|{CommitHash}|9f8e7d6c");

        Assert.That(stateB!.ToLogLine(), Is.Not.EqualTo(stateA!.ToLogLine()));
        Assert.That(stateB.ToLogLine(), Is.EqualTo($"{BaseVersion}+r1234.g{CommitHash}+9f8e7d6c"));
        // 脏状态只影响日志行，不影响 UI 显示的提交数
        Assert.That(stateB.ToDisplayVersion(), Is.EqualTo(stateA.ToDisplayVersion()));
    }

    [Test]
    public void Parse_NoGit_UsesSourceFingerprint()
    {
        AppVersionInfo? info = AppVersionInfo.TryParse($"{BaseVersion}|0||");

        Assert.That(info!.HasGit, Is.False);
        Assert.That(info.CommitCount, Is.EqualTo(0));
        Assert.That(info.ToLogLine(), Does.StartWith($"{BaseVersion}+src."));

        // 指纹为 8 位小写十六进制，且只由 base 决定（无时间戳/随机量，可复现）
        string fingerprint = info.ToLogLine()[$"{BaseVersion}+src.".Length..];
        Assert.That(fingerprint, Does.Match("^[0-9a-f]{8}$"));
        Assert.That(AppVersionInfo.TryParse($"{BaseVersion}|0||")!.ToLogLine(), Is.EqualTo(info.ToLogLine()));
        Assert.That(AppVersionInfo.TryParse("1.11.0.0|0||")!.ToLogLine(), Is.Not.EqualTo(info.ToLogLine()));

        // UI 末位取指纹哈希值
        Assert.That(info.ToDisplayVersion(), Does.Match("^v1\\.10\\.(\\d{1,5})$"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("garbage")]
    [TestCase("1.10.2.1")]
    [TestCase("1.10.2.1|1234|3f91c2|4a1b2c3d|extra")]
    [TestCase("1.10.2.1|abc|3f91c2|")]
    [TestCase("1.10.2.1|-1|3f91c2|")]
    [TestCase("|1234|3f91c2|")]
    [TestCase("1.10.2.1|12||")] // 有提交数却没有 hash
    [TestCase("1.10.2.1|0|3f91c2|")] // 有 hash 却 0 提交
    [TestCase("1.10.2.1|12|xyz|")] // hash 非十六进制
    [TestCase("1.10.2.1|12|3f91c2|zzzz")] // delta 非十六进制
    [TestCase("1.10.2.1|12|3f91c2|4a1b2c")] // delta 长度不足
    public void TryParse_CorruptResource_ReturnsNull(string? line)
    {
        Assert.That(AppVersionInfo.TryParse(line), Is.Null);
    }

    [Test]
    public void LoadFrom_MissingOrCorruptResource_FallsBackWithoutThrowing()
    {
        AppVersionInfo missing = AppVersion.LoadFrom(null, BaseVersion);

        Assert.That(missing.ToLogLine(), Is.EqualTo($"{BaseVersion}+src.unknown"));
        Assert.That(missing.ToDisplayVersion(), Is.EqualTo("v1.10.0"));

        AppVersionInfo corrupt = AppVersion.LoadFrom("not-a-version-line", BaseVersion);

        Assert.That(corrupt.ToLogLine(), Is.EqualTo($"{BaseVersion}+src.unknown"));
        Assert.That(corrupt.ToDisplayVersion(), Is.EqualTo("v1.10.0"));
    }

    [Test]
    public void ToResourceLine_RoundTrips()
    {
        AppVersionInfo info = AppVersionInfo.TryParse($"{BaseVersion}|1234|{CommitHash}|4a1b2c3d")!;

        Assert.That(info.ToResourceLine(), Is.EqualTo($"{BaseVersion}|1234|{CommitHash}|4a1b2c3d"));
        Assert.That(AppVersionInfo.TryParse(info.ToResourceLine())!.ToLogLine(), Is.EqualTo(info.ToLogLine()));
    }

    [Test]
    public void Current_ReadsEmbeddedResource_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => { _ = AppVersion.Current; });

        // base 版本始终来自程序集/manifest 版本，资源缺失时也不会丢失
        Assert.That(AppVersion.Current.BaseVersion, Is.EqualTo(RuntimeHelper.GetVersion()));
        Assert.That(AppVersion.DisplayVersion, Is.Not.Empty);
        Assert.That(AppVersion.LogLine, Is.Not.Empty);

        // 构建期写入了资源（说明构建机有 git）时，运行期必须解析出 git 信息而不是回退
        if (typeof(AppVersion).Assembly.GetManifestResourceNames().Contains(AppVersion.ResourceName))
            Assert.That(AppVersion.Current.HasGit, Is.True, "构建期写入了版本资源，但运行期没有解析出来");
    }
}
