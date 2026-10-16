using NUnit.Framework;
using StudyScope.Core.Models;

namespace StudyScope.Tests.Models;

[TestFixture]
public class DocumentItemTests
{
    [Test]
    public void NewDocumentItem_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var doc = new DocumentItem();

        // Assert
        Assert.That(doc.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(doc.IsSelected, Is.True);
        Assert.That(doc.IsMissing, Is.False);
        Assert.That(doc.ExtractedChunkCount, Is.EqualTo(0));
        Assert.That(doc.FileName, Is.Empty);
        Assert.That(doc.FilePath, Is.Empty);
    }

    [Test]
    public void DocumentItem_WhenMarkedMissing_ShouldRetainMissingState()
    {
        // Arrange
        var doc = new DocumentItem
        {
            FileName = "TestDocument.pdf",
            FilePath = "/path/to/TestDocument.pdf"
        };

        // Act
        doc.IsMissing = true;

        // Assert
        Assert.That(doc.IsMissing, Is.True);
    }
}