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

    [TestCase(RssType.Bangumi)]
    [TestCase(RssType.Vndb)]
    [TestCase(RssType.Hikarinagi)]
    public async Task MatchingSources_ExcludeUnrelatedSourceIdsAndFields(RssType unrelatedSource)
    {
        Dictionary<RssType, Galgame> metadata = new()
        {
            [RssType.Bangumi] = Metadata(RssType.Bangumi),
            [RssType.Vndb] = Metadata(RssType.Vndb),
            [RssType.Hikarinagi] = new Galgame("Fixture Game")
            {
                RssType = RssType.Hikarinagi,
                Id = "hk-42",
                Description = "HK description",
                ImageUrl = "https://images.example/hikari.png",
            },
        };
        metadata[unrelatedSource].Name.Value = "ZZZZZZZZZZZZZZZZZZZZ";
        metadata[unrelatedSource].Description.Value = "Unrelated description";
        metadata[unrelatedSource].ImageUrl = "https://images.example/unrelated.png";
        RssType[] matchingSources = metadata.Keys.Where(type => type != unrelatedSource).ToArray();
        MixedPhraserOrder order = new MixedPhraserOrder().SetToDefault();
        order.DescriptionOrder = [unrelatedSource, .. matchingSources];
        order.ImageUrlOrder = [unrelatedSource, .. matchingSources];
        MixedPhraser phraser = CreatePhraser(
            new InMemoryPhraser(RssType.Bangumi, metadata[RssType.Bangumi]),
            new InMemoryPhraser(RssType.Vndb, metadata[RssType.Vndb]), order: order,
            enabled: new MixedPhraserEnabled { BangumiEnabled = true, HikarinagiEnabled = true },
            hikarinagi: new InMemoryPhraser(RssType.Hikarinagi, metadata[RssType.Hikarinagi]));

        Galgame? result = await phraser.GetGalgameInfo(new Galgame("Query"));

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Ids[(int)RssType.Hikarinagi], Is.EqualTo(unrelatedSource == RssType.Hikarinagi ? null : "hk-42"));
            Assert.That(result.Name.Value, Is.EqualTo("Fixture Game"));
            Assert.That(result.Ids[(int)RssType.Bangumi], Is.EqualTo(unrelatedSource == RssType.Bangumi ? null : "bgm-42"));
            Assert.That(result.Ids[(int)RssType.Vndb], Is.EqualTo(unrelatedSource == RssType.Vndb ? null : "vn-42"));
            Assert.That(result.Description.Value, Is.EqualTo(unrelatedSource == RssType.Bangumi ? "VN description" : "BGM description"));
            Assert.That(result.ImageUrl, Is.EqualTo(unrelatedSource == RssType.Bangumi ? "https://images.example/vndb.png" : "https://images.example/bgm.png"));
            Assert.That(result.AlternateImageUrls, Is.EqualTo(new[]
            {
                unrelatedSource == RssType.Hikarinagi ? "https://images.example/vndb.png" : "https://images.example/hikari.png",
            }));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SourceModifiesRequestName_DoesNotModifyCallerOrOtherRequests(bool nameLocked)
    {
        string? vndbRequestName = null;
        bool? vndbRequestLocked = null;
        InMemoryPhraser bangumi = new(RssType.Bangumi, request =>
        {
            request.Name.ForceSet("Changed by Bangumi");
            return Task.FromResult<Galgame?>(Metadata(RssType.Bangumi));
        });
        InMemoryPhraser vndb = new(RssType.Vndb, request =>
        {
            vndbRequestName = request.Name.Value;
            vndbRequestLocked = request.Name.IsLock;
            return Task.FromResult<Galgame?>(Metadata(RssType.Vndb));
        });
        MixedPhraser phraser = CreatePhraser(bangumi, vndb);
        Galgame query = new("Original query");
        query.Name.IsLock = nameLocked;
        var callerNotifications = 0;
        query.Name.OnValueChanged += _ => callerNotifications++;

        Galgame? result = await phraser.GetGalgameInfo(query);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(query.Name.Value, Is.EqualTo("Original query"));
            Assert.That(query.Name.IsLock, Is.EqualTo(nameLocked));
            Assert.That(callerNotifications, Is.Zero);
            Assert.That(vndbRequestName, Is.EqualTo("Original query"));
            Assert.That(vndbRequestLocked, Is.EqualTo(nameLocked));
            Assert.That(result.Name.Value, Is.EqualTo("Fixture Game"));
            Assert.That(result.Ids[(int)RssType.Bangumi], Is.EqualTo("bgm-42"));
            Assert.That(result.Ids[(int)RssType.Vndb], Is.EqualTo("vn-42"));
        });
    }

    [Test]
    public async Task UnrelatedSources_KeepOnlyFirstSourceMetadata()
    {
        Galgame unrelated = Metadata(RssType.Vndb);
        unrelated.Name.Value = "ZZZZZZZZZZZZZZZZZZZZ";
        MixedPhraser phraser = CreatePhraser(vndb: new InMemoryPhraser(RssType.Vndb, unrelated));

        Galgame? result = await phraser.GetGalgameInfo(new Galgame("Query"));

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Name.Value, Is.EqualTo("Fixture Game"));
            Assert.That(result.Description.Value, Is.EqualTo("BGM description"));
            Assert.That(result.Ids[(int)RssType.Bangumi], Is.EqualTo("bgm-42"));
            Assert.That(result.Ids[(int)RssType.Vndb], Is.Null);
            Assert.That(result.ImageUrl, Is.EqualTo("https://images.example/bgm.png"));
            Assert.That(result.AlternateImageUrls, Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CoverSourceModifiesRequestName_DoesNotModifyCallerOrOtherRequests(bool nameLocked)
    {
        InMemoryCoverPhraser bangumi = new(RssType.Bangumi, request =>
        {
            bool expectedRequest = request.RssType == RssType.Bangumi && request.Id == "bgm-42"
                && request.Name.Value == "Original query" && request.Name.IsLock == nameLocked;
            request.Name.ForceSet("Changed by Bangumi");
            request.Ids[(int)RssType.Bangumi] = "changed-id";
            return Task.FromResult<List<string>>(expectedRequest ? ["https://images.example/bgm-extra.png"] : []);
        });
        InMemoryCoverPhraser vndb = new(RssType.Vndb, request => Task.FromResult<List<string>>(
            request.RssType == RssType.Vndb && request.Id == "vn-42" && request.Name.Value == "Original query"
                && request.Name.IsLock == nameLocked ? ["https://images.example/vndb-extra.png"] : []));
        MixedPhraserOrder order = new MixedPhraserOrder().SetToDefault();
        order.ImageUrlOrder = [RssType.Bangumi, RssType.Vndb];
        MixedPhraser phraser = CreatePhraser(bangumi, vndb, order);
        Galgame query = new("Original query") { RssType = RssType.Mixed, Id = "bgm:bgm-42,vndb:vn-42" };
        query.Name.IsLock = nameLocked;
        string?[] originalIds = (string?[])query.Ids.Clone();
        var callerNotifications = 0;
        query.Name.OnValueChanged += _ => callerNotifications++;

        List<string> covers = await phraser.GetGalCoversAsync(query);

        Assert.Multiple(() =>
        {
            Assert.That(covers, Is.EqualTo(new[] { "https://images.example/bgm-extra.png", "https://images.example/vndb-extra.png" }));
            Assert.That(query.Name.Value, Is.EqualTo("Original query"));
            Assert.That(query.Name.IsLock, Is.EqualTo(nameLocked));
            Assert.That(query.RssType, Is.EqualTo(RssType.Mixed));
            Assert.That(query.Ids, Is.EqualTo(originalIds));
            Assert.That(callerNotifications, Is.Zero);
        });
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

    private sealed class InMemoryCoverPhraser(RssType type, Func<Galgame, Task<List<string>>> parse)
        : IGalInfoPhraser, IGalCoversParser
    {
        public Task<Galgame?> GetGalgameInfo(Galgame galgame) => Task.FromResult<Galgame?>(null);

        public RssType GetPhraseType() => type;

        public Task<List<string>> GetGalCoversAsync(Galgame galgame) => parse(galgame);
    }

    private static MixedPhraser CreatePhraser(IGalInfoPhraser? bangumi = null, IGalInfoPhraser? vndb = null,
        MixedPhraserOrder? order = null, MixedPhraserEnabled? enabled = null,
        IGalInfoPhraser? hikarinagi = null) => new(
        bangumi ?? new InMemoryPhraser(RssType.Bangumi, Metadata(RssType.Bangumi)),
        vndb ?? new InMemoryPhraser(RssType.Vndb, Metadata(RssType.Vndb)),
        new InMemoryPhraser(RssType.Ymgal, (Galgame?)null),
        new InMemoryPhraser(RssType.Steam, (Galgame?)null),
        hikarinagi ?? new InMemoryPhraser(RssType.Hikarinagi, (Galgame?)null),
        new MixedPhraserData
        {
            Order = order ?? new MixedPhraserOrder().SetToDefault(),
            Enabled = enabled ?? new MixedPhraserEnabled { BangumiEnabled = true },
        });
}
