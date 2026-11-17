using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Sprachausgabe über Google Gemini TTS (Cloud) mit der Interactions-API
/// (POST …/v1beta/interactions, Schlüssel im Header <c>x-goog-api-key</c>). Der Schlüssel kommt aus den
/// KI-Einstellungen des Profils, wenn dort Gemini mit Schlüssel eingerichtet ist, sonst aus dem eigenen Schlüssel der
/// Sprachausgabe – unabhängig davon, welche Chat-KI das Skript schreibt.
/// <para>
/// Lange Skripte werden in Teile von höchstens <see cref="MaxPartCharacters"/> Zeichen zerlegt (an Segment- bzw.
/// Satzgrenzen), einzeln erzeugt und zu einer WAV-Datei zusammengefügt. Beim Podcast gehen beide Sprecher mit je
/// eigener Stimme in dieselbe Anfrage (ein Inhaltselement je Sprecherwechsel mit <c>speech_metadata.speaker</c>).
/// </para>
/// </summary>
public class GeminiTextToSpeechService : IAudioFileSpeechService
{
    public const string ProviderName = "Gemini TTS";
    public const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>Höchstlänge je Anfrage (etwa 3–4 Minuten Sprache).</summary>
    public const int MaxPartCharacters = 3000;

    /// <summary>Kurze Pause zwischen zwei erzeugten Teilen.</summary>
    public const double PauseBetweenPartsSeconds = 0.35;

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Sprechanweisung (Stil) für jedes Inhaltselement.</summary>
    public const string SpeechStyle = "Auf Deutsch, natürlich, ruhig und deutlich vorlesen.";

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _retryDelay;

    public GeminiTextToSpeechService() : this(null)
    {
    }

    /// <param name="httpClient">Für Tests ein Client mit eigenem Handler; Zeitgrenzen setzt der Dienst je Anfrage.</param>
    /// <param name="retryDelay">Wartezeit vor dem einen Wiederholungsversuch bei Überlastung (Tests: 0).</param>
    public GeminiTextToSpeechService(HttpClient? httpClient, TimeSpan? retryDelay = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
    }

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

    public const string MissingKeyHint =
        "Tragen Sie unter ⚙️ Einstellungen › Sprachausgabe einen Gemini-API-Schlüssel ein (kostenlos erhältlich in Google AI Studio). " +
        "Die Chat-KI des Profils kann dabei bleiben, wie sie ist.";

    /// <summary>Schlüssel für Gemini TTS: zuerst aus der Chat-KI des Profils (wenn Gemini), sonst der eigene der Sprachausgabe.</summary>
    public static (string Key, KeySource Source) ResolveApiKey(SpeechContext context)
    {
        if (context.ChatConfig is { SelectedProvider: AiProviderType.GoogleGemini } chat && !string.IsNullOrWhiteSpace(chat.ApiKey))
            return (chat.ApiKey.Trim(), KeySource.ChatSettings);

        if (!string.IsNullOrWhiteSpace(context.Settings.GeminiApiKey))
            return (context.Settings.GeminiApiKey.Trim(), KeySource.SpeechSettings);

        return (string.Empty, KeySource.None);
    }

    public static string KeySourceText(KeySource source) => source switch
    {
        KeySource.ChatSettings => "Schlüssel aus den KI-Einstellungen des Profils (Google Gemini)",
        KeySource.SpeechSettings => "eigener Schlüssel der Sprachausgabe",
        _ => "kein Schlüssel"
    };

    /// <summary>Modellname aus den Einstellungen (leer = Standard).</summary>
    public static string ResolveModel(SpeechSettings settings) =>
        string.IsNullOrWhiteSpace(settings.GeminiModel) ? GeminiTtsInfo.DefaultModel : settings.GeminiModel.Trim();

    public Task<SpeechAvailability> GetAvailabilityAsync(SpeechContext context, CancellationToken cancellationToken = default)
    {
        var (_, source) = ResolveApiKey(context);
        var availability = source switch
        {
            KeySource.ChatSettings => SpeechAvailability.Ready("Bereit – Schlüssel aus den KI-Einstellungen des Profils (Google Gemini)."),
            KeySource.SpeechSettings => SpeechAvailability.Ready("Bereit – eigener Schlüssel der Sprachausgabe."),
            _ => SpeechAvailability.Unavailable("Kein API-Schlüssel für Gemini TTS.", MissingKeyHint)
        };
        return Task.FromResult(availability);
    }

    public Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SpeechVoice>>(GeminiTtsInfo.Voices
            .Select(v => new SpeechVoice(v, v, "mehrsprachig", IsLocal: false))
            .ToList());

    // ---------------- Verbindung testen (kostenlos) ----------------

    /// <summary>
    /// Prüft Schlüssel und Modell über die kostenlose Modellabfrage (GET …/models/{Modell}) – es wird keine Sprache
    /// erzeugt. 400/401/403 = Schlüssel abgelehnt, 404 = Modell unbekannt, 429 = Kontingent.
    /// </summary>
    public async Task<string> TestConnectionAsync(SpeechContext context, CancellationToken cancellationToken = default)
    {
        var (key, source) = ResolveApiKey(context);
        if (source == KeySource.None)
            throw new LlmServiceException($"Kein API-Schlüssel für Gemini TTS. {MissingKeyHint}", ProviderName);

        var model = ResolveModel(context.Settings);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ValidationTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models/{Uri.EscapeDataString(model)}");
        request.Headers.Add("x-goog-api-key", key);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlmServiceException(
                $"{ProviderName} hat nicht rechtzeitig geantwortet (Zeitüberschreitung nach {ValidationTimeout.TotalSeconds:0} s). Bitte Internetverbindung prüfen und erneut versuchen.",
                ProviderName);
        }
        catch (HttpRequestException ex)
        {
            throw LlmServiceException.FromNetworkError(ProviderName, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
            if (!response.IsSuccessStatusCode)
                throw ErrorFromResponse((int)response.StatusCode, body, model);

            var displayName = TryGetString(body, "displayName");
            var name = string.IsNullOrWhiteSpace(displayName) ? model : $"{model} ({displayName})";
            return $"✓ Verbindung zu Gemini TTS erfolgreich – Modell „{name}“ ist verfügbar ({KeySourceText(source)}). " +
                   "Der Test hat keine Kosten verursacht.";
        }
    }

    // ---------------- Audiodatei erzeugen ----------------

    /// <summary>Ein Stück Sprechtext: aus welchem Segment, welcher Sprecher-Platz (0/1).</summary>
    public sealed record SpeechPiece(int SegmentIndex, int Slot, string Text);

    /// <summary>
    /// Zerlegt das Skript in Anfrage-Teile von höchstens <paramref name="maxCharacters"/> Zeichen: Segmente bleiben
    /// zusammen, wenn sie passen; zu lange Segmente werden an Satzenden geteilt. Leere Segmente entfallen.
    /// </summary>
    public static List<List<SpeechPiece>> BuildParts(AudioSummary summary, int maxCharacters = MaxPartCharacters)
    {
        var parts = new List<List<SpeechPiece>>();
        var current = new List<SpeechPiece>();
        var length = 0;

        for (var i = 0; i < summary.Segments.Count; i++)
        {
            var segment = summary.Segments[i];
            var slot = AudioScriptFormats.SpeakerSlot(summary, segment.Speaker);
            foreach (var text in SplitText(segment.Text, maxCharacters))
            {
                if (current.Count > 0 && length + text.Length > maxCharacters)
                {
                    parts.Add(current);
                    current = new List<SpeechPiece>();
                    length = 0;
                }

                current.Add(new SpeechPiece(i, slot, text));
                length += text.Length + 2;
            }
        }

        if (current.Count > 0)
            parts.Add(current);
        return parts;
    }

    /// <summary>Teilt einen Text an Satzenden (notfalls an Leerzeichen) in Stücke von höchstens <paramref name="maxLength"/> Zeichen.</summary>
    public static List<string> SplitText(string? text, int maxLength)
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

    /// <summary>Sprechernamen in der Anfrage (müssen in Stimmenzuordnung und Inhaltselementen übereinstimmen).</summary>
    public static string SpeakerAlias(int slot) => slot == 0 ? AudioScriptFormats.HostName : AudioScriptFormats.ExpertName;

    /// <summary>Stimme aus den Einstellungen (unbekannte Namen → Standardstimme).</summary>
    public static string ResolveVoice(string? voice, string fallback) =>
        GeminiTtsInfo.Voices.FirstOrDefault(v => string.Equals(v, voice?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? fallback;

    /// <summary>
    /// Anfrage an die Interactions-API: ein Sprecher → ein Textelement und <c>speech_config</c> als Liste mit einer
    /// Stimme; zwei Sprecher → ein Textelement je Sprecherwechsel mit <c>speech_metadata.speaker</c> und
    /// <c>speech_config.speakers</c> mit beiden Stimmen.
    /// </summary>
    public static JsonObject BuildRequestBody(string model, IReadOnlyList<SpeechPiece> pieces, bool twoSpeakers, string voicePrimary, string voiceSecondary)
    {
        var content = new JsonArray();
        JsonNode speechConfig;

        if (twoSpeakers)
        {
            // Aufeinanderfolgende Stücke desselben Sprechers zu einem Beitrag zusammenfassen
            var turns = new List<(int Slot, StringBuilder Text)>();
            foreach (var piece in pieces)
            {
                if (turns.Count > 0 && turns[^1].Slot == piece.Slot)
                    turns[^1].Text.Append('\n').Append(piece.Text);
                else
                    turns.Add((piece.Slot, new StringBuilder(piece.Text)));
            }

            foreach (var (slot, text) in turns)
            {
                content.Add(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text.ToString(),
                    ["annotations"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "speech_metadata",
                        ["speaker"] = SpeakerAlias(slot),
                        ["style"] = SpeechStyle
                    })
                });
            }

            speechConfig = new JsonObject
            {
                ["speakers"] = new JsonArray(
                    new JsonObject { ["speaker"] = SpeakerAlias(0), ["voice"] = voicePrimary },
                    new JsonObject { ["speaker"] = SpeakerAlias(1), ["voice"] = voiceSecondary })
            };
        }
        else
        {
            content.Add(new JsonObject
            {
                ["type"] = "text",
                ["text"] = string.Join("\n\n", pieces.Select(p => p.Text)),
                ["annotations"] = new JsonArray(new JsonObject { ["type"] = "speech_metadata", ["style"] = SpeechStyle })
            });
            speechConfig = new JsonArray(new JsonObject { ["voice"] = voicePrimary });
        }

        return new JsonObject
        {
            ["model"] = model,
            ["input"] = new JsonArray(new JsonObject { ["type"] = "user_input", ["content"] = content }),
            ["response_format"] = new JsonObject { ["type"] = "audio" },
            ["generation_config"] = new JsonObject { ["speech_config"] = speechConfig }
        };
    }

    public async Task<AudioFileResult> SynthesizeToFileAsync(AudioSummary summary, SpeechContext context, string filePath,
        IProgress<SpeechSynthesisProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var (key, source) = ResolveApiKey(context);
        if (source == KeySource.None)
            throw new LlmServiceException($"Kein API-Schlüssel für Gemini TTS. {MissingKeyHint}", ProviderName);

        var parts = BuildParts(summary);
        if (parts.Count == 0)
            throw new LlmServiceException("Das Skript ist leer – es gibt nichts vorzulesen.", ProviderName);

        var model = ResolveModel(context.Settings);
        var twoSpeakers = AudioScriptFormats.HasTwoSpeakers(summary);
        var voicePrimary = ResolveVoice(context.Settings.GeminiVoicePrimary, GeminiTtsInfo.DefaultVoicePrimary);
        var voiceSecondary = ResolveVoice(context.Settings.GeminiVoiceSecondary, GeminiTtsInfo.DefaultVoiceSecondary);
        if (twoSpeakers && voiceSecondary == voicePrimary)
            voiceSecondary = voicePrimary == GeminiTtsInfo.DefaultVoiceSecondary ? GeminiTtsInfo.DefaultVoicePrimary : GeminiTtsInfo.DefaultVoiceSecondary;

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Erst in eine temporäre Datei schreiben: bei Abbruch/Fehler bleibt keine halbe WAV-Datei zurück
        var tempPath = filePath + ".part";
        TryDelete(tempPath);

        var starts = Enumerable.Repeat(double.NaN, summary.Segments.Count).ToArray();
        double duration;
        try
        {
            progress?.Report(new SpeechSynthesisProgress(0, parts.Count));
            using (var writer = new WavFileWriter(tempPath))
            {
                for (var p = 0; p < parts.Count; p++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var part = parts[p];
                    var body = BuildRequestBody(model, part, twoSpeakers, voicePrimary, voiceSecondary);
                    var audio = await RequestAudioAsync(key, model, body, cancellationToken);

                    if (p > 0)
                        writer.AppendSilence(PauseBetweenPartsSeconds);

                    var partStart = writer.DurationSeconds;
                    try
                    {
                        writer.Append(audio);
                    }
                    catch (InvalidDataException ex)
                    {
                        throw new LlmServiceException($"{ProviderName} hat Audio in unerwartetem Format geliefert. Bitte erneut versuchen.",
                            ProviderName, null, ex.Message, ex);
                    }

                    // Startzeiten innerhalb des Teils nach Zeichenanteil schätzen
                    var totalChars = Math.Max(1, part.Sum(x => x.Text.Length));
                    var before = 0;
                    foreach (var piece in part)
                    {
                        var start = partStart + audio.DurationSeconds * before / totalChars;
                        if (double.IsNaN(starts[piece.SegmentIndex]))
                            starts[piece.SegmentIndex] = Math.Round(start, 2);
                        before += piece.Text.Length;
                    }

                    progress?.Report(new SpeechSynthesisProgress(p + 1, parts.Count));
                }

                duration = writer.DurationSeconds;
                writer.Complete();
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        // Leere Segmente bekommen die Startzeit des vorherigen
        var last = 0.0;
        for (var i = 0; i < starts.Length; i++)
        {
            if (double.IsNaN(starts[i])) starts[i] = last;
            last = starts[i];
        }

        return new AudioFileResult(Math.Round(duration, 2), starts, $"Gemini TTS ({model})");
    }

    private async Task<PcmAudio> RequestAudioAsync(string key, string model, JsonObject body, CancellationToken cancellationToken)
    {
        var json = body.ToJsonString();
        for (var attempt = 1; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/interactions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-goog-api-key", key);

            string responseBody;
            HttpStatusCode status;
            try
            {
                using var response = await _httpClient.SendAsync(request, timeout.Token);
                status = response.StatusCode;
                responseBody = await response.Content.ReadAsStringAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new LlmServiceException(
                    $"{ProviderName} hat nicht rechtzeitig geantwortet (Zeitüberschreitung nach {RequestTimeout.TotalMinutes:0} Minuten). " +
                    "Bitte erneut versuchen; bei sehr langen Abschnitten den Text kürzen.", ProviderName);
            }
            catch (HttpRequestException ex)
            {
                throw LlmServiceException.FromNetworkError(ProviderName, ex);
            }

            if ((int)status is 500 or 502 or 503 && attempt == 1)
            {
                await Task.Delay(_retryDelay, cancellationToken);
                continue;
            }

            if ((int)status is < 200 or > 299)
                throw ErrorFromResponse((int)status, responseBody, model);

            return ParseAudioResponse(responseBody);
        }
    }

    /// <summary>
    /// Liest die Audiodaten aus der Antwort: <c>steps[]</c> (type „model_output“) → <c>content[]</c> (type „audio“)
    /// → <c>data</c> (base64); zur Sicherheit auch an anderer Stelle (beliebiges Objekt mit type „audio“ und data,
    /// bzw. generateContent-Form <c>inlineData</c>).
    /// </summary>
    public static PcmAudio ParseAudioResponse(string responseBody)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(responseBody);
        }
        catch (JsonException ex)
        {
            throw new LlmServiceException($"{ProviderName} hat eine unerwartete Antwort geliefert. Bitte erneut versuchen.",
                ProviderName, null, Shorten(responseBody), ex);
        }

        var audio = FindAudio(root);
        if (audio == null)
        {
            throw new LlmServiceException(
                $"{ProviderName} hat keine Audiodaten geliefert. Möglicherweise wurde der Text vom Inhaltsfilter blockiert oder das Modell " +
                "erzeugt keine Sprache. Bitte erneut versuchen, den Abschnitt umformulieren oder unter ⚙️ Einstellungen › Sprachausgabe den Modellnamen prüfen.",
                ProviderName, null, Shorten(responseBody));
        }

        try
        {
            var bytes = Convert.FromBase64String(audio.Value.Data);
            return WavAudio.Decode(bytes, audio.Value.MimeType, audio.Value.SampleRate);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            throw new LlmServiceException($"{ProviderName} hat Audio in unerwartetem Format geliefert ({audio.Value.MimeType}). Bitte erneut versuchen.",
                ProviderName, null, ex.Message, ex);
        }
    }

    private static (string Data, string? MimeType, int? SampleRate)? FindAudio(JsonNode? node)
    {
        // Bevorzugt: steps[] → model_output → content[] → audio
        if (node is JsonObject root && root["steps"] is JsonArray steps)
        {
            foreach (var step in steps.OfType<JsonObject>())
            {
                if (!string.Equals(step["type"]?.GetValue<string>(), "model_output", StringComparison.OrdinalIgnoreCase)) continue;
                if (step["content"] is not JsonArray content) continue;
                foreach (var item in content.OfType<JsonObject>())
                {
                    if (ReadAudioObject(item) is { } found) return found;
                }
            }
        }

        return SearchAudio(node, 0);
    }

    private static (string, string?, int?)? ReadAudioObject(JsonObject item)
    {
        var type = item["type"] is JsonValue t && t.TryGetValue<string>(out var ts) ? ts : null;
        if (string.Equals(type, "audio", StringComparison.OrdinalIgnoreCase) && item["data"] is JsonValue d && d.TryGetValue<string>(out var data) && data.Length > 0)
        {
            var mime = item["mime_type"] is JsonValue m && m.TryGetValue<string>(out var ms) ? ms : null;
            int? rate = item["sample_rate"] is JsonValue r && r.TryGetValue<int>(out var ri) ? ri : null;
            return (data, mime, rate);
        }

        // generateContent: {"inlineData": {"mimeType": "audio/L16;codec=pcm;rate=24000", "data": "…"}}
        if (item["inlineData"] is JsonObject inline && inline["data"] is JsonValue id && id.TryGetValue<string>(out var inlineData) && inlineData.Length > 0)
        {
            var mime = inline["mimeType"] is JsonValue m && m.TryGetValue<string>(out var ms) ? ms : null;
            return (inlineData, mime, null);
        }

        return null;
    }

    private static (string, string?, int?)? SearchAudio(JsonNode? node, int depth)
    {
        if (node == null || depth > 8) return null;
        if (node is JsonObject obj)
        {
            if (ReadAudioObject(obj) is { } found) return found;
            foreach (var (_, child) in obj)
            {
                if (SearchAudio(child, depth + 1) is { } inner) return inner;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (SearchAudio(child, depth + 1) is { } inner) return inner;
            }
        }
        return null;
    }

    // ---------------- Fehlermeldungen ----------------

    /// <summary>
    /// Übersetzt eine Fehlerantwort von Gemini in einen deutschen Satz mit Handlungsempfehlung
    /// (Fehlerformat: <c>{"error": {"code", "message", "status", "details": [{"reason"}]}}</c>).
    /// </summary>
    public static LlmServiceException ErrorFromResponse(int statusCode, string? body, string model)
    {
        var (status, message, reasons) = ParseError(body);
        var detail = string.IsNullOrWhiteSpace(message) ? Shorten(body) : $"{status} {message}".Trim();
        var text = message.ToLowerInvariant();

        string userMessage;
        if (reasons.Contains("API_KEY_INVALID") || text.Contains("api key not valid") || text.Contains("api key expired")
            || (statusCode == 400 && text.Contains("api key")))
        {
            userMessage = "Der Gemini-API-Schlüssel wurde abgelehnt (ungültig oder abgelaufen). Bitte den Schlüssel unter ⚙️ Einstellungen › Sprachausgabe " +
                          "bzw. in den KI-Einstellungen prüfen – einen neuen Schlüssel gibt es kostenlos in Google AI Studio.";
        }
        else if (statusCode is 401 or 403)
        {
            userMessage = $"Der Gemini-API-Schlüssel hat keinen Zugriff auf die Sprachausgabe (HTTP {statusCode}). Prüfen Sie in Google AI Studio, " +
                          "ob der Schlüssel aktiv ist und die Gemini API für das Projekt freigeschaltet ist.";
        }
        else if (statusCode == 404)
        {
            userMessage = $"Das Modell „{model}“ wurde bei Gemini nicht gefunden. Bitte unter ⚙️ Einstellungen › Sprachausgabe den Modellnamen prüfen " +
                          $"(Standard: {GeminiTtsInfo.DefaultModel}).";
        }
        else if (statusCode == 429)
        {
            userMessage = "Das Kontingent für Gemini TTS ist erschöpft oder es wurden zu viele Anfragen gestellt (HTTP 429). In der kostenlosen Stufe gelten " +
                          "Minuten- und Tageslimits: Warten Sie etwas (bzw. bis morgen) und versuchen Sie es erneut, oder aktivieren Sie die Abrechnung in Google AI Studio.";
        }
        else if (statusCode == 400 && (text.Contains("location") || text.Contains("region") || text.Contains("country")))
        {
            userMessage = "Gemini TTS ist für diesen Schlüssel in Ihrer Region nicht verfügbar (HTTP 400). Bitte die Verfügbarkeit in Google AI Studio prüfen.";
        }
        else if (statusCode == 400)
        {
            userMessage = $"Gemini hat die Anfrage abgelehnt (HTTP 400). Möglicherweise erzeugt das Modell „{model}“ keine Sprache – bitte unter " +
                          $"⚙️ Einstellungen › Sprachausgabe den Modellnamen prüfen (Standard: {GeminiTtsInfo.DefaultModel}).";
        }
        else if (statusCode >= 500)
        {
            userMessage = $"Gemini TTS ist gerade überlastet oder gestört (HTTP {statusCode}). Bitte in einigen Minuten erneut versuchen.";
        }
        else
        {
            return LlmServiceException.FromHttpStatus(ProviderName, statusCode, detail);
        }

        return new LlmServiceException(userMessage, ProviderName, statusCode, detail);
    }

    private static (string Status, string Message, HashSet<string> Reasons) ParseError(string? body)
    {
        var reasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (JsonNode.Parse(body ?? string.Empty) is JsonObject root && root["error"] is JsonObject error)
            {
                var status = error["status"] is JsonValue s && s.TryGetValue<string>(out var st) ? st : string.Empty;
                var message = error["message"] is JsonValue m && m.TryGetValue<string>(out var ms) ? ms : string.Empty;
                if (error["details"] is JsonArray details)
                {
                    foreach (var detail in details.OfType<JsonObject>())
                    {
                        if (detail["reason"] is JsonValue r && r.TryGetValue<string>(out var reason))
                            reasons.Add(reason);
                    }
                }
                return (status, message, reasons);
            }
        }
        catch (JsonException)
        {
            // keine JSON-Antwort
        }
        return (string.Empty, string.Empty, reasons);
    }

    private static string? TryGetString(string body, string property)
    {
        try
        {
            return JsonNode.Parse(body) is JsonObject obj && obj[property] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Shorten(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        // base64-Audio nicht in die Details übernehmen
        return value.Length <= 500 ? value : value[..500] + " …";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
