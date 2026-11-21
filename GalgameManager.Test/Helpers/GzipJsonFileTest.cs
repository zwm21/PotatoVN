using System.IO.Compression;
using System.Text;
using GalgameManager.Helpers;
using Newtonsoft.Json.Linq;

namespace GalgameManager.Test.Helpers;

/// <summary>
/// gzip 压缩数据文件的读取契约。
/// <para>
/// 随包发布的 <c>Assets/Data/*.json.gz</c> 由同目录下未压缩的 <c>*.json</c> 生成，
/// 两者必须逐字节一致——否则包里发的是过期数据。这些测试把这条不变量钉死：
/// 解压后的字节流必须与仓库里的源 JSON 完全相同，解析结果也必须与源 JSON 一致。
/// </para>
/// </summary>
[TestFixture]
public class GzipJsonFileTest
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PotatoVN.Test", "GzipJsonFile",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private void WriteGz(string name, string json)
    {
        string path = Path.Combine(_dir, name);
        using FileStream fs = File.Create(path);
        using var gz = new GZipStream(fs, CompressionLevel.Optimal);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        gz.Write(bytes, 0, bytes.Length);
    }

    #region 夹具：解压与解析的基本契约

    [Test]
    public void Parse_Fixture_DecompressesAndParses()
    {
        WriteGz("fixture.json.gz", """[{"id":"p1","name":"闇雲通信","alias":"Yamitsu"}]""");

        JToken? result = GzipJsonFile.Parse(Path.Combine(_dir, "fixture.json.gz"));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!, Is.InstanceOf<JArray>());
        JArray array = (JArray)result;
        Assert.That(array.Count, Is.EqualTo(1));
        Assert.That(array[0]["id"]!.ToString(), Is.EqualTo("p1"));
        Assert.That(array[0]["name"]!.ToString(), Is.EqualTo("闇雲通信"));
        Assert.That(array[0]["alias"]!.ToString(), Is.EqualTo("Yamitsu"));
    }

    [Test]
    public void Parse_Fixture_EmptyArray_ParsesToEmptyArray()
    {
        WriteGz("empty.json.gz", "[]");

        JToken? result = GzipJsonFile.Parse(Path.Combine(_dir, "empty.json.gz"));

        Assert.That(result, Is.InstanceOf<JArray>());
        Assert.That(((JArray)result!).Count, Is.EqualTo(0));
    }

    [Test]
    public void Parse_MissingFile_ReturnsNull()
    {
        // 与旧行为一致：数据文件不在就跳过，不抛
        JToken? result = GzipJsonFile.Parse(Path.Combine(_dir, "no-such-file.json.gz"));

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Parse_NotGzip_Throws()
    {
        // 坏文件必须炸出来，不能静默当成空数据
        string path = Path.Combine(_dir, "broken.json.gz");
        File.WriteAllText(path, "this is not gzip");

        Assert.Throws<InvalidDataException>(() => GzipJsonFile.Parse(path));
    }

    #endregion

    #region 随包发布的三个数据文件：解压结果必须与源 JSON 逐字节一致

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }

        Assert.Fail($"未找到 {relativePath}。当前测试目录: {AppContext.BaseDirectory}");
        return string.Empty;
    }

    private static string Decompress(string gzPath)
    {
        using FileStream fs = File.OpenRead(gzPath);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using StreamReader reader = new(gz, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Test]
    public void Parse_ShippedTagDb_DecompressesToOriginalJson()
    {
        AssertShippedAssetMatchesSource("GalgameManager/Assets/Data/vndb-tags-latest.json.gz",
            "GalgameManager/Assets/Data/vndb-tags-latest.json");
    }

    [Test]
    public void Parse_ShippedTagTranslation_DecompressesToOriginalJson()
    {
        AssertShippedAssetMatchesSource("GalgameManager/Assets/Data/vndb-tags-translation.json.gz",
            "GalgameManager/Assets/Data/vndb-tags-translation.json");
    }

    [Test]
    public void Parse_ShippedProducers_DecompressesToOriginalJson()
    {
        AssertShippedAssetMatchesSource("GalgameManager/Assets/Data/producers.json.gz",
            "GalgameManager/Assets/Data/producers.json");
    }

    private static void AssertShippedAssetMatchesSource(string gzRelative, string jsonRelative)
    {
        byte[] shipped = Encoding.UTF8.GetBytes(Decompress(FindRepoFile(gzRelative)));
        byte[] source = File.ReadAllBytes(FindRepoFile(jsonRelative));

        Assert.That(shipped, Is.EqualTo(source),
            $"{gzRelative} 解压后与 {jsonRelative} 不一致：.gz 是过期产物，需要重新生成");
    }

    #endregion

    #region 随包发布的数据文件：解析结果与源 JSON 一致

    [Test]
    public void Parse_ShippedTagDb_ParsesSameAsSourceJson()
    {
        string gz = FindRepoFile("GalgameManager/Assets/Data/vndb-tags-latest.json.gz");
        string json = FindRepoFile("GalgameManager/Assets/Data/vndb-tags-latest.json");

        JToken? fromGz = GzipJsonFile.Parse(gz);
        JToken fromJson = JToken.Parse(File.ReadAllText(json));

        Assert.That(fromGz, Is.Not.Null);
        Assert.That(JToken.DeepEquals(fromGz, fromJson), Is.True);
        Assert.That(((JArray)fromGz).Count, Is.EqualTo(3014));
    }

    [Test]
    public void Parse_ShippedTagTranslation_ParsesSameAsSourceJson()
    {
        string gz = FindRepoFile("GalgameManager/Assets/Data/vndb-tags-translation.json.gz");
        string json = FindRepoFile("GalgameManager/Assets/Data/vndb-tags-translation.json");

        JToken? fromGz = GzipJsonFile.Parse(gz);
        JToken fromJson = JToken.Parse(File.ReadAllText(json));

        Assert.That(fromGz, Is.Not.Null);
        Assert.That(JToken.DeepEquals(fromGz, fromJson), Is.True);
        Assert.That(((JObject)fromGz).Count, Is.EqualTo(5438));
    }

    [Test]
    public void Parse_ShippedProducers_ParsesSameAsSourceJson()
    {
        string gz = FindRepoFile("GalgameManager/Assets/Data/producers.json.gz");
        string json = FindRepoFile("GalgameManager/Assets/Data/producers.json");

        JToken? fromGz = GzipJsonFile.Parse(gz);
        JToken fromJson = JToken.Parse(File.ReadAllText(json));

        Assert.That(fromGz, Is.Not.Null);
        Assert.That(JToken.DeepEquals(fromGz, fromJson), Is.True);
    }

    /// <summary>
    /// ProducerDataHelper 实际消费的字段：id / name / latin / alias（alias 按换行拆分）。
    /// 用源 JSON 的第 1 条与最后 1 条做锚点，确保 gzip 化没有改变业务可见的数据。
    /// </summary>
    [Test]
    public void Parse_ShippedProducers_BusinessFieldsUnchanged()
    {
        JArray producers = (JArray)GzipJsonFile.Parse(
            FindRepoFile("GalgameManager/Assets/Data/producers.json.gz"))!;

        Assert.That(producers.Count, Is.GreaterThan(1000));

        JObject first = (JObject)producers[0];
        Assert.That(first["id"]!.ToString(), Is.EqualTo("p1"));
        Assert.That(first["name"]!.ToString(), Is.EqualTo("闇雲通信"));
        Assert.That(first["latin"]!.ToString(), Is.EqualTo("Yamikumo-Communications"));
        Assert.That(first["alias"]!.ToString().Split('\n'),
            Is.EqualTo(new[] { "Yamitsu" }));

        JObject last = (JObject)producers[producers.Count - 1];
        Assert.That(last["id"]?.ToString(), Is.Not.Null.And.Not.Empty);
        Assert.That(last["name"]?.ToString(), Is.Not.Null.And.Not.Empty);
    }

    /// <summary>VndbPhraser 实际消费的字段：按 id 建字典，再按 name 应用翻译。</summary>
    [Test]
    public void Parse_ShippedTagDb_FieldsUsedByVndbPhraserUnchanged()
    {
        JArray tags = (JArray)GzipJsonFile.Parse(
            FindRepoFile("GalgameManager/Assets/Data/vndb-tags-latest.json.gz"))!;

        Dictionary<int, JToken> byId = new();
        foreach (JToken tag in tags) byId.Add(int.Parse(tag["id"]!.ToString()), tag);

        Assert.That(byId[1]["name"]!.ToString(), Is.EqualTo("Theme"));
        Assert.That(byId[4186]["name"]!.ToString(), Is.EqualTo("Protagonist's Full Brother as a Hero"));
        Assert.That(byId[4186]["cat"]!.ToString(), Is.EqualTo("cont"));
        Assert.That(byId[4186]["parents"]!.ToObject<List<int>>(), Is.EqualTo(new List<int> { 1548 }));

        JObject translation = (JObject)GzipJsonFile.Parse(
            FindRepoFile("GalgameManager/Assets/Data/vndb-tags-translation.json.gz"))!;
        Assert.That(translation["Theme"]?.ToString(), Is.EqualTo("主题"));
        Assert.That(translation["Romance"]?.ToString(), Is.EqualTo("恋爱"));
        Assert.That(translation["Brother/Sister Incest"]?.ToString(), Is.Not.Null.And.Not.Empty);
    }

    #endregion

    #region 应用目录相对路径

    [Test]
    public void Parse_RelativePath_ResolvesAgainstAppDirectory()
    {
        // 随包发布的数据文件会被 ProjectReference 带进测试输出目录，
        // 这里用应用目录相对路径读它，锁死生产代码的解析方式。
        JToken? result = GzipJsonFile.Parse(@"Assets\Data\producers.json.gz");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.InstanceOf<JArray>());
        Assert.That(((JArray)result!).Count, Is.GreaterThan(1000));
    }

    [Test]
    public void Parse_RelativePath_Missing_ReturnsNull()
    {
        Assert.That(GzipJsonFile.Parse(@"Assets\Data\definitely-not-here.json.gz"), Is.Null);
    }

    #endregion
}
