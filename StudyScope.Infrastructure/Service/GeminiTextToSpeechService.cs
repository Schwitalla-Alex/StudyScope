using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Sprachausgabe über Google Gemini TTS (Cloud). Der Schlüssel kommt aus den KI-Einstellungen des Profils, wenn dort
/// Gemini mit Schlüssel eingerichtet ist, sonst aus dem eigenen Schlüssel der Sprachausgabe – unabhängig davon,
/// welche Chat-KI das Skript schreibt.
/// </summary>
public class GeminiTextToSpeechService : ITextToSpeechService
{
    public SpeechProviderType Provider => SpeechProviderType.GeminiTts;
    public string DisplayName => "Google Gemini TTS (Cloud, Audiodatei)";
    public bool SupportsLivePlayback => false;
    public bool SupportsFileExport => true;

    /// <summary>Woher der Schlüssel stammt.</summary>
    public enum KeySource
    {
        None,
        ChatSettings,
        SpeechSettings
    }

    /// <summary>Schlüssel für Gemini TTS: zuerst aus der Chat-KI des Profils (wenn Gemini), sonst der eigene der Sprachausgabe.</summary>
    public static (string Key, KeySource Source) ResolveApiKey(SpeechContext context)
    {
        if (context.ChatConfig is { SelectedProvider: AiProviderType.GoogleGemini } chat && !string.IsNullOrWhiteSpace(chat.ApiKey))
            return (chat.ApiKey.Trim(), KeySource.ChatSettings);

        if (!string.IsNullOrWhiteSpace(context.Settings.GeminiApiKey))
            return (context.Settings.GeminiApiKey.Trim(), KeySource.SpeechSettings);

        return (string.Empty, KeySource.None);
    }

    public Task<SpeechAvailability> GetAvailabilityAsync(SpeechContext context, CancellationToken cancellationToken = default)
    {
        var (_, source) = ResolveApiKey(context);
        var availability = source switch
        {
            KeySource.ChatSettings => SpeechAvailability.Ready("Bereit – Schlüssel aus den KI-Einstellungen des Profils (Google Gemini)."),
            KeySource.SpeechSettings => SpeechAvailability.Ready("Bereit – eigener Schlüssel der Sprachausgabe."),
            _ => SpeechAvailability.Unavailable(
                "Kein API-Schlüssel für Gemini TTS.",
                "Tragen Sie unter ⚙️ Einstellungen › Sprachausgabe einen Gemini-API-Schlüssel ein (kostenlos erhältlich in Google AI Studio). " +
                "Die Chat-KI des Profils kann dabei bleiben, wie sie ist.")
        };
        return Task.FromResult(availability);
    }

    public Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SpeechVoice>>(GeminiTtsInfo.Voices
            .Select(v => new SpeechVoice(v, v, "mehrsprachig", IsLocal: false))
            .ToList());
}
