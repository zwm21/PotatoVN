using System.Reflection;
using GalgameManager.Models;

namespace GalgameManager.Test.Helpers.Phrase;

/// <summary>
/// 锁死「ex-vndb 死路径已移除」这件事。
/// <para>
/// <c>ExVndb</c>（<c>PhraseHelper</c> 里的静态类）读的是 <c>Assets/Data/ex-vndb.db</c>，
/// 而该文件自 <c>86e0b7c7</c> 引入起从未写进 csproj，从未随任何发布包发出过。
/// 于是 <c>Init()</c> 永远拿不到文件、<c>_db</c> 恒为 null，而
/// <c>TryGetExVnAsync</c> 里是 <c>_db!.Table&lt;ExVn&gt;()</c>——每个新装用户每次走
/// VNDB 头图查询都会吃一个 NullReferenceException，被 BgTask 基类吞掉后静默失败。
/// 只有 <c>UserData</c> 里留有历史副本的老用户能侥幸work。
/// </para>
/// <para>
/// 移除后 VNDB 头图直接走同一函数里本就存在的在线 API 截图回退逻辑，功能不变。
/// 这些测试防止有人再把这条死路径加回来。
/// </para>
/// </summary>
[TestFixture]
public class ExVndbRemovedTest
{
    private static readonly Assembly AppAssembly = typeof(Galgame).Assembly;

    [Test]
    public void ExVndbType_IsGoneFromAppAssembly()
    {
        List<Type> leftovers = AppAssembly.GetTypes()
            .Where(t => t.Name.Contains("ExVndb", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.That(leftovers, Is.Empty,
            "ExVndb / ExVn 这些类型应当已删除；重新引入前先看 PhraseHelper 的历史提交");
    }

    [Test]
    public void ExVndbDbFile_IsGoneFromRepo()
    {
        // 仓库里不该再有这个文件（Assets/Data/.gitignore 的 !*.db 白名单会让它重新被跟踪）。
        // 发布包里不会有它：csproj 从未为它写过 Content 项（这正是当初静默失效的原因）。
        Assert.That(RepoFileExists("GalgameManager/Assets/Data/ex-vndb.db"), Is.False,
            "Assets/Data/ex-vndb.db 应当已从仓库删除");
    }

    [Test]
    public void NoSourceFile_ReferencesExVndb()
    {
        string self = FindRepoFile("GalgameManager.Test/Helpers/Phraser/ExVndbRemovedTest.cs")!;
        List<string> hits = [];
        foreach (string file in Directory.EnumerateFiles(FindRepoRoot()!, "*.cs", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase))
                continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            string text = File.ReadAllText(file);
            if (text.Contains("ExVndb", StringComparison.Ordinal) ||
                text.Contains("ex-vndb", StringComparison.OrdinalIgnoreCase))
                hits.Add(Path.GetRelativePath(FindRepoRoot()!, file));
        }

        Assert.That(hits, Is.Empty, "还有源码在引用 ExVndb / ex-vndb.db: " + string.Join(", ", hits));
    }

    private static bool RepoFileExists(string relativePath)
    {
        string? root = FindRepoRoot();
        return root is not null && File.Exists(Path.Combine(root, relativePath));
    }

    private static string? FindRepoFile(string relativePath)
    {
        string? root = FindRepoRoot();
        return root is null ? null : Path.Combine(root, relativePath);
    }

    private static string? FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GalgameManager", "GalgameManager.csproj")))
                return dir.FullName;
        }
        return null;
    }
}
