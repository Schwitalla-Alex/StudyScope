using StudyScope.Core.Interfaces;
using StudyScope.ViewModels.Resources;

namespace StudyScope.ViewModels;

/// <summary>
/// Hinweise zum Schutz der API-Schlüssel in den Einstellungen (KI-Modell, Sprachausgabe, Websuche). Versprechen nie
/// mehr als das Verfahren hält: DPAPI bindet an das Windows-Konto, schützt aber nicht vor Programmen im eigenen Konto.
/// </summary>
public static class KeyProtectionTexts
{
    /// <summary>Zeile unter dem Schlüsselfeld; null ohne Angabe (Tests).</summary>
    public static string? Line(IKeyProtectionInfo? info) => info?.Method switch
    {
        SecretProtectionMethod.WindowsAccount => VmStrings.KeyProtection_WindowsAccount,
        SecretProtectionMethod.Fallback => VmStrings.KeyProtection_Fallback,
        _ => null
    };

    /// <summary>Rückfall: Hinweis als Warnung anzeigen.</summary>
    public static bool IsFallback(IKeyProtectionInfo? info) => info?.Method == SecretProtectionMethod.Fallback;

    /// <summary>Gespeicherter Schlüssel hier nicht lesbar.</summary>
    public static string Unreadable => VmStrings.KeyProtection_Unreadable;
}
