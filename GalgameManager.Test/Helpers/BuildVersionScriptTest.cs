using System.Diagnostics;
using System.Text;
using GalgameManager.Helpers;

namespace GalgameManager.Test.Helpers;

/// <summary>
/// 构建期采集脚本 <c>BuildScripts/Get-BuildVersionInfo.ps1</c> 的契约测试：
/// 用临时 git 仓库模拟干净 / 两种不同脏状态 / 无 git 四种场景，
/// 校验写出的资源行能被运行时正确解析，且不含时间戳等不可复现量。
/// </summary>
[TestFixture]
[Category("E2E")]
public class BuildVersionScriptTest
{
    private const string BaseVersion = "1.2.3.4";
    private const string ScriptRelativePath = @"GalgameManager\BuildScripts\Get-BuildVersionInfo.ps1";

    private string _scriptPath = null!;
    private string _workRoot = null!;
    private string _powerShell = null!;

    [SetUp]
    public void SetUp()
    {
        if (!TryRun("git", "--version") || (!TryRun("pwsh", "-Command exit") && !TryRun("powershell.exe", "-Command exit")))
            Assert.Ignore("需要 git 与 PowerShell（pwsh 或 powershell.exe）才能运行构建期脚本测试");
        _powerShell = ResolvePowerShell();
        _scriptPath = FindScriptPath();
        _workRoot = Path.Combine(Path.GetTempPath(), "potatovn-buildversion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (_workRoot != null! && Directory.Exists(_workRoot)) DeleteDirectory(_workRoot);
    }

    [Test]
    public void Collect_CleanRepo_WritesResourceWithoutDelta()
    {
        string repo = CreateRepo();
        WriteAndCommit(repo, "b.txt", "b");

        AppVersionInfo? info = AppVersionInfo.TryParse(Collect(repo));

        Assert.That(info, Is.Not.Null);
        Assert.That(info!.BaseVersion, Is.EqualTo(BaseVersion));
        Assert.That(info.CommitCount, Is.EqualTo(2));
        Assert.That(info.CommitHash, Is.EqualTo(RunGit(repo, "rev-parse --short=7 HEAD").Trim()));
        Assert.That(info.Delta, Is.Null.Or.Empty);
        Assert.That(info.ToLogLine(), Is.EqualTo($"{BaseVersion}+r2.g{info.CommitHash}"));
        Assert.That(info.ToDisplayVersion(), Is.EqualTo($"v{BaseVersion}"));
    }

    [Test]
    public async Task Collect_BuildNumberIncrementsPerCommit()
    {
        string repo = CreateRepo();
        string manifest = Path.Combine(repo, "Package.appxmanifest");

        // 改动基础版本的那次提交：构建号 0，动态版本即 manifest 版本本身
        WriteAndCommit(repo, "Package.appxmanifest", "<Package/>");
        AppVersionInfo atBump = AppVersionInfo.TryParse(await CollectAsync(repo, manifest))!;
        Assert.That(atBump.BaseVersion, Is.EqualTo(BaseVersion));

        // 之后每提交一次第 4 段 +1（1.2.3.4 -> 1.2.3.5 -> 1.2.3.6）
        WriteAndCommit(repo, "b.txt", "b");
        AppVersionInfo first = AppVersionInfo.TryParse(await CollectAsync(repo, manifest))!;
        Assert.That(first.BaseVersion, Is.EqualTo("1.2.3.5"));
        Assert.That(first.ToDisplayVersion(), Is.EqualTo("v1.2.3.5"));

        WriteAndCommit(repo, "c.txt", "c");
        AppVersionInfo second = AppVersionInfo.TryParse(await CollectAsync(repo, manifest))!;
        Assert.That(second.BaseVersion, Is.EqualTo("1.2.3.6"));
    }

    [Test]
    public void Collect_DifferentDirtyStates_ProduceDifferentDeltas()
    {
        string repo = CreateRepo();

        // 状态 A：修改已跟踪文件
        File.WriteAllText(Path.Combine(repo, "a.txt"), "changed-a");
        AppVersionInfo stateA = AppVersionInfo.TryParse(Collect(repo))!;

        // 状态 B：还原上面的改动，改为新增未跟踪文件
        File.WriteAllText(Path.Combine(repo, "a.txt"), "original");
        File.WriteAllText(Path.Combine(repo, "untracked.txt"), "changed-b");
        AppVersionInfo stateB = AppVersionInfo.TryParse(Collect(repo))!;

        Assert.That(stateA.Delta, Does.Match("^[0-9a-f]{8}$"));
        Assert.That(stateB.Delta, Does.Match("^[0-9a-f]{8}$"));
        Assert.That(stateB.Delta, Is.Not.EqualTo(stateA.Delta));
        Assert.That(stateA.ToLogLine(), Does.EndWith("+" + stateA.Delta));
        Assert.That(stateB.ToLogLine(), Does.EndWith("+" + stateB.Delta));
    }

    [Test]
    public void Collect_DirectoryWithoutGit_WritesEmptyGitFields()
    {
        string dir = Path.Combine(_workRoot, "no-git");
        Directory.CreateDirectory(dir);

        AppVersionInfo? info = AppVersionInfo.TryParse(Collect(dir));

        Assert.That(info, Is.Not.Null);
        Assert.That(info!.HasGit, Is.False);
        Assert.That(info.CommitCount, Is.EqualTo(0));
        Assert.That(info.ToLogLine(), Does.StartWith($"{BaseVersion}+src."));
    }

    [Test]
    public void Collect_BuildOutputAndIgnoredFiles_AreExcludedFromDelta()
    {
        string repo = CreateRepo();
        WriteAndCommit(repo, ".gitignore", "ignored.txt\n");

        // Build/ 未被 gitignore（等价于仓库里的 MSIX 输出目录），也应被脚本排除
        Directory.CreateDirectory(Path.Combine(repo, "Build"));
        File.WriteAllText(Path.Combine(repo, "Build", "pkg.msix"), "binary");
        File.WriteAllText(Path.Combine(repo, "ignored.txt"), "ignored");

        Assert.That(AppVersionInfo.TryParse(Collect(repo))!.Delta, Is.Null.Or.Empty);

        // 对照组：真实的未跟踪源码文件必须被算进 delta
        File.WriteAllText(Path.Combine(repo, "real.txt"), "real");
        Assert.That(AppVersionInfo.TryParse(Collect(repo))!.Delta, Does.Match("^[0-9a-f]{8}$"));
    }

    /// <summary>运行采集脚本并返回写出的资源行原文。</summary>
    private string Collect(string startDir) => CollectAsync(startDir).Result;

    /// <summary>运行采集脚本并返回写出的资源行原文；<paramref name="manifestPath"/> 用于计算构建号。</summary>
    private Task<string> CollectAsync(string startDir, string? manifestPath = null)
    {
        string outputPath = Path.Combine(_workRoot, "BuildVersionInfo.txt");
        if (File.Exists(outputPath)) File.Delete(outputPath);

        ProcessStartInfo info = new(_powerShell,
            $"-NoProfile -ExecutionPolicy Bypass -File \"{_scriptPath}\" -BaseVersion {BaseVersion} " +
            $"-StartDir \"{startDir}\" -OutputPath \"{outputPath}\"" +
            (manifestPath is null ? string.Empty : $" -ManifestPath \"{manifestPath}\""))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _workRoot,
        };
        using Process process = Process.Start(info)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.That(process.ExitCode, Is.EqualTo(0), $"脚本执行失败：{stdout}{stderr}");
        Assert.That(File.Exists(outputPath), Is.True, $"脚本未写出 {outputPath}");
        return Task.FromResult(File.ReadAllText(outputPath).Trim());
    }

    private static void WriteAndCommit(string repo, string fileName, string content)
    {
        File.WriteAllText(Path.Combine(repo, fileName), content);
        RunGit(repo, "add .");
        RunGit(repo, "commit -q -m add-" + fileName);
    }

    private string CreateRepo()
    {
        string dir = Path.Combine(_workRoot, "repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        RunGit(dir, "init -q");
        RunGit(dir, "config user.email buildversion@test.local");
        RunGit(dir, "config user.name buildversion");
        RunGit(dir, "config core.autocrlf false");
        File.WriteAllText(Path.Combine(dir, "a.txt"), "original");
        RunGit(dir, "add a.txt");
        RunGit(dir, "commit -q -m init");
        return dir;
    }

    private static string RunGit(string workingDir, string args)
    {
        ProcessStartInfo info = new("git", $"-c commit.gpgsign=false {args}")
        {
            WorkingDirectory = workingDir,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using Process process = Process.Start(info)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.That(process.ExitCode, Is.EqualTo(0), $"git {args} 失败：{stderr}");
        return stdout;
    }

    private static string ResolvePowerShell()
    {
        foreach (string host in new[] { "pwsh", "powershell.exe" })
            if (TryRun(host, "-Command exit")) return host;

        Assert.Fail("未找到可用的 PowerShell 主机（pwsh / powershell.exe）");
        return string.Empty;
    }

    private static bool TryRun(string fileName, string args)
    {
        try
        {
            ProcessStartInfo info = new(fileName, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using Process process = Process.Start(info)!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string FindScriptPath()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, ScriptRelativePath);
            if (File.Exists(candidate)) return candidate;
        }

        Assert.Fail($"未找到 {ScriptRelativePath}。当前测试目录: {AppContext.BaseDirectory}");
        return string.Empty;
    }

    /// <summary>git 对象是只读文件，递归删除前需要先去掉只读属性。</summary>
    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, true);
    }
}
