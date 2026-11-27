using System.Globalization;
using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Resources;

namespace StudyScope.ViewModels;

/// <summary>
/// Titel, den die Dienste nehmen, wenn die KI keinen liefert („Glossar: Skript“, „Unterlagen: Standard“) – in der
/// Ergebnissprache, nicht in der Oberflächensprache. Die Sprache wird wie im Dienst aus der Einstellung und den
/// Textstellen bestimmt (<see cref="OutputLanguage.Resolve(string?, IEnumerable{string})"/>); bei „Wie die Dokumente“
/// wertet der Dienst nur seine Auswahl der Textstellen aus, das Ergebnis stimmt praktisch immer überein.
/// </summary>
public static class FallbackTitles
{
    /// <summary>Ergebnissprache für die Einstellung und alle Textstellen der ausgewählten Dokumente.</summary>
    public static CultureInfo Language(string? setting, IEnumerable<string> texts) => OutputLanguage.Resolve(setting, texts);

    /// <summary>
    /// „{Art}: {Quelle}“ – Quelle ist der Dateiname bei genau einem Dokument, sonst der (angezeigte) Profilname.
    /// <paramref name="kind"/> wird in der Ergebnissprache gelesen (z. B. <c>() => TemplateTexts.Name(template)</c>).
    /// </summary>
    public static string Titled(CultureInfo language, Func<string> kind, IReadOnlyList<DocumentItem> documents, string profileName)
    {
        using var scope = CultureScope.Use(language);
        var source = documents.Count == 1 ? Path.GetFileNameWithoutExtension(documents[0].FileName) : ProfileDisplay.Name(profileName);
        return Loc.Format(VmStrings.Fallback_Titled, ("kind", kind()), ("source", source));
    }

    /// <summary>Dateiname bei genau einem Dokument, sonst „Unterlagen: {Profil}“ (Präsentation, Folien fürs Video).</summary>
    public static string Documents(CultureInfo language, IReadOnlyList<DocumentItem> documents, string profileName)
    {
        if (documents.Count == 1) return Path.GetFileNameWithoutExtension(documents[0].FileName);
        using var scope = CultureScope.Use(language);
        return Loc.Format(VmStrings.Fallback_Documents, ("profile", ProfileDisplay.Name(profileName)));
    }
}
