using GalgameManager.Helpers;
using NUnit.Framework;
using Windows.Storage;

namespace GalgameManager.Test.Helpers;

[TestFixture]
public class StoragePathHelperTest
{
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(TestEnvironmentSetup.Root, nameof(StoragePathHelperTest),
            TestContext.CurrentContext.Test.Name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_testDir)) Directory.Delete(_testDir, recursive: true);
        }
        catch
        {
            // ignore
        }
    }

    [Test]
    public void RelocatePath_PathUnderOldRoot_ReturnsNewPath()
    {
        string oldRoot = Path.Combine(_testDir, "old");
        string newRoot = Path.Combine(_testDir, "new");
        string path = Path.Combine(oldRoot, "game", "game.exe");

        string? result = StoragePathHelper.RelocatePath(path, oldRoot, newRoot);

        Assert.That(result, Is.EqualTo(Path.Combine(newRoot, "game", "game.exe")));
    }

    [Test]
    public void RelocatePath_RootItself_ReturnsNewRoot()
    {
        string oldRoot = Path.Combine(_testDir, "old");
        string newRoot = Path.Combine(_testDir, "new");

        string? result = StoragePathHelper.RelocatePath(oldRoot, oldRoot, newRoot);

        Assert.That(result, Is.EqualTo(Path.GetFullPath(newRoot)));
    }

    [Test]
    public void RelocatePath_PathOutsideOldRoot_ReturnsNull()
    {
        string oldRoot = Path.Combine(_testDir, "old");
        string outside = Path.Combine(_testDir, "outside", "game.exe");

        string? result = StoragePathHelper.RelocatePath(outside, oldRoot, Path.Combine(_testDir, "new"));

        Assert.That(result, Is.Null);
    }

    [Test]
    public void EnumerateTrailingSuffixCandidates_ReturnsOnlyLongerPrefixedEntries()
    {
        Directory.CreateDirectory(Path.Combine(_testDir, "target"));
        Directory.CreateDirectory(Path.Combine(_testDir, "target\u3000"));
        Directory.CreateDirectory(Path.Combine(_testDir, "target2"));
        File.WriteAllText(Path.Combine(_testDir, "target_file\u3000"), "x");

        List<string> matches = StoragePathHelper
            .EnumerateTrailingSuffixCandidates(_testDir, "target")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.That(matches, Is.EqualTo(new[] { "target2", "target\u3000" }));
    }

    [Test]
    public void EnumerateTrailingSuffixCandidates_IncludeFiles_ReturnsFilesToo()
    {
        Directory.CreateDirectory(Path.Combine(_testDir, "target\u3000"));
        File.WriteAllText(Path.Combine(_testDir, "target\u00A0"), "x");

        List<string> matches = StoragePathHelper
            .EnumerateTrailingSuffixCandidates(_testDir, "target", includeFiles: true)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.That(matches, Is.EqualTo(new[] { "target\u00A0", "target\u3000" }));
    }

    [Test]
    public async Task TryResolveExactPathAsync_TrailingWhitespaceFolder_ResolvesToRealPath()
    {
        string actual = Path.Combine(_testDir, "game\u3000");
        Directory.CreateDirectory(actual);
        string stored = Path.Combine(_testDir, "game");

        string? resolved = await StoragePathHelper.TryResolveExactPathAsync(stored);

        Assert.That(resolved, Is.EqualTo(actual));
    }

    [Test]
    public async Task TryResolveExactPathAsync_TrailingWhitespaceFile_ResolvesToRealPath()
    {
        string actual = Path.Combine(_testDir, "archive\u3000");
        File.WriteAllText(actual, "x");
        string stored = Path.Combine(_testDir, "archive");

        string? resolved = await StoragePathHelper.TryResolveExactPathAsync(stored);

        Assert.That(resolved, Is.EqualTo(actual));
    }

    [Test]
    public async Task TryResolveExactPathAsync_AmbiguousCandidates_ReturnsNull()
    {
        Directory.CreateDirectory(Path.Combine(_testDir, "game\u3000"));
        Directory.CreateDirectory(Path.Combine(_testDir, "game\u00A0"));
        string stored = Path.Combine(_testDir, "game");

        string? resolved = await StoragePathHelper.TryResolveExactPathAsync(stored);

        Assert.That(resolved, Is.Null);
    }

    [Test]
    public async Task GetExactPathAsync_TrailingWhitespaceStorageFolder_ResolvesToRealPath()
    {
        string actual = Path.Combine(_testDir, "game\u3000");
        Directory.CreateDirectory(actual);
        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(actual);

        string? resolved = await StoragePathHelper.GetExactPathAsync(folder);

        Assert.That(resolved, Is.EqualTo(actual));
    }
}
