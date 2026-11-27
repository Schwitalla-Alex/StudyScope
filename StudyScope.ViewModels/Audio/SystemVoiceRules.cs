using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Core.Localization;
using StudyScope.ViewModels.Resources;

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
/// Reihenfolge der Stimmen und automatische Stimmenwahl je Sprecher – jeweils für die Sprache des Beitrags
/// (Code „de“, „en“, siehe <see cref="SpeechLanguage"/>). Bevorzugte Stimmen und Namen für die Schätzung des
/// Geschlechts stehen je Sprache in <c>Languages/&lt;sprache&gt;/voices.txt</c> (<see cref="VoiceNames"/>).
/// </summary>
public static class SystemVoiceRules
{
    /// <summary>Installationsweg einer Stimme der Sprache unter Windows.</summary>
    public static string WindowsVoiceHint(string? language) =>
        Loc.Format(VmStrings.Voice_WindowsInstallHint, ("language", LanguageKey(language)));

    public static string LinuxNoSpeechHint => VmStrings.Voice_LinuxNoSpeech;

    public static SpeechAvailability Evaluate(bool supported, IReadOnlyList<SpeechVoice> voices, bool isLinux, string? language)
    {
        var key = LanguageKey(language);
        if (!supported)
        {
            return SpeechAvailability.Unavailable(
                VmStrings.Voice_NotSupported,
                isLinux ? LinuxNoSpeechHint : VmStrings.Voice_UseGemini);
        }

        if (voices.Count == 0)
        {
            return SpeechAvailability.Unavailable(
                VmStrings.Voice_NoneInstalled,
                isLinux ? LinuxNoSpeechHint : WindowsVoiceHint(key));
        }

        var matching = voices.Where(v => v.Matches(key)).ToList();
        if (matching.Count == 0)
        {
            return SpeechAvailability.Warning(
                Loc.Format(VmStrings.Voice_NoMatchingLanguage, ("language", key)),
                isLinux ? null : WindowsVoiceHint(key));
        }

        var local = matching.Count(v => v.IsLocal);
        if (local == 0)
        {
            return SpeechAvailability.Warning(
                Loc.Format(VmStrings.Voice_OnlyOnline, ("count", matching.Count), ("language", key)),
                isLinux ? null : WindowsVoiceHint(key));
        }

        return SpeechAvailability.Ready(Loc.Format(VmStrings.Voice_Ready, ("count", matching.Count), ("language", key)));
    }

    /// <summary>
    /// Stimmen der Sprache zuerst, dabei Offline- vor Online-Stimmen, dann nach Vorrang aus <c>voices.txt</c>,
    /// sonst alphabetisch.
    /// </summary>
    public static List<SpeechVoice> Order(IEnumerable<SpeechVoice> voices, string? language) =>
        voices
            .OrderByDescending(v => v.Matches(language))
            .ThenByDescending(v => v.IsLocal)
            .ThenBy(v => VoiceNames.Rank(NameWords(v), language))
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Stimme für einen Sprecher: die gewählte, falls vorhanden; sonst automatisch (Sprache des Beitrags, offline
    /// bevorzugt) – passend zum Sprecher (<paramref name="preferredGender"/>, z. B. „Experte“ → männliche Stimme),
    /// soweit installiert, und beim zweiten Sprecher möglichst eine andere Stimme als beim ersten.
    /// </summary>
    public static string ResolveVoice(IReadOnlyList<SpeechVoice> voices, string chosenId, int speakerIndex, string? otherVoiceId,
        VoiceGender preferredGender, string? language)
    {
        if (!string.IsNullOrWhiteSpace(chosenId) && voices.Any(v => v.Id == chosenId))
            return chosenId;

        var ordered = Order(voices, language);
        if (ordered.Count == 0)
            return string.Empty;

        var preferredLanguage = ordered[0].Matches(language);
        var candidates = ordered.Where(v => v.Matches(language) == preferredLanguage && (speakerIndex == 0 || v.Id != otherVoiceId)).ToList();

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

    /// <summary>
    /// Schätzt das Geschlecht einer Stimme anhand ihres Namens (z. B. „Microsoft Stefan - German (Germany)“) über die
    /// Namenslisten aller Sprachen (<see cref="VoiceNames"/>); die Web Speech API liefert kein Geschlecht.
    /// </summary>
    public static VoiceGender GuessGender(SpeechVoice voice)
    {
        foreach (var word in NameWords(voice))
        {
            var gender = VoiceNames.GenderOf(word);
            if (gender != VoiceGender.Unknown)
                return gender;
        }
        return VoiceGender.Unknown;
    }

    private static IEnumerable<string> NameWords(SpeechVoice voice) => VoiceNames.Words(voice.Name);

    /// <summary>
    /// Schlüssel der Sprache für die ICU-Auswahl in den Texten („de“, „en“; unbekannt bzw. leer → Deutsch wie vor 3c-3).
    /// </summary>
    public static string LanguageKey(string? language) => SpeechLanguage.Code(language) ?? SpeechLanguage.Default;

    /// <summary>Gewünschte Stimme je Sprecherrolle: „Sprecherin“/„Moderatorin“ weiblich, „Experte“ männlich.</summary>
    public static VoiceGender SpeakerGender(string? speaker) => (speaker ?? string.Empty).Trim() switch
    {
        AudioScriptFormats.NarratorName or AudioScriptFormats.HostName => VoiceGender.Female,
        AudioScriptFormats.ExpertName => VoiceGender.Male,
        _ => VoiceGender.Unknown
    };

    /// <summary>Weitere Stimmen der Sprache unter Windows installieren (mit Verweis auf „↻ Neu prüfen“).</summary>
    public static string WindowsMoreVoicesHint(string? language) =>
        Loc.Format(VmStrings.Voice_WindowsMoreVoices, ("language", LanguageKey(language)), ("recheck", VmStrings.Speech_Recheck));

    /// <summary>
    /// Hinweis, wenn die automatisch gewählte Stimme des zweiten Sprechers nicht passt: nur eine Stimme der Sprache
    /// (beide klingen gleich) oder keine Stimme des gewünschten Geschlechts. null = alles passend.
    /// </summary>
    public static string? SecondVoiceHint(IReadOnlyList<SpeechVoice> voices, string primaryId, string secondaryId, string roleName,
        VoiceGender wanted, bool isLinux, string? language)
    {
        var key = LanguageKey(language);
        if (!voices.Any(v => v.Matches(key)))
            return null; // Darauf weist schon die Verfügbarkeit hin

        var second = voices.FirstOrDefault(v => v.Id == secondaryId);
        if (second == null)
            return null;

        string message;
        if (secondaryId == primaryId || !second.Matches(key))
            message = Loc.Format(VmStrings.Voice_OnlyOneVoice, ("language", key));
        else if (wanted != VoiceGender.Unknown && GuessGender(second) != wanted)
            message = Loc.Format(VmStrings.Voice_NoMatchingGender, ("gender", wanted == VoiceGender.Male ? "male" : "female"),
                ("language", key), ("role", MediaTexts.SpeakerName(roleName)), ("voice", ShortName(second)));
        else
            return null;

        return isLinux ? message : $"{message} {WindowsMoreVoicesHint(key)}";
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
