namespace StudyScope.Core.Models;

/// <summary>Anbieter der Sprachausgabe (unabhängig von der Chat-KI des Profils).</summary>
public enum SpeechProviderType
{
    /// <summary>Stimmen des Betriebssystems über die Web Speech API der WebView (kostenlos, offline, keine Datei).</summary>
    SystemVoice,

    /// <summary>Google Gemini TTS (Cloud, Audiodatei, Podcast mit zwei Stimmen).</summary>
    GeminiTts
}

/// <summary>
/// Einstellungen der Sprachausgabe je Profil (Rückfall: globale config.json). Der eigene Gemini-Schlüssel wird nur
/// genutzt, wenn das Profil nicht ohnehin Gemini als Chat-KI mit Schlüssel eingerichtet hat; er wird wie die anderen
/// API-Keys verschlüsselt gespeichert und ist im Speicher Klartext.
/// </summary>
public class SpeechSettings
{
    public const double MinRate = 0.5;
    public const double MaxRate = 2.0;

    public SpeechProviderType Provider { get; set; } = SpeechProviderType.SystemVoice;

    /// <summary>Eigener Schlüssel für Gemini TTS (wenn das Profil keinen Gemini-Schlüssel hat).</summary>
    public string GeminiApiKey { get; set; } = string.Empty;

    /// <summary>Gemini-Modell für die Sprachausgabe (änderbar, weil Vorschau-Modelle umbenannt werden).</summary>
    public string GeminiModel { get; set; } = GeminiTtsInfo.DefaultModel;

    public string GeminiVoicePrimary { get; set; } = GeminiTtsInfo.DefaultVoicePrimary;
    public string GeminiVoiceSecondary { get; set; } = GeminiTtsInfo.DefaultVoiceSecondary;

    /// <summary>
    /// Kennung (voiceURI) der Systemstimme für den ersten Sprecher bei deutschen Beiträgen; leer = automatisch.
    /// Vor 3c-3 die einzige Wahl (damals wurde immer deutsch vorgelesen); weitere Sprachen stehen in
    /// <see cref="SystemVoicesByLanguage"/>. Zugriff über <see cref="GetSystemVoice"/>/<see cref="SetSystemVoice"/>.
    /// </summary>
    public string SystemVoicePrimary { get; set; } = string.Empty;

    /// <summary>Kennung der Systemstimme für den zweiten Sprecher (Podcast) bei deutschen Beiträgen; leer = automatisch eine andere Stimme.</summary>
    public string SystemVoiceSecondary { get; set; } = string.Empty;

    /// <summary>
    /// Gewählte Systemstimmen je Sprache des Beitrags (Code „en“ → Stimmen für Sprecher 1 und 2). Deutsch steht
    /// zusätzlich in <see cref="SystemVoicePrimary"/>/<see cref="SystemVoiceSecondary"/>, damit ältere Fassungen
    /// der App die Wahl weiter lesen.
    /// </summary>
    public Dictionary<string, SystemVoiceChoice> SystemVoicesByLanguage { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gewählte Systemstimme eines Sprechers (0/1) für die Sprache; leer = automatisch.</summary>
    public string GetSystemVoice(string? language, int speakerIndex)
    {
        var code = LanguageCode(language);
        if (SystemVoicesByLanguage?.TryGetValue(code, out var choice) == true && choice != null)
            return (speakerIndex == 0 ? choice.Primary : choice.Secondary) ?? string.Empty;
        if (code == GermanCode)
            return (speakerIndex == 0 ? SystemVoicePrimary : SystemVoiceSecondary) ?? string.Empty;
        return string.Empty;
    }

    /// <summary>Merkt die Systemstimme eines Sprechers (0/1) für die Sprache; leer = automatisch.</summary>
    public void SetSystemVoice(string? language, int speakerIndex, string? voiceId)
    {
        var code = LanguageCode(language);
        SystemVoicesByLanguage ??= new Dictionary<string, SystemVoiceChoice>(StringComparer.OrdinalIgnoreCase);
        if (!SystemVoicesByLanguage.TryGetValue(code, out var choice) || choice == null)
        {
            choice = code == GermanCode
                ? new SystemVoiceChoice { Primary = SystemVoicePrimary ?? string.Empty, Secondary = SystemVoiceSecondary ?? string.Empty }
                : new SystemVoiceChoice();
            SystemVoicesByLanguage[code] = choice;
        }

        if (speakerIndex == 0) choice.Primary = voiceId ?? string.Empty;
        else choice.Secondary = voiceId ?? string.Empty;

        if (code == GermanCode)
        {
            SystemVoicePrimary = choice.Primary;
            SystemVoiceSecondary = choice.Secondary;
        }
    }

    private const string GermanCode = "de";

    private static string LanguageCode(string? language)
    {
        var value = (language ?? string.Empty).Trim();
        var dash = value.IndexOfAny(new[] { '-', '_' });
        if (dash > 0) value = value[..dash];
        return value.Length == 0 ? GermanCode : value.ToLowerInvariant();
    }

    /// <summary>Sprechgeschwindigkeit (1 = normal).</summary>
    public double Rate { get; set; } = 1.0;

    public SpeechSettings Clone()
    {
        var copy = (SpeechSettings)MemberwiseClone();
        copy.SystemVoicesByLanguage = new Dictionary<string, SystemVoiceChoice>(StringComparer.OrdinalIgnoreCase);
        foreach (var (language, choice) in SystemVoicesByLanguage ?? new Dictionary<string, SystemVoiceChoice>())
        {
            if (choice != null)
                copy.SystemVoicesByLanguage[language] = new SystemVoiceChoice { Primary = choice.Primary, Secondary = choice.Secondary };
        }
        return copy;
    }

    public static double ClampRate(double rate) =>
        double.IsNaN(rate) ? 1.0 : Math.Clamp(rate, MinRate, MaxRate);
}

/// <summary>Gewählte Systemstimmen einer Sprache (leer = automatisch).</summary>
public sealed class SystemVoiceChoice
{
    public string Primary { get; set; } = string.Empty;
    public string Secondary { get; set; } = string.Empty;
}

/// <summary>
/// Feste Angaben zu Gemini TTS (Stand November 2026, Quelle: aistudio.google.com/docs/speech-generation und /docs/pricing).
/// Preise und Modellnamen ändern sich – hier zentral pflegen.
/// </summary>
public static class GeminiTtsInfo
{
    public const string DefaultModel = "gemini-3.8-flash-tts";
    public const string DefaultVoicePrimary = "Kore";
    public const string DefaultVoiceSecondary = "Puck";

    /// <summary>Audio-Token je Sekunde erzeugter Sprache.</summary>
    public const double AudioTokensPerSecond = 25;

    /// <summary>Preis in US-Dollar je 1 Mio. Audio-Token (Ausgabe), gemini-3.8-flash-tts bis 31.12.2026.</summary>
    public const double UsdPerMillionAudioTokens = 9.0;

    /// <summary>Preis in US-Dollar je 1 Mio. Text-Token (Eingabe).</summary>
    public const double UsdPerMillionTextTokens = 0.5;

    /// <summary>WAV mit 24 kHz, 16 Bit, mono: Bytes je Sekunde.</summary>
    public const int WavBytesPerSecond = 24000 * 2;

    /// <summary>Höchstens zwei Sprecher je Anfrage.</summary>
    public const int MaxSpeakers = 2;

    /// <summary>Bis zu diesem Tag gilt der Einführungspreis; danach verdoppelt Google den Preis für Audio-Ausgabe.</summary>
    public static readonly DateTime IntroductoryPriceUntil = new(2026, 12, 31);

    /// <summary>Preis je 1 Mio. Audio-Token zum angegebenen Datum.</summary>
    public static double UsdPerMillionAudioTokensAt(DateTime date) =>
        date.Date <= IntroductoryPriceUntil ? UsdPerMillionAudioTokens : UsdPerMillionAudioTokens * 2;

    /// <summary>Geschätzte Audiodauer, Dateigröße (WAV) und Kosten für ein Skript.</summary>
    public static GeminiTtsEstimate Estimate(int words, int characters, DateTime date)
    {
        var seconds = Math.Max(1, words) / AudioScriptFormats.WordsPerMinute * 60;
        var audioTokens = seconds * AudioTokensPerSecond;
        var textTokens = characters / 4.0 + 50;
        var usd = audioTokens * UsdPerMillionAudioTokensAt(date) / 1_000_000 + textTokens * UsdPerMillionTextTokens / 1_000_000;
        return new GeminiTtsEstimate(seconds, (long)(seconds * WavBytesPerSecond) + 44, usd);
    }

    /// <summary>Stimmlage der vorgefertigten Stimmen (laut Google; für die Vorauswahl je Sprecherrolle).</summary>
    public static bool IsFemaleVoice(string voice) => FemaleVoices.Contains(voice);

    private static readonly HashSet<string> FemaleVoices = new(StringComparer.OrdinalIgnoreCase)
    {
        "Zephyr", "Kore", "Leda", "Aoede", "Callirrhoe", "Autonoe", "Despina", "Erinome", "Laomedeia", "Achernar",
        "Gacrux", "Pulcherrima", "Vindemiatrix", "Sulafat"
    };

    /// <summary>Die 30 vorgefertigten Stimmen.</summary>
    public static IReadOnlyList<string> Voices { get; } = new[]
    {
        "Zephyr", "Puck", "Charon", "Kore", "Fenrir", "Leda", "Orus", "Aoede", "Callirrhoe", "Autonoe",
        "Enceladus", "Iapetus", "Umbriel", "Algieba", "Despina", "Erinome", "Algenib", "Rasalgethi", "Laomedeia", "Achernar",
        "Alnilam", "Schedar", "Gacrux", "Pulcherrima", "Achird", "Zubenelgenubi", "Vindemiatrix", "Sadachbia", "Sadaltager", "Sulafat"
    };
}

/// <summary>Schätzung vor dem Erzeugen: Dauer, Dateigröße und Kosten (US-Dollar, ohne kostenlose Stufe).</summary>
public sealed record GeminiTtsEstimate(double Seconds, long FileBytes, double Usd)
{
    public double Megabytes => FileBytes / 1_000_000.0;
}
