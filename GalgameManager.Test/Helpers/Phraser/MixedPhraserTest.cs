using GalgameManager.Contracts.Phrase;
using GalgameManager.Enums;
using GalgameManager.Helpers.Phrase;
using GalgameManager.Models;

namespace GalgameManager.Test.Helpers.Phraser;

[TestFixture]
public class MixedPhraserTest
{
    [Test]
    public async Task CustomOrder_SelectsEachFieldIndependently()
    {
        MixedPhraserOrder order = new MixedPhraserOrder().SetToDefault();
        order.NameOrder = [RssType.Vndb, RssType.Bangumi];
        order.DescriptionOrder = [RssType.Vndb, RssType.Bangumi];
        order.DeveloperOrder = [RssType.Vndb, RssType.Bangumi];
        order.ImageUrlOrder = [RssType.Bangumi, RssType.Vndb];
        MixedPhraser phraser = CreatePhraser(order: order);

        Galgame? result = await phraser.GetGalgameInfo(new Galgame("Query"));

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Name.Value, Is.EqualTo("Fixture Game!"));
            Assert.That(result.Description.Value, Is.EqualTo("VN description"));
            Assert.That(result.Developer.Value, Is.EqualTo("VN studio"));
            Assert.That(result.ImageUrl, Is.EqualTo("https://images.example/bgm.png"));
            Assert.That(result.AlternateImageUrls, Is.EqualTo(new[] { "https://images.example/vndb.png" }));
            Assert.That(result.Ids[(int)RssType.Bangumi], Is.EqualTo("bgm-42"));
            Assert.That(result.Ids[(int)RssType.Vndb], Is.EqualTo("vn-42"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DisabledOrMissingSource_IsNotCalled_AndOtherSourceStillSucceeds(bool missing)
    {
        var calls = 0;
        InMemoryPhraser bangumi = new(RssType.Bangumi, _ =>
        {
            calls++;
            return Task.FromResult<Galgame?>(Metadata(RssType.Bangumi));
        });
        MixedPhraser phraser = CreatePhraser(bangumi: bangumi,
            enabled: new MixedPhraserEnabled { BangumiEnabled = missing });
        Galgame query = new("Query");
        if (missing) query.Ids[(int)RssType.Bangumi] = "-1";

        Galgame? result = await phraser.GetGalgameInfo(query);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.Zero);
            Assert.That(result.Ids[(int)RssType.Bangumi], Is.EqualTo(missing ? "-1" : null));
            Assert.That(result.Ids[(int)RssType.Vndb], Is.EqualTo("vn-42"));
            Assert.That(result.Name.Value, Is.EqualTo("Fixture Game!"));
            Assert.That(result.Description.Value, Is.EqualTo("VN description"));
        });
    }

    [Test]
    public async Task ParseById_PassesSourceSpecificIdsAndDoesNotModifyQuery()
    {
        InMemoryPhraser bangumi = new(RssType.Bangumi, query => Task.FromResult(
            query.RssType == RssType.Bangumi && query.Id == "bgm-42" ? Metadata(RssType.Bangumi) : null));
        InMemoryPhraser vndb = new(RssType.Vndb, query => Task.FromResult(
            query.RssType == RssType.Vndb && query.Id == "vn-42" ? Metadata(RssType.Vndb) : null));
        MixedPhraser phraser = CreatePhraser(bangumi, vndb);
        Galgame query = new("Deliberately wrong name") { RssType = RssType.Mixed, Id = "bgm:bgm-42,vndb:vn-42" };
        string?[] originalIds = (string?[])query.Ids.Clone();

        Galgame? result = await phraser.GetGalgameInfo(query);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Name.Value, Is.EqualTo("Fixture Game"));
            Assert.That(result.Ids[(int)RssType.Bangumi], Is.EqualTo("bgm-42"));
            Assert.That(result.Ids[(int)RssType.Vndb], Is.EqualTo("vn-42"));
            Assert.That(query.Name.Value, Is.EqualTo("Deliberately wrong name"));
            Assert.That(query.Ids, Is.EqualTo(originalIds));
            Assert.That(query.RssType, Is.EqualTo(RssType.Mixed));
        });
    }

    [Test]
    public async Task AllSourcesReturnNull_ReturnsNull()
    {
        MixedPhraser phraser = CreatePhraser(new InMemoryPhraser(RssType.Bangumi, (Galgame?)null),
            new InMemoryPhraser(RssType.Vndb, (Galgame?)null));

        Assert.That(await phraser.GetGalgameInfo(new Galgame("Query")), Is.Null);
    }

    [Test]
    public async Task FaultedSource_DoesNotDiscardSuccessfulMetadata()
    {
        MixedPhraser phraser = CreatePhraser(new InMemoryPhraser(RssType.Bangumi,
            _ => Task.FromException<Galgame?>(new InvalidOperationException("Source unavailable"))));

        Galgame? result = await phraser.GetGalgameInfo(new Galgame("Query"));

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Ids[(int)RssType.Bangumi], Is.Null);
            Assert.That(result.Ids[(int)RssType.Vndb], Is.EqualTo("vn-42"));
            Assert.That(result.Name.Value, Is.EqualTo("Fixture Game!"));
            Assert.That(result.Description.Value, Is.EqualTo("VN description"));
        });
    }

    [Test]
    public async Task CustomOrder_WithNoAvailableSource_UsesFieldDefault()
    {
        MixedPhraserOrder order = new MixedPhraserOrder().SetToDefault();
        order.DescriptionOrder = [RssType.Ymgal];
        MixedPhraser phraser = CreatePhraser(order: order);

        Galgame? result = await phraser.GetGalgameInfo(new Galgame("Query"));

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Name.Value, Is.EqualTo("Fixture Game"));
            Assert.That(result.Description.Value, Is.Empty);
            Assert.That(result.Ids[(int)RssType.Ymgal], Is.Null);
        });
    }

    private static Galgame Metadata(RssType type) => new(type == RssType.Bangumi ? "Fixture Game" : "Fixture Game!")
    {
        RssType = type,
        Id = type == RssType.Bangumi ? "bgm-42" : "vn-42",
        Description = type == RssType.Bangumi ? "BGM description" : "VN description",
        Developer = type == RssType.Bangumi ? "BGM studio" : "VN studio",
        ImageUrl = type == RssType.Bangumi ? "https://images.example/bgm.png" : "https://images.example/vndb.png",
    };

    private static MixedPhraser CreatePhraser(IGalInfoPhraser? bangumi = null, IGalInfoPhraser? vndb = null,
        MixedPhraserOrder? order = null, MixedPhraserEnabled? enabled = null) => new(
        bangumi ?? new InMemoryPhraser(RssType.Bangumi, Metadata(RssType.Bangumi)),
        vndb ?? new InMemoryPhraser(RssType.Vndb, Metadata(RssType.Vndb)),
        new InMemoryPhraser(RssType.Ymgal, (Galgame?)null),
        new InMemoryPhraser(RssType.Steam, (Galgame?)null),
        new InMemoryPhraser(RssType.Hikarinagi, (Galgame?)null),
        new MixedPhraserData
        {
            Order = order ?? new MixedPhraserOrder().SetToDefault(),
            Enabled = enabled ?? new MixedPhraserEnabled { BangumiEnabled = true },
        });
}
