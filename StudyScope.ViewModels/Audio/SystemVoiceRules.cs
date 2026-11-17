using StudyScope.Core.Interfaces;

namespace StudyScope.ViewModels.Audio;

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
    /// beim zweiten Sprecher möglichst eine andere Stimme als beim ersten.
    /// </summary>
    public static string ResolveVoice(IReadOnlyList<SpeechVoice> voices, string chosenId, int speakerIndex, string? otherVoiceId = null)
    {
        if (!string.IsNullOrWhiteSpace(chosenId) && voices.Any(v => v.Id == chosenId))
            return chosenId;

        var ordered = Order(voices);
        if (ordered.Count == 0)
            return string.Empty;

        if (speakerIndex == 0)
            return ordered[0].Id;

        var preferredLanguage = ordered[0].IsGerman;
        return ordered.FirstOrDefault(v => v.Id != otherVoiceId && v.IsGerman == preferredLanguage)?.Id
               ?? ordered.FirstOrDefault(v => v.Id != otherVoiceId)?.Id
               ?? ordered[0].Id;
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
