using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.ViewModels.Audio;

/// <summary>Geschätztes Geschlecht einer Stimme (aus dem Namen) bzw. gewünschtes Geschlecht eines Sprechers.</summary>
public enum VoiceGender
{
    Unknown,
    Female,
    Male
}

/// <summary>
/// Bewertung der Systemstimme (Web Speech API der WebView) ohne Oberfläche: Verfügbarkeit samt Hinweistext,
/// Reihenfolge der Stimmen (deutsch und offline zuerst) und automatische Stimmenwahl je Sprecher.
/// </summary>
public static class SystemVoiceRules
{
    public const string WindowsGermanVoiceHint =
        "Deutsche Stimme unter Windows installieren: Einstellungen › Zeit und Sprache › Sprache und Region › Deutsch › Sprachoptionen › " +
        "bei „Sprachausgabe“ bzw. „Text-zu-Sprache“ herunterladen (Windows 11 alternativ: Zeit und Sprache › Spracherkennung › Stimmen verwalten › Stimmen hinzufügen). " +
        "Danach StudyScope neu starten.";

    public const string LinuxNoSpeechHint =
        "Unter Linux unterstützt die eingebaute Browser-Komponente (WebKitGTK) die Sprachausgabe häufig nicht. " +
        "Nutzen Sie stattdessen Gemini TTS – damit entsteht zugleich eine Audiodatei.";

    public static SpeechAvailability Evaluate(bool supported, IReadOnlyList<SpeechVoice> voices, bool isLinux)
    {
        if (!supported)
        {
            return SpeechAvailability.Unavailable(
                "Ihre Systemumgebung stellt keine Sprachausgabe bereit.",
                isLinux ? LinuxNoSpeechHint : "Nutzen Sie Gemini TTS, um den Beitrag als Audiodatei zu erzeugen.");
        }

        if (voices.Count == 0)
        {
            return SpeechAvailability.Unavailable(
                "Es ist keine Stimme installiert.",
                isLinux ? LinuxNoSpeechHint : WindowsGermanVoiceHint);
        }

        var german = voices.Where(v => v.IsGerman).ToList();
        if (german.Count == 0)
        {
            return SpeechAvailability.Warning(
                "Bereit – aber keine deutsche Stimme gefunden; der Text wird mit einer fremdsprachigen Stimme vorgelesen.",
                isLinux ? null : WindowsGermanVoiceHint);
        }

        var local = german.Count(v => v.IsLocal);
        if (local == 0)
        {
            return SpeechAvailability.Warning(
                $"Bereit – {CountText(german.Count, "deutsche Online-Stimme", "deutsche Online-Stimmen")}. " +
                "Online-Stimmen senden den Text an den Hersteller (Microsoft); offline ist keine deutsche Stimme installiert.",
                isLinux ? null : WindowsGermanVoiceHint);
        }

        return SpeechAvailability.Ready($"Bereit – {CountText(german.Count, "deutsche Stimme", "deutsche Stimmen")} verfügbar.");
    }

    private static string CountText(int count, string singular, string plural) => count == 1 ? $"1 {singular}" : $"{count} {plural}";

    /// <summary>Deutsche zuerst, dabei Offline- vor Online-Stimmen, sonst alphabetisch.</summary>
    public static List<SpeechVoice> Order(IEnumerable<SpeechVoice> voices) =>
        voices
            .OrderByDescending(v => v.IsGerman)
            .ThenByDescending(v => v.IsLocal)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Stimme für einen Sprecher: die gewählte, falls vorhanden; sonst automatisch (deutsch, offline bevorzugt) –
    /// passend zum Sprecher (<paramref name="preferredGender"/>, z. B. „Experte“ → männliche Stimme), soweit installiert,
    /// und beim zweiten Sprecher möglichst eine andere Stimme als beim ersten.
    /// </summary>
    public static string ResolveVoice(IReadOnlyList<SpeechVoice> voices, string chosenId, int speakerIndex, string? otherVoiceId = null,
        VoiceGender preferredGender = VoiceGender.Unknown)
    {
        if (!string.IsNullOrWhiteSpace(chosenId) && voices.Any(v => v.Id == chosenId))
            return chosenId;

        var ordered = Order(voices);
        if (ordered.Count == 0)
            return string.Empty;

        var preferredLanguage = ordered[0].IsGerman;
        var candidates = ordered.Where(v => v.IsGerman == preferredLanguage && (speakerIndex == 0 || v.Id != otherVoiceId)).ToList();

        if (preferredGender != VoiceGender.Unknown)
        {
            var matching = candidates.FirstOrDefault(v => GuessGender(v) == preferredGender);
            if (matching != null)
                return matching.Id;
        }

        if (speakerIndex == 0)
            return ordered[0].Id;

        return candidates.FirstOrDefault()?.Id
               ?? ordered.FirstOrDefault(v => v.Id != otherVoiceId)?.Id
               ?? ordered[0].Id;
    }

    // Bekannte Stimmen (Windows, Microsoft-Online-Stimmen, macOS/Google) – die Web Speech API liefert kein Geschlecht.
    private static readonly HashSet<string> FemaleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "hedda", "katja", "amala", "louisa", "seraphina", "ingrid", "leni", "elke", "gisela", "klarissa", "maja", "tanja",
        "anna", "petra", "marlene", "vicki", "helena", "hanna", "sabine", "yvonne", "female", "weiblich", "frau"
    };

    private static readonly HashSet<string> MaleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "stefan", "conrad", "killian", "florian", "jonas", "jan", "bernd", "christoph", "kasper", "ralf", "klaus", "hans",
        "markus", "yannick", "daniel", "michael", "thomas", "male", "männlich", "mann"
    };

    /// <summary>Schätzt das Geschlecht einer Stimme anhand ihres Namens (z. B. „Microsoft Stefan - German (Germany)“).</summary>
    public static VoiceGender GuessGender(SpeechVoice voice)
    {
        var words = System.Text.RegularExpressions.Regex.Split(voice.Name ?? string.Empty, @"[^\p{L}]+");
        foreach (var word in words)
        {
            if (word.Length == 0) continue;
            if (FemaleNames.Contains(word)) return VoiceGender.Female;
            if (MaleNames.Contains(word)) return VoiceGender.Male;
        }
        return VoiceGender.Unknown;
    }

    /// <summary>Gewünschte Stimme je Sprecherrolle: „Sprecherin“/„Moderatorin“ weiblich, „Experte“ männlich.</summary>
    public static VoiceGender SpeakerGender(string? speaker) => (speaker ?? string.Empty).Trim() switch
    {
        AudioScriptFormats.NarratorName or AudioScriptFormats.HostName => VoiceGender.Female,
        AudioScriptFormats.ExpertName => VoiceGender.Male,
        _ => VoiceGender.Unknown
    };

    public const string WindowsMoreVoicesHint =
        "Weitere deutsche Stimmen (z. B. „Stefan“) unter Windows: Einstellungen › Zeit und Sprache › Sprache und Region › Deutsch › " +
        "Sprachoptionen › bei „Sprachausgabe“ bzw. „Text-zu-Sprache“ herunterladen. Danach hier „↻ Neu prüfen“ bzw. StudyScope neu starten.";

    /// <summary>
    /// Hinweis, wenn die automatisch gewählte Stimme des zweiten Sprechers nicht passt: nur eine deutsche Stimme
    /// (beide klingen gleich) oder keine Stimme des gewünschten Geschlechts. null = alles passend.
    /// </summary>
    public static string? SecondVoiceHint(IReadOnlyList<SpeechVoice> voices, string primaryId, string secondaryId, string roleName,
        VoiceGender wanted, bool isLinux)
    {
        var german = voices.Where(v => v.IsGerman).ToList();
        if (german.Count == 0)
            return null; // Darauf weist schon die Verfügbarkeit hin

        var second = voices.FirstOrDefault(v => v.Id == secondaryId);
        if (second == null)
            return null;

        string message;
        if (secondaryId == primaryId || !second.IsGerman)
            message = $"Für zwei Sprecher ist nur eine deutsche Stimme installiert – beide klingen gleich.";
        else if (wanted != VoiceGender.Unknown && GuessGender(second) != wanted)
            message = $"Keine {(wanted == VoiceGender.Male ? "männliche" : "weibliche")} deutsche Stimme für „{roleName}“ installiert – " +
                      $"es wird „{ShortName(second)}“ verwendet.";
        else
            return null;

        return isLinux ? message : $"{message} {WindowsMoreVoicesHint}";
    }

    /// <summary>Kurzer Stimmenname: „Microsoft Stefan - German (Germany)“ → „Stefan“.</summary>
    public static string ShortName(SpeechVoice voice)
    {
        var name = voice.Name ?? string.Empty;
        var dash = name.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) name = name[..dash];
        if (name.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase)) name = name["Microsoft ".Length..];
        if (name.StartsWith("Google ", StringComparison.OrdinalIgnoreCase)) name = name["Google ".Length..];
        return name.Trim().Length > 0 ? name.Trim() : voice.Id;
    }

    /// <summary>
    /// Teilt einen Segmenttext in Sprechstücke (an Satzenden, höchstens <paramref name="maxLength"/> Zeichen):
    /// Manche Stimmen brechen sehr lange Äußerungen ab, und Pause/Weiter reagiert so feiner.
    /// </summary>
    public static List<string> SplitForSpeech(string? text, int maxLength = 280)
    {
        var result = new List<string>();
        var rest = (text ?? string.Empty).Trim();
        while (rest.Length > maxLength)
        {
            var cut = rest.LastIndexOfAny(new[] { '.', '!', '?', ';' }, maxLength - 1);
            if (cut < maxLength / 3)
                cut = rest.LastIndexOf(' ', maxLength - 1);
            if (cut <= 0)
                cut = maxLength - 1;

            result.Add(rest[..(cut + 1)].Trim());
            rest = rest[(cut + 1)..].Trim();
        }

        if (rest.Length > 0)
            result.Add(rest);
        return result;
    }
}
