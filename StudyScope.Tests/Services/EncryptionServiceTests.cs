using NUnit.Framework;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

[TestFixture]
public class EncryptionServiceTests
{
    private AesEncryptionService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new AesEncryptionService();
    }

    [Test]
    public void Encrypt_WhenGivenPlainText_ShouldReturnEncryptedBase64String()
    {
        // Arrange
        var rawApiKey = "AIzaSyB9CgV9HcnC4IDkNi4fTQrotj1dPyZzudA";

        // Act
        var encrypted = _service.Encrypt(rawApiKey);

        // Assert
        Assert.That(encrypted, Is.Not.Null.And.Not.Empty);
        Assert.That(encrypted, Is.Not.EqualTo(rawApiKey));
    }

    [Test]
    public void Decrypt_WhenGivenEncryptedString_ShouldRestoreOriginalPlainText()
    {
        // Arrange
        var rawApiKey = "AIzaSyB9CgV9HcnC4IDkNi4fTQrotj1dPyZzudA";
        var encrypted = _service.Encrypt(rawApiKey);

        // Act
        var decrypted = _service.Decrypt(encrypted);

        // Assert
        Assert.That(decrypted, Is.EqualTo(rawApiKey));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void EncryptAndDecrypt_WhenEmptyOrNull_ShouldHandleSafely(string? input)
    {
        // Act
        var encrypted = _service.Encrypt(input!);
        var decrypted = _service.Decrypt(encrypted);

        // Assert
        Assert.That(string.IsNullOrWhiteSpace(decrypted), Is.True);
    }

    [Test]
    public void Encrypt_WhenCalledTwiceWithSamePlainText_ShouldProduceDifferentCiphertexts()
    {
        // Arrange: Gleicher Key soll wegen zufälliger Nonce/IV niemals denselben Chiffretext ergeben
        var rawApiKey = "AIzaSyB9CgV9HcnC4IDkNi4fTQrotj1dPyZzudA";

        // Act
        var encryptedFirst = _service.Encrypt(rawApiKey);
        var encryptedSecond = _service.Encrypt(rawApiKey);

        // Assert
        Assert.That(encryptedFirst, Is.Not.EqualTo(encryptedSecond));
        Assert.That(_service.Decrypt(encryptedFirst), Is.EqualTo(rawApiKey));
        Assert.That(_service.Decrypt(encryptedSecond), Is.EqualTo(rawApiKey));
    }

    [Test]
    public void Decrypt_WhenGivenInvalidOrCorruptedBase64_ShouldNotThrowAndHandleGracefully()
    {
        // Arrange: Manipulierter Base64-Payload
        var corruptedPayload = "VGhpcyBJcyBBbiBJbnZhbGlkIENpcGhlclRleHQh";

        // Act & Assert
        Assert.DoesNotThrow(() =>
        {
            var result = _service.Decrypt(corruptedPayload);
            Assert.That(result, Is.Not.Null);
        });
    }

    [Test]
    public void EncryptAndDecrypt_WhenPlainTextContainsSpecialCharactersAndUmlauts_ShouldRestoreExactly()
    {
        // Arrange
        var specialSecret = "Schlüssel_mit_Sonderzeichen_!@#$%^&*()_äöüß_€";

        // Act
        var encrypted = _service.Encrypt(specialSecret);
        var decrypted = _service.Decrypt(encrypted);

        // Assert
        Assert.That(decrypted, Is.EqualTo(specialSecret));
    }
}