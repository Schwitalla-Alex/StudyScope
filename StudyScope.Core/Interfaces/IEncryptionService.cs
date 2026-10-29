namespace StudyScope.Core.Interfaces;

/// <summary>
/// Stellt Methoden zur symmetrischen Ver- und Entschlüsselung sensibler Konfigurationsdaten bereit.
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Verschlüsselt einen Klartext-String in einen geschützten Base64-String.
    /// </summary>
    string Encrypt(string plainText);

    /// <summary>
    /// Entschlüsselt einen Base64-String wieder in den Klartext.
    /// </summary>
    string Decrypt(string cipherText);
}