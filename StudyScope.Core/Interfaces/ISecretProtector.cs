namespace StudyScope.Core.Interfaces;

/// <summary>Wie gespeicherte API-Schlüssel geschützt werden.</summary>
public enum SecretProtectionMethod
{
    /// <summary>Rückfall: bisheriges Verfahren mit fest abgeleitetem Schlüssel (nur verschleiert, Format wie 1.2.0).</summary>
    Fallback,

    /// <summary>Windows DPAPI (CurrentUser): an das Windows-Konto gebunden.</summary>
    WindowsAccount
}

/// <summary>Ergebnis beim Lesen eines gespeicherten Schlüssels.</summary>
public enum SecretReadStatus
{
    /// <summary>Kein Schlüssel gespeichert.</summary>
    Empty,

    /// <summary>Gelesen, bereits im aktuellen Verfahren gespeichert.</summary>
    Ok,

    /// <summary>Gelesen, aber in einem älteren bzw. schwächeren Format gespeichert (beim nächsten Speichern neu schützen).</summary>
    NeedsUpgrade,

    /// <summary>Nicht lesbar (anderes Konto oder anderer Computer, unbekanntes Format, beschädigt). Der Wert ist leer.</summary>
    Unreadable
}

/// <summary>Gelesener Schlüssel: <see cref="Value"/> ist Klartext bzw. bei <see cref="SecretReadStatus.Unreadable"/> leer.</summary>
public readonly record struct SecretReadResult(SecretReadStatus Status, string Value)
{
    public static SecretReadResult Empty => new(SecretReadStatus.Empty, string.Empty);
    public static SecretReadResult Unreadable => new(SecretReadStatus.Unreadable, string.Empty);
}

/// <summary>Nur die Angabe, wie Schlüssel geschützt werden (für Hinweise in den Einstellungen).</summary>
public interface IKeyProtectionInfo
{
    SecretProtectionMethod Method { get; }
}

/// <summary>
/// Schützt API-Schlüssel für die Speicherung in den JSON-Dateien. Im Speicher sind Schlüssel immer Klartext.
/// Gespeicherte Werte tragen ein Präfix mit Verfahren und Version (z. B. „ss2:dpapi:…“); Werte ohne Präfix stammen
/// aus 1.2.0 (bzw. aus dem Rückfall) oder sind alter Klartext.
/// </summary>
public interface ISecretProtector : IKeyProtectionInfo
{
    /// <summary>Schützt einen Klartext-Schlüssel; leer bleibt leer.</summary>
    string Protect(string plainText);

    /// <summary>Liest einen gespeicherten Wert. Ein nicht lesbarer Wert wird nie als Klartext zurückgegeben.</summary>
    SecretReadResult Unprotect(string? stored);
}
