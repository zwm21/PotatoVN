using GalgameManager.Helpers;

namespace GalgameManager.Test.Helpers;

[TestFixture]
public class CustomSortOrderHelperTest
{
    [Test]
    public void PutFirst_NullOrder_ReturnsSingleItem()
    {
        Guid uuid = Guid.NewGuid();

        List<string> result = CustomSortOrderHelper.PutFirst(null, uuid);

        Assert.That(result, Is.EqualTo(new[] { uuid.ToString() }));
    }

    [Test]
    public void PutFirst_EmptyOrder_ReturnsSingleItem()
    {
        Guid uuid = Guid.NewGuid();

        List<string> result = CustomSortOrderHelper.PutFirst([], uuid);

        Assert.That(result, Is.EqualTo(new[] { uuid.ToString() }));
    }

    [Test]
    public void PutFirst_NewUuid_InsertsAtFrontAndKeepsOtherOrder()
    {
        Guid uuid = Guid.NewGuid();
        List<string> order = ["a", "b", "c"];

        List<string> result = CustomSortOrderHelper.PutFirst(order, uuid);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new[] { uuid.ToString(), "a", "b", "c" }));
            Assert.That(order, Is.EqualTo(new[] { "a", "b", "c" }), "不应修改传入的列表");
        });
    }

    [Test]
    public void PutFirst_ExistingUuid_MovesToFrontWithoutDuplicate()
    {
        Guid uuid = Guid.NewGuid();
        string existing = uuid.ToString();
        List<string> order = ["a", existing, "b"];

        List<string> result = CustomSortOrderHelper.PutFirst(order, uuid);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new[] { existing, "a", "b" }));
            Assert.That(result.Count(item => string.Equals(item, existing, StringComparison.OrdinalIgnoreCase)),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void PutFirst_ExistingUuidWithDifferentCase_Deduplicates()
    {
        Guid uuid = Guid.NewGuid();
        List<string> order = [uuid.ToString().ToUpperInvariant(), "a"];

        List<string> result = CustomSortOrderHelper.PutFirst(order, uuid);

        Assert.That(result, Is.EqualTo(new[] { uuid.ToString(), "a" }));
    }
}
