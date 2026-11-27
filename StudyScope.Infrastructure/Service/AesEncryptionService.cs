using System.Security.Cryptography;
using System.Text;
using StudyScope.Core.Interfaces;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Bisheriges Verfahren bis 1.2.0 (AES-GCM mit fest abgeleitetem Schlüssel, Base64 ohne Präfix). Wird nur noch zum Lesen
/// alter Werte und als Rückfall genutzt, wenn das Betriebssystem keinen Schutz bietet (siehe <see cref="SecretProtector"/>).
/// Wer StudyScope und die Datei hat, kann solche Werte entschlüsseln – sie sind nur verschleiert.
/// </summary>
public class AesEncryptionService : IEncryptionService
{
    // Fest abgeleiteter 256-Bit-Schlüssel (muss gleich bleiben, sonst sind Werte aus 1.2.0 nicht mehr lesbar)
    private static readonly byte[] Key = SHA256.HashData(Encoding.UTF8.GetBytes("StudyScope-Secret-Storage-Key-v1-2026"));

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return string.Empty;

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 Bytes
        RandomNumberGenerator.Fill(nonce);

        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 Bytes

        using var aesGcm = new AesGcm(Key, AesGcm.TagByteSizes.MaxSize);
        aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Aufbau: Nonce (12) + Tag (16) + CipherText
        var payload = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, payload, nonce.Length + tag.Length, cipherBytes.Length);

        return Convert.ToBase64String(payload);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
            return string.Empty;

        // Altes Verhalten: nicht entschlüsselbarer Text kommt unverändert zurück (alter Klartext)
        return TryDecrypt(cipherText, out var plain) ? plain : cipherText;
    }

    /// <summary>Entschlüsselt einen Wert im Format bis 1.2.0; false, wenn er kein solches Chiffrat ist.</summary>
    public static bool TryDecrypt(string? cipherText, out string plainText)
    {
        plainText = string.Empty;
        if (string.IsNullOrWhiteSpace(cipherText))
            return false;

        try
        {
            var payload = Convert.FromBase64String(cipherText);
            var nonceSize = AesGcm.NonceByteSizes.MaxSize;
            var tagSize = AesGcm.TagByteSizes.MaxSize;

            if (payload.Length < nonceSize + tagSize)
                return false;

            var nonce = new byte[nonceSize];
            var tag = new byte[tagSize];
            var cipherLength = payload.Length - nonceSize - tagSize;
            var cipherBytes = new byte[cipherLength];

            Buffer.BlockCopy(payload, 0, nonce, 0, nonceSize);
            Buffer.BlockCopy(payload, nonceSize, tag, 0, tagSize);
            Buffer.BlockCopy(payload, nonceSize + tagSize, cipherBytes, 0, cipherLength);

            var plainBytes = new byte[cipherLength];
            using var aesGcm = new AesGcm(Key, tagSize);
            aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

            plainText = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            // Kein Chiffrat (alter Klartext) oder manipuliert
            return false;
        }
    }
}