using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using StudyScope.Core.Interfaces;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Schutz der API-Schlüssel in den JSON-Dateien.
/// <list type="bullet">
/// <item>Windows: DPAPI (<see cref="DataProtectionScope.CurrentUser"/>), gespeichert als „ss2:dpapi:&lt;Base64&gt;“ –
/// an das Windows-Konto gebunden, auf einem anderen Computer oder unter einem anderen Konto nicht lesbar.</item>
/// <item>Sonst (Rückfall): bisheriges Verfahren aus 1.2.0 (<see cref="AesEncryptionService"/>, ohne Präfix), damit
/// 1.2.0 die Werte weiter lesen kann. Nur verschleiert.</item>
/// </list>
/// Gelesen werden alle Formate: Werte aus 1.2.0 (auch mehrfach verschlüsselt), alter Klartext und „ss2:…“-Werte, die
/// 1.2.0 nach einem Wechsel zurück noch einmal verschlüsselt hat. Ein nicht lesbarer „ss2:…“-Wert wird nie als
/// Klartext ausgegeben (<see cref="SecretReadStatus.Unreadable"/>).
/// </summary>
public sealed class SecretProtector : ISecretProtector
{
    /// <summary>Präfix aller Werte ab 1.3.0 (Version 2 des Formats).</summary>
    public const string FormatPrefix = "ss2:";

    /// <summary>Präfix für DPAPI-geschützte Werte.</summary>
    public const string DpapiPrefix = FormatPrefix + "dpapi:";

    // Zusätzliche Entropie für DPAPI: fest (nicht der Pfad), damit ein verschobener Benutzerdaten-Ordner lesbar bleibt
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("StudyScope.ApiKey.v2");

    // Frühere Versionen haben verschlüsselte Werte beim erneuten Speichern noch einmal verschlüsselt
    private const int MaxLegacyLayers = 10;

    private SecretProtector(SecretProtectionMethod method) => Method = method;

    public SecretProtectionMethod Method { get; }

    /// <summary>Verfahren des Betriebssystems: Windows DPAPI, sonst Rückfall.</summary>
    public static SecretProtector ForCurrentPlatform() =>
        new(OperatingSystem.IsWindows() ? SecretProtectionMethod.WindowsAccount : SecretProtectionMethod.Fallback);

    /// <summary>Nur das bisherige Verfahren (Rückfall); Standard für Tests mit eigenem Ordner.</summary>
    public static SecretProtector Fallback() => new(SecretProtectionMethod.Fallback);

    /// <summary>Windows DPAPI.</summary>
    [SupportedOSPlatform("windows")]
    public static SecretProtector WindowsAccount() => new(SecretProtectionMethod.WindowsAccount);

    public string Protect(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return string.Empty;

        if (Method == SecretProtectionMethod.WindowsAccount && OperatingSystem.IsWindows())
            return DpapiPrefix + Convert.ToBase64String(DpapiProtect(Encoding.UTF8.GetBytes(plainText)));

        return new AesEncryptionService().Encrypt(plainText);
    }

    public SecretReadResult Unprotect(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return SecretReadResult.Empty;

        if (stored.StartsWith(FormatPrefix, StringComparison.Ordinal))
            return UnprotectVersion2(stored);

        // Format bis 1.2.0: so oft entschlüsseln, wie es geht (mehrfach verschlüsselte Werte), sonst alter Klartext
        var value = stored;
        var layers = 0;
        while (layers < MaxLegacyLayers && AesEncryptionService.TryDecrypt(value, out var inner))
        {
            value = inner;
            layers++;
        }

        if (string.IsNullOrWhiteSpace(value))
            return SecretReadResult.Empty;

        // 1.2.0 hat einen „ss2:…“-Wert nach einem Wechsel zurück noch einmal verschlüsselt: auspacken, neu speichern
        if (value.StartsWith(FormatPrefix, StringComparison.Ordinal))
        {
            var wrapped = UnprotectVersion2(value);
            return wrapped.Status == SecretReadStatus.Ok ? wrapped with { Status = SecretReadStatus.NeedsUpgrade } : wrapped;
        }

        // Genau einmal verschlüsselt ist im Rückfall das aktuelle Format; Klartext und alles andere wird neu geschützt
        var current = Method == SecretProtectionMethod.Fallback && layers == 1;
        return new SecretReadResult(current ? SecretReadStatus.Ok : SecretReadStatus.NeedsUpgrade, value);
    }

    private SecretReadResult UnprotectVersion2(string stored)
    {
        if (!stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            return SecretReadResult.Unreadable; // unbekanntes Verfahren (z. B. aus einer neueren Version oder von Linux)

        if (!OperatingSystem.IsWindows())
            return SecretReadResult.Unreadable;

        try
        {
            var plain = Encoding.UTF8.GetString(DpapiUnprotect(Convert.FromBase64String(stored[DpapiPrefix.Length..])));
            if (string.IsNullOrWhiteSpace(plain))
                return SecretReadResult.Unreadable;

            // Im Rückfall (nur Tests unter Windows) bleibt ein lesbarer DPAPI-Wert stehen – nie zurückstufen
            return new SecretReadResult(SecretReadStatus.Ok, plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Anderes Konto, anderer Computer oder beschädigt
            return SecretReadResult.Unreadable;
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[] DpapiProtect(byte[] data) =>
        ProtectedData.Protect(data, DpapiEntropy, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] DpapiUnprotect(byte[] data) =>
        ProtectedData.Unprotect(data, DpapiEntropy, DataProtectionScope.CurrentUser);
}
