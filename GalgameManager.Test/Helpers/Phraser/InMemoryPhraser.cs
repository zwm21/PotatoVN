using GalgameManager.Contracts.Phrase;
using GalgameManager.Enums;
using GalgameManager.Models;

namespace GalgameManager.Test.Helpers.Phraser;

internal sealed class InMemoryPhraser(RssType type, Func<Galgame, Task<Galgame?>> parse) : IGalInfoPhraser
{
    public InMemoryPhraser(RssType type, Galgame? result) : this(type, _ => Task.FromResult(result))
    {
    }

    public Task<Galgame?> GetGalgameInfo(Galgame galgame) => parse(galgame);

    public RssType GetPhraseType() => type;
}
