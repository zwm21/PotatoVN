using GalgameManager.Enums;
using GalgameManager.Helpers.Phrase;
using GalgameManager.Models;

namespace GalgameManager.Test.Helpers.Phraser;

[TestFixture]
public class VndbPhraserIdTest
{
    [TestCase("17", "v17")]
    [TestCase("v17", "v17")]
    [TestCase("42", "v42")]
    [TestCase("v42", "v42")]
    public async Task GetVndbId_ShouldNormalize_ToVnId(string storedId, string expectedVnId)
    {
        VndbPhraser phraser = new();
        Galgame input = new("dummy");
        input.Ids[(int)RssType.Vndb] = storedId;

        string? vnId = await phraser.GetVndbIdAsync(input);

        Assert.That(vnId, Is.EqualTo(expectedVnId));
    }
}
