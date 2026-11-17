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

    /// <summary>Kennung (voiceURI) der Systemstimme für den ersten Sprecher; leer = automatisch (deutsch bevorzugt).</summary>
    public string SystemVoicePrimary { get; set; } = string.Empty;

    /// <summary>Kennung der Systemstimme für den zweiten Sprecher (Podcast); leer = automatisch eine andere Stimme.</summary>
    public string SystemVoiceSecondary { get; set; } = string.Empty;

    /// <summary>Sprechgeschwindigkeit (1 = normal).</summary>
    public double Rate { get; set; } = 1.0;

    public SpeechSettings Clone() => (SpeechSettings)MemberwiseClone();

    public static double ClampRate(double rate) =>
        double.IsNaN(rate) ? 1.0 : Math.Clamp(rate, MinRate, MaxRate);
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

    /// <summary>Die 30 vorgefertigten Stimmen.</summary>
    public static IReadOnlyList<string> Voices { get; } = new[]
    {
        "Zephyr", "Puck", "Charon", "Kore", "Fenrir", "Leda", "Orus", "Aoede", "Callirrhoe", "Autonoe",
        "Enceladus", "Iapetus", "Umbriel", "Algieba", "Despina", "Erinome", "Algenib", "Rasalgethi", "Laomedeia", "Achernar",
        "Alnilam", "Schedar", "Gacrux", "Pulcherrima", "Achird", "Zubenelgenubi", "Vindemiatrix", "Sadachbia", "Sadaltager", "Sulafat"
    };
}
