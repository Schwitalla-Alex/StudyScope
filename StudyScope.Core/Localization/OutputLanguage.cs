using System.Globalization;
using System.Text.RegularExpressions;
using StudyScope.Core.Common;

namespace StudyScope.Core.Localization;

/// <summary>
/// Sprache der KI-Ergebnisse (Chat-Antworten, Karteikarten, Mindmap, Präsentation, Quiz, Datentabelle, Bericht,
/// Audio- und Video-Skript) – unabhängig von der Oberfläche. Einstellung als Text:
/// <see cref="FollowUi"/> („ui“, Standard), <see cref="FollowDocuments"/> („documents“) oder ein Kulturname („de“, „en“).
/// Gespeichert je Profil (<c>UserProfile.OutputLanguage</c>) und global als Rückfall (<c>UiSettings.OutputLanguage</c>).
/// <para>
/// Die Prompts bleiben deutsch (Erkennung der Aufträge durch Fake-KI und JSON-Modus); die Sprache steht in genau einer
/// Zeile (<see cref="PromptLine"/>), die die KI unabhängig von der Sprache des restlichen Prompts versteht.
/// Wählbar sind nur Sprachen der Oberfläche, damit feste Inhalte und Exporte immer passen.
/// </para>
/// </summary>
public static partial class OutputLanguage
{
    /// <summary>Wie die Oberfläche (Standard).</summary>
    public const string FollowUi = "ui";

    /// <summary>Wie die Dokumente (Erkennung über <see cref="LanguageGuess"/>, unsicher → Oberfläche).</summary>
    public const string FollowDocuments = "documents";

    /// <summary>Einstellung bereinigen: „ui“, „documents“ oder ein bekannter Kulturname; leer/unbekannt → „ui“.</summary>
    public static string Normalize(string? setting)
    {
        var v = setting?.Trim();
        if (string.IsNullOrEmpty(v) || string.Equals(v, FollowUi, StringComparison.OrdinalIgnoreCase)) return FollowUi;
        if (string.Equals(v, FollowDocuments, StringComparison.OrdinalIgnoreCase)) return FollowDocuments;
        return TryGetCulture(v) is { } c ? c.TwoLetterISOLanguageName : FollowUi;
    }

    /// <summary>
    /// Konkrete Sprache (neutrale Kultur, z. B. „en“) für eine Einstellung. Nur Sprachen aus <paramref name="available"/>
    /// (Sprachen der Oberfläche); alles andere – auch eine nicht erkannte Dokumentsprache – ergibt die Oberflächensprache.
    /// </summary>
    public static CultureInfo Resolve(string? setting, CultureInfo uiCulture, IEnumerable<string> texts, IReadOnlyCollection<string> available)
    {
        var ui = Neutral(uiCulture) ?? CultureInfo.GetCultureInfo(UiLanguage.NeutralLanguage);
        bool IsAvailable(string lang) => available.Any(a => string.Equals(Neutral(a), lang, StringComparison.OrdinalIgnoreCase));

        var normalized = Normalize(setting);
        if (normalized == FollowUi) return ui;
        if (normalized == FollowDocuments)
            return LanguageGuess.Guess(texts) is { } guessed && IsAvailable(guessed) ? CultureInfo.GetCultureInfo(guessed) : ui;
        return IsAvailable(normalized) ? CultureInfo.GetCultureInfo(normalized) : ui;
    }

    /// <summary>
    /// Die Sprachzeile des Prompts, z. B. „Sprache der Antwort: Englisch (English).“ – deutscher Name und Eigenname,
    /// damit auch kleine lokale Modelle sie verstehen. Steht in jedem Auftrag genau einmal.
    /// </summary>
    public static string PromptLine(CultureInfo culture)
    {
        var neutral = Neutral(culture) ?? culture;
        var germanName = GermanName(neutral);
        var native = UiLanguage.NativeName(neutral.Name);
        var name = string.Equals(germanName, native, StringComparison.OrdinalIgnoreCase) ? neutral.Name : native;
        return $"{LinePrefix} {germanName} ({name}). {LineRule}";
    }

    /// <summary>Beginn der Sprachzeile; daran erkennt der Dispatcher die Sprache einer Chat-Anfrage.</summary>
    public const string LinePrefix = "Sprache der Antwort:";

    /// <summary>
    /// Zusatz der Sprachzeile: alle Texte samt Titeln und Fachbegriffen in dieser Sprache, unabhängig von den Auszügen.
    /// Seit 3c-2 ausdrücklich mit Übersetzen der Fachbegriffe und ohne Originalbegriff in Klammern – im Echttest blieben
    /// sonst vereinzelt Begriffe aus den Auszügen stehen („Calvin-Zyklus“, „(Atmungskette)“). Ohne Klammern, damit
    /// <see cref="TryParse"/> nur die Sprachangabe findet.
    /// </summary>
    public const string LineRule =
        "Alle Texte in dieser Sprache schreiben, auch Titel, Überschriften und Fachbegriffe, auch wenn die Auszüge eine andere Sprache haben. " +
        "Fachbegriffe aus den Auszügen in den in dieser Sprache üblichen Fachbegriff übersetzen, nicht unverändert übernehmen und nicht zusätzlich in der Sprache der Auszüge in Klammern nennen. " +
        "Unverändert bleiben nur Eigennamen, Formeln, Einheiten und feste Abkürzungen.";

    /// <summary>Sprache aus der Sprachzeile eines Prompts (der Eigenname bzw. Code in Klammern) oder null.</summary>
    public static CultureInfo? TryParse(string? prompt)
    {
        if (string.IsNullOrEmpty(prompt)) return null;
        var m = LineRegex().Match(prompt);
        if (!m.Success) return null;
        var inner = m.Groups["inner"].Value.Trim();
        if (TryGetCulture(inner) is { } byCode) return byCode;
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            if (string.IsNullOrEmpty(culture.Name)) continue;
            if (string.Equals(UiLanguage.NativeName(culture.Name), inner, StringComparison.OrdinalIgnoreCase))
                return culture;
        }
        return null;
    }

    /// <summary>Prompt ohne Sprachzeile (z. B. für die Anzeige der Frage in der Fake-KI).</summary>
    public static string WithoutLine(string prompt) => LineRegex().Replace(prompt, string.Empty).TrimEnd();

    /// <summary>
    /// Wählbare Sprachen der Ergebnisse = Sprachen der Oberfläche (neutral, Satellitenordner neben dem Programm).
    /// </summary>
    public static IReadOnlyList<string> AvailableLanguages() =>
        UiLanguage.AvailableCultures(AppContext.BaseDirectory, typeof(Resources.CoreStrings).Assembly.GetName().Name!);

    /// <summary>Kurzform von <see cref="Resolve(string?, CultureInfo, IEnumerable{string}, IReadOnlyCollection{string})"/> mit Oberflächensprache und verfügbaren Sprachen.</summary>
    public static CultureInfo Resolve(string? setting, IEnumerable<string> texts) =>
        Resolve(setting, CultureInfo.CurrentUICulture, texts, AvailableLanguages());

    /// <summary>Neutrale Kultur („en-GB“ → „en“) oder null bei der invarianten Kultur.</summary>
    public static CultureInfo? Neutral(CultureInfo culture) =>
        string.IsNullOrEmpty(culture.Name) ? null : CultureInfo.GetCultureInfo(culture.TwoLetterISOLanguageName);

    private static string? Neutral(string cultureName) => TryGetCulture(cultureName)?.TwoLetterISOLanguageName;

    /// <summary>Deutscher Name der Sprache („Englisch“), unabhängig von der Oberflächensprache.</summary>
    private static string GermanName(CultureInfo culture)
    {
        // .NET liefert DisplayName in der Sprache der Installation; für den Prompt braucht es verlässlich Deutsch
        return culture.TwoLetterISOLanguageName switch
        {
            "de" => "Deutsch",
            "en" => "Englisch",
            "fr" => "Französisch",
            "es" => "Spanisch",
            "it" => "Italienisch",
            "pt" => "Portugiesisch",
            "nl" => "Niederländisch",
            "pl" => "Polnisch",
            "tr" => "Türkisch",
            "ar" => "Arabisch",
            _ => culture.EnglishName
        };
    }

    private static CultureInfo? TryGetCulture(string name)
    {
        try
        {
            var c = CultureInfo.GetCultureInfo(name, predefinedOnly: true);
            return string.IsNullOrEmpty(c.Name) ? null : CultureInfo.GetCultureInfo(c.TwoLetterISOLanguageName);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^[ \t-]*Sprache der Antwort:[^\n(]*\((?<inner>[^)\n]+)\)[^\n]*\r?\n?", RegexOptions.Multiline)]
    private static partial Regex LineRegex();
}
