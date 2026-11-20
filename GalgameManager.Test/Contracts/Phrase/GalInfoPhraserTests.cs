using System.Diagnostics.CodeAnalysis;
using GalgameManager.Contracts.Phrase;

namespace GalgameManager.Test.Contracts.Phrase;

[TestFixture]
[SuppressMessage("ReSharper", "StringLiteralTypo")]
public class GalInfoPhraserTests
{
    [Test]
    [TestCase("bacde", "abed", 0.672)]
    [TestCase("FAREMVIEL", "FARMVILLE", 0.88425)]
    [TestCase("AMBITIOUS MISSION", "ambitious mission", 1)]
    [TestCase("", "abc", 0)]
    [TestCase("abc", "xyz", 0)]
    [TestCase("ＡＢＣ　ＤＥＦ", "abcdef", 1)]
    public void SimilarityTest(string first, string second, double target)
    {
        // Arrange
        // Act
        var result = IGalInfoPhraser.Similarity(first, second);
        // Console.WriteLine(result);
        // Assert
        Assert.That(result, Is.EqualTo(target).Within(0.001));
    }
}
