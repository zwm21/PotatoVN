using GalgameManager.Enums;
using GalgameManager.Helpers.Phrase;
using GalgameManager.Models;

namespace GalgameManager.Test.Helpers.Phraser;

[TestFixture]
[Category("Phraser")]
public class CngalPhraserTest
{
    private CngalPhraser _phraser = new();

    [SetUp]
    public void Init()
    {
        _phraser = new();
    }

    [Test]
    [TestCase("三色绘恋")]
    public async Task PhraseTest(string name)
    {
        Galgame? game = new(name);
        game = await _phraser.GetGalgameInfo(game);
        Assert.That(game, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(game.Name.Value, Is.EqualTo("三色△绘恋"));
            Assert.That(game.Id, Is.EqualTo("80"));
            Assert.That(game.Developer.Value, Is.EqualTo("绘恋制作组"));
            Assert.That(game.ReleaseDate.Value, Is.EqualTo(new DateTime(2017, 9, 21)));
            Assert.That(game.Characters, Has.Count.EqualTo(9));
        });
    }
}
