using NUnit.Framework;
using StudyScope.Core.Interfaces;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

[TestFixture]
public class FileOpenerServiceTests
{
    private FileOpenerService _service;

    [SetUp]
    public void SetUp()
    {
        _service = new FileOpenerService();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void OpenFile_WhenPathIsNullOrWhitespace_ShouldReturnFalse(string? invalidPath)
    {
        // Act
        var result = _service.OpenFile(invalidPath!);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void OpenFile_WhenFileDoesNotExist_ShouldReturnFalse()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "definitely_not_existing_file_12345.pdf");

        // Act
        //var result = _service.OpenFile(nonExistentPath);
        var result = _service.OpenFile(nonExistentPath);

        // Assert
        Assert.That(result, Is.False);
    }
}