using GalgameManager.Contracts.Phrase;
using GalgameManager.Enums;
using GalgameManager.Helpers.Phrase;
using GalgameManager.Models;

namespace GalgameManager.Test.Helpers.Phraser;

[TestFixture]
public class MixedPhraserTimeoutTest
{
    [Test]
    public async Task GetGalgameInfo_FastSourceSucceeds_SlowSourceTimedOut_ReturnsMergedResult()
    {
        TaskCompletionSource<Galgame?> slow = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MixedPhraser phraser = CreateMixedPhraser(1,
            new InMemoryPhraser(RssType.Bangumi, Metadata(RssType.Bangumi, "bgm-1")),
            new InMemoryPhraser(RssType.Vndb, _ => slow.Task));
        try
        {
            Galgame? result = await phraser.GetGalgameInfo(new Galgame("TestGame")).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(result, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(result.Ids[(int)RssType.Bangumi], Is.EqualTo("bgm-1"));
                Assert.That(result.Ids[(int)RssType.Vndb], Is.Null);
                Assert.That(result.Name.Value, Is.EqualTo("TestGame"));
                Assert.That(result.Description.Value, Is.EqualTo("Bangumi-desc"));
            });
        }
        finally
        {
            slow.TrySetResult(Metadata(RssType.Vndb, "vndb-1"));
        }
    }

    [Test]
    public async Task GetGalgameInfo_TimeoutZero_WaitsForSlowSource()
    {
        TaskCompletionSource<Galgame?> slow = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource called = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MixedPhraser phraser = CreateMixedPhraser(0,
            new InMemoryPhraser(RssType.Bangumi, Metadata(RssType.Bangumi, "bgm-1")),
            new InMemoryPhraser(RssType.Vndb, _ =>
            {
                called.SetResult();
                return slow.Task;
            }));
        Task<Galgame?> parsing = phraser.GetGalgameInfo(new Galgame("TestGame"));
        try
        {
            await called.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(parsing.IsCompleted, Is.False);
            slow.SetResult(Metadata(RssType.Vndb, "vndb-1"));

            Galgame? result = await parsing.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(result, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(result.Ids[(int)RssType.Bangumi], Is.EqualTo("bgm-1"));
                Assert.That(result.Ids[(int)RssType.Vndb], Is.EqualTo("vndb-1"));
                Assert.That(result.Description.Value, Is.EqualTo("Bangumi-desc"));
            });
        }
        finally
        {
            slow.TrySetResult(null);
        }
    }

    [Test]
    public async Task GetGalgameInfo_AllSourcesTimedOut_ReturnsNull()
    {
        TaskCompletionSource<Galgame?> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MixedPhraser phraser = CreateMixedPhraser(1,
            new InMemoryPhraser(RssType.Bangumi, _ => pending.Task),
            new InMemoryPhraser(RssType.Vndb, _ => pending.Task));
        try
        {
            Galgame? result = await phraser.GetGalgameInfo(new Galgame("TestGame")).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(result, Is.Null);
        }
        finally
        {
            pending.TrySetResult(null);
        }
    }

    private static Galgame Metadata(RssType type, string id) => new("TestGame")
    {
        RssType = type,
        Id = id,
        Description = $"{type}-desc",
    };

    private static MixedPhraser CreateMixedPhraser(int timeoutSeconds, IGalInfoPhraser bangumi, IGalInfoPhraser vndb) =>
        new(bangumi, vndb,
            new InMemoryPhraser(RssType.Ymgal, (Galgame?)null),
            new InMemoryPhraser(RssType.Steam, (Galgame?)null),
            new InMemoryPhraser(RssType.Hikarinagi, (Galgame?)null),
            new MixedPhraserData
            {
                Order = new MixedPhraserOrder().SetToDefault(),
                Enabled = new MixedPhraserEnabled
                {
                    BangumiEnabled = true,
                    YmgalEnabled = false,
                    SteamEnabled = false,
                    HikarinagiEnabled = false,
                },
                TimeoutSeconds = timeoutSeconds,
            });
}
