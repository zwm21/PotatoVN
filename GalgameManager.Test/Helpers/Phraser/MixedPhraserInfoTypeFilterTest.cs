using System.Collections.ObjectModel;
using GalgameManager.Enums;
using GalgameManager.Helpers.Phrase;
using GalgameManager.Models;

namespace GalgameManager.Test.Helpers.Phraser;

[TestFixture]
public class MixedPhraserInfoTypeFilterTest
{
    public enum InfoField
    {
        Name, Description, Developer, Engine, Tags, Rating, ExpectedPlayTime,
        ReleaseDate, CnName, ImageUrl, Characters,
    }

    private static IEnumerable<InfoField> Fields => Enum.GetValues<InfoField>();

    [TestCaseSource(nameof(Fields))]
    public async Task InformationType_Disabled_PreservesDefaultAndOtherFields(InfoField field)
    {
        MixedPhraserEnabled enabled = new();
        SetEnabled(enabled, field, false);

        Galgame result = await ParseAsync(enabled);

        foreach (InfoField expectedField in Fields)
            AssertField(result, expectedField, expectedField != field);
    }

    [TestCaseSource(nameof(Fields))]
    public async Task InformationType_EnabledAlone_ReturnsOnlySelectedField(InfoField field)
    {
        MixedPhraserEnabled enabled = DisableAllFields();
        SetEnabled(enabled, field, true);

        Galgame result = await ParseAsync(enabled);

        foreach (InfoField expectedField in Fields)
            AssertField(result, expectedField, expectedField == field);
    }

    [Test]
    public async Task MultipleTypesEnabled_ReturnsSelectedFieldsOnly()
    {
        MixedPhraserEnabled enabled = DisableAllFields();
        enabled.NameEnabled = true;
        enabled.DescriptionEnabled = true;
        enabled.TagsEnabled = true;

        Galgame result = await ParseAsync(enabled);

        foreach (InfoField field in Fields)
            AssertField(result, field, field is InfoField.Name or InfoField.Description or InfoField.Tags);
    }

    [Test]
    public async Task AllTypesDisabled_PreservesDefaultsAndSourceIds()
    {
        Galgame result = await ParseAsync(DisableAllFields());

        foreach (InfoField field in Fields)
            AssertField(result, field, false);
    }

    [Test]
    public async Task DefaultConfiguration_MergesEachFieldFromItsPreferredSource()
    {
        Galgame result = await ParseAsync(new MixedPhraserEnabled());

        foreach (InfoField field in Fields)
            AssertField(result, field, true);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task DeveloperFromTags_RequiresTagsEnabledAndMissingDirectDeveloper(bool tagsEnabled)
    {
        MixedPhraserEnabled enabled = DisableAllFields();
        enabled.DeveloperEnabled = true;
        enabled.TagsEnabled = tagsEnabled;
        Galgame preferred = CreateMetadata(RssType.Hikarinagi);
        Galgame fallback = CreateMetadata(RssType.Vndb);
        preferred.Developer = Galgame.DefaultString;
        fallback.Developer = Galgame.DefaultString;
        preferred.Tags = new ObservableCollection<string> { "Navel" };

        Galgame result = await ParseAsync(enabled, preferred, fallback);

        Assert.Multiple(() =>
        {
            Assert.That(result.Developer.Value, Is.EqualTo(tagsEnabled ? "Navel" : Galgame.DefaultString));
            Assert.That(result.Tags.Value, Is.EqualTo(tagsEnabled ? new[] { "Navel" } : Array.Empty<string>()));
        });
    }

    [Test]
    public async Task UnavailablePreferredValues_FallBackToNextSource()
    {
        Galgame preferred = CreateMetadata(RssType.Hikarinagi);
        preferred.Developer = Galgame.DefaultString;
        preferred.Tags = new ObservableCollection<string>();
        preferred.ReleaseDate = DateTime.MinValue;
        preferred.CnName = string.Empty;
        preferred.ImageUrl = null;
        preferred.Characters.Clear();

        Galgame result = await ParseAsync(new MixedPhraserEnabled(), preferred);

        Assert.Multiple(() =>
        {
            Assert.That(result.Developer.Value, Is.EqualTo("VN studio"));
            Assert.That(result.Tags.Value, Is.EqualTo(new[] { "VN tag" }));
            Assert.That(result.ReleaseDate.Value, Is.EqualTo(new DateTime(2017, 1, 2)));
            Assert.That(result.CnName, Is.EqualTo("备用中文标题"));
            Assert.That(result.ImageUrl, Is.EqualTo("https://images.example/vndb.png"));
            Assert.That(result.AlternateImageUrls, Is.Empty);
            Assert.That(result.Characters.Select(character => (character.Name, character.Relation)),
                Is.EqualTo(new[] { ("VN heroine", "Main") }));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DeveloperFromUnrelatedTags_PreservesDefaultDeveloper(bool hasValidDeveloperTag)
    {
        MixedPhraserEnabled enabled = DisableAllFields();
        enabled.DeveloperEnabled = true;
        enabled.TagsEnabled = true;
        Galgame preferred = CreateMetadata(RssType.Hikarinagi);
        Galgame fallback = CreateMetadata(RssType.Vndb);
        preferred.Developer = Galgame.DefaultString;
        fallback.Developer = Galgame.DefaultString;
        string unrelatedTag = new('x', 100);
        preferred.Tags = new ObservableCollection<string> { unrelatedTag };
        if (hasValidDeveloperTag)
            preferred.Tags.Value!.Add("Navel");

        Galgame result = await ParseAsync(enabled, preferred, fallback);

        Assert.Multiple(() =>
        {
            Assert.That(result.Developer.Value, Is.EqualTo(hasValidDeveloperTag ? "Navel" : Galgame.DefaultString));
            Assert.That(result.Tags.Value, Is.EqualTo(hasValidDeveloperTag ? new[] { unrelatedTag, "Navel" } : new[] { unrelatedTag }));
        });
    }

    private static MixedPhraserEnabled DisableAllFields()
    {
        MixedPhraserEnabled enabled = new();
        foreach (InfoField field in Fields)
            SetEnabled(enabled, field, false);
        return enabled;
    }

    private static void SetEnabled(MixedPhraserEnabled enabled, InfoField field, bool value) =>
        typeof(MixedPhraserEnabled).GetProperty($"{field}Enabled")!.SetValue(enabled, value);

    private static async Task<Galgame> ParseAsync(MixedPhraserEnabled enabled,
        Galgame? preferred = null, Galgame? fallback = null)
    {
        MixedPhraser phraser = new(
            new InMemoryPhraser(RssType.Bangumi, (Galgame?)null),
            new InMemoryPhraser(RssType.Vndb, fallback ?? CreateMetadata(RssType.Vndb)),
            new InMemoryPhraser(RssType.Ymgal, (Galgame?)null),
            new InMemoryPhraser(RssType.Steam, (Galgame?)null),
            new InMemoryPhraser(RssType.Hikarinagi, preferred ?? CreateMetadata(RssType.Hikarinagi)),
            new MixedPhraserData { Order = new MixedPhraserOrder().SetToDefault(), Enabled = enabled });

        Galgame? result = await phraser.GetGalgameInfo(new Galgame("Query Game"));

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.RssType, Is.EqualTo(RssType.Mixed));
            Assert.That(result.Ids[(int)RssType.Hikarinagi], Is.EqualTo("hk-42"));
            Assert.That(result.Ids[(int)RssType.Vndb], Is.EqualTo("vn-42"));
            Assert.That(result.Ids[(int)RssType.Bangumi], Is.Null);
            Assert.That(result.Ids[(int)RssType.Ymgal], Is.Null);
            Assert.That(result.Ids[(int)RssType.Steam], Is.Null);
        });
        return result;
    }

    private static Galgame CreateMetadata(RssType type)
    {
        bool preferred = type == RssType.Hikarinagi;
        Galgame game = new(preferred ? "Canonical Game" : "Canonical Game!")
        {
            RssType = type,
            Id = preferred ? "hk-42" : "vn-42",
            Description = preferred ? "Preferred description" : "Fallback description",
            Developer = preferred ? "HK studio" : "VN studio",
            Engine = preferred ? "Other engine" : "RenPy",
            Rating = preferred ? 2f : 8.25f,
            ExpectedPlayTime = preferred ? "1h" : "24h",
            ReleaseDate = preferred ? new DateTime(2018, 7, 1) : new DateTime(2017, 1, 2),
            CnName = preferred ? "首选中文标题" : "备用中文标题",
            ImageUrl = preferred ? "https://images.example/hikari.png" : "https://images.example/vndb.png",
            Tags = new ObservableCollection<string>(preferred ? new[] { "Story", "Choices" } : new[] { "VN tag" }),
            Characters = new ObservableCollection<GalgameCharacter>
            {
                new() { Name = preferred ? "HK heroine" : "VN heroine", Relation = "Main" },
            },
        };
        return game;
    }

    private static void AssertField(Galgame result, InfoField field, bool enabled)
    {
        switch (field)
        {
            case InfoField.Name:
                Assert.That(result.Name.Value, Is.EqualTo(enabled ? "Canonical Game" : string.Empty));
                break;
            case InfoField.Description:
                Assert.That(result.Description.Value, Is.EqualTo(enabled ? "Preferred description" : string.Empty));
                break;
            case InfoField.Developer:
                Assert.That(result.Developer.Value, Is.EqualTo(enabled ? "HK studio" : Galgame.DefaultString));
                break;
            case InfoField.Engine:
                Assert.That(result.Engine.Value, Is.EqualTo(enabled ? "RenPy" : Galgame.DefaultString));
                break;
            case InfoField.Tags:
                Assert.That(result.Tags.Value, Is.EqualTo(enabled ? new[] { "Story", "Choices" } : Array.Empty<string>()));
                break;
            case InfoField.Rating:
                Assert.That(result.Rating.Value, Is.EqualTo(enabled ? 8.25f : 0f));
                break;
            case InfoField.ExpectedPlayTime:
                Assert.That(result.ExpectedPlayTime.Value, Is.EqualTo(enabled ? "24h" : Galgame.DefaultString));
                break;
            case InfoField.ReleaseDate:
                Assert.That(result.ReleaseDate.Value, Is.EqualTo(enabled ? new DateTime(2018, 7, 1) : DateTime.MinValue));
                break;
            case InfoField.CnName:
                Assert.That(result.CnName, Is.EqualTo(enabled ? "首选中文标题" : string.Empty));
                break;
            case InfoField.ImageUrl:
                Assert.That(result.ImageUrl, Is.EqualTo(enabled ? "https://images.example/vndb.png" : null));
                Assert.That(result.AlternateImageUrls, Is.EqualTo(enabled ? new[] { "https://images.example/hikari.png" } : Array.Empty<string>()));
                break;
            case InfoField.Characters:
                Assert.That(result.Characters.Select(character => (character.Name, character.Relation)),
                    Is.EqualTo(enabled ? new[] { ("HK heroine", "Main") } : Array.Empty<(string, string)>()));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }
    }
}
